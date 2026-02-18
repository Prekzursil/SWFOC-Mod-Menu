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
    public async Task ProbeCapabilitiesAsync_ShouldMarkSetCreditsVerified_WhenBridgeReportsCapability()
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
        var commandId = requestDoc.RootElement.GetProperty("commandId").GetString() ?? string.Empty;
        var response = JsonSerializer.Serialize(new
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
                    set_credits = new
                    {
                        available = true,
                        state = "Verified",
                        reasonCode = "CAPABILITY_PROBE_PASS"
                    }
                }
            }
        });
        await writer.WriteLineAsync(response.AsMemory(), cts.Token);
        var report = await reportTask;

        report.IsFeatureAvailable("set_credits").Should().BeTrue(
            $"probeReason={report.ProbeReasonCode} diagnostics={System.Text.Json.JsonSerializer.Serialize(report.Diagnostics)}");
        report.Capabilities["set_credits"].Confidence.Should().Be(CapabilityConfidenceState.Verified);
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
        var action = new ActionSpec(
            Id: "set_credits",
            Category: ActionCategory.Economy,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Sdk,
            PayloadSchema: new JsonObject(),
            VerifyReadback: true,
            CooldownMs: 0,
            Description: "set credits");
        var request = new ActionExecutionRequest(
            Action: action,
            Payload: new JsonObject
            {
                ["symbol"] = "credits",
                ["intValue"] = 1000000,
                ["forcePatchHook"] = true
            },
            ProfileId: "roe_3447786229_swfoc",
            RuntimeMode: RuntimeMode.Galactic);
        var capabilityReport = new CapabilityReport(
            "roe_3447786229_swfoc",
            DateTimeOffset.UtcNow,
            new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
            {
                ["set_credits"] = new BackendCapability(
                    "set_credits",
                    Available: true,
                    CapabilityConfidenceState.Verified,
                    RuntimeReasonCode.CAPABILITY_PROBE_PASS)
            },
            RuntimeReasonCode.CAPABILITY_PROBE_PASS);

        var waitTask = server.WaitForConnectionAsync(cts.Token);
        var executeTask = backend.ExecuteAsync(request, capabilityReport, cts.Token);
        await waitTask;

        using var reader = new StreamReader(server);
        await using var writer = new StreamWriter(server) { AutoFlush = true };
        var requestJson = await reader.ReadLineAsync(cts.Token) ?? string.Empty;
        using var requestDoc = JsonDocument.Parse(requestJson);
        var root = requestDoc.RootElement;
        var commandId = root.GetProperty("commandId").GetString() ?? string.Empty;
        var payload = root.GetProperty("payload");
        var forcePatchHook = payload.GetProperty("forcePatchHook").GetBoolean();
        var response = JsonSerializer.Serialize(new
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
        await writer.WriteLineAsync(response.AsMemory(), cts.Token);
        var result = await executeTask;

        result.Succeeded.Should().BeTrue($"message={result.Message} diagnostics={System.Text.Json.JsonSerializer.Serialize(result.Diagnostics)}");
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
