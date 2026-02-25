using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed partial class NamedPipeExtenderBackend : IExecutionBackend
{
    private const int DefaultConnectTimeoutMs = 2000;
    private const int DefaultResponseTimeoutMs = 2000;
    private const string DefaultPipeName = "SwfocExtenderBridge";
    private const string ExtenderBackendId = "extender";
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
