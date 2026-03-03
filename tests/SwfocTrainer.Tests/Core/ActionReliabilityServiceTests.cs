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
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
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
}
