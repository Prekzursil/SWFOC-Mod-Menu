using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class NamedPipeExtenderBackend : IExecutionBackend
{
    private const int DefaultConnectTimeoutMs = 2000;
    private const int DefaultResponseTimeoutMs = 2000;
    private const string DefaultPipeName = "SwfocExtenderBridge";
    private const string PipeNameEnvironmentVariable = "SWFOC_EXTENDER_PIPE_NAME";
    private const string ExtenderBackendId = "extender";
    private const string ProbePlaceholderAnchorValue = "probe";
    private const string ProbeResolvedAnchorsMetadataKey = "probeResolvedAnchorsJson";
    private const string NativeDirectoryName = "native";
    private const string BridgeHostWindowsExecutableName = "SwfocExtender.Host.exe";
    private const string BridgeHostPosixExecutableName = "SwfocExtender.Host";
    private static readonly string[] NativeAuthoritativeFeatureIds =
    [
        "freeze_timer",
        "toggle_fog_reveal",
        "toggle_ai",
        "set_unit_cap",
        "toggle_instant_build_patch",
        "set_credits"
    ];

    private static readonly object HostSync = new();
    private static Process? _bridgeHostProcess;
    private readonly string _pipeName;
    private readonly bool _autoStartBridgeHost;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public NamedPipeExtenderBackend(string? pipeName = null, bool autoStartBridgeHost = true)
    {
        _pipeName = string.IsNullOrWhiteSpace(pipeName)
            ? ResolvePipeNameFromEnvironment()
            : pipeName.Trim();
        _autoStartBridgeHost = autoStartBridgeHost;
    }

    public ExecutionBackendKind BackendKind => ExecutionBackendKind.Extender;

    public Task<CapabilityReport> ProbeCapabilitiesAsync(
        string profileId,
        ProcessMetadata processContext)
    {
        return ProbeCapabilitiesAsync(profileId, processContext, CancellationToken.None);
    }

    public async Task<CapabilityReport> ProbeCapabilitiesAsync(
        string profileId,
        ProcessMetadata processContext,
        CancellationToken cancellationToken)
    {
        var result = await SendAsync(CreateProbeCommand(profileId, processContext), cancellationToken);
        if (!result.Succeeded)
        {
            return CreateProbeFailureReport(profileId, result);
        }

        return CreateProbeSuccessReport(profileId, result);
    }

    private static ExtenderCommand CreateProbeCommand(string profileId, ProcessMetadata processContext)
    {
        return new ExtenderCommand(
            CommandId: Guid.NewGuid().ToString("N"),
            FeatureId: "probe_capabilities",
            ProfileId: profileId,
            Mode: processContext.Mode,
            Payload: new System.Text.Json.Nodes.JsonObject(),
            ProcessId: processContext.ProcessId,
            ProcessName: processContext.ProcessName,
            ResolvedAnchors: BuildProbeAnchors(profileId, processContext),
            RequestedBy: "runtime_adapter",
            TimestampUtc: DateTimeOffset.UtcNow);
    }

    private static JsonObject BuildProbeAnchors(string profileId, ProcessMetadata processContext)
    {
        var anchors = new JsonObject();
        if (processContext.ProcessId <= 0)
        {
            return anchors;
        }

        SeedDefaultProbeAnchors(profileId, anchors);
        MergeProbeAnchorsFromMetadata(processContext, anchors);

        return anchors;
    }

    private static void SeedDefaultProbeAnchors(string profileId, JsonObject anchors)
    {
        if (!ShouldSeedProbeDefaults(profileId))
        {
            return;
        }

        // Seed known-profile anchors to keep legacy/base promoted routes deterministic
        // when host command-line launch markers are unavailable.
        anchors["credits"] = ProbePlaceholderAnchorValue;
        anchors["set_credits"] = ProbePlaceholderAnchorValue;
        anchors["game_timer_freeze"] = ProbePlaceholderAnchorValue;
        anchors["freeze_timer"] = ProbePlaceholderAnchorValue;
        anchors["fog_reveal"] = ProbePlaceholderAnchorValue;
        anchors["toggle_fog_reveal"] = ProbePlaceholderAnchorValue;
        anchors["ai_enabled"] = ProbePlaceholderAnchorValue;
        anchors["toggle_ai"] = ProbePlaceholderAnchorValue;
        anchors["unit_cap"] = ProbePlaceholderAnchorValue;
        anchors["set_unit_cap"] = ProbePlaceholderAnchorValue;
        anchors["instant_build_patch_injection"] = ProbePlaceholderAnchorValue;
        anchors["instant_build_patch"] = ProbePlaceholderAnchorValue;
        anchors["toggle_instant_build_patch"] = ProbePlaceholderAnchorValue;
    }

    private static void MergeProbeAnchorsFromMetadata(ProcessMetadata processContext, JsonObject anchors)
    {
        if (!TryGetProbeAnchorsJson(processContext, out var rawAnchors))
        {
            return;
        }

        try
        {
            if (JsonNode.Parse(rawAnchors) is JsonObject parsedAnchors)
            {
                AppendNonEmptyAnchorValues(parsedAnchors, anchors);
            }
        }
        catch
        {
            // Keep seeded defaults when metadata parsing fails.
        }
    }

    private static bool TryGetProbeAnchorsJson(ProcessMetadata processContext, out string rawAnchors)
    {
        rawAnchors = string.Empty;
        if (processContext.Metadata is null ||
            !processContext.Metadata.TryGetValue(ProbeResolvedAnchorsMetadataKey, out var candidate) ||
            string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        rawAnchors = candidate;
        return true;
    }

    private static void AppendNonEmptyAnchorValues(JsonObject sourceAnchors, JsonObject destinationAnchors)
    {
        foreach (var kv in sourceAnchors)
        {
            var normalized = kv.Value?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(normalized))
            {
                destinationAnchors[kv.Key] = normalized;
            }
        }
    }

    private static bool ShouldSeedProbeDefaults(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            return false;
        }

        return profileId.Equals("base_swfoc", StringComparison.OrdinalIgnoreCase) ||
               profileId.Equals("base_sweaw", StringComparison.OrdinalIgnoreCase) ||
               profileId.StartsWith("aotr_", StringComparison.OrdinalIgnoreCase) ||
               profileId.StartsWith("roe_", StringComparison.OrdinalIgnoreCase);
    }

    private CapabilityReport CreateProbeFailureReport(string profileId, ExtenderResult result)
    {
        return CapabilityReport.Unknown(profileId, result.ReasonCode) with
        {
            Diagnostics = new Dictionary<string, object?>
            {
                ["backend"] = ExtenderBackendId,
                ["pipe"] = _pipeName,
                ["reasonCode"] = result.ReasonCode.ToString(),
                ["message"] = result.Message
            }
        };
    }

    private CapabilityReport CreateProbeSuccessReport(string profileId, ExtenderResult result)
    {
        var capabilities = NamedPipeExtenderBackendContextHelpers.ParseCapabilities(result.Diagnostics, NativeAuthoritativeFeatureIds);
        capabilities["probe_capabilities"] = new BackendCapability(
            FeatureId: "probe_capabilities",
            Available: true,
            Confidence: CapabilityConfidenceState.Verified,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Notes: "Named-pipe probe acknowledged.");

        return new CapabilityReport(
            profileId,
            DateTimeOffset.UtcNow,
            capabilities,
            RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            new Dictionary<string, object?>
            {
                ["backend"] = ExtenderBackendId,
                ["pipe"] = _pipeName,
                ["hookState"] = result.HookState
            });
    }

    public Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionRequest command,
        CapabilityReport capabilityReport)
    {
        return ExecuteAsync(command, capabilityReport, CancellationToken.None);
    }

    public async Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionRequest command,
        CapabilityReport capabilityReport,
        CancellationToken cancellationToken)
    {
        var commandContext = command.Context;
        var extenderCommand = new ExtenderCommand(
            CommandId: Guid.NewGuid().ToString("N"),
            FeatureId: command.Action.Id,
            ProfileId: command.ProfileId,
            Mode: command.RuntimeMode,
            Payload: command.Payload,
            ProcessId: NamedPipeExtenderBackendContextHelpers.ReadContextInt(commandContext, "processId"),
            ProcessName: NamedPipeExtenderBackendContextHelpers.ReadContextString(commandContext, "processName"),
            ResolvedAnchors: NamedPipeExtenderBackendContextHelpers.ReadContextAnchors(commandContext),
            RequestedBy: "external_app",
            TimestampUtc: DateTimeOffset.UtcNow);

        var result = await SendAsync(extenderCommand, cancellationToken);
        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["extenderCommandId"] = result.CommandId
        };

        if (result.Diagnostics is not null)
        {
            foreach (var kv in result.Diagnostics)
            {
                diagnostics[kv.Key] = kv.Value;
            }
        }

        diagnostics["reasonCode"] = result.ReasonCode.ToString();
        diagnostics["backend"] = result.Backend;
        diagnostics["hookState"] = result.HookState;
        diagnostics["probeReasonCode"] = capabilityReport.ProbeReasonCode.ToString();

        return new ActionExecutionResult(
            result.Succeeded,
            result.Message,
            AddressSource.None,
            diagnostics);
    }

    public Task<BackendHealth> GetHealthAsync()
    {
        return GetHealthAsync(CancellationToken.None);
    }

    public async Task<BackendHealth> GetHealthAsync(CancellationToken cancellationToken)
    {
        var probe = await SendCoreAsync(new ExtenderCommand(
            CommandId: Guid.NewGuid().ToString("N"),
            FeatureId: "health",
            ProfileId: "unknown",
            Mode: RuntimeMode.Unknown,
            Payload: new System.Text.Json.Nodes.JsonObject(),
            ProcessId: 0,
            ProcessName: string.Empty,
            ResolvedAnchors: new System.Text.Json.Nodes.JsonObject(),
            RequestedBy: "runtime_adapter",
            TimestampUtc: DateTimeOffset.UtcNow), cancellationToken);

        return new BackendHealth(
            BackendId: ExtenderBackendId,
            Backend: ExecutionBackendKind.Extender,
            IsHealthy: probe.Succeeded,
            ReasonCode: probe.ReasonCode,
            Message: probe.Message,
            Diagnostics: new Dictionary<string, object?>
            {
                ["pipe"] = _pipeName,
                ["hookState"] = probe.HookState
            });
    }

    private async Task<ExtenderResult> SendAsync(ExtenderCommand command, CancellationToken cancellationToken)
    {
        var firstAttempt = await SendCoreAsync(command, cancellationToken);
        if (firstAttempt.Succeeded || firstAttempt.HookState != "unreachable")
        {
            return firstAttempt;
        }

        if (!_autoStartBridgeHost || !TryStartBridgeHostProcess())
        {
            return firstAttempt;
        }

        return await SendCoreAsync(command, cancellationToken);
    }

    private async Task<ExtenderResult> SendCoreAsync(ExtenderCommand command, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CreateRequestTimeoutTokenSource(cancellationToken);
            using var client = CreatePipeClient(_pipeName);

            await client.ConnectAsync(DefaultConnectTimeoutMs, timeoutCts.Token);

            using var writer = CreatePipeWriter(client);
            using var reader = CreatePipeReader(client);

            await WriteCommandAsync(writer, command, timeoutCts.Token);
            var line = await reader.ReadLineAsync(timeoutCts.Token).AsTask();
            return ParseResponse(command.CommandId, line);
        }
        catch (OperationCanceledException)
        {
            return CreateTimeoutResult(command.CommandId);
        }
        catch (Exception ex)
        {
            return CreateUnreachableResult(command.CommandId, ex.Message);
        }
    }

    private static CancellationTokenSource CreateRequestTimeoutTokenSource(CancellationToken cancellationToken)
    {
        var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(DefaultResponseTimeoutMs + DefaultConnectTimeoutMs + 100);
        return timeoutCts;
    }

    private static NamedPipeClientStream CreatePipeClient(string pipeName)
    {
        return new NamedPipeClientStream(
            serverName: ".",
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);
    }

    private static string ResolvePipeNameFromEnvironment()
    {
        var candidate = Environment.GetEnvironmentVariable(PipeNameEnvironmentVariable);
        return string.IsNullOrWhiteSpace(candidate)
            ? DefaultPipeName
            : candidate.Trim();
    }

    private static StreamWriter CreatePipeWriter(NamedPipeClientStream client)
    {
        return new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true)
        {
            AutoFlush = true
        };
    }

    private static StreamReader CreatePipeReader(NamedPipeClientStream client)
    {
        return new StreamReader(client, Encoding.UTF8, leaveOpen: true);
    }

    private static async Task WriteCommandAsync(
        StreamWriter writer,
        ExtenderCommand command,
        CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.Serialize(command, JsonOptions);
        await writer.WriteLineAsync(payload.AsMemory(), cancellationToken);
    }

    private static ExtenderResult ParseResponse(string commandId, string? responseLine)
    {
        if (string.IsNullOrWhiteSpace(responseLine))
        {
            return CreateNoResponseResult(commandId);
        }

        var result = JsonSerializer.Deserialize<ExtenderResult>(responseLine, JsonOptions);
        return result ?? CreateInvalidResponseResult(commandId);
    }

    private static ExtenderResult CreateNoResponseResult(string commandId)
    {
        return new ExtenderResult(
            CommandId: commandId,
            Succeeded: false,
            ReasonCode: RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE,
            Backend: ExtenderBackendId,
            HookState: "no_response",
            Message: "Extender pipe did not return a response.");
    }

    private static ExtenderResult CreateInvalidResponseResult(string commandId)
    {
        return new ExtenderResult(
            CommandId: commandId,
            Succeeded: false,
            ReasonCode: RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE,
            Backend: ExtenderBackendId,
            HookState: "invalid_response",
            Message: "Extender response payload was invalid.");
    }

    private static ExtenderResult CreateTimeoutResult(string commandId)
    {
        return new ExtenderResult(
            CommandId: commandId,
            Succeeded: false,
            ReasonCode: RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE,
            Backend: ExtenderBackendId,
            HookState: "timeout",
            Message: "Extender pipe request timed out.");
    }

    private static ExtenderResult CreateUnreachableResult(string commandId, string exceptionMessage)
    {
        return new ExtenderResult(
            CommandId: commandId,
            Succeeded: false,
            ReasonCode: RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE,
            Backend: ExtenderBackendId,
            HookState: "unreachable",
            Message: $"Extender pipe unavailable: {exceptionMessage}");
    }

    private static bool TryStartBridgeHostProcess()
    {
        lock (HostSync)
        {
            if (_bridgeHostProcess is not null && !_bridgeHostProcess.HasExited)
            {
                return true;
            }

            var hostPath = ResolveBridgeHostPath();
            if (hostPath is null)
            {
                return false;
            }

            try
            {
                _bridgeHostProcess = Process.Start(new ProcessStartInfo
                {
                    FileName = hostPath,
                    WorkingDirectory = Path.GetDirectoryName(hostPath) ?? AppContext.BaseDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                Thread.Sleep(200);
                return _bridgeHostProcess is not null && !_bridgeHostProcess.HasExited;
            }
            catch
            {
                _bridgeHostProcess = null;
                return false;
            }
        }
    }

    private static string? ResolveBridgeHostPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("SWFOC_EXTENDER_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv))
        {
            return fromEnv;
        }

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in ResolveSearchRoots())
        {
            AddKnownCandidatePaths(candidates, root);
            AddDiscoveredNativeBuildCandidates(candidates, root);
        }

        return candidates
            .Where(File.Exists)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name.Equals(BridgeHostWindowsExecutableName, StringComparison.OrdinalIgnoreCase))
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private static IEnumerable<string> ResolveSearchRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        TryAddRoot(roots, AppContext.BaseDirectory);
        TryAddRoot(roots, Environment.CurrentDirectory);
        TryAddAncestorRoots(roots, AppContext.BaseDirectory, 6);
        TryAddAncestorRoots(roots, Environment.CurrentDirectory, 6);
        return roots;
    }

    private static void TryAddRoot(HashSet<string> roots, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            roots.Add(Path.GetFullPath(path));
        }
        catch
        {
            // ignored
        }
    }

    private static void TryAddAncestorRoots(HashSet<string> roots, string? startPath, int maxDepth)
    {
        if (string.IsNullOrWhiteSpace(startPath))
        {
            return;
        }

        DirectoryInfo? directory = null;
        try
        {
            directory = new DirectoryInfo(startPath);
        }
        catch
        {
            return;
        }

        for (var depth = 0; directory is not null && depth < maxDepth; depth++)
        {
            TryAddRoot(roots, directory.FullName);
            directory = directory.Parent;
        }
    }

    private static void AddKnownCandidatePaths(HashSet<string> candidates, string root)
    {
        var known = new[]
        {
            Path.Combine(root, NativeDirectoryName, "runtime", BridgeHostWindowsExecutableName),
            Path.Combine(root, NativeDirectoryName, "build-win-vs", "SwfocExtender.Bridge", "Release", BridgeHostWindowsExecutableName),
            Path.Combine(root, NativeDirectoryName, "build-win-vs", "SwfocExtender.Bridge", "x64", "Release", BridgeHostWindowsExecutableName),
            Path.Combine(root, NativeDirectoryName, "build-win-codex", "SwfocExtender.Bridge", "Release", BridgeHostWindowsExecutableName),
            Path.Combine(root, NativeDirectoryName, "build-win", BridgeHostWindowsExecutableName),
            Path.Combine(root, NativeDirectoryName, "build-wsl", BridgeHostPosixExecutableName)
        };

        foreach (var path in known)
        {
            candidates.Add(path);
        }
    }

    private static void AddDiscoveredNativeBuildCandidates(HashSet<string> candidates, string root)
    {
        var nativeRoot = Path.Combine(root, NativeDirectoryName);
        if (!Directory.Exists(nativeRoot))
        {
            return;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(nativeRoot, BridgeHostWindowsExecutableName, SearchOption.AllDirectories))
            {
                candidates.Add(path);
            }

            foreach (var path in Directory.EnumerateFiles(nativeRoot, BridgeHostPosixExecutableName, SearchOption.AllDirectories))
            {
                candidates.Add(path);
            }
        }
        catch
        {
            // ignored: candidate discovery is best-effort.
        }
    }

}
