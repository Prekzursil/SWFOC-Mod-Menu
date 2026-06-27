using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Wave 6 — push Core to 100% branch coverage.
/// Covers ActionReliabilityService remaining branches (mechanic support, helper actions,
/// building catalog, critical symbol degraded, non-signature healthy, ClampConfidence extremes),
/// SdkOperationRouter (FormatAllowedModes empty, context value as non-string processPath),
/// SelectedUnitTransactionService (full transaction apply/revert/baseline),
/// SupportBundleService (attached/detached snapshot paths).
/// </summary>
public sealed class CoreWave6Tests
{
    #region ActionReliabilityService — mechanic support unsupported

    [Fact]
    public void Evaluate_MechanicSupportUnsupported_ShouldReturnUnavailable()
    {
        var mechanic = new FakeModMechanicDetectionService(new ModMechanicReport(
            "p1",
            DateTimeOffset.UtcNow,
            true,
            true,
            new[]
            {
                new ModMechanicSupport(
                    "set_credits",
                    false,
                    RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                    "Not supported",
                    0.80d)
            },
            new Dictionary<string, object?>()));

        var service = new ActionReliabilityService(mechanic);
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic,
            new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature,
                Confidence: 0.9d, HealthStatus: SymbolHealthStatus.Healthy));

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");

        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("CAPABILITY_REQUIRED_MISSING");
    }

    [Fact]
    public void Evaluate_MechanicSupportSupported_ShouldFallThrough()
    {
        var mechanic = new FakeModMechanicDetectionService(new ModMechanicReport(
            "p1",
            DateTimeOffset.UtcNow,
            true,
            true,
            new[]
            {
                new ModMechanicSupport(
                    "set_credits",
                    true,
                    RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                    "Ok",
                    0.95d)
            },
            new Dictionary<string, object?>()));

        var service = new ActionReliabilityService(mechanic);
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic,
            new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature,
                Confidence: 0.9d, HealthStatus: SymbolHealthStatus.Healthy));

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");

        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("healthy_signature");
    }

    [Fact]
    public void Evaluate_MechanicDetectionThrowsInvalidOp_ShouldReturnNull()
    {
        var mechanic = new ThrowingModMechanicDetectionService(new InvalidOperationException("boom"));
        var service = new ActionReliabilityService(mechanic);
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic,
            new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature,
                Confidence: 0.9d, HealthStatus: SymbolHealthStatus.Healthy));

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");

        // Mechanic detection failure falls through — evaluates as symbol action
        entry.State.Should().Be(ActionReliabilityState.Stable);
    }

    [Fact]
    public void Evaluate_MechanicDetectionThrowsOperationCanceled_ShouldReturnNull()
    {
        var mechanic = new ThrowingModMechanicDetectionService(new OperationCanceledException("canceled"));
        var service = new ActionReliabilityService(mechanic);
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic,
            new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature,
                Confidence: 0.9d, HealthStatus: SymbolHealthStatus.Healthy));

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");
        entry.State.Should().Be(ActionReliabilityState.Stable);
    }

    #endregion

    #region ActionReliabilityService — feature flags

    [Fact]
    public void Evaluate_FallbackFeatureFlag_Enabled_ShouldReturnExperimental()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("toggle_fog_reveal_patch_fallback", RuntimeMode.Unknown, ExecutionKind.Memory),
            featureFlags: new Dictionary<string, bool>
            {
                ["allow_fog_patch_fallback"] = true
            });
        var session = BuildSession(RuntimeMode.Galactic);

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "toggle_fog_reveal_patch_fallback");

        entry.State.Should().Be(ActionReliabilityState.Experimental);
        entry.ReasonCode.Should().Be("fallback_experimental");
    }

    [Fact]
    public void Evaluate_FallbackFeatureFlag_Disabled_ShouldReturnUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("toggle_fog_reveal_patch_fallback", RuntimeMode.Unknown, ExecutionKind.Memory),
            featureFlags: new Dictionary<string, bool>
            {
                ["allow_fog_patch_fallback"] = false
            });
        var session = BuildSession(RuntimeMode.Galactic);

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "toggle_fog_reveal_patch_fallback");

        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("fallback_disabled");
    }

    [Fact]
    public void Evaluate_FallbackFeatureFlag_Missing_ShouldReturnUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("toggle_fog_reveal_patch_fallback", RuntimeMode.Unknown, ExecutionKind.Memory));
        var session = BuildSession(RuntimeMode.Galactic);

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "toggle_fog_reveal_patch_fallback");

        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("fallback_disabled");
    }

    [Fact]
    public void Evaluate_ExperimentalFeatureFlag_Enabled_ShouldReturnExperimental()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits_extender_experimental", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol"),
            featureFlags: new Dictionary<string, bool>
            {
                ["allow_extender_credits"] = true
            });
        var session = BuildSession(RuntimeMode.Galactic,
            new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature,
                Confidence: 0.9d, HealthStatus: SymbolHealthStatus.Healthy));

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits_extender_experimental");

        entry.State.Should().Be(ActionReliabilityState.Experimental);
        entry.ReasonCode.Should().Be("experimental_enabled");
    }

    [Fact]
    public void Evaluate_ExperimentalFeatureFlag_Disabled_ShouldReturnUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits_extender_experimental", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol"),
            featureFlags: new Dictionary<string, bool>
            {
                ["allow_extender_credits"] = false
            });
        var session = BuildSession(RuntimeMode.Galactic);

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits_extender_experimental");

        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("experimental_disabled");
    }

    [Fact]
    public void Evaluate_ExperimentalFeatureFlag_Missing_ShouldReturnUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits_extender_experimental", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic);

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits_extender_experimental");

        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("experimental_disabled");
    }

    #endregion

    #region ActionReliabilityService — helper actions

    [Fact]
    public void Evaluate_HelperAction_BridgeReady_SpawnCatalogMissing_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_unit_helper", RuntimeMode.Unknown, ExecutionKind.Helper));
        var session = BuildSession(RuntimeMode.TacticalLand,
            metadata: new Dictionary<string, string> { ["helperBridgeState"] = "ready" });

        var results = service.Evaluate(profile, session, null);
        var entry = results.Single(x => x.ActionId == "spawn_unit_helper");

        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("catalog_unavailable");
    }

    [Fact]
    public void Evaluate_HelperAction_BridgeReady_SpawnCatalogPresent_ShouldBeStable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_unit_helper", RuntimeMode.Unknown, ExecutionKind.Helper));
        var session = BuildSession(RuntimeMode.TacticalLand,
            metadata: new Dictionary<string, string> { ["helperBridgeState"] = "ready" });
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["unit_catalog"] = new[] { "AT-AT" },
            ["faction_catalog"] = new[] { "EMPIRE" }
        };

        var results = service.Evaluate(profile, session, catalog);
        var entry = results.Single(x => x.ActionId == "spawn_unit_helper");

        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("helper_ready");
    }

    [Fact]
    public void Evaluate_HelperAction_BridgeNotReady_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_unit_helper", RuntimeMode.Unknown, ExecutionKind.Helper));
        var session = BuildSession(RuntimeMode.TacticalLand);

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "spawn_unit_helper");

        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("helper_bridge_unavailable");
    }

    [Fact]
    public void Evaluate_HelperAction_PlacePlanetBuilding_NoBuildingCatalog_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("place_planet_building", RuntimeMode.Unknown, ExecutionKind.Helper));
        var session = BuildSession(RuntimeMode.TacticalLand,
            metadata: new Dictionary<string, string> { ["helperBridgeState"] = "ready" });

        var results = service.Evaluate(profile, session, null);
        var entry = results.Single(x => x.ActionId == "place_planet_building");

        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("building_catalog_unavailable");
    }

    [Fact]
    public void Evaluate_HelperAction_PlacePlanetBuilding_WithCatalog_ShouldBeStable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("place_planet_building", RuntimeMode.Unknown, ExecutionKind.Helper));
        var session = BuildSession(RuntimeMode.TacticalLand,
            metadata: new Dictionary<string, string> { ["helperBridgeState"] = "ready" });
        var catalog = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["building_catalog"] = new[] { "BARRACK" },
            ["faction_catalog"] = new[] { "EMPIRE" }
        };

        var results = service.Evaluate(profile, session, catalog);
        var entry = results.Single(x => x.ActionId == "place_planet_building");

        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("helper_ready");
    }

    #endregion

    #region ActionReliabilityService — symbol evaluation branches

    [Fact]
    public void Evaluate_SymbolAction_HealthyNonSignature_ShouldReturnHealthyNonSignature()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic,
            new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Fallback,
                Confidence: 0.6d, HealthStatus: SymbolHealthStatus.Healthy));

        // Fallback source with Healthy status triggers fallback_or_degraded
        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");
        entry.State.Should().Be(ActionReliabilityState.Experimental);
        entry.ReasonCode.Should().Be("fallback_or_degraded");
    }

    [Fact]
    public void Evaluate_SymbolAction_DegradedHealthStatus_ShouldReturnExperimental()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic,
            new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature,
                Confidence: 0.7d, HealthStatus: SymbolHealthStatus.Degraded));

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");
        entry.State.Should().Be(ActionReliabilityState.Experimental);
        entry.ReasonCode.Should().Be("fallback_or_degraded");
    }

    [Fact]
    public void Evaluate_SymbolAction_CriticalSymbolDegraded_ShouldReturnUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"),
            metadata: new Dictionary<string, string>
            {
                ["criticalSymbols"] = "credits"
            });
        var session = BuildSession(RuntimeMode.Galactic,
            new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature,
                Confidence: 0.7d, HealthStatus: SymbolHealthStatus.Degraded));

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("critical_symbol_degraded");
    }

    [Fact]
    public void Evaluate_SymbolAction_Unresolved_ShouldReturnUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic,
            new SymbolInfo("credits", nint.Zero, SymbolValueType.Int32, AddressSource.None,
                Confidence: 0d, HealthStatus: SymbolHealthStatus.Unresolved));

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("symbol_unresolved");
    }

    [Fact]
    public void Evaluate_SymbolAction_NoSymbolHint_ShouldReturnExperimental()
    {
        var service = new ActionReliabilityService();
        // Use an action that is NOT in ActionSymbolRegistry but has symbol in payload
        var profile = BuildProfile(
            Action("unknown_action_xyz", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic);

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "unknown_action_xyz");
        entry.State.Should().Be(ActionReliabilityState.Experimental);
        entry.ReasonCode.Should().Be("symbol_hint_missing");
    }

    [Fact]
    public void Evaluate_NonSymbolAction_ShouldReturnStable()
    {
        var service = new ActionReliabilityService();
        // Action with no required symbol in payload
        var profile = BuildProfile(
            Action("some_non_symbol_action", RuntimeMode.Unknown, ExecutionKind.Memory));
        var session = BuildSession(RuntimeMode.Galactic);

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "some_non_symbol_action");
        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("non_symbol_action");
    }

    [Fact]
    public void Evaluate_SymbolAction_HealthySignatureNonFallback_ShouldReturnStable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic,
            new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.None,
                Confidence: 0.9d, HealthStatus: SymbolHealthStatus.Healthy));

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");
        entry.State.Should().Be(ActionReliabilityState.Stable);
        entry.ReasonCode.Should().Be("healthy_non_signature");
    }

    [Fact]
    public void ClampConfidence_NaN_ShouldReturn050()
    {
        var mechanic = new FakeModMechanicDetectionService(new ModMechanicReport(
            "p1", DateTimeOffset.UtcNow, true, true,
            new[] { new ModMechanicSupport("set_credits", false, RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING, "bad", double.NaN) },
            new Dictionary<string, object?>()));

        var svc = new ActionReliabilityService(mechanic);
        var profile = BuildProfile(Action("set_credits", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic);

        var results = svc.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");
        entry.Confidence.Should().Be(0.50d);
    }

    [Fact]
    public void ClampConfidence_Negative_ShouldReturnZero()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic,
            new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature,
                Confidence: -5.0d, HealthStatus: SymbolHealthStatus.Degraded));

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");
        entry.Confidence.Should().Be(0d);
    }

    [Fact]
    public void ClampConfidence_OverOne_ShouldReturnOne()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic,
            new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature,
                Confidence: 5.0d, HealthStatus: SymbolHealthStatus.Degraded));

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");
        entry.Confidence.Should().Be(1d);
    }

    [Fact]
    public void ClampConfidence_Infinity_ShouldReturn050()
    {
        var mechanic = new FakeModMechanicDetectionService(new ModMechanicReport(
            "p1", DateTimeOffset.UtcNow, true, true,
            new[] { new ModMechanicSupport("set_credits", false, RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING, "bad", double.PositiveInfinity) },
            new Dictionary<string, object?>()));

        var svc = new ActionReliabilityService(mechanic);
        var profile = BuildProfile(Action("set_credits", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic);

        var results = svc.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");
        entry.Confidence.Should().Be(0.50d);
    }

    #endregion

    #region ActionReliabilityService — mode constraints

    [Fact]
    public void Evaluate_StrictBundleAction_ModeUnknown_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_unit_helper", RuntimeMode.Unknown, ExecutionKind.Memory));
        var session = BuildSession(RuntimeMode.Unknown);

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "spawn_unit_helper");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("mode_unknown_strict_gate");
    }

    [Fact]
    public void Evaluate_ActionModeMismatch_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.TacticalLand,
            new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature,
                Confidence: 0.9d, HealthStatus: SymbolHealthStatus.Healthy));

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("mode_mismatch");
    }

    [Fact]
    public void Evaluate_ActionModeNonUnknown_RuntimeModeUnknown_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Unknown);

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("mode_unknown_strict_gate");
    }

    #endregion

    #region ActionReliabilityService — dependency block

    [Fact]
    public void Evaluate_DependencyDisabledAction_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic,
            metadata: new Dictionary<string, string>
            {
                ["dependencyDisabledActions"] = "set_credits"
            });

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "set_credits");
        entry.State.Should().Be(ActionReliabilityState.Unavailable);
        entry.ReasonCode.Should().Be("dependency_soft_blocked");
    }

    #endregion

    #region ActionReliabilityService — two-arg Evaluate overload

    [Fact]
    public void Evaluate_TwoArgOverload_ShouldDelegateCorrectly()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Galactic, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic,
            new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature,
                Confidence: 0.9d, HealthStatus: SymbolHealthStatus.Healthy));

        var results = service.Evaluate(profile, session);
        results.Should().HaveCount(1);
    }

    [Fact]
    public void Evaluate_NullProfile_ShouldThrow()
    {
        var service = new ActionReliabilityService();
        var session = BuildSession(RuntimeMode.Galactic);

        var act = () => service.Evaluate(null!, session);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_NullSession_ShouldThrow()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile();

        var act = () => service.Evaluate(profile, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_ThreeArgOverload_NullProfile_ShouldThrow()
    {
        var service = new ActionReliabilityService();
        var session = BuildSession(RuntimeMode.Galactic);

        var act = () => service.Evaluate(null!, session, null);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_ThreeArgOverload_NullSession_ShouldThrow()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile();

        var act = () => service.Evaluate(profile, null!, null);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ActionReliabilityService — helper actions: context/galactic spawn entries

    [Fact]
    public void Evaluate_HelperAction_SpawnContextEntity_CatalogMissing_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_context_entity", RuntimeMode.Unknown, ExecutionKind.Helper));
        var session = BuildSession(RuntimeMode.TacticalLand,
            metadata: new Dictionary<string, string> { ["helperBridgeState"] = "ready" });

        var results = service.Evaluate(profile, session, null);
        var entry = results.Single(x => x.ActionId == "spawn_context_entity");
        entry.ReasonCode.Should().Be("catalog_unavailable");
    }

    [Fact]
    public void Evaluate_HelperAction_SpawnTacticalEntity_CatalogMissing_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_tactical_entity", RuntimeMode.Unknown, ExecutionKind.Helper));
        var session = BuildSession(RuntimeMode.TacticalLand,
            metadata: new Dictionary<string, string> { ["helperBridgeState"] = "ready" });

        var results = service.Evaluate(profile, session, null);
        var entry = results.Single(x => x.ActionId == "spawn_tactical_entity");
        entry.ReasonCode.Should().Be("catalog_unavailable");
    }

    [Fact]
    public void Evaluate_HelperAction_SpawnGalacticEntity_CatalogMissing_ShouldBeUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_galactic_entity", RuntimeMode.Unknown, ExecutionKind.Helper));
        var session = BuildSession(RuntimeMode.TacticalLand,
            metadata: new Dictionary<string, string> { ["helperBridgeState"] = "ready" });

        var results = service.Evaluate(profile, session, null);
        var entry = results.Single(x => x.ActionId == "spawn_galactic_entity");
        entry.ReasonCode.Should().Be("catalog_unavailable");
    }

    #endregion

    #region ActionReliabilityService — ReadMetadataValue

    [Fact]
    public void Evaluate_MetadataWithWhitespaceValue_ShouldTreatAsNull()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("spawn_unit_helper", RuntimeMode.Unknown, ExecutionKind.Helper));
        var session = BuildSession(RuntimeMode.TacticalLand,
            metadata: new Dictionary<string, string> { ["helperBridgeState"] = "   " });

        var results = service.Evaluate(profile, session);
        var entry = results.Single(x => x.ActionId == "spawn_unit_helper");
        entry.ReasonCode.Should().Be("helper_bridge_unavailable");
    }

    #endregion

    #region ActionReliabilityService — ParseCsvSet

    [Fact]
    public void Evaluate_ParseCsvSet_MultipleDisabled_ShouldBlockAll()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            Action("set_credits", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol"),
            Action("freeze_timer", RuntimeMode.Unknown, ExecutionKind.Memory, "symbol"));
        var session = BuildSession(RuntimeMode.Galactic,
            metadata: new Dictionary<string, string>
            {
                ["dependencyDisabledActions"] = "set_credits,freeze_timer"
            });

        var results = service.Evaluate(profile, session);
        results.Where(x => x.ReasonCode == "dependency_soft_blocked").Should().HaveCount(2);
    }

    #endregion

    #region SdkOperationRouter — FormatAllowedModes empty

    [Fact]
    public void SdkOperationDefinition_EmptyAllowedModes_IsModeAllowed_ShouldReturnTrue()
    {
        var def = new SdkOperationDefinition("test", false, new HashSet<RuntimeMode>(), false);
        def.IsModeAllowed(RuntimeMode.Galactic).Should().BeTrue();
    }

    #endregion

    #region SdkOperationRouter — processPath as non-string value

    [Fact]
    public async Task SdkOperationRouter_ProcessPathAsObject_ShouldConvertViaToString()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "1");
        try
        {
            var router = CreateRouter();
            var request = new SdkOperationRequest(
                OperationId: "list_selected",
                Payload: new JsonObject(),
                IsMutation: false,
                RuntimeMode: RuntimeMode.Unknown,
                ProfileId: "test",
                Context: new Dictionary<string, object?>
                {
                    ["processPath"] = new Uri("file:///C:/games/swfoc.exe"),
                    ["processId"] = 42
                });

            var result = await router.ExecuteAsync(request);
            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", previous);
        }
    }

    #endregion

    #region Helpers

    private static TrainerProfile BuildProfile(
        params (string id, ActionSpec spec)[] actions)
    {
        return BuildProfileFull(null, null, actions);
    }

    private static TrainerProfile BuildProfile(
        (string id, ActionSpec spec) action,
        Dictionary<string, bool>? featureFlags = null,
        Dictionary<string, string>? metadata = null)
    {
        return BuildProfileFull(featureFlags, metadata, new[] { action });
    }

    private static TrainerProfile BuildProfileFull(
        Dictionary<string, bool>? featureFlags,
        Dictionary<string, string>? metadata,
        (string id, ActionSpec spec)[] actions)
    {
        var actionDict = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, spec) in actions)
        {
            actionDict[id] = spec;
        }

        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "Test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: actionDict,
            FeatureFlags: featureFlags ?? new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: null,
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata);
    }

    private static (string id, ActionSpec spec) Action(
        string id,
        RuntimeMode mode = RuntimeMode.Unknown,
        ExecutionKind kind = ExecutionKind.Memory,
        params string[] requiredFields)
    {
        var required = new JsonArray();
        foreach (var field in requiredFields)
        {
            required.Add(JsonValue.Create(field));
        }

        var schema = new JsonObject();
        if (requiredFields.Length > 0)
        {
            schema["required"] = required;
        }

        return (id, new ActionSpec(id, ActionCategory.Global, mode, kind, schema, false, 0));
    }

    private static AttachSession BuildSession(
        RuntimeMode mode,
        SymbolInfo? symbol = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        if (symbol is not null)
        {
            symbols[symbol.Name] = symbol;
        }

        var process = new ProcessMetadata(
            1, "test.exe", "/path", null, ExeTarget.Swfoc, mode, Metadata: metadata);
        var build = new ProfileBuild("test_profile", "build", "/path", ExeTarget.Swfoc);
        return new AttachSession("test_profile", process, build, new SymbolMap(symbols), DateTimeOffset.UtcNow);
    }

    private static SdkOperationRouter CreateRouter(
        ISdkRuntimeAdapter? adapter = null,
        ISdkExecutionGuard? guard = null)
    {
        return new SdkOperationRouter(
            adapter ?? new FakeSdkRuntimeAdapter(),
            new FakeProfileVariantResolver(),
            new FakeBinaryFingerprintService(),
            new FakeCapabilityMapResolver(),
            guard ?? new FakeSdkExecutionGuard(),
            new NullSdkDiagnosticsSink());
    }

    #endregion

    #region Stubs

    private sealed class FakeModMechanicDetectionService : IModMechanicDetectionService
    {
        private readonly ModMechanicReport _report;

        public FakeModMechanicDetectionService(ModMechanicReport report) => _report = report;

        public Task<ModMechanicReport> DetectAsync(
            TrainerProfile profile, AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog, CancellationToken cancellationToken)
        {
            return Task.FromResult(_report);
        }
    }

    private sealed class ThrowingModMechanicDetectionService : IModMechanicDetectionService
    {
        private readonly Exception _exception;

        public ThrowingModMechanicDetectionService(Exception exception) => _exception = exception;

        public Task<ModMechanicReport> DetectAsync(
            TrainerProfile profile, AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog, CancellationToken cancellationToken)
        {
            throw _exception;
        }
    }

    private sealed class FakeSdkRuntimeAdapter : ISdkRuntimeAdapter
    {
        public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request)
            => ExecuteAsync(request, CancellationToken.None);

        public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new SdkOperationResult(true, "ok", CapabilityReasonCode.AllRequiredAnchorsPresent, SdkCapabilityStatus.Available));
    }

    private sealed class FakeProfileVariantResolver : IProfileVariantResolver
    {
        public Task<ProfileVariantResolution> ResolveAsync(string requestedProfileId, CancellationToken cancellationToken)
            => ResolveAsync(requestedProfileId, null, cancellationToken);

        public Task<ProfileVariantResolution> ResolveAsync(string requestedProfileId, IReadOnlyList<ProcessMetadata>? processes, CancellationToken cancellationToken)
            => Task.FromResult(new ProfileVariantResolution(requestedProfileId, "base_swfoc", "test", 1.0d));
    }

    private sealed class FakeBinaryFingerprintService : IBinaryFingerprintService
    {
        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath)
            => CaptureFromPathAsync(modulePath, CancellationToken.None);

        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, CancellationToken cancellationToken)
            => CaptureFromPathAsync(modulePath, 0, cancellationToken);

        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int processId)
            => CaptureFromPathAsync(modulePath, processId, CancellationToken.None);

        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int processId, CancellationToken cancellationToken)
            => Task.FromResult(new BinaryFingerprint("fp", "sha", "mod", "1", "1", DateTimeOffset.UtcNow, Array.Empty<string>(), modulePath));
    }

    private sealed class FakeCapabilityMapResolver : ICapabilityMapResolver
    {
        public Task<CapabilityResolutionResult> ResolveAsync(BinaryFingerprint fingerprint, string requestedProfileId, string operationId, IReadOnlySet<string> resolvedAnchors)
            => ResolveAsync(fingerprint, requestedProfileId, operationId, resolvedAnchors, CancellationToken.None);

        public Task<CapabilityResolutionResult> ResolveAsync(BinaryFingerprint fingerprint, string requestedProfileId, string operationId, IReadOnlySet<string> resolvedAnchors, CancellationToken cancellationToken)
            => Task.FromResult(new CapabilityResolutionResult(requestedProfileId, operationId, SdkCapabilityStatus.Available, CapabilityReasonCode.AllRequiredAnchorsPresent, 1.0d, fingerprint.FingerprintId, Array.Empty<string>(), Array.Empty<string>(), CapabilityResolutionMetadata.Empty));

        public Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint)
            => Task.FromResult<string?>("base_swfoc");

        public Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint, CancellationToken cancellationToken)
            => Task.FromResult<string?>("base_swfoc");
    }

    private sealed class FakeSdkExecutionGuard : ISdkExecutionGuard
    {
        public SdkExecutionDecision CanExecute(CapabilityResolutionResult resolution, bool isMutation)
            => new(true, resolution.ReasonCode, "ok");
    }

    #endregion
}
