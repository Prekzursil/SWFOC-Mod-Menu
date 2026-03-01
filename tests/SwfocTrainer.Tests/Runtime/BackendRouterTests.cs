using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class BackendRouterTests
{
    [Theory]
    [InlineData("freeze_timer", ExecutionKind.Memory)]
    [InlineData("toggle_fog_reveal", ExecutionKind.Memory)]
    [InlineData("toggle_ai", ExecutionKind.Memory)]
    [InlineData("set_unit_cap", ExecutionKind.CodePatch)]
    [InlineData("toggle_instant_build_patch", ExecutionKind.CodePatch)]
    public void Resolve_ShouldKeepLegacyBackend_ForPromotedActions_WhenOverrideDisabled(
        string actionId,
        ExecutionKind executionKind)
    {
        using var _ = PromotedExtenderOverrideScope.Disable();
        var router = new BackendRouter();
        var request = BuildRequest(
            actionId,
            executionKind,
            context: new Dictionary<string, object?>
            {
                ["capabilityMapReasonCode"] = "CAPABILITY_PROBE_PASS",
                ["capabilityMapState"] = "Verified",
                ["capabilityDeclaredAvailable"] = true
            });
        var profile = BuildProfile(backendPreference: "auto");
        var process = BuildProcess();
        var report = new CapabilityReport(
            profile.Id,
            DateTimeOffset.UtcNow,
            new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
            {
                [actionId] = new BackendCapability(
                    actionId,
                    Available: true,
                    CapabilityConfidenceState.Verified,
                    RuntimeReasonCode.CAPABILITY_PROBE_PASS)
            },
            RuntimeReasonCode.CAPABILITY_PROBE_PASS);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeTrue();
        decision.Backend.Should().Be(ExecutionBackendKind.Memory);
        decision.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        decision.Diagnostics.Should().ContainKey("hybridExecution");
        decision.Diagnostics!["hybridExecution"].Should().Be(false);
        decision.Diagnostics.Should().ContainKey("promotedExtenderAction");
        decision.Diagnostics!["promotedExtenderAction"].Should().Be(false);
        decision.Diagnostics.Should().ContainKey("promotedExtenderOverrideEnabled");
        decision.Diagnostics!["promotedExtenderOverrideEnabled"].Should().Be(false);
        decision.Diagnostics.Should().ContainKey("promotedExtenderOverrideSource");
        decision.Diagnostics!["promotedExtenderOverrideSource"].Should().Be("default");
        decision.Diagnostics.Should().ContainKey("promotedExtenderApplied");
        decision.Diagnostics!["promotedExtenderApplied"].Should().Be(false);
        decision.Diagnostics.Should().ContainKey("capabilityMapReasonCode");
        decision.Diagnostics!["capabilityMapReasonCode"].Should().Be("CAPABILITY_PROBE_PASS");
        decision.Diagnostics.Should().ContainKey("capabilityMapState");
        decision.Diagnostics!["capabilityMapState"].Should().Be("Verified");
        decision.Diagnostics.Should().ContainKey("capabilityDeclaredAvailable");
        decision.Diagnostics!["capabilityDeclaredAvailable"].Should().Be(true);
    }

    [Theory]
    [InlineData("freeze_timer", ExecutionKind.Memory)]
    [InlineData("toggle_fog_reveal", ExecutionKind.Memory)]
    [InlineData("toggle_ai", ExecutionKind.Memory)]
    [InlineData("set_unit_cap", ExecutionKind.CodePatch)]
    [InlineData("toggle_instant_build_patch", ExecutionKind.CodePatch)]
    public void Resolve_ShouldKeepLegacyBackend_ForPromotedActions_WhenCapabilityIsMissing_AndOverrideDisabled(
        string actionId,
        ExecutionKind executionKind)
    {
        using var _ = PromotedExtenderOverrideScope.Disable();
        var router = new BackendRouter();
        var request = BuildRequest(actionId, executionKind);
        var profile = BuildProfile(backendPreference: "auto");
        var process = BuildProcess();
        var report = CapabilityReport.Unknown(profile.Id, RuntimeReasonCode.CAPABILITY_UNKNOWN);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeTrue();
        decision.Backend.Should().Be(ExecutionBackendKind.Memory);
        decision.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        decision.Diagnostics.Should().ContainKey("hybridExecution");
        decision.Diagnostics!["hybridExecution"].Should().Be(false);
        decision.Diagnostics.Should().ContainKey("promotedExtenderAction");
        decision.Diagnostics!["promotedExtenderAction"].Should().Be(false);
        decision.Diagnostics.Should().ContainKey("promotedExtenderOverrideEnabled");
        decision.Diagnostics!["promotedExtenderOverrideEnabled"].Should().Be(false);
        decision.Diagnostics.Should().ContainKey("promotedExtenderOverrideSource");
        decision.Diagnostics!["promotedExtenderOverrideSource"].Should().Be("default");
        decision.Diagnostics.Should().ContainKey("promotedExtenderApplied");
        decision.Diagnostics!["promotedExtenderApplied"].Should().Be(false);
        decision.Diagnostics.Should().NotContainKey("fallbackBackend");
    }

    [Theory]
    [InlineData("freeze_timer", ExecutionKind.Memory)]
    [InlineData("toggle_fog_reveal", ExecutionKind.Memory)]
    [InlineData("toggle_ai", ExecutionKind.Memory)]
    [InlineData("set_unit_cap", ExecutionKind.CodePatch)]
    [InlineData("toggle_instant_build_patch", ExecutionKind.CodePatch)]
    public void Resolve_ShouldKeepLegacyBackend_ForPromotedActions_WhenCapabilityIsUnverified_AndOverrideDisabled(
        string actionId,
        ExecutionKind executionKind)
    {
        using var _ = PromotedExtenderOverrideScope.Disable();
        var router = new BackendRouter();
        var request = BuildRequest(actionId, executionKind);
        var profile = BuildProfile(backendPreference: "auto");
        var process = BuildProcess();
        var report = new CapabilityReport(
            profile.Id,
            DateTimeOffset.UtcNow,
            new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
            {
                [actionId] = new BackendCapability(
                    actionId,
                    Available: true,
                    Confidence: CapabilityConfidenceState.Experimental,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_FEATURE_EXPERIMENTAL)
            },
            RuntimeReasonCode.CAPABILITY_PROBE_PASS);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeTrue();
        decision.Backend.Should().Be(ExecutionBackendKind.Memory);
        decision.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        decision.Diagnostics.Should().ContainKey("hybridExecution");
        decision.Diagnostics!["hybridExecution"].Should().Be(false);
        decision.Diagnostics.Should().ContainKey("promotedExtenderAction");
        decision.Diagnostics!["promotedExtenderAction"].Should().Be(false);
        decision.Diagnostics.Should().ContainKey("promotedExtenderOverrideEnabled");
        decision.Diagnostics!["promotedExtenderOverrideEnabled"].Should().Be(false);
        decision.Diagnostics.Should().ContainKey("promotedExtenderOverrideSource");
        decision.Diagnostics!["promotedExtenderOverrideSource"].Should().Be("default");
        decision.Diagnostics.Should().ContainKey("promotedExtenderApplied");
        decision.Diagnostics!["promotedExtenderApplied"].Should().Be(false);
        decision.Diagnostics.Should().NotContainKey("fallbackBackend");
    }

    [Theory]
    [InlineData("freeze_timer", ExecutionKind.Memory)]
    [InlineData("toggle_fog_reveal", ExecutionKind.Memory)]
    [InlineData("toggle_ai", ExecutionKind.Memory)]
    [InlineData("set_unit_cap", ExecutionKind.CodePatch)]
    [InlineData("toggle_instant_build_patch", ExecutionKind.CodePatch)]
    public void Resolve_ShouldPromoteActions_ToExtender_WhenOverrideEnabled_AndCapabilityIsVerified(
        string actionId,
        ExecutionKind executionKind)
    {
        using var _ = PromotedExtenderOverrideScope.Enable();
        var router = new BackendRouter();
        var request = BuildRequest(actionId, executionKind);
        var profile = BuildProfile(backendPreference: "auto");
        var process = BuildProcess();
        var report = new CapabilityReport(
            profile.Id,
            DateTimeOffset.UtcNow,
            new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
            {
                [actionId] = new BackendCapability(
                    actionId,
                    Available: true,
                    CapabilityConfidenceState.Verified,
                    RuntimeReasonCode.CAPABILITY_PROBE_PASS)
            },
            RuntimeReasonCode.CAPABILITY_PROBE_PASS);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeTrue();
        decision.Backend.Should().Be(ExecutionBackendKind.Extender);
        decision.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        decision.Diagnostics.Should().ContainKey("promotedExtenderAction");
        decision.Diagnostics!["promotedExtenderAction"].Should().Be(true);
        decision.Diagnostics.Should().ContainKey("promotedExtenderOverrideEnabled");
        decision.Diagnostics!["promotedExtenderOverrideEnabled"].Should().Be(true);
        decision.Diagnostics.Should().ContainKey("promotedExtenderOverrideSource");
        decision.Diagnostics!["promotedExtenderOverrideSource"].Should().Be("env");
        decision.Diagnostics.Should().ContainKey("promotedExtenderApplied");
        decision.Diagnostics!["promotedExtenderApplied"].Should().Be(true);
    }

    [Theory]
    [InlineData("freeze_timer", ExecutionKind.Memory)]
    [InlineData("toggle_fog_reveal", ExecutionKind.Memory)]
    [InlineData("toggle_ai", ExecutionKind.Memory)]
    [InlineData("set_unit_cap", ExecutionKind.CodePatch)]
    [InlineData("toggle_instant_build_patch", ExecutionKind.CodePatch)]
    public void Resolve_ShouldBlockPromotedActions_WhenOverrideEnabled_AndCapabilityIsMissing(
        string actionId,
        ExecutionKind executionKind)
    {
        using var _ = PromotedExtenderOverrideScope.Enable();
        var router = new BackendRouter();
        var request = BuildRequest(actionId, executionKind);
        var profile = BuildProfile(backendPreference: "auto");
        var process = BuildProcess();
        var report = CapabilityReport.Unknown(profile.Id, RuntimeReasonCode.CAPABILITY_UNKNOWN);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeFalse();
        decision.Backend.Should().Be(ExecutionBackendKind.Extender);
        decision.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING);
        decision.Diagnostics.Should().ContainKey("promotedExtenderAction");
        decision.Diagnostics!["promotedExtenderAction"].Should().Be(true);
        decision.Diagnostics.Should().ContainKey("promotedExtenderApplied");
        decision.Diagnostics!["promotedExtenderApplied"].Should().Be(true);
    }

    [Theory]
    [InlineData("freeze_timer", ExecutionKind.Memory)]
    [InlineData("toggle_fog_reveal", ExecutionKind.Memory)]
    [InlineData("toggle_ai", ExecutionKind.Memory)]
    [InlineData("set_unit_cap", ExecutionKind.CodePatch)]
    [InlineData("toggle_instant_build_patch", ExecutionKind.CodePatch)]
    public void Resolve_ShouldFailClosedForPromotedActions_WhenOverrideEnabled_AndCapabilityIsUnverified(
        string actionId,
        ExecutionKind executionKind)
    {
        using var _ = PromotedExtenderOverrideScope.Enable();
        var router = new BackendRouter();
        var request = BuildRequest(actionId, executionKind);
        var profile = BuildProfile(backendPreference: "auto");
        var process = BuildProcess();
        var report = new CapabilityReport(
            profile.Id,
            DateTimeOffset.UtcNow,
            new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
            {
                [actionId] = new BackendCapability(
                    actionId,
                    Available: true,
                    Confidence: CapabilityConfidenceState.Experimental,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_FEATURE_EXPERIMENTAL)
            },
            RuntimeReasonCode.CAPABILITY_PROBE_PASS);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeFalse();
        decision.Backend.Should().Be(ExecutionBackendKind.Extender);
        decision.ReasonCode.Should().Be(RuntimeReasonCode.SAFETY_FAIL_CLOSED);
        decision.Diagnostics.Should().ContainKey("promotedExtenderApplied");
        decision.Diagnostics!["promotedExtenderApplied"].Should().Be(true);
    }

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
    public void Resolve_ShouldKeepLegacyMemoryRoute_ForHybridActionId_WhenProfileTargetsSweaw()
    {
        var router = new BackendRouter();
        var request = BuildRequest("freeze_timer", ExecutionKind.Memory, profileId: "base_sweaw");
        var profile = BuildProfile(
            backendPreference: "auto",
            id: "base_sweaw",
            displayName: "Base EAW",
            exeTarget: ExeTarget.Sweaw);
        var process = BuildProcess(
            exeTarget: ExeTarget.Sweaw,
            processName: "sweaw.exe",
            processPath: "C:/Games/EmpireAtWar/sweaw.exe",
            commandLine: "sweaw.exe");
        var report = CapabilityReport.Unknown(profile.Id, RuntimeReasonCode.CAPABILITY_UNKNOWN);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeTrue();
        decision.Backend.Should().Be(ExecutionBackendKind.Memory);
        decision.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        decision.Diagnostics.Should().ContainKey("hybridExecution");
        decision.Diagnostics!["hybridExecution"].Should().Be(false);
        decision.Diagnostics.Should().ContainKey("promotedExtenderAction");
        decision.Diagnostics!["promotedExtenderAction"].Should().Be(false);
    }

    [Fact]
    public void Resolve_ShouldKeepFallbackPatchActions_OnMemoryRoute_ForFoCProfiles()
    {
        var router = new BackendRouter();
        var request = BuildRequest("set_unit_cap_patch_fallback", ExecutionKind.CodePatch);
        var profile = BuildProfile(backendPreference: "auto");
        var process = BuildProcess();
        var report = CapabilityReport.Unknown(profile.Id, RuntimeReasonCode.CAPABILITY_UNKNOWN);

        var decision = router.Resolve(request, profile, process, report);

        decision.Allowed.Should().BeTrue();
        decision.Backend.Should().Be(ExecutionBackendKind.Memory);
        decision.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        decision.Diagnostics.Should().ContainKey("promotedExtenderAction");
        decision.Diagnostics!["promotedExtenderAction"].Should().Be(false);
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

    private static ActionExecutionRequest BuildRequest(
        string actionId,
        ExecutionKind executionKind,
        string profileId = "roe_3447786229_swfoc",
        IReadOnlyDictionary<string, object?>? context = null)
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
        return new ActionExecutionRequest(action, new JsonObject(), profileId, RuntimeMode.Galactic, context);
    }

    private static TrainerProfile BuildProfile(
        string backendPreference,
        IReadOnlyList<string>? requiredCapabilities = null,
        string id = "roe_3447786229_swfoc",
        string displayName = "ROE",
        ExeTarget exeTarget = ExeTarget.Swfoc)
    {
        return new TrainerProfile(
            Id: id,
            DisplayName: displayName,
            Inherits: null,
            ExeTarget: exeTarget,
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

    private static ProcessMetadata BuildProcess(
        ExeTarget exeTarget = ExeTarget.Swfoc,
        string processName = "StarWarsG.exe",
        string processPath = "C:/Games/Corruption/StarWarsG.exe",
        string commandLine = "StarWarsG.exe STEAMMOD=3447786229")
    {
        return new ProcessMetadata(
            ProcessId: 1234,
            ProcessName: processName,
            ProcessPath: processPath,
            CommandLine: commandLine,
            ExeTarget: exeTarget,
            Mode: RuntimeMode.Galactic,
            Metadata: new Dictionary<string, string>(),
            LaunchContext: null,
            HostRole: ProcessHostRole.GameHost,
            MainModuleSize: 321_000_000,
            WorkshopMatchCount: 1,
            SelectionScore: 1311.0d);
    }

    private sealed class PromotedExtenderOverrideScope : IDisposable
    {
        private const string VariableName = "SWFOC_FORCE_PROMOTED_EXTENDER";
        private readonly string? _previousValue;

        private PromotedExtenderOverrideScope(string? value)
        {
            _previousValue = Environment.GetEnvironmentVariable(VariableName);
            Environment.SetEnvironmentVariable(VariableName, value);
        }

        public static PromotedExtenderOverrideScope Disable()
        {
            return new PromotedExtenderOverrideScope(value: null);
        }

        public static PromotedExtenderOverrideScope Enable()
        {
            return new PromotedExtenderOverrideScope(value: "1");
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(VariableName, _previousValue);
        }
    }
}
