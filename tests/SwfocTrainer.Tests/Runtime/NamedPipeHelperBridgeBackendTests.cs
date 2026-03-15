using System.Text.Json.Nodes;
using System.Reflection;
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
    public async Task ProbeAsync_ShouldFailClosed_WhenNoHelperFeaturesAreAvailable()
    {
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = CapabilityReport.Unknown("test_profile")
        };
        var backend = new NamedPipeHelperBridgeBackend(stubBackend);

        var result = await backend.ProbeAsync(
            new HelperBridgeProbeRequest("test_profile", BuildProcess(processId: 4242), Array.Empty<HelperHookSpec>()),
            CancellationToken.None);

        result.Available.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE);
        result.Diagnostics.Should().NotBeNull();
        var diagnostics = result.Diagnostics!;
        diagnostics["helperBridgeState"]?.ToString().Should().Be("unavailable");
        diagnostics["probeReasonCode"]?.ToString().Should().Be(RuntimeReasonCode.CAPABILITY_UNKNOWN.ToString());
    }

    [Fact]
    public async Task ProbeAsync_ShouldReturnReady_WhenAnyHelperFeatureIsAvailable()
    {
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = new CapabilityReport(
                ProfileId: "test_profile",
                ProbedAtUtc: DateTimeOffset.UtcNow,
                Capabilities: new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
                {
                    ["spawn_context_entity"] = new BackendCapability(
                        FeatureId: "spawn_context_entity",
                        Available: true,
                        Confidence: CapabilityConfidenceState.Verified,
                        ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS),
                    ["switch_player_faction"] = new BackendCapability(
                        FeatureId: "switch_player_faction",
                        Available: false,
                        Confidence: CapabilityConfidenceState.Unknown,
                        ReasonCode: RuntimeReasonCode.CAPABILITY_UNKNOWN)
                },
                ProbeReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS)
        };
        var backend = new NamedPipeHelperBridgeBackend(stubBackend);

        var result = await backend.ProbeAsync(
            new HelperBridgeProbeRequest("test_profile", BuildProcess(processId: 4242), Array.Empty<HelperHookSpec>()),
            CancellationToken.None);

        result.Available.Should().BeTrue();
        result.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_PROBE_PASS);
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics!["helperBridgeState"]?.ToString().Should().Be("ready");
        result.Diagnostics["availableFeatures"]?.ToString().Should().Be("spawn_context_entity");
        result.Diagnostics["capabilityCount"]?.ToString().Should().Be("2");
    }

    [Fact]
    public async Task ProbeAsync_ShouldReturnExperimentalDiagnostics_WhenHooksExistButNativeDispatchIsUnavailable()
    {
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = new CapabilityReport(
                ProfileId: "test_profile",
                ProbedAtUtc: DateTimeOffset.UtcNow,
                Capabilities: new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
                {
                    ["set_credits"] = new BackendCapability(
                        FeatureId: "set_credits",
                        Available: true,
                        Confidence: CapabilityConfidenceState.Verified,
                        ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS)
                },
                ProbeReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS)
        };
        var backend = new NamedPipeHelperBridgeBackend(stubBackend);

        var result = await backend.ProbeAsync(
            new HelperBridgeProbeRequest(
                "test_profile",
                BuildProcess(processId: 4242),
                new[]
                {
                    new HelperHookSpec(
                        Id: "spawn_bridge",
                        Script: "scripts/common/spawn_bridge.lua",
                        Version: "1.0.0",
                        EntryPoint: "SWFOC_Trainer_Spawn_Context")
                }),
            CancellationToken.None);

        result.Available.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_VERIFICATION_FAILED);
        result.Diagnostics.Should().NotBeNull();
        var diagnostics = result.Diagnostics!;
        diagnostics["helperBridgeState"]?.ToString().Should().Be("experimental");
        diagnostics["configuredHooks"]?.ToString().Should().Be("spawn_bridge");
        diagnostics["configuredEntryPoints"]?.ToString().Should().Be("SWFOC_Trainer_Spawn_Context");
        diagnostics["helperExecutionPath"]?.ToString().Should().Be("native_dispatch_unavailable");
    }

    [Fact]
    public async Task ProbeAsync_ShouldIncludeAutoloadEvidence_WhenTelemetryReportsHelperAutoloadReady()
    {
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = new CapabilityReport(
                ProfileId: "aotr_1397421866_swfoc",
                ProbedAtUtc: DateTimeOffset.UtcNow,
                Capabilities: new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase)
                {
                    ["set_credits"] = new BackendCapability(
                        FeatureId: "set_credits",
                        Available: true,
                        Confidence: CapabilityConfidenceState.Verified,
                        ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS)
                },
                ProbeReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS)
        };
        var telemetry = new StubTelemetryLogTailService
        {
            AutoloadResult = new HelperAutoloadVerification(
                Ready: true,
                ReasonCode: "helper_autoload_ready",
                SourcePath: @"C:\Games\_LogFile.txt",
                TimestampUtc: DateTimeOffset.UtcNow,
                RawLine: "SWFOC_TRAINER_HELPER_AUTOLOAD_READY profile=aotr_1397421866_swfoc strategy=story_wrapper_chain script=Library/PGStoryMode.lua",
                Strategy: "story_wrapper_chain",
                Script: "Library/PGStoryMode.lua")
        };
        var backend = new NamedPipeHelperBridgeBackend(stubBackend, telemetry);

        var result = await backend.ProbeAsync(
            new HelperBridgeProbeRequest(
                "aotr_1397421866_swfoc",
                BuildProcess(processId: 4242),
                new[]
                {
                    new HelperHookSpec(
                        Id: "spawn_bridge",
                        Script: "scripts/common/spawn_bridge.lua",
                        Version: "1.0.0",
                        EntryPoint: "SWFOC_Trainer_Spawn_Context")
                }),
            CancellationToken.None);

        result.Available.Should().BeFalse();
        result.Diagnostics.Should().NotBeNull();
        var diagnostics = result.Diagnostics!;
        diagnostics["helperBridgeState"]?.ToString().Should().Be("experimental");
        diagnostics["helperAutoloadState"]?.ToString().Should().Be("ready");
        diagnostics["helperAutoloadReasonCode"]?.ToString().Should().Be("helper_autoload_ready");
        diagnostics["helperAutoloadStrategy"]?.ToString().Should().Be("story_wrapper_chain");
        diagnostics["helperAutoloadScript"]?.ToString().Should().Be("Library/PGStoryMode.lua");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldSurfaceProbeFailure_WhenHelperBridgeIsUnavailable()
    {
        var backend = new NamedPipeHelperBridgeBackend(new StubExecutionBackend());
        var request = BuildHelperRequest(payload: new JsonObject(), hook: null, processId: 0);

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE);
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics!["processId"]?.ToString().Should().Be("0");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailInvocation_WhenBackendExecutionFails()
    {
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = BuildHelperProbeReport(),
            ExecuteResult = new ActionExecutionResult(
                Succeeded: false,
                Message: "backend invocation failed",
                AddressSource: AddressSource.None,
                Diagnostics: null)
        };
        var backend = new NamedPipeHelperBridgeBackend(stubBackend);
        var request = BuildHelperRequest(
            payload: new JsonObject(),
            hook: null,
            actionId: "unknown_helper_action");

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_INVOCATION_FAILED);
        result.Message.Should().Be("backend invocation failed");
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics!["helperHookId"]?.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldApplySpawnContextDefaults_AndFallbackEntryPoint()
    {
        JsonObject? observedPayload = null;
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = BuildHelperProbeReport(),
            ExecuteFactory = command =>
            {
                observedPayload = command.Payload;
                var operationToken = command.Payload["operationToken"]?.GetValue<string>() ?? string.Empty;
                return new ActionExecutionResult(
                    Succeeded: true,
                    Message: "helper command applied",
                    AddressSource: AddressSource.None,
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["operationToken"] = operationToken,
                        ["helperVerifyState"] = "applied",
                        ["helperExecutionPath"] = "plugin_dispatch"
                    });
            }
        };
        var backend = CreateBackendWithVerifiedTelemetry(stubBackend);
        var request = BuildHelperRequest(
            payload: new JsonObject { ["entityBlueprintId"] = "unit_x" },
            hook: new HelperHookSpec(
                Id: "spawn_context_hook",
                Script: "scripts/spawn/context.lua",
                Version: "1.0.0",
                EntryPoint: " "),
            actionId: "spawn_context_entity");

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        observedPayload.Should().NotBeNull();
        observedPayload!["helperEntryPoint"]?.GetValue<string>().Should().Be("SWFOC_Trainer_Spawn_Context");
        observedPayload["populationPolicy"]?.GetValue<string>().Should().Be("ForceZeroTactical");
        observedPayload["persistencePolicy"]?.GetValue<string>().Should().Be("EphemeralBattleOnly");
        observedPayload["allowCrossFaction"]?.GetValue<bool>().Should().BeTrue();
        observedPayload["operationKind"]?.GetValue<string>().Should().Be(HelperBridgeOperationKind.SpawnContextEntity.ToString());
    }

    [Fact]
    public async Task ExecuteAsync_ShouldApplyPlanetBuildingDefaults_AndFallbackEntryPoint()
    {
        JsonObject? observedPayload = null;
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = BuildHelperProbeReport(),
            ExecuteFactory = command =>
            {
                observedPayload = command.Payload;
                var operationToken = command.Payload["operationToken"]?.GetValue<string>() ?? string.Empty;
                return new ActionExecutionResult(
                    Succeeded: true,
                    Message: "helper command applied",
                    AddressSource: AddressSource.None,
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["operationToken"] = operationToken,
                        ["helperVerifyState"] = "applied",
                        ["helperExecutionPath"] = "plugin_dispatch"
                    });
            }
        };
        var backend = CreateBackendWithVerifiedTelemetry(stubBackend);
        var request = BuildHelperRequest(
            payload: new JsonObject { ["planetId"] = "coruscant" },
            hook: new HelperHookSpec(
                Id: "place_building_hook",
                Script: "scripts/build/place.lua",
                Version: "1.0.0"),
            actionId: "place_planet_building");

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        observedPayload.Should().NotBeNull();
        observedPayload!["helperEntryPoint"]?.GetValue<string>().Should().Be("SWFOC_Trainer_Place_Building");
        observedPayload["placementMode"]?.GetValue<string>().Should().Be("safe_rules");
        observedPayload["forceOverride"]?.GetValue<bool>().Should().BeFalse();
        observedPayload["allowCrossFaction"]?.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldApplyGalacticSpawnDefaults_AndPreserveActionContext()
    {
        JsonObject? observedPayload = null;
        IReadOnlyDictionary<string, object?>? observedContext = null;
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = BuildHelperProbeReport(),
            ExecuteFactory = command =>
            {
                observedPayload = command.Payload;
                observedContext = command.Context;
                var operationToken = command.Payload["operationToken"]?.GetValue<string>() ?? string.Empty;
                return new ActionExecutionResult(
                    Succeeded: true,
                    Message: "helper command applied",
                    AddressSource: AddressSource.None,
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["operationToken"] = operationToken,
                        ["helperVerifyState"] = "applied",
                        ["helperExecutionPath"] = "plugin_dispatch"
                    });
            }
        };
        var backend = CreateBackendWithVerifiedTelemetry(stubBackend);
        var request = BuildHelperRequest(
            payload: new JsonObject { ["entityBlueprintId"] = "unit_y" },
            hook: new HelperHookSpec(
                Id: "spawn_galactic_hook",
                Script: "scripts/spawn/galactic.lua",
                Version: "1.0.0",
                EntryPoint: null),
            actionId: "spawn_galactic_entity",
            actionContext: new Dictionary<string, object?>()
            {
                ["fromCaller"] = "true"
            });

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        observedPayload.Should().NotBeNull();
        observedPayload!["helperEntryPoint"]?.GetValue<string>().Should().Be("SWFOC_Trainer_Spawn_Context");
        observedPayload["populationPolicy"]?.GetValue<string>().Should().Be("Normal");
        observedPayload["persistencePolicy"]?.GetValue<string>().Should().Be("PersistentGalactic");
        observedPayload["allowCrossFaction"]?.GetValue<bool>().Should().BeTrue();
        observedContext.Should().NotBeNull();
        observedContext!["fromCaller"]?.ToString().Should().Be("true");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldUseUnknownOperationKind_AndSkipEntrypoint_WhenNoFallbackExists()
    {
        JsonObject? observedPayload = null;
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = BuildHelperProbeReport(),
            ExecuteFactory = command =>
            {
                observedPayload = command.Payload;
                var operationToken = command.Payload["operationToken"]?.GetValue<string>() ?? string.Empty;
                return new ActionExecutionResult(
                    Succeeded: true,
                    Message: "helper command applied",
                    AddressSource: AddressSource.None,
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["operationToken"] = operationToken,
                        ["helperVerifyState"] = "applied",
                        ["helperExecutionPath"] = "plugin_dispatch"
                    });
            }
        };
        var backend = CreateBackendWithVerifiedTelemetry(stubBackend);
        var request = BuildHelperRequest(
            payload: new JsonObject(),
            hook: new HelperHookSpec(
                Id: "unknown_hook",
                Script: "scripts/unknown.lua",
                Version: "1.0.0",
                EntryPoint: " ",
                ArgContract: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["entityBlueprintId"] = "required:string"
                }),
            actionId: "unknown_helper_action");

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        observedPayload.Should().NotBeNull();
        observedPayload!.ContainsKey("helperEntryPoint").Should().BeFalse();
        observedPayload["operationKind"]?.GetValue<string>().Should().Be(HelperBridgeOperationKind.Unknown.ToString());
        observedPayload.ContainsKey("helperArgContract").Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailVerification_WhenOperationTokenMismatches()
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
                    ["operationToken"] = "mismatched-token"
                })
        };
        var backend = new NamedPipeHelperBridgeBackend(stubBackend);
        var request = BuildHelperRequest(
            payload: new JsonObject(),
            hook: null,
            actionId: "unknown_helper_action",
            operationToken: "expected-token");

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_VERIFICATION_FAILED);
        result.Message.Should().Contain("operation token mismatch");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailVerification_WhenDiagnosticValueMismatchesContract()
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
                    ["helperVerifyState"] = "unexpected",
                    ["operationToken"] = "token-verify",
                    ["helperExecutionPath"] = "plugin_dispatch"
                })
        };
        var backend = new NamedPipeHelperBridgeBackend(stubBackend);
        var request = BuildHelperRequest(
            payload: new JsonObject(),
            hook: new HelperHookSpec(
                Id: "verify_hook",
                Script: "scripts/verify.lua",
                Version: "1.0.0",
                VerifyContract: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["helperVerifyState"] = "applied"
                }),
            operationToken: "token-verify");

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_VERIFICATION_FAILED);
        result.Message.Should().Contain("expected 'applied' but was 'unexpected'");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailVerification_WhenVerifyContractIsNotSatisfied()
    {
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = BuildHelperProbeReport(),
            ExecuteFactory = command =>
            {
                var operationToken = command.Payload["operationToken"]?.GetValue<string>() ?? string.Empty;
                return new ActionExecutionResult(
                    Succeeded: true,
                    Message: "helper command accepted",
                    AddressSource: AddressSource.None,
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["operationToken"] = operationToken,
                        ["helperVerifyState"] = "applied",
                        ["helperExecutionPath"] = "plugin_dispatch"
                    });
            }
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
            ExecuteFactory = command =>
            {
                var operationToken = command.Payload["operationToken"]?.GetValue<string>() ?? string.Empty;
                return new ActionExecutionResult(
                    Succeeded: true,
                    Message: "helper command applied",
                    AddressSource: AddressSource.None,
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["globalKey"] = "AOTR_HERO_KEY",
                        ["helperVerifyState"] = "applied",
                        ["operationToken"] = operationToken,
                        ["helperExecutionPath"] = "plugin_dispatch"
                    });
            }
        };
        var backend = CreateBackendWithVerifiedTelemetry(stubBackend);
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
        diagnostics["operationToken"]?.ToString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailVerification_WhenTelemetryServiceIsUnavailable()
    {
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = BuildHelperProbeReport(),
            ExecuteFactory = command =>
            {
                var operationToken = command.Payload["operationToken"]?.GetValue<string>() ?? string.Empty;
                return new ActionExecutionResult(
                    Succeeded: true,
                    Message: "helper command applied",
                    AddressSource: AddressSource.None,
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["helperVerifyState"] = "applied",
                        ["operationToken"] = operationToken,
                        ["helperExecutionPath"] = "plugin_dispatch"
                    });
            }
        };
        var backend = new NamedPipeHelperBridgeBackend(stubBackend);
        var request = BuildHelperRequest(
            payload: new JsonObject { ["globalKey"] = "AOTR_HERO_KEY", ["intValue"] = 1 },
            hook: new HelperHookSpec(
                Id: "aotr_hero_state_bridge",
                Script: "scripts/aotr/hero_state_bridge.lua",
                Version: "1.0.0",
                EntryPoint: "SWFOC_Trainer_Set_Hero_Respawn"),
            operationToken: "token-runtime-evidence");

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_VERIFICATION_FAILED);
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics!["helperVerifyState"]?.ToString().Should().Be("failed_runtime_evidence");
        result.Diagnostics["helperEvidenceState"]?.ToString().Should().Be("missing");
        result.Diagnostics["helperEvidenceReasonCode"]?.ToString().Should().Be("helper_operation_verification_not_supported");
    }


    [Fact]
    public async Task ExecuteAsync_ShouldFailVerification_WhenTelemetryEvidenceIsMissing()
    {
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = BuildHelperProbeReport(),
            ExecuteFactory = command =>
            {
                var operationToken = command.Payload["operationToken"]?.GetValue<string>() ?? string.Empty;
                return new ActionExecutionResult(
                    Succeeded: true,
                    Message: "helper command applied",
                    AddressSource: AddressSource.None,
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["helperVerifyState"] = "applied",
                        ["operationToken"] = operationToken,
                        ["helperExecutionPath"] = "plugin_dispatch"
                    });
            }
        };

        var telemetry = new StubTelemetryLogTailService
        {
            VerificationResult = HelperOperationVerification.Unavailable("helper_operation_token_not_found")
        };

        var backend = new NamedPipeHelperBridgeBackend(stubBackend, telemetry);
        var request = BuildHelperRequest(
            payload: new JsonObject { ["globalKey"] = "AOTR_HERO_KEY", ["intValue"] = 1 },
            hook: new HelperHookSpec(
                Id: "aotr_hero_state_bridge",
                Script: "scripts/aotr/hero_state_bridge.lua",
                Version: "1.0.0",
                EntryPoint: "SWFOC_Trainer_Set_Hero_Respawn"),
            operationToken: "token-evidence-missing");

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_VERIFICATION_FAILED);
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics!["helperEvidenceState"]?.ToString().Should().Be("missing");
        result.Diagnostics["helperEvidenceReasonCode"]?.ToString().Should().Be("helper_operation_token_not_found");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldReturnApplied_WhenTelemetryEvidenceIsVerified()
    {
        var stubBackend = new StubExecutionBackend
        {
            ProbeReport = BuildHelperProbeReport(),
            ExecuteFactory = command =>
            {
                var operationToken = command.Payload["operationToken"]?.GetValue<string>() ?? string.Empty;
                return new ActionExecutionResult(
                    Succeeded: true,
                    Message: "helper command applied",
                    AddressSource: AddressSource.None,
                    Diagnostics: new Dictionary<string, object?>
                    {
                        ["helperVerifyState"] = "applied",
                        ["operationToken"] = operationToken,
                        ["helperExecutionPath"] = "plugin_dispatch"
                    });
            }
        };

        var telemetry = new StubTelemetryLogTailService
        {
            VerificationResult = new HelperOperationVerification(
                Verified: true,
                ReasonCode: "helper_operation_token_verified",
                SourcePath: @"C:\Games\_LogFile.txt",
                TimestampUtc: DateTimeOffset.UtcNow,
                RawLine: "SWFOC_TRAINER_APPLIED token-evidence-ok entity=UNIT")
        };

        var backend = new NamedPipeHelperBridgeBackend(stubBackend, telemetry);
        var request = BuildHelperRequest(
            payload: new JsonObject { ["globalKey"] = "AOTR_HERO_KEY", ["intValue"] = 1 },
            hook: new HelperHookSpec(
                Id: "aotr_hero_state_bridge",
                Script: "scripts/aotr/hero_state_bridge.lua",
                Version: "1.0.0",
                EntryPoint: "SWFOC_Trainer_Set_Hero_Respawn"),
            operationToken: "token-evidence-ok");

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_EXECUTION_APPLIED);
        result.Diagnostics.Should().NotBeNull();
        result.Diagnostics!["helperEvidenceState"]?.ToString().Should().Be("verified");
        result.Diagnostics["helperEvidenceReasonCode"]?.ToString().Should().Be("helper_operation_token_verified");
        result.Diagnostics["helperEvidenceSourcePath"]?.ToString().Should().Contain("_LogFile.txt");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailVerification_WhenOperationTokenRoundTripIsMissing()
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

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_VERIFICATION_FAILED);
        result.Message.Should().Contain("operation token");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailVerification_WhenExecutionPathIsMissing()
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
                    ["helperVerifyState"] = "applied",
                    ["operationToken"] = "token-verify-path"
                })
        };

        var backend = new NamedPipeHelperBridgeBackend(stubBackend);
        var request = BuildHelperRequest(
            payload: new JsonObject { ["globalKey"] = "AOTR_HERO_KEY", ["intValue"] = 1 },
            hook: new HelperHookSpec(
                Id: "aotr_hero_state_bridge",
                Script: "scripts/aotr/hero_state_bridge.lua",
                Version: "1.0.0",
                EntryPoint: "SWFOC_Trainer_Set_Hero_Respawn"),
            operationToken: "token-verify-path");

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_VERIFICATION_FAILED);
        result.Message.Should().Contain("helperExecutionPath");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldFailVerification_WhenExecutionPathIsContractValidationOnly()
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
                    ["helperVerifyState"] = "applied",
                    ["operationToken"] = "token-verify-path",
                    ["helperExecutionPath"] = "contract_validation_only"
                })
        };

        var backend = new NamedPipeHelperBridgeBackend(stubBackend);
        var request = BuildHelperRequest(
            payload: new JsonObject { ["globalKey"] = "AOTR_HERO_KEY", ["intValue"] = 1 },
            hook: new HelperHookSpec(
                Id: "aotr_hero_state_bridge",
                Script: "scripts/aotr/hero_state_bridge.lua",
                Version: "1.0.0",
                EntryPoint: "SWFOC_Trainer_Set_Hero_Respawn"),
            operationToken: "token-verify-path");

        var result = await backend.ExecuteAsync(request, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(RuntimeReasonCode.HELPER_VERIFICATION_FAILED);
        result.Message.Should().Contain("must not equal 'contract_validation_only'");
    }

    [Theory]
    [InlineData("set_context_faction", HelperBridgeOperationKind.SetContextAllegiance)]
    [InlineData("toggle_roe_respawn_helper", HelperBridgeOperationKind.ToggleRoeRespawnHelper)]
    [InlineData("spawn_galactic_entity", HelperBridgeOperationKind.SpawnGalacticEntity)]
    [InlineData("transfer_fleet_safe", HelperBridgeOperationKind.TransferFleetSafe)]
    [InlineData("flip_planet_owner", HelperBridgeOperationKind.FlipPlanetOwner)]
    [InlineData("switch_player_faction", HelperBridgeOperationKind.SwitchPlayerFaction)]
    [InlineData("edit_hero_state", HelperBridgeOperationKind.EditHeroState)]
    [InlineData("create_hero_variant", HelperBridgeOperationKind.CreateHeroVariant)]
    [InlineData("unknown_helper_action", HelperBridgeOperationKind.Unknown)]
    public void ResolveOperationKind_ShouldMapKnownAliases(string actionId, HelperBridgeOperationKind expected)
    {
        var method = typeof(NamedPipeHelperBridgeBackend).GetMethod(
            "ResolveOperationKind",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var actual = (HelperBridgeOperationKind)method!.Invoke(null, new object?[] { actionId })!;
        actual.Should().Be(expected);
    }

    [Fact]
    public void ValidateVerificationEntry_ShouldHandleRequiredAndMismatchPaths()
    {
        var method = typeof(NamedPipeHelperBridgeBackend).GetMethod(
            "ValidateVerificationEntry",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var diagnostics = new Dictionary<string, object?>
        {
            ["globalKey"] = "KEY_A",
            ["helperVerifyState"] = "applied"
        };
        var argsRequired = new object?[] { "globalKey", "required:echo", diagnostics, string.Empty };
        var requiredResult = (bool)method!.Invoke(null, argsRequired)!;
        requiredResult.Should().BeTrue();

        var argsMismatch = new object?[] { "helperVerifyState", "expected", diagnostics, string.Empty };
        var mismatchResult = (bool)method.Invoke(null, argsMismatch)!;
        mismatchResult.Should().BeFalse();
        argsMismatch[3]!.ToString().Should().Contain("expected 'expected'");

        var argsNotAllowed = new object?[] { "helperExecutionPath", "not:contract_validation_only", new Dictionary<string, object?> { ["helperExecutionPath"] = "runtime_verified" }, string.Empty };
        var notAllowedResult = (bool)method.Invoke(null, argsNotAllowed)!;
        notAllowedResult.Should().BeTrue();

        var argsNotAllowedFailure = new object?[] { "helperExecutionPath", "not:contract_validation_only", new Dictionary<string, object?> { ["helperExecutionPath"] = "contract_validation_only" }, string.Empty };
        var notAllowedFailureResult = (bool)method.Invoke(null, argsNotAllowedFailure)!;
        notAllowedFailureResult.Should().BeFalse();
        argsNotAllowedFailure[3]!.ToString().Should().Contain("must not equal 'contract_validation_only'");

        var argsRequiredNot = new object?[] { "helperExecutionPath", "required_not:contract_validation_only", new Dictionary<string, object?> { ["helperExecutionPath"] = "runtime_verified" }, string.Empty };
        var requiredNotResult = (bool)method.Invoke(null, argsRequiredNot)!;
        requiredNotResult.Should().BeTrue();

        var argsRequiredNotMissing = new object?[] { "helperExecutionPath", "required_not:contract_validation_only", new Dictionary<string, object?>(), string.Empty };
        var requiredNotMissingResult = (bool)method.Invoke(null, argsRequiredNotMissing)!;
        requiredNotMissingResult.Should().BeFalse();
        argsRequiredNotMissing[3]!.ToString().Should().Contain("required diagnostic 'helperExecutionPath'");
    }

    [Theory]
    [InlineData("place_planet_building", "CustomEntryPoint", "SWFOC_Trainer_Place_Building")]
    [InlineData("unknown_helper_action", "CustomEntryPoint", "CustomEntryPoint")]
    [InlineData("unknown_helper_action", " ", "")]
    public void ResolveDefaultHelperEntryPoint_ShouldPreferKnownDefaults(string actionId, string configuredEntryPoint, string expected)
    {
        var method = typeof(NamedPipeHelperBridgeBackend).GetMethod(
            "ResolveDefaultHelperEntryPoint",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var actual = (string)method!.Invoke(null, new object?[] { actionId, configuredEntryPoint })!;

        actual.Should().Be(expected);
    }

    [Fact]
    public void BuildEffectiveVerificationContract_ShouldMergeContracts_AndAddImplicitGuards()
    {
        var method = typeof(NamedPipeHelperBridgeBackend).GetMethod(
            "BuildEffectiveVerificationContract",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var request = new HelperBridgeRequest(
            ActionRequest: new ActionExecutionRequest(
                Action: new ActionSpec(
                    "set_hero_state_helper",
                    ActionCategory.Hero,
                    RuntimeMode.Galactic,
                    ExecutionKind.Helper,
                    new JsonObject(),
                    VerifyReadback: false,
                    CooldownMs: 0),
                Payload: new JsonObject(),
                ProfileId: "test_profile",
                RuntimeMode: RuntimeMode.Galactic,
                Context: null),
            Process: BuildProcess(processId: 4242),
            Hook: new HelperHookSpec(
                Id: "hero_hook",
                Script: "scripts/hero.lua",
                Version: "1.0.0",
                VerifyContract: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["globalKey"] = "required:echo",
                    ["customState"] = "hook-default"
                }),
            VerificationContract: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["customState"] = "request-override"
            });

        var contract = (IReadOnlyDictionary<string, string>)method!.Invoke(null, new object?[] { request })!;

        contract["globalKey"].Should().Be("required:echo");
        contract["customState"].Should().Be("request-override");
        contract["helperVerifyState"].Should().Be("applied");
        contract["helperExecutionPath"].Should().Be("required_not:contract_validation_only");
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

    private static HelperBridgeRequest BuildHelperRequest(
        JsonObject payload,
        HelperHookSpec? hook,
        string actionId = "set_hero_state_helper",
        IReadOnlyDictionary<string, object?>? actionContext = null,
        HelperBridgeOperationKind operationKind = HelperBridgeOperationKind.Unknown,
        string? operationToken = null)
    {
        var action = new ActionSpec(
            Id: actionId,
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
            RuntimeMode: RuntimeMode.Galactic,
            Context: actionContext);

        return new HelperBridgeRequest(
            ActionRequest: actionRequest,
            Process: BuildProcess(processId: 4242),
            Hook: hook,
            OperationKind: operationKind,
            OperationToken: operationToken,
            Context: null);
    }

    private static HelperBridgeRequest BuildHelperRequest(
        JsonObject payload,
        HelperHookSpec? hook,
        int processId,
        string actionId = "set_hero_state_helper",
        IReadOnlyDictionary<string, object?>? actionContext = null,
        HelperBridgeOperationKind operationKind = HelperBridgeOperationKind.Unknown,
        string? operationToken = null)
    {
        var action = new ActionSpec(
            Id: actionId,
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
            RuntimeMode: RuntimeMode.Galactic,
            Context: actionContext);

        return new HelperBridgeRequest(
            ActionRequest: actionRequest,
            Process: BuildProcess(processId: processId),
            Hook: hook,
            OperationKind: operationKind,
            OperationToken: operationToken,
            Context: null);
    }


    private static NamedPipeHelperBridgeBackend CreateBackendWithVerifiedTelemetry(StubExecutionBackend stubBackend)
    {
        var telemetry = new StubTelemetryLogTailService
        {
            VerificationResult = new HelperOperationVerification(
                Verified: true,
                ReasonCode: "helper_operation_token_verified",
                SourcePath: @"C:\Games\_LogFile.txt",
                TimestampUtc: DateTimeOffset.UtcNow,
                RawLine: "SWFOC_TRAINER_APPLIED token")
        };

        return new NamedPipeHelperBridgeBackend(stubBackend, telemetry);
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

    private sealed class StubTelemetryLogTailService : ITelemetryLogTailService
    {
        public HelperOperationVerification VerificationResult { get; set; } =
            HelperOperationVerification.Unavailable("helper_operation_token_not_found");

        public HelperAutoloadVerification AutoloadResult { get; set; } =
            HelperAutoloadVerification.Unavailable("helper_autoload_not_found");

        public TelemetryModeResolution ResolveLatestMode(string? processPath, DateTimeOffset nowUtc, TimeSpan freshnessWindow)
        {
            _ = processPath;
            _ = nowUtc;
            _ = freshnessWindow;
            return TelemetryModeResolution.Unavailable("telemetry_line_missing");
        }

        public HelperOperationVerification VerifyOperationToken(string? processPath, string operationToken, DateTimeOffset nowUtc, TimeSpan freshnessWindow)
        {
            _ = processPath;
            _ = operationToken;
            _ = nowUtc;
            _ = freshnessWindow;
            return VerificationResult;
        }

        public HelperAutoloadVerification VerifyAutoloadProfile(string? processPath, string? profileId, DateTimeOffset nowUtc, TimeSpan freshnessWindow)
        {
            _ = processPath;
            _ = profileId;
            _ = nowUtc;
            _ = freshnessWindow;
            return AutoloadResult;
        }
    }

    private sealed class StubExecutionBackend : IExecutionBackend
    {
        public ExecutionBackendKind BackendKind => ExecutionBackendKind.Extender;

        public CapabilityReport ProbeReport { get; init; } = CapabilityReport.Unknown("test_profile");

        public ActionExecutionResult ExecuteResult { get; init; } = new(
            Succeeded: false,
            Message: "stub",
            AddressSource: AddressSource.None);

        public Func<ActionExecutionRequest, ActionExecutionResult>? ExecuteFactory { get; init; }

        public Task<CapabilityReport> ProbeCapabilitiesAsync(string profileId, ProcessMetadata processContext)
        {
            _ = profileId;
            _ = processContext;
            return Task.FromResult(ProbeReport);
        }

        public Task<CapabilityReport> ProbeCapabilitiesAsync(string profileId, ProcessMetadata processContext, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = processContext;
            _ = cancellationToken;
            return Task.FromResult(ProbeReport);
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest command, CapabilityReport capabilityReport)
        {
            _ = capabilityReport;
            return Task.FromResult(ExecuteFactory?.Invoke(command) ?? ExecuteResult);
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest command, CapabilityReport capabilityReport, CancellationToken cancellationToken)
        {
            _ = capabilityReport;
            _ = cancellationToken;
            return Task.FromResult(ExecuteFactory?.Invoke(command) ?? ExecuteResult);
        }

        public Task<BackendHealth> GetHealthAsync()
            => Task.FromResult(new BackendHealth(
                BackendId: "stub",
                Backend: BackendKind,
                IsHealthy: true,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "ok"));

        public Task<BackendHealth> GetHealthAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return GetHealthAsync();
        }
    }
}
