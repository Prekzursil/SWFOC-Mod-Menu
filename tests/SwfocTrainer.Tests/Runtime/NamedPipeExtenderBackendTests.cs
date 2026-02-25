using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class NamedPipeExtenderBackendTests
{
    [Fact]
    public async Task GetHealthAsync_ShouldReturnUnavailable_WhenBridgeIsNotRunning()
    {
        var backend = new NamedPipeExtenderBackend();

        var health = await backend.GetHealthAsync();

        health.Backend.Should().Be(ExecutionBackendKind.Extender);
        health.IsHealthy.Should().BeFalse();
        health.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE);
    }

    [Fact]
    public async Task ProbeCapabilitiesAsync_ShouldParseAllPromotedAndCreditsCapabilities_WhenBridgeReportsMultiFeatureProbe()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            "SwfocExtenderBridge",
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend();
        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var reportTask = backend.ProbeCapabilitiesAsync(
            "roe_3447786229_swfoc",
            BuildProcess(),
            cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var requestDoc = JsonDocument.Parse(requestJson);
        requestDoc.RootElement.GetProperty("processId").GetInt32().Should().Be(4242);
        requestDoc.RootElement.GetProperty("processName").GetString().Should().Be("StarWarsG.exe");
        var commandId = requestDoc.RootElement.GetProperty("commandId").GetString() ?? string.Empty;
        var response = BuildProbeResponse(commandId);
        await writer.WriteLineAsync(response.AsMemory(), cts.Token);
        var report = await reportTask;

        AssertProbeReport(report);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldPassLegacyForcePatchHookPayload_ToExtenderServer()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            "SwfocExtenderBridge",
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend();
        var request = BuildSetCreditsRequest();
        var capabilityReport = BuildCapabilityReport(request.ProfileId, request.Action.Id);

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var executeTask = backend.ExecuteAsync(request, capabilityReport, cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var (commandId, forcePatchHook) = await ReadCommandEnvelopeAsync(reader, cts.Token);
        var response = BuildExecuteResponse(commandId, forcePatchHook);
        await writer.WriteLineAsync(response.AsMemory(), cts.Token);
        var result = await executeTask;

        AssertHookLockResult(result);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSendProcessContextAndResolvedAnchors_FromRequestContext()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        await using var server = new NamedPipeServerStream(
            "SwfocExtenderBridge",
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var backend = new NamedPipeExtenderBackend();
        var request = BuildFreezeTimerRequestWithContext();
        var capabilityReport = BuildCapabilityReport(request.ProfileId, request.Action.Id);

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var executeTask = backend.ExecuteAsync(request, capabilityReport, cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var requestDoc = JsonDocument.Parse(requestJson);
        var root = requestDoc.RootElement;
        var commandId = root.GetProperty("commandId").GetString() ?? string.Empty;

        root.GetProperty("processId").GetInt32().Should().Be(4242);
        root.GetProperty("processName").GetString().Should().Be("StarWarsG.exe");
        var anchors = root.GetProperty("resolvedAnchors");
        anchors.GetProperty("freeze_timer").GetString().Should().Be("0x1234ABCD");
        anchors.GetProperty("game_timer_freeze").GetString().Should().Be("0x1234ABCD");

        var response = BuildExecuteResponse(commandId, forcePatchHook: false);
        await writer.WriteLineAsync(response.AsMemory(), cts.Token);
        var result = await executeTask;

        result.Succeeded.Should().BeTrue();
    }

    private static ActionExecutionRequest BuildSetCreditsRequest()
    {
        var action = new ActionSpec(
            Id: "set_credits",
            Category: ActionCategory.Economy,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Sdk,
            PayloadSchema: new JsonObject(),
            VerifyReadback: true,
            CooldownMs: 0,
            Description: "set credits");
        return new ActionExecutionRequest(
            Action: action,
            Payload: new JsonObject
            {
                ["symbol"] = "credits",
                ["intValue"] = 1000000,
                ["forcePatchHook"] = true
            },
            ProfileId: "roe_3447786229_swfoc",
            RuntimeMode: RuntimeMode.Galactic);
    }

    private static ActionExecutionRequest BuildFreezeTimerRequestWithContext()
    {
        var action = new ActionSpec(
            Id: "freeze_timer",
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Sdk,
            PayloadSchema: new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0,
            Description: "freeze timer");
        return new ActionExecutionRequest(
            Action: action,
            Payload: new JsonObject
            {
                ["symbol"] = "game_timer_freeze",
                ["boolValue"] = true
            },
            ProfileId: "roe_3447786229_swfoc",
            RuntimeMode: RuntimeMode.Galactic,
            Context: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["processId"] = 4242,
                ["processName"] = "StarWarsG.exe",
                ["resolvedAnchors"] = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
                {
                    ["freeze_timer"] = "0x1234ABCD",
                    ["game_timer_freeze"] = "0x1234ABCD"
                }
            });
    }

    private static CapabilityReport BuildCapabilityReport(string profileId, string featureId)
    {
        return new CapabilityReport(
            profileId,
            DateTimeOffset.UtcNow,
            new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
            {
                [featureId] = new BackendCapability(
                    featureId,
                    Available: true,
                    CapabilityConfidenceState.Verified,
                    RuntimeReasonCode.CAPABILITY_PROBE_PASS)
            },
            RuntimeReasonCode.CAPABILITY_PROBE_PASS);
    }

    private static async Task<(string CommandId, bool ForcePatchHook)> ReadCommandEnvelopeAsync(
        StreamReader reader,
        CancellationToken cancellationToken)
    {
        var requestJson = await reader.ReadLineAsync(cancellationToken) ?? string.Empty;
        using var requestDoc = JsonDocument.Parse(requestJson);
        var root = requestDoc.RootElement;
        var commandId = root.GetProperty("commandId").GetString() ?? string.Empty;
        var forcePatchHook = root.GetProperty("payload").GetProperty("forcePatchHook").GetBoolean();
        return (commandId, forcePatchHook);
    }

    private static string BuildExecuteResponse(string commandId, bool forcePatchHook)
    {
        return JsonSerializer.Serialize(new
        {
            commandId,
            succeeded = true,
            reasonCode = "CAPABILITY_PROBE_PASS",
            backend = "extender",
            hookState = forcePatchHook ? "HOOK_LOCK" : "HOOK_ONESHOT",
            message = "Executed",
            diagnostics = new
            {
                forcePatchHook = forcePatchHook.ToString().ToLowerInvariant()
            }
        });
    }

    private static string BuildProbeResponse(string commandId)
    {
        return JsonSerializer.Serialize(new
        {
            commandId,
            succeeded = true,
            reasonCode = "CAPABILITY_PROBE_PASS",
            backend = "extender",
            hookState = "HOOK_READY",
            message = "Probe completed.",
            diagnostics = new
            {
                capabilities = new
                {
                    freeze_timer = new { available = true, state = "Verified", reasonCode = "CAPABILITY_PROBE_PASS" },
                    toggle_fog_reveal = new { available = true, state = "Verified", reasonCode = "CAPABILITY_PROBE_PASS" },
                    toggle_ai = new { available = false, state = "Unknown", reasonCode = "CAPABILITY_REQUIRED_MISSING" },
                    set_unit_cap = new { available = true, state = "Experimental", reasonCode = "CAPABILITY_FEATURE_EXPERIMENTAL" },
                    toggle_instant_build_patch = new { available = false, state = "Unknown", reasonCode = "CAPABILITY_REQUIRED_MISSING" },
                    set_credits = new { available = true, state = "Verified", reasonCode = "CAPABILITY_PROBE_PASS" }
                }
            }
        });
    }

    private static void AssertProbeReport(CapabilityReport report)
    {
        report.Capabilities.Should().ContainKey("freeze_timer");
        report.Capabilities.Should().ContainKey("toggle_fog_reveal");
        report.Capabilities.Should().ContainKey("toggle_ai");
        report.Capabilities.Should().ContainKey("set_unit_cap");
        report.Capabilities.Should().ContainKey("toggle_instant_build_patch");
        report.Capabilities.Should().ContainKey("set_credits");
        report.IsFeatureAvailable("set_credits").Should().BeTrue(
            $"probeReason={report.ProbeReasonCode} diagnostics={System.Text.Json.JsonSerializer.Serialize(report.Diagnostics)}");
        report.Capabilities["set_unit_cap"].Confidence.Should().Be(CapabilityConfidenceState.Experimental);
        report.Capabilities["set_credits"].Confidence.Should().Be(CapabilityConfidenceState.Verified);
    }

    private static void AssertHookLockResult(ActionExecutionResult result)
    {
        result.Succeeded.Should().BeTrue(
            $"message={result.Message} diagnostics={System.Text.Json.JsonSerializer.Serialize(result.Diagnostics)}");
        result.Diagnostics.Should().ContainKey("hookState");
        result.Diagnostics!["hookState"]!.ToString().Should().Be("HOOK_LOCK");
    }

    private static ProcessMetadata BuildProcess()
    {
        return new ProcessMetadata(
            ProcessId: 4242,
            ProcessName: "StarWarsG.exe",
            ProcessPath: @"C:\Games\Corruption\StarWarsG.exe",
            CommandLine: "StarWarsG.exe STEAMMOD=3447786229",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic,
            Metadata: new Dictionary<string, string>(),
            LaunchContext: null,
            HostRole: ProcessHostRole.GameHost,
            MainModuleSize: 123,
            WorkshopMatchCount: 1,
            SelectionScore: 1001d);
    }

}
