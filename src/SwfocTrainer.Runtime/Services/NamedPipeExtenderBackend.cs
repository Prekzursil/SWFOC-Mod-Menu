using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
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
    private static readonly object HostSync = new();
    private static Process? _bridgeHostProcess;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public ExecutionBackendKind BackendKind => ExecutionBackendKind.Extender;

    public async Task<CapabilityReport> ProbeCapabilitiesAsync(
        string profileId,
        ProcessMetadata processContext,
        CancellationToken cancellationToken = default)
    {
        var pingCommand = new ExtenderCommand(
            CommandId: Guid.NewGuid().ToString("N"),
            FeatureId: "probe_capabilities",
            ProfileId: profileId,
            Mode: processContext.Mode,
            Payload: new System.Text.Json.Nodes.JsonObject(),
            RequestedBy: "runtime_adapter",
            TimestampUtc: DateTimeOffset.UtcNow);

        var result = await SendAsync(pingCommand, cancellationToken);
        if (!result.Succeeded)
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

    public async Task<ActionExecutionResult> ExecuteAsync(
        ActionExecutionRequest command,
        CapabilityReport capabilityReport,
        CancellationToken cancellationToken = default)
    {
        var extenderCommand = new ExtenderCommand(
            CommandId: Guid.NewGuid().ToString("N"),
            FeatureId: command.Action.Id,
            ProfileId: command.ProfileId,
            Mode: command.RuntimeMode,
            Payload: command.Payload,
            RequestedBy: "external_app",
            TimestampUtc: DateTimeOffset.UtcNow);

        var result = await SendAsync(extenderCommand, cancellationToken);
        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["reasonCode"] = result.ReasonCode.ToString(),
            ["backend"] = result.Backend,
            ["hookState"] = result.HookState,
            ["extenderCommandId"] = result.CommandId,
            ["probeReasonCode"] = capabilityReport.ProbeReasonCode.ToString()
        };

        if (result.Diagnostics is not null)
        {
            foreach (var kv in result.Diagnostics)
            {
                diagnostics[kv.Key] = kv.Value;
            }
        }

        return new ActionExecutionResult(
            result.Succeeded,
            result.Message,
            AddressSource.None,
            diagnostics);
    }

    public async Task<BackendHealth> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        var probe = await SendAsync(new ExtenderCommand(
            CommandId: Guid.NewGuid().ToString("N"),
            FeatureId: "health",
            ProfileId: "unknown",
            Mode: RuntimeMode.Unknown,
            Payload: new System.Text.Json.Nodes.JsonObject(),
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
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(DefaultResponseTimeoutMs + DefaultConnectTimeoutMs + 100);

            using var client = new NamedPipeClientStream(
                serverName: ".",
                pipeName: DefaultPipeName,
                direction: PipeDirection.InOut,
                options: PipeOptions.Asynchronous);

            await client.ConnectAsync(DefaultConnectTimeoutMs, timeoutCts.Token);

            using var writer = new StreamWriter(client, new UTF8Encoding(false), leaveOpen: true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(client, Encoding.UTF8, leaveOpen: true);

            var payload = JsonSerializer.Serialize(command, JsonOptions);
            await writer.WriteLineAsync(payload.AsMemory(), timeoutCts.Token);

            var readTask = reader.ReadLineAsync(timeoutCts.Token).AsTask();
            var line = await readTask;
            if (string.IsNullOrWhiteSpace(line))
            {
                return new ExtenderResult(
                    CommandId: command.CommandId,
                    Succeeded: false,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE,
                    Backend: ExtenderBackendId,
                    HookState: "no_response",
                    Message: "Extender pipe did not return a response.");
            }

            var result = JsonSerializer.Deserialize<ExtenderResult>(line, JsonOptions);
            return result ?? new ExtenderResult(
                CommandId: command.CommandId,
                Succeeded: false,
                ReasonCode: RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE,
                Backend: ExtenderBackendId,
                HookState: "invalid_response",
                Message: "Extender response payload was invalid.");
        }
        catch (OperationCanceledException)
        {
            return new ExtenderResult(
                CommandId: command.CommandId,
                Succeeded: false,
                ReasonCode: RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE,
                Backend: ExtenderBackendId,
                HookState: "timeout",
                Message: "Extender pipe request timed out.");
        }
        catch (Exception ex)
        {
            return new ExtenderResult(
                CommandId: command.CommandId,
                Succeeded: false,
                ReasonCode: RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE,
                Backend: ExtenderBackendId,
                HookState: "unreachable",
                Message: $"Extender pipe unavailable: {ex.Message}");
        }
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

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "native", "runtime", "SwfocExtender.Host.exe"),
            Path.Combine(AppContext.BaseDirectory, "native", "build-win", "SwfocExtender.Host.exe"),
            Path.Combine(AppContext.BaseDirectory, "native", "build-wsl", "SwfocExtender.Host")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static Dictionary<string, BackendCapability> ParseCapabilities(
        IReadOnlyDictionary<string, object?>? diagnostics)
    {
        var capabilities = new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase);
        if (!TryGetCapabilitiesElement(diagnostics, out var element))
        {
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

        return capabilities;
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
}
