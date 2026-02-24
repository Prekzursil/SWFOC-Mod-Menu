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
    private const string ExtenderBackendId = "extender";
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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

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
            ResolvedAnchors: new System.Text.Json.Nodes.JsonObject(),
            RequestedBy: "runtime_adapter",
            TimestampUtc: DateTimeOffset.UtcNow);
    }

    private static CapabilityReport CreateProbeFailureReport(string profileId, ExtenderResult result)
    {
        return CapabilityReport.Unknown(profileId, result.ReasonCode) with
        {
            Diagnostics = new Dictionary<string, object?>
            {
                ["backend"] = ExtenderBackendId,
                ["pipe"] = DefaultPipeName,
                ["reasonCode"] = result.ReasonCode.ToString(),
                ["message"] = result.Message
            }
        };
    }

    private static CapabilityReport CreateProbeSuccessReport(string profileId, ExtenderResult result)
    {
        var capabilities = ParseCapabilities(result.Diagnostics);
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
                ["pipe"] = DefaultPipeName,
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
            ProcessId: ReadContextInt(commandContext, "processId"),
            ProcessName: ReadContextString(commandContext, "processName"),
            ResolvedAnchors: ReadContextAnchors(commandContext),
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
                ["pipe"] = DefaultPipeName,
                ["hookState"] = probe.HookState
            });
    }

    private static async Task<ExtenderResult> SendAsync(ExtenderCommand command, CancellationToken cancellationToken)
    {
        var firstAttempt = await SendCoreAsync(command, cancellationToken);
        if (firstAttempt.Succeeded || firstAttempt.HookState != "unreachable")
        {
            return firstAttempt;
        }

        if (!TryStartBridgeHostProcess())
        {
            return firstAttempt;
        }

        return await SendCoreAsync(command, cancellationToken);
    }

    private static async Task<ExtenderResult> SendCoreAsync(ExtenderCommand command, CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CreateRequestTimeoutTokenSource(cancellationToken);
            using var client = CreatePipeClient();

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

    private static NamedPipeClientStream CreatePipeClient()
    {
        return new NamedPipeClientStream(
            serverName: ".",
            pipeName: DefaultPipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);
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
            .ThenByDescending(file => file.Name.Equals("SwfocExtender.Host.exe", StringComparison.OrdinalIgnoreCase))
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

    private static void AddKnownCandidatePaths(HashSet<string> candidates, string root)
    {
        var known = new[]
        {
            Path.Combine(root, "native", "runtime", "SwfocExtender.Host.exe"),
            Path.Combine(root, "native", "build-win-vs", "SwfocExtender.Bridge", "Release", "SwfocExtender.Host.exe"),
            Path.Combine(root, "native", "build-win-vs", "SwfocExtender.Bridge", "x64", "Release", "SwfocExtender.Host.exe"),
            Path.Combine(root, "native", "build-win-codex", "SwfocExtender.Bridge", "Release", "SwfocExtender.Host.exe"),
            Path.Combine(root, "native", "build-win", "SwfocExtender.Host.exe"),
            Path.Combine(root, "native", "build-wsl", "SwfocExtender.Host")
        };

        foreach (var path in known)
        {
            candidates.Add(path);
        }
    }

    private static void AddDiscoveredNativeBuildCandidates(HashSet<string> candidates, string root)
    {
        var nativeRoot = Path.Combine(root, "native");
        if (!Directory.Exists(nativeRoot))
        {
            return;
        }

        try
        {
            foreach (var path in Directory.EnumerateFiles(nativeRoot, "SwfocExtender.Host.exe", SearchOption.AllDirectories))
            {
                candidates.Add(path);
            }

            foreach (var path in Directory.EnumerateFiles(nativeRoot, "SwfocExtender.Host", SearchOption.AllDirectories))
            {
                candidates.Add(path);
            }
        }
        catch
        {
            // ignored: candidate discovery is best-effort.
        }
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

    private static Dictionary<string, BackendCapability> ParseCapabilities(
        IReadOnlyDictionary<string, object?>? diagnostics)
    {
        var capabilities = new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetCapabilitiesElement(diagnostics, out var element))
        {
            EnsureNativeFeatureEntries(capabilities);
            return capabilities;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (!TryParseCapability(property, out var capability))
            {
                continue;
            }

            capabilities[property.Name] = capability;
        }

        EnsureNativeFeatureEntries(capabilities);
        return capabilities;
    }

    private static void EnsureNativeFeatureEntries(Dictionary<string, BackendCapability> capabilities)
    {
        foreach (var featureId in NativeAuthoritativeFeatureIds)
        {
            if (capabilities.ContainsKey(featureId))
            {
                continue;
            }

            capabilities[featureId] = new BackendCapability(
                FeatureId: featureId,
                Available: false,
                Confidence: CapabilityConfidenceState.Unknown,
                ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                Notes: "Feature omitted from capability probe payload.");
        }
    }

    private static bool TryGetCapabilitiesElement(
        IReadOnlyDictionary<string, object?>? diagnostics,
        out JsonElement capabilitiesElement)
    {
        capabilitiesElement = default;
        if (diagnostics is null || diagnostics.Count == 0)
        {
            return false;
        }

        if (!diagnostics.TryGetValue("capabilities", out var rawCapabilities) || rawCapabilities is not JsonElement element)
        {
            return false;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        capabilitiesElement = element;
        return true;
    }

    private static bool TryParseCapability(JsonProperty property, out BackendCapability capability)
    {
        capability = null!;
        var value = property.Value;
        if (value.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var available = IsAvailable(value);
        var state = TryGetStringProperty(value, "state", out var rawState)
            ? ParseCapabilityConfidence(rawState)
            : CapabilityConfidenceState.Unknown;
        var reasonCode = TryGetStringProperty(value, "reasonCode", out var rawReasonCode)
            ? ParseRuntimeReasonCode(rawReasonCode)
            : RuntimeReasonCode.CAPABILITY_UNKNOWN;

        capability = new BackendCapability(
            FeatureId: property.Name,
            Available: available,
            Confidence: state,
            ReasonCode: reasonCode,
            Notes: "Feature returned by extender capability probe.");
        return true;
    }

    private static bool IsAvailable(JsonElement value)
    {
        return value.TryGetProperty("available", out var availableElement) &&
               availableElement.ValueKind is JsonValueKind.True or JsonValueKind.False &&
               availableElement.GetBoolean();
    }

    private static bool TryGetStringProperty(JsonElement value, string propertyName, out string? propertyValue)
    {
        propertyValue = null;
        if (!value.TryGetProperty(propertyName, out var propertyElement) || propertyElement.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        propertyValue = propertyElement.GetString();
        return true;
    }

    private static CapabilityConfidenceState ParseCapabilityConfidence(string? rawState)
    {
        if (string.Equals(rawState, "Verified", StringComparison.OrdinalIgnoreCase))
        {
            return CapabilityConfidenceState.Verified;
        }

        if (string.Equals(rawState, "Experimental", StringComparison.OrdinalIgnoreCase))
        {
            return CapabilityConfidenceState.Experimental;
        }

        return CapabilityConfidenceState.Unknown;
    }

    private static RuntimeReasonCode ParseRuntimeReasonCode(string? rawCode)
    {
        return Enum.TryParse<RuntimeReasonCode>(rawCode, ignoreCase: true, out var parsed)
            ? parsed
            : RuntimeReasonCode.CAPABILITY_UNKNOWN;
    }

    private static int ReadContextInt(IReadOnlyDictionary<string, object?>? context, string key)
    {
        if (!TryReadContextValue(context, key, out var raw) || raw is null)
        {
            return 0;
        }

        if (raw is int intValue)
        {
            return intValue;
        }

        if (raw is long longValue && longValue >= int.MinValue && longValue <= int.MaxValue)
        {
            return (int)longValue;
        }

        return int.TryParse(raw.ToString(), out var parsed) ? parsed : 0;
    }

    private static string ReadContextString(IReadOnlyDictionary<string, object?>? context, string key)
    {
        if (!TryReadContextValue(context, key, out var raw) || raw is null)
        {
            return string.Empty;
        }

        return raw as string ?? raw.ToString() ?? string.Empty;
    }

    private static JsonObject ReadContextAnchors(IReadOnlyDictionary<string, object?>? context)
    {
        var resolved = new JsonObject();
        if (TryReadContextValue(context, "resolvedAnchors", out var anchorsRaw))
        {
            MergeAnchors(resolved, anchorsRaw);
        }

        if (resolved.Count == 0 && TryReadContextValue(context, "anchors", out var legacyAnchorsRaw))
        {
            MergeAnchors(resolved, legacyAnchorsRaw);
        }

        return resolved;
    }

    private static void MergeAnchors(JsonObject destination, object? rawAnchors)
    {
        if (rawAnchors is null)
        {
            return;
        }

        if (TryMergeJsonObjectAnchors(destination, rawAnchors) ||
            TryMergeJsonElementAnchors(destination, rawAnchors) ||
            TryMergeObjectDictionaryAnchors(destination, rawAnchors) ||
            TryMergeStringPairAnchors(destination, rawAnchors))
        {
            return;
        }

        TryMergeSerializedAnchors(destination, rawAnchors);
    }

    private static bool TryMergeJsonObjectAnchors(JsonObject destination, object rawAnchors)
    {
        if (rawAnchors is not JsonObject jsonObject)
        {
            return false;
        }

        foreach (var kv in jsonObject)
        {
            if (kv.Value is null)
            {
                continue;
            }

            destination[kv.Key] = kv.Value.ToString();
        }

        return true;
    }

    private static bool TryMergeJsonElementAnchors(JsonObject destination, object rawAnchors)
    {
        if (rawAnchors is not JsonElement element || element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            destination[property.Name] = property.Value.ToString();
        }

        return true;
    }

    private static bool TryMergeObjectDictionaryAnchors(JsonObject destination, object rawAnchors)
    {
        if (rawAnchors is not IReadOnlyDictionary<string, object?> dictionary)
        {
            return false;
        }

        foreach (var kv in dictionary)
        {
            if (kv.Value is null)
            {
                continue;
            }

            destination[kv.Key] = kv.Value.ToString();
        }

        return true;
    }

    private static bool TryMergeStringPairAnchors(JsonObject destination, object rawAnchors)
    {
        if (rawAnchors is not IEnumerable<KeyValuePair<string, string>> stringPairs)
        {
            return false;
        }

        foreach (var kv in stringPairs)
        {
            if (string.IsNullOrWhiteSpace(kv.Value))
            {
                continue;
            }

            destination[kv.Key] = kv.Value;
        }

        return true;
    }

    private static void TryMergeSerializedAnchors(JsonObject destination, object rawAnchors)
    {
        if (rawAnchors is not string serialized || string.IsNullOrWhiteSpace(serialized))
        {
            return;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(serialized, JsonOptions);
            if (parsed is null)
            {
                return;
            }

            foreach (var kv in parsed)
            {
                if (string.IsNullOrWhiteSpace(kv.Value))
                {
                    continue;
                }

                destination[kv.Key] = kv.Value;
            }
        }
        catch
        {
            // ignored
        }
    }

    private static bool TryReadContextValue(
        IReadOnlyDictionary<string, object?>? context,
        string key,
        out object? value)
    {
        value = null;
        if (context is null)
        {
            return false;
        }

        if (!context.TryGetValue(key, out var raw))
        {
            return false;
        }

        value = raw;
        return true;
    }
}
