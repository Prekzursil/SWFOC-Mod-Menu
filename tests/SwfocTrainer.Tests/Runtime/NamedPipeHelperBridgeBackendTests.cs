using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class NamedPipeHelperBridgeBackendTests
{
    [Fact]
    public async Task ProbeAsync_ShouldFailClosed_WhenProcessIsMissing()
    {
        var backend = new NamedPipeHelperBridgeBackend(new StubExecutionBackend());
        var process = BuildProcess(processId: 0);

        var result = await backend.ProbeAsync(
            new HelperBridgeProbeRequest("test_profile", process, Array.Empty<HelperHookSpec>()),
            CancellationToken.None);

        result.Available.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE);
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailVerification_WhenVerifyContractIsNotSatisfied()
    {
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = BuildHelperProbeReport(),
            ExecuteResult = new ActionExecutionResult(
                Succeeded: true,
                Message: "helper command accepted",
                AddressSource: AddressSource.None,
                Diagnostics: new Dictionary<string, object?>())
        };
        var backend = new NamedPipeHelperBridgeBackend(stubBackend);
        var request = BuildHelperRequest(
            payload: new JsonObject { ["globalKey"] = "AOTR_HERO_KEY", ["intValue"] = 1 },
            hook: new HelperHookSpec(
                Id: "aotr_hero_state_bridge",
                Script: "scripts/aotr/hero_state_bridge.lua",
                Version: "1.0.0",
                EntryPoint: "SWFOC_Trainer_Set_Hero_Respawn",
                VerifyContract: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["helperVerifyState"] = "applied",
                    ["globalKey"] = "required:echo"
                }));

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_VERIFICATION_FAILED);
        result.Diagnostics.Should().NotBeNull();
        var diagnostics = result.Diagnostics!;
        diagnostics["helperVerifyState"]?.ToString().Should().Be("failed_contract");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnApplied_WhenVerifyContractIsSatisfied()
    {
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = BuildHelperProbeReport(),
            ExecuteResult = new ActionExecutionResult(
                Succeeded: true,
                Message: "helper command applied",
                AddressSource: AddressSource.None,
                Diagnostics: new Dictionary<string, object?>
                {
                    ["globalKey"] = "AOTR_HERO_KEY",
                    ["helperVerifyState"] = "applied"
                })
        };
        var backend = new NamedPipeHelperBridgeBackend(stubBackend);
        var request = BuildHelperRequest(
            payload: new JsonObject { ["globalKey"] = "AOTR_HERO_KEY", ["intValue"] = 1 },
            hook: new HelperHookSpec(
                Id: "aotr_hero_state_bridge",
                Script: "scripts/aotr/hero_state_bridge.lua",
                Version: "1.0.0",
                EntryPoint: "SWFOC_Trainer_Set_Hero_Respawn",
                VerifyContract: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["helperVerifyState"] = "applied",
                    ["globalKey"] = "required:echo"
                }));

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_EXECUTION_APPLIED);
        result.Diagnostics.Should().NotBeNull();
        var diagnostics = result.Diagnostics!;
        diagnostics["helperVerifyState"]?.ToString().Should().Be("applied");
    }

    private static CapabilityReport BuildHelperProbeReport()
    {
        return new CapabilityReport(
            ProfileId: "test_profile",
            ProbedAtUtc: DateTimeOffset.UtcNow,
            Capabilities: new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
            {
                ["set_hero_state_helper"] = new BackendCapability(
                    FeatureId: "set_hero_state_helper",
                    Available: true,
                    Confidence: CapabilityConfidenceState.Verified,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS)
            },
            ProbeReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS);
    }

    private static HelperBridgeRequest BuildHelperRequest(JsonObject payload, HelperHookSpec hook)
    {
        var action = new ActionSpec(
            Id: "set_hero_state_helper",
            Category: ActionCategory.Hero,
            Mode: RuntimeMode.Galactic,
            ExecutionKind: ExecutionKind.Helper,
            PayloadSchema: new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0);

        var actionRequest = new ActionExecutionRequest(
            Action: action,
            Payload: payload,
            ProfileId: "test_profile",
            RuntimeMode: RuntimeMode.Galactic);

        return new HelperBridgeRequest(
            ActionRequest: actionRequest,
            Process: BuildProcess(processId: 4242),
            Hook: hook,
            Context: null);
    }

    private static ProcessMetadata BuildProcess(int processId)
    {
        return new ProcessMetadata(
            ProcessId: processId,
            ProcessName: "StarWarsG.exe",
            ProcessPath: @"C:\Games\StarWarsG.exe",
            CommandLine: "STEAMMOD=1397421866",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic);
    }

    private sealed class StubExecutionBackend : IExecutionBackend
    {
        public ExecutionBackendKind BackendKind => ExecutionBackendKind.Extender;

        public CapabilityReport ProbeReport { get; init; } = CapabilityReport.Unknown("test_profile");

        public ActionExecutionResult ExecuteResult { get; init; } = new(
            Succeeded: false,
            Message: "stub",
            AddressSource: AddressSource.None);

        public Task<CapabilityReport> ProbeCapabilitiesAsync(string profileId, ProcessMetadata processContext)
            => Task.FromResult(ProbeReport);

        public Task<CapabilityReport> ProbeCapabilitiesAsync(string profileId, ProcessMetadata processContext, CancellationToken cancellationToken)
            => Task.FromResult(ProbeReport);

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest command, CapabilityReport capabilityReport)
            => Task.FromResult(ExecuteResult);

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest command, CapabilityReport capabilityReport, CancellationToken cancellationToken)
            => Task.FromResult(ExecuteResult);

        public Task<BackendHealth> GetHealthAsync()
            => Task.FromResult(new BackendHealth(
                BackendId: "stub",
                Backend: BackendKind,
                IsHealthy: true,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok"));

        public Task<BackendHealth> GetHealthAsync(CancellationToken cancellationToken)
            => GetHealthAsync();
    }
}
