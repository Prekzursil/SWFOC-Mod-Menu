using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class ActionReliabilityServiceTests
{
    [Fact]
    public void Evaluate_TacticalHealthyAction_ShouldBeStable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_selected_hp", RuntimeMode.AnyTactical, ExecutionKind.Memory, "symbol", "floatValue"));
        var session = BuildSession(
            RuntimeMode.AnyTactical,
            new SymbolInfo(
                "selected_hp",
                (nint)0x1234,
                SymbolValueType.Float,
                AddressSource.Signature,
                Confidence: 0.93d,
                HealthStatus: SymbolHealthStatus.Healthy));

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_selected_hp");

        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("healthy_signature");
    }

    [Fact]
    public void Evaluate_AnyTacticalAction_ShouldBeCompatibleWithLandMode()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_selected_hp", RuntimeMode.AnyTactical, ExecutionKind.Memory, "symbol", "floatValue"));
        var session = BuildSession(
            RuntimeMode.TacticalLand,
            new SymbolInfo(
                "selected_hp",
                (nint)0x1234,
                SymbolValueType.Float,
                AddressSource.Signature,
                Confidence: 0.93d,
                HealthStatus: SymbolHealthStatus.Healthy));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_selected_hp");

        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("healthy_signature");
    }

    [Fact]
    public void Evaluate_AnyTacticalAction_ShouldBeCompatibleWithSpaceMode()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_selected_hp", RuntimeMode.AnyTactical, ExecutionKind.Memory, "symbol", "floatValue"));
        var session = BuildSession(
            RuntimeMode.TacticalSpace,
            new SymbolInfo(
                "selected_hp",
                (nint)0x1234,
                SymbolValueType.Float,
                AddressSource.Signature,
                Confidence: 0.93d,
                HealthStatus: SymbolHealthStatus.Healthy));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_selected_hp");

        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("healthy_signature");
    }

    [Fact]
    public void Evaluate_ModeUnknownForTactical_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_selected_hp", RuntimeMode.AnyTactical, ExecutionKind.Memory, "symbol", "floatValue"));
        var session = BuildSession(
            RuntimeMode.Unknown,
            new SymbolInfo(
                "selected_hp",
                (nint)0x1234,
                SymbolValueType.Float,
                AddressSource.Signature,
                Confidence: 0.93d,
                HealthStatus: SymbolHealthStatus.Healthy));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_selected_hp");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("mode_unknown_strict_gate");
    }

    [Fact]
    public void Evaluate_ModeMismatch_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_selected_hp", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol", "floatValue"));
        var session = BuildSession(
            RuntimeMode.TacticalLand,
            new SymbolInfo(
                "selected_hp",
                (nint)0x1234,
                SymbolValueType.Float,
                AddressSource.Signature,
                Confidence: 0.93d,
                HealthStatus: SymbolHealthStatus.Healthy));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_selected_hp");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("mode_mismatch");
    }

    [Fact]
    public void Evaluate_DependencyBlockedHelper_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_unit_helper", RuntimeMode.Unknown, ExecutionKind.Helper, "helperHookId", "unitId", "entryMarker", "faction"));
        var session = BuildSession(
            RuntimeMode.Galactic,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dependencyDisabledActions"] = "spawn_unit_helper"
            });

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "spawn_unit_helper");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("dependency_soft_blocked");
    }

    [Fact]
    public void Evaluate_DependencyBlockedHelper_ShouldParseMetadataCsvWithWhitespace()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_unit_helper", RuntimeMode.Unknown, ExecutionKind.Helper, "helperHookId", "unitId", "entryMarker", "faction"));
        var session = BuildSession(
            RuntimeMode.Galactic,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["dependencyDisabledActions"] = " set_credits , spawn_unit_helper "
            });

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "spawn_unit_helper");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("dependency_soft_blocked");
    }

    [Fact]
    public void Evaluate_HelperActionWithoutReadyBridge_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_unit_helper", RuntimeMode.Unknown, ExecutionKind.Helper, "helperHookId", "unitId", "entryMarker", "faction"));
        var session = BuildSession(
            RuntimeMode.TacticalLand,
            symbol: null,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = "unavailable"
            });

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "spawn_unit_helper");

        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("helper_bridge_unavailable");
    }

    [Fact]
    public void Evaluate_HelperActionWithWhitespaceBridgeState_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_unit_helper", RuntimeMode.Unknown, ExecutionKind.Helper, "helperHookId", "unitId", "entryMarker", "faction"));
        var session = BuildSession(
            RuntimeMode.TacticalLand,
            symbol: null,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = "   "
            });

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "spawn_unit_helper");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("helper_bridge_unavailable");
    }

    [Fact]
    public void Evaluate_HelperSpawnActionWithReadyBridgeAndCatalog_ShouldBeStable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_unit_helper", RuntimeMode.Unknown, ExecutionKind.Helper, "helperHookId", "unitId", "entryMarker", "faction"));
        var session = BuildSession(
            RuntimeMode.TacticalLand,
            symbol: null,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = " ready "
            });
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = new[] { "STORMTROOPER" },
            ["faction_catalog"] = new[] { "Empire" }
        };

        var entry = service.Evaluate(profile, session, catalog).Single(x => x.ActionId == "spawn_unit_helper");

        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("helper_ready");
    }

    [Fact]
    public void Evaluate_HelperSpawnActionWithReadyBridgeButMissingUnitCatalog_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_unit_helper", RuntimeMode.Unknown, ExecutionKind.Helper, "helperHookId", "unitId", "entryMarker", "faction"));
        var session = BuildSession(
            RuntimeMode.TacticalLand,
            symbol: null,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = "ready"
            });
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["faction_catalog"] = new[] { "Empire" }
        };

        var entry = service.Evaluate(profile, session, catalog).Single(x => x.ActionId == "spawn_unit_helper");

        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("catalog_unavailable");
    }

    [Fact]
    public void Evaluate_HelperBuildingActionWithReadyBridgeButMissingBuildingCatalog_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("place_planet_building", RuntimeMode.Unknown, ExecutionKind.Helper, "helperHookId", "entityId", "faction"));
        var session = BuildSession(
            RuntimeMode.Galactic,
            symbol: null,
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = "ready"
            });
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["faction_catalog"] = new[] { "Empire" }
        };

        var entry = service.Evaluate(profile, session, catalog).Single(x => x.ActionId == "place_planet_building");

        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("building_catalog_unavailable");
    }

    [Fact]
    public void Evaluate_UnknownModeStrictSpawnBundle_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_unit_helper", RuntimeMode.Unknown, ExecutionKind.Helper, "helperHookId", "unitId", "entryMarker", "faction"));
        var session = BuildSession(RuntimeMode.Unknown, null);
        var catalog = new Dictionary<string, IReadOnlyList<string>>
        {
            ["unit_catalog"] = new[] { "STORMTROOPER" }
        };

        var entry = service.Evaluate(profile, session, catalog).Single(x => x.ActionId == "spawn_unit_helper");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("mode_unknown_strict_gate");
    }

    [Fact]
    public void Evaluate_FallbackDegradedNonCritical_ShouldBeExperimental()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_game_speed", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol", "floatValue"));
        var session = BuildSession(
            RuntimeMode.Galactic,
            new SymbolInfo(
                "game_speed",
                (nint)0x1234,
                SymbolValueType.Float,
                AddressSource.Fallback,
                Confidence: 0.63d,
                HealthStatus: SymbolHealthStatus.Degraded));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_game_speed");
        entry.State.Should().Be(ActionReliabilityState.Experimental);
        entry.ReasonCode.Should().Be("fallback_or_degraded");
    }

    [Fact]
    public void Evaluate_FallbackActionDisabledByFeatureFlag_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["allow_fog_patch_fallback"] = false
            },
            Action("toggle_fog_reveal_patch_fallback", RuntimeMode.Unknown, ExecutionKind.CodePatch, "enable"));
        var session = BuildSession(RuntimeMode.Galactic, symbol: null);

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "toggle_fog_reveal_patch_fallback");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("fallback_disabled");
    }

    [Fact]
    public void Evaluate_FallbackActionEnabled_ShouldBeExperimental()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["allow_unit_cap_patch_fallback"] = true
            },
            Action("set_unit_cap_patch_fallback", RuntimeMode.Unknown, ExecutionKind.CodePatch, "intValue"));
        var session = BuildSession(RuntimeMode.Galactic, symbol: null);

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_unit_cap_patch_fallback");
        entry.State.Should().Be(ActionReliabilityState.Experimental);
        entry.ReasonCode.Should().Be("fallback_experimental");
    }

    [Fact]
    public void Evaluate_ExtenderCreditsExperimentalDisabled_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["allow_extender_credits"] = false
            },
            Action("set_credits_extender_experimental", RuntimeMode.Unknown, ExecutionKind.Sdk, "symbol", "intValue"));
        var session = BuildSession(RuntimeMode.Galactic, symbol: null);

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_credits_extender_experimental");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("experimental_disabled");
    }

    [Fact]
    public void Evaluate_ExtenderCreditsExperimentalEnabled_ShouldBeExperimental()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["allow_extender_credits"] = true
            },
            Action("set_credits_extender_experimental", RuntimeMode.Unknown, ExecutionKind.Sdk, "symbol", "intValue"));
        var session = BuildSession(RuntimeMode.Galactic, symbol: null);

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_credits_extender_experimental");
        entry.State.Should().Be(ActionReliabilityState.Experimental);
        entry.ReasonCode.Should().Be("experimental_enabled");
    }

    [Fact]
    public void Evaluate_MechanicDetectionUnsupported_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService(
            new StubModMechanicDetectionService(
                new ModMechanicReport(
                    ProfileId: "test",
                    GeneratedAtUtc: DateTimeOffset.UtcNow,
                    DependenciesSatisfied: false,
                    HelperBridgeReady: false,
                    ActionSupport: new[]
                    {
                        new ModMechanicSupport(
                            ActionId: "set_selected_hp",
                            Supported: false,
                            ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                            Message: "Mechanic unavailable for this mod chain.",
                            Confidence: 0.91d)
                    },
                    Diagnostics: new Dictionary<string, object?>())));

        var profile = BuildProfile(
            Action("set_selected_hp", RuntimeMode.AnyTactical, ExecutionKind.Memory, "symbol", "floatValue"));
        var session = BuildSession(
            RuntimeMode.AnyTactical,
            new SymbolInfo(
                "selected_hp",
                (nint)0x1234,
                SymbolValueType.Float,
                AddressSource.Signature,
                Confidence: 0.93d,
                HealthStatus: SymbolHealthStatus.Healthy));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_selected_hp");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be(RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING.ToString());
    }

    [Fact]
    public void Evaluate_HelperAction_ShouldBeUnavailable_WhenHelperBridgeNotReady()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_hero_state_helper", RuntimeMode.Galactic, ExecutionKind.Helper, "helperHookId", "globalKey", "intValue"));
        var session = BuildSession(
            RuntimeMode.Galactic,
            symbol: null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = "unavailable"
            });

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_hero_state_helper");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("helper_bridge_unavailable");
    }

    [Fact]
    public void Evaluate_HelperSpawnAction_ShouldBeUnavailable_WhenCatalogMissing()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_context_entity", RuntimeMode.AnyTactical, ExecutionKind.Helper, "helperHookId", "entityId"));
        var session = BuildSession(
            RuntimeMode.TacticalLand,
            symbol: null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = "ready"
            });
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = Array.Empty<string>(),
            ["faction_catalog"] = Array.Empty<string>()
        };

        var entry = service.Evaluate(profile, session, catalog).Single(x => x.ActionId == "spawn_context_entity");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("catalog_unavailable");
    }

    [Fact]
    public void Evaluate_HelperBuildingAction_ShouldBeUnavailable_WhenBuildingCatalogMissing()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("place_planet_building", RuntimeMode.Galactic, ExecutionKind.Helper, "helperHookId", "entityId"));
        var session = BuildSession(
            RuntimeMode.Galactic,
            symbol: null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = "ready"
            });
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["faction_catalog"] = new[] { "Empire" },
            ["building_catalog"] = Array.Empty<string>()
        };

        var entry = service.Evaluate(profile, session, catalog).Single(x => x.ActionId == "place_planet_building");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("building_catalog_unavailable");
    }

    [Fact]
    public void Evaluate_HelperAction_ShouldBeStable_WhenBridgeReadyAndCatalogAvailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_tactical_entity", RuntimeMode.AnyTactical, ExecutionKind.Helper, "helperHookId", "entityId"));
        var session = BuildSession(
            RuntimeMode.TacticalLand,
            symbol: null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["helperBridgeState"] = " ready "
            });
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = new[] { "STORMTROOPER" },
            ["faction_catalog"] = new[] { "Empire" }
        };

        var entry = service.Evaluate(profile, session, catalog).Single(x => x.ActionId == "spawn_tactical_entity");
        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("helper_ready");
    }

    [Fact]
    public void Evaluate_ShouldRemainNonThrowing_WhenMechanicDetectionThrows()
    {
        var service = new ActionReliabilityService(new ThrowingModMechanicDetectionService());
        var profile = BuildProfile(
            Action("set_selected_hp", RuntimeMode.AnyTactical, ExecutionKind.Memory, "symbol", "floatValue"));
        var session = BuildSession(
            RuntimeMode.AnyTactical,
            new SymbolInfo(
                "selected_hp",
                (nint)0x1234,
                SymbolValueType.Float,
                AddressSource.Signature,
                Confidence: 0.93d,
                HealthStatus: SymbolHealthStatus.Healthy));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_selected_hp");
        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("healthy_signature");
    }

    [Fact]
    public void Evaluate_ShouldRemainNonThrowing_WhenMechanicDetectionThrowsOperationCanceled()
    {
        var service = new ActionReliabilityService(new CancelingModMechanicDetectionService());
        var profile = BuildProfile(
            Action("set_selected_hp", RuntimeMode.AnyTactical, ExecutionKind.Memory, "symbol", "floatValue"));
        var session = BuildSession(
            RuntimeMode.AnyTactical,
            new SymbolInfo(
                "selected_hp",
                (nint)0x1234,
                SymbolValueType.Float,
                AddressSource.Signature,
                Confidence: 0.93d,
                HealthStatus: SymbolHealthStatus.Healthy));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_selected_hp");
        entry.State.Should().Be(ActionReliabilityState.Stable);
    }

    [Fact]
    public void Evaluate_TwoParamOverload_ShouldWork()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_selected_hp", RuntimeMode.AnyTactical, ExecutionKind.Memory, "symbol", "floatValue"));
        var session = BuildSession(
            RuntimeMode.AnyTactical,
            new SymbolInfo(
                "selected_hp",
                (nint)0x1234,
                SymbolValueType.Float,
                AddressSource.Signature,
                Confidence: 0.93d,
                HealthStatus: SymbolHealthStatus.Healthy));

        var results = service.Evaluate(profile, session);
        results.Should().ContainSingle(x => x.ActionId == "set_selected_hp");
    }

    [Fact]
    public void Evaluate_ShouldThrow_WhenProfileIsNull()
    {
        var service = new ActionReliabilityService();
        var session = BuildSession(RuntimeMode.Galactic, null);

        var act = () => service.Evaluate(null!, session);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_ShouldThrow_WhenSessionIsNull()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile();

        var act = () => service.Evaluate(profile, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_SymbolActionWithNoSymbolRegistry_ShouldBeExperimental()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("unknown_action_no_mapping", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol", "intValue"));
        var session = BuildSession(RuntimeMode.Galactic, null);

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "unknown_action_no_mapping");
        entry.State.Should().Be(ActionReliabilityState.Experimental);
        entry.ReasonCode.Should().Be("symbol_hint_missing");
    }

    [Fact]
    public void Evaluate_SymbolActionWithUnresolvedSymbol_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol", "intValue"));
        var session = BuildSession(
            RuntimeMode.Galactic,
            new SymbolInfo(
                "credits",
                nint.Zero,
                SymbolValueType.Int32,
                AddressSource.None,
                Confidence: 0d,
                HealthStatus: SymbolHealthStatus.Unresolved));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_credits");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("symbol_unresolved");
    }

    [Fact]
    public void Evaluate_CriticalSymbolDegraded_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["criticalSymbols"] = "credits" },
            Action("set_credits", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol", "intValue"));
        var session = BuildSession(
            RuntimeMode.Galactic,
            new SymbolInfo(
                "credits",
                (nint)0x1234,
                SymbolValueType.Int32,
                AddressSource.Signature,
                Confidence: 0.50d,
                HealthStatus: SymbolHealthStatus.Degraded));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_credits");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("critical_symbol_degraded");
    }

    [Fact]
    public void Evaluate_NonSymbolAction_ShouldBeStable()
    {
        var service = new ActionReliabilityService();
        var noSymbolSchema = new JsonObject { ["required"] = new JsonArray(JsonValue.Create("intValue")!) };
        var action = new ActionSpec(
            "no_symbol_action",
            ActionCategory.Global,
            RuntimeMode.Unknown,
            ExecutionKind.Memory,
            noSymbolSchema,
            VerifyReadback: false,
            CooldownMs: 0);
        var profile = BuildProfile(action);
        var session = BuildSession(RuntimeMode.Galactic, null);

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "no_symbol_action");
        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("non_symbol_action");
    }

    [Fact]
    public void Evaluate_HealthyNonSignatureSymbol_ShouldBeStable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol", "intValue"));
        var session = BuildSession(
            RuntimeMode.Galactic,
            new SymbolInfo(
                "credits",
                (nint)0x1234,
                SymbolValueType.Int32,
                AddressSource.Fallback,
                Confidence: 0.93d,
                HealthStatus: SymbolHealthStatus.Healthy));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_credits");
        entry.State.Should().Be(ActionReliabilityState.Experimental);
        entry.ReasonCode.Should().Be("fallback_or_degraded");
    }

    [Fact]
    public void Evaluate_SymbolMissingFromSession_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol", "intValue"));
        var session = BuildSession(RuntimeMode.Galactic, null);

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_credits");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("symbol_unresolved");
    }

    [Theory]
    [InlineData(double.NaN, 0.50d)]
    [InlineData(double.PositiveInfinity, 0.50d)]
    [InlineData(double.NegativeInfinity, 0.50d)]
    [InlineData(-0.5d, 0d)]
    [InlineData(1.5d, 1d)]
    [InlineData(0.75d, 0.75d)]
    public void Evaluate_ShouldClampConfidenceValues(double rawConfidence, double expected)
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_game_speed", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol", "floatValue"));
        var session = BuildSession(
            RuntimeMode.Galactic,
            new SymbolInfo(
                "game_speed",
                (nint)0x1234,
                SymbolValueType.Float,
                AddressSource.Fallback,
                Confidence: rawConfidence,
                HealthStatus: SymbolHealthStatus.Degraded));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_game_speed");
        entry.Confidence.Should().Be(expected);
    }

    [Fact]
    public void Evaluate_MechanicSupportedAction_ShouldFallThroughToNormalEvaluation()
    {
        var service = new ActionReliabilityService(
            new StubModMechanicDetectionService(
                new ModMechanicReport(
                    ProfileId: "test",
                    GeneratedAtUtc: DateTimeOffset.UtcNow,
                    DependenciesSatisfied: true,
                    HelperBridgeReady: true,
                    ActionSupport: new[]
                    {
                        new ModMechanicSupport(
                            ActionId: "set_selected_hp",
                            Supported: true,
                            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                            Message: "supported",
                            Confidence: 0.95d)
                    },
                    Diagnostics: new Dictionary<string, object?>())));

        var profile = BuildProfile(
            Action("set_selected_hp", RuntimeMode.AnyTactical, ExecutionKind.Memory, "symbol", "floatValue"));
        var session = BuildSession(
            RuntimeMode.AnyTactical,
            new SymbolInfo("selected_hp", (nint)0x1234, SymbolValueType.Float, AddressSource.Signature, Confidence: 0.93d, HealthStatus: SymbolHealthStatus.Healthy));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_selected_hp");
        entry.State.Should().Be(ActionReliabilityState.Stable);
    }

    [Fact]
    public void Evaluate_FallbackFlagMissingFromProfile_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            Action("toggle_fog_reveal_patch_fallback", RuntimeMode.Unknown, ExecutionKind.CodePatch, "enable"));
        var session = BuildSession(RuntimeMode.Galactic, null);

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "toggle_fog_reveal_patch_fallback");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("fallback_disabled");
    }

    [Fact]
    public void Evaluate_ExperimentalFlagMissingFromProfile_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            Action("set_credits_extender_experimental", RuntimeMode.Unknown, ExecutionKind.Sdk, "symbol", "intValue"));
        var session = BuildSession(RuntimeMode.Galactic, null);

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_credits_extender_experimental");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("experimental_disabled");
    }

    [Fact]
    public void Evaluate_NullMetadata_ShouldParseEmptyCsv()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_unit_helper", RuntimeMode.Unknown, ExecutionKind.Helper, "helperHookId", "unitId", "entryMarker", "faction"));
        var session = BuildSession(RuntimeMode.TacticalLand, null, metadata: null);

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "spawn_unit_helper");
        entry.ReasonCode.Should().NotBe("dependency_soft_blocked");
    }

    [Fact]
    public void Evaluate_HelperBuildingActionWithBothCatalogs_ShouldBeStable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("place_planet_building", RuntimeMode.Galactic, ExecutionKind.Helper, "helperHookId", "entityId"));
        var session = BuildSession(
            RuntimeMode.Galactic,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["helperBridgeState"] = "ready" });
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["building_catalog"] = new[] { "BARRACKS" },
            ["faction_catalog"] = new[] { "EMPIRE" }
        };

        var entry = service.Evaluate(profile, session, catalog).Single(x => x.ActionId == "place_planet_building");
        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("helper_ready");
    }

    [Fact]
    public void Evaluate_HelperNonSpawnNonBuildingAction_WithReadyBridge_ShouldBeStable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_hero_state_helper", RuntimeMode.Galactic, ExecutionKind.Helper, "helperHookId", "globalKey", "intValue"));
        var session = BuildSession(
            RuntimeMode.Galactic,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["helperBridgeState"] = "ready" });

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_hero_state_helper");
        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("helper_ready");
    }

    [Fact]
    public void Evaluate_GalacticActionInTactical_ShouldBeModeMismatch()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_planet_owner_action", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol", "intValue"));
        var session = BuildSession(
            RuntimeMode.TacticalSpace,
            new SymbolInfo("planet_owner", (nint)0x1234, SymbolValueType.Int32, AddressSource.Signature, Confidence: 0.9d, HealthStatus: SymbolHealthStatus.Healthy));

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "set_planet_owner_action");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("mode_mismatch");
    }

    [Fact]
    public void DefaultConstructor_ShouldCreateServiceWithNullMechanicDetection()
    {
        var service = new ActionReliabilityService();
        service.Should().NotBeNull();
    }

    [Fact]
    public void Evaluate_ActionModeUnknown_WithSessionModeUnknown_ShouldNotBlockOnModeConstraints()
    {
        var service = new ActionReliabilityService();
        var noSymbolSchema = new JsonObject { ["required"] = new JsonArray(JsonValue.Create("intValue")!) };
        var action = new ActionSpec(
            "some_generic_action",
            ActionCategory.Global,
            RuntimeMode.Unknown,
            ExecutionKind.Memory,
            noSymbolSchema,
            VerifyReadback: false,
            CooldownMs: 0);
        var profile = BuildProfile(action);
        var session = BuildSession(RuntimeMode.Unknown, null);

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "some_generic_action");
        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("non_symbol_action");
    }

    [Fact]
    public void Evaluate_SpawnGalacticEntity_WithReadyBridgeNoCatalog_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_galactic_entity", RuntimeMode.Galactic, ExecutionKind.Helper, "helperHookId", "entityId"));
        var session = BuildSession(
            RuntimeMode.Galactic,
            null,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["helperBridgeState"] = "ready" });

        var entry = service.Evaluate(profile, session).Single(x => x.ActionId == "spawn_galactic_entity");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("catalog_unavailable");
    }

    private static ActionSpec Action(string id, RuntimeMode mode, ExecutionKind kind, params string[] required)
    {
        var requiredArray = new JsonArray(required.Select(x => (JsonNode)JsonValue.Create(x)!).ToArray());
        return new ActionSpec(
            id,
            ActionCategory.Unit,
            mode,
            kind,
            new JsonObject { ["required"] = requiredArray },
            VerifyReadback: false,
            CooldownMs: 0);
    }

    private static TrainerProfile BuildProfile(params ActionSpec[] actions)
    {
        return BuildProfile(
            new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            actions);
    }

    private static TrainerProfile BuildProfile(
        IReadOnlyDictionary<string, bool> featureFlags,
        params ActionSpec[] actions)
    {
        return BuildProfile(featureFlags, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), actions);
    }

    private static TrainerProfile BuildProfile(
        IReadOnlyDictionary<string, bool> featureFlags,
        IReadOnlyDictionary<string, string> metadata,
        params ActionSpec[] actions)
    {
        return new TrainerProfile(
            "test",
            "test",
            null,
            ExeTarget.Swfoc,
            null,
            Array.Empty<SignatureSet>(),
            new Dictionary<string, long>(),
            actions.ToDictionary(x => x.Id, x => x, StringComparer.OrdinalIgnoreCase),
            featureFlags,
            Array.Empty<CatalogSource>(),
            "test",
            Array.Empty<HelperHookSpec>(),
            metadata);
    }

    private static AttachSession BuildSession(RuntimeMode mode, SymbolInfo? symbol, IReadOnlyDictionary<string, string>? metadata = null)
    {
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        if (symbol is not null)
        {
            symbols[symbol.Name] = symbol;
        }

        return new AttachSession(
            "test",
            new ProcessMetadata(
                123,
                "swfoc",
                @"C:\Games\swfoc.exe",
                null,
                ExeTarget.Swfoc,
                mode,
                metadata,
                null),
            new ProfileBuild("test", "test", @"C:\Games\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(symbols),
            DateTimeOffset.UtcNow);
    }

    private sealed class StubModMechanicDetectionService : IModMechanicDetectionService
    {
        private readonly ModMechanicReport _report;

        public StubModMechanicDetectionService(ModMechanicReport report)
        {
            _report = report;
        }

        public Task<ModMechanicReport> DetectAsync(
            TrainerProfile profile,
            AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_report with { ProfileId = profile.Id });
        }
    }

    private sealed class ThrowingModMechanicDetectionService : IModMechanicDetectionService
    {
        public Task<ModMechanicReport> DetectAsync(
            TrainerProfile profile,
            AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
            CancellationToken cancellationToken)
        {
            _ = profile;
            _ = session;
            _ = catalog;
            _ = cancellationToken;
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class CancelingModMechanicDetectionService : IModMechanicDetectionService
    {
        public Task<ModMechanicReport> DetectAsync(
            TrainerProfile profile,
            AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
            CancellationToken cancellationToken)
        {
            _ = profile;
            _ = session;
            _ = catalog;
            _ = cancellationToken;
            throw new OperationCanceledException("canceled");
        }
    }
}
