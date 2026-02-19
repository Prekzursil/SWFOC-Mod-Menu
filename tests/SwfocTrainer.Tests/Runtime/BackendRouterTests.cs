using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class BackendRouterTests
{
    [Fact]
    public void Resolve_ShouldFailClosed_WhenExtenderIsRequiredButCapabilityUnknownForMutation()
    {
        var router = new BackendRouter();
        var request = BuildRequest("set_credits", ExecutionKind.Memory);
        var profile = BuildProfile(backendPreference: "extender");
        var process = BuildProcess();
        var report = CapabilityReport.Unknown(profile.Id, RuntimeReasonCode.CAPABILITY_UNKNOWN);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeFalse();
        decision.Backend.Should().Be(ExecutionBackendKind.Extender);
        decision.ReasonCode.Should().Be(RuntimeReasonCode.SAFETY_FAIL_CLOSED);
    }

    [Fact]
    public void Resolve_ShouldRouteToExtender_WhenCapabilityIsAvailable()
    {
        var router = new BackendRouter();
        var request = BuildRequest("set_credits", ExecutionKind.Sdk);
        var profile = BuildProfile(backendPreference: "extender");
        var process = BuildProcess();
        var report = new CapabilityReport(
            profile.Id,
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

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeTrue();
        decision.Backend.Should().Be(ExecutionBackendKind.Extender);
        decision.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
    }

    [Fact]
    public void Resolve_ShouldBlockMutation_WhenRequiredCapabilityMissing()
    {
        var router = new BackendRouter();
        var request = BuildRequest("toggle_fog_reveal", ExecutionKind.Sdk);
        var profile = BuildProfile(backendPreference: "extender", requiredCapabilities: ["set_credits", "toggle_fog_reveal"]);
        var process = BuildProcess();
        var report = new CapabilityReport(
            profile.Id,
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

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeFalse();
        decision.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING);
    }

    [Fact]
    public void Resolve_ShouldKeepLegacyMemoryRoute_WhenAutoPreferenceAndExecutionKindMemory()
    {
        var router = new BackendRouter();
        var request = BuildRequest("set_credits", ExecutionKind.Memory);
        var profile = BuildProfile(backendPreference: "auto");
        var process = BuildProcess();
        var report = CapabilityReport.Unknown(profile.Id, RuntimeReasonCode.CAPABILITY_UNKNOWN);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeTrue();
        decision.Backend.Should().Be(ExecutionBackendKind.Memory);
    }

    [Fact]
    public void Resolve_ShouldBlockSdkCreditsMutation_WhenAutoPreferenceAndExtenderCapabilityUnknown()
    {
        var router = new BackendRouter();
        var request = BuildRequest("set_credits", ExecutionKind.Sdk);
        var profile = BuildProfile(backendPreference: "auto");
        var process = BuildProcess();
        var report = CapabilityReport.Unknown(profile.Id, RuntimeReasonCode.CAPABILITY_BACKEND_UNAVAILABLE);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeFalse();
        decision.Backend.Should().Be(ExecutionBackendKind.Extender);
        decision.ReasonCode.Should().Be(RuntimeReasonCode.SAFETY_MUTATION_BLOCKED);
    }

    [Fact]
    public void Resolve_ShouldNotBlockSetCredits_WhenUnrelatedRequiredCapabilitiesAreMissing()
    {
        var router = new BackendRouter();
        var request = BuildRequest("set_credits", ExecutionKind.Sdk);
        var profile = BuildProfile(
            backendPreference: "auto",
            requiredCapabilities: ["set_credits", "set_hero_state_helper", "toggle_roe_respawn_helper"]);
        var process = BuildProcess();
        var report = new CapabilityReport(
            profile.Id,
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

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeTrue();
        decision.Backend.Should().Be(ExecutionBackendKind.Extender);
        decision.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
    }

    private static ActionExecutionRequest BuildRequest(string actionId, ExecutionKind executionKind)
    {
        var action = new ActionSpec(
            Id: actionId,
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: executionKind,
            PayloadSchema: new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0,
            Description: "test");
        return new ActionExecutionRequest(action, new JsonObject(), "roe_3447786229_swfoc", RuntimeMode.Galactic);
    }

    private static TrainerProfile BuildProfile(string backendPreference, IReadOnlyList<string>? requiredCapabilities = null)
    {
        return new TrainerProfile(
            Id: "roe_3447786229_swfoc",
            DisplayName: "ROE",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: "3447786229",
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "test",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(),
            BackendPreference: backendPreference,
            RequiredCapabilities: requiredCapabilities,
            HostPreference: "starwarsg_preferred",
            ExperimentalFeatures: Array.Empty<string>());
    }

    private static ProcessMetadata BuildProcess()
    {
        return new ProcessMetadata(
            ProcessId: 1234,
            ProcessName: "StarWarsG.exe",
            ProcessPath: "C:/Games/Corruption/StarWarsG.exe",
            CommandLine: "StarWarsG.exe STEAMMOD=3447786229",
            ExeTarget: ExeTarget.Swfoc,
            Mode: RuntimeMode.Galactic,
            Metadata: new Dictionary<string, string>(),
            LaunchContext: null,
            HostRole: ProcessHostRole.GameHost,
            MainModuleSize: 321_000_000,
            WorkshopMatchCount: 1,
            SelectionScore: 1311.0d);
    }
}
