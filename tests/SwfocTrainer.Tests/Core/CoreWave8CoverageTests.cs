using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Wave 8 coverage: remaining branches in ActionReliabilityService, ActionSymbolRegistry,
/// SelectedUnitDraft.IsEmpty, and ClampConfidence edge cases.
/// </summary>
public sealed class CoreWave8CoverageTests
{
    #region ActionReliabilityService — constructor and null guards

    [Fact]
    public void Evaluate_ShouldThrow_WhenProfileIsNull()
    {
        var service = new ActionReliabilityService();
        var act = () => service.Evaluate(null!, BuildSession());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_ShouldThrow_WhenSessionIsNull()
    {
        var service = new ActionReliabilityService();
        var act = () => service.Evaluate(BuildProfile(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_TwoParam_ShouldThrow_WhenProfileIsNull()
    {
        var service = new ActionReliabilityService();
        var act = () => service.Evaluate(null!, BuildSession());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Evaluate_ThreeParam_ShouldThrow_WhenSessionIsNull()
    {
        var service = new ActionReliabilityService();
        var act = () => service.Evaluate(BuildProfile(), null!, null);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region ActionReliabilityService — mechanic support (unsupported)

    [Fact]
    public void Evaluate_ShouldReturnUnavailable_WhenMechanicSupportIsUnsupported()
    {
        var mechanicService = new StubModMechanicDetectionService(new ModMechanicReport(
            ProfileId: "test",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            DependenciesSatisfied: true,
            HelperBridgeReady: true,
            ActionSupport: new[]
            {
                new ModMechanicSupport("test_action", Supported: false, RuntimeReasonCode.UNKNOWN, "not supported", 0.5d)
            },
            Diagnostics: new Dictionary<string, object?>()));

        var service = new ActionReliabilityService(mechanicService);
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["test_action"] = BuildAction()
        });
        var session = BuildSession();

        var results = service.Evaluate(profile, session, null);
        results.Should().ContainSingle();
        results[0].State.Should().Be(ActionReliabilityState.Unavailable);
    }

    [Fact]
    public void Evaluate_ShouldSkipMechanic_WhenMechanicSupportIsSupported()
    {
        var mechanicService = new StubModMechanicDetectionService(new ModMechanicReport(
            ProfileId: "test",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            DependenciesSatisfied: true,
            HelperBridgeReady: true,
            ActionSupport: new[]
            {
                new ModMechanicSupport("test_action", Supported: true, RuntimeReasonCode.UNKNOWN, "ok", 0.85d)
            },
            Diagnostics: new Dictionary<string, object?>()));

        var service = new ActionReliabilityService(mechanicService);
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["test_action"] = BuildAction()
        });
        var session = BuildSession();

        var results = service.Evaluate(profile, session, null);
        results.Should().ContainSingle();
        // When mechanic is supported, it falls through to next evaluator
        results[0].State.Should().NotBe(ActionReliabilityState.Unavailable);
    }

    [Fact]
    public void Evaluate_ShouldHandleNull_WhenMechanicServiceThrowsInvalidOperation()
    {
        var mechanicService = new ThrowingModMechanicDetectionService(new InvalidOperationException("boom"));
        var service = new ActionReliabilityService(mechanicService);
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["test_action"] = BuildAction()
        });
        var session = BuildSession();

        // Should not throw, returns results from other evaluators
        var results = service.Evaluate(profile, session, null);
        results.Should().ContainSingle();
    }

    [Fact]
    public void Evaluate_ShouldHandleNull_WhenMechanicServiceThrowsOperationCanceled()
    {
        var mechanicService = new ThrowingModMechanicDetectionService(new OperationCanceledException("cancel"));
        var service = new ActionReliabilityService(mechanicService);
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["test_action"] = BuildAction()
        });

        var results = service.Evaluate(profile, session: BuildSession(), catalog: null);
        results.Should().ContainSingle();
    }

    #endregion

    #region ActionReliabilityService — fallback feature flags

    [Fact]
    public void Evaluate_ShouldReturnUnavailable_WhenFallbackFeatureFlagIsDisabled()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            actions: new Dictionary<string, ActionSpec>
            {
                ["toggle_fog_reveal_patch_fallback"] = BuildAction()
            },
            featureFlags: new Dictionary<string, bool>
            {
                ["allow_fog_patch_fallback"] = false
            });

        var results = service.Evaluate(profile, BuildSession());
        results.Should().ContainSingle();
        results[0].ReasonCode.Should().Be("fallback_disabled");
    }

    [Fact]
    public void Evaluate_ShouldReturnExperimental_WhenFallbackFeatureFlagIsEnabled()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            actions: new Dictionary<string, ActionSpec>
            {
                ["toggle_fog_reveal_patch_fallback"] = BuildAction()
            },
            featureFlags: new Dictionary<string, bool>
            {
                ["allow_fog_patch_fallback"] = true
            });

        var results = service.Evaluate(profile, BuildSession());
        results.Should().ContainSingle();
        results[0].State.Should().Be(ActionReliabilityState.Experimental);
        results[0].ReasonCode.Should().Be("fallback_experimental");
    }

    #endregion

    #region ActionReliabilityService — experimental feature flags

    [Fact]
    public void Evaluate_ShouldReturnUnavailable_WhenExperimentalFlagIsDisabled()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            actions: new Dictionary<string, ActionSpec>
            {
                ["set_credits_extender_experimental"] = BuildAction()
            },
            featureFlags: new Dictionary<string, bool>
            {
                ["allow_extender_credits"] = false
            });

        var results = service.Evaluate(profile, BuildSession());
        results.Should().ContainSingle();
        results[0].ReasonCode.Should().Be("experimental_disabled");
    }

    [Fact]
    public void Evaluate_ShouldReturnExperimental_WhenExperimentalFlagIsEnabled()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(
            actions: new Dictionary<string, ActionSpec>
            {
                ["set_credits_extender_experimental"] = BuildAction()
            },
            featureFlags: new Dictionary<string, bool>
            {
                ["allow_extender_credits"] = true
            });

        var results = service.Evaluate(profile, BuildSession());
        results.Should().ContainSingle();
        results[0].State.Should().Be(ActionReliabilityState.Experimental);
        results[0].ReasonCode.Should().Be("experimental_enabled");
    }

    #endregion

    #region ActionReliabilityService — dependency block

    [Fact]
    public void Evaluate_ShouldReturnUnavailable_WhenDependencyBlocked()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["blocked_action"] = BuildAction()
        });
        var session = BuildSession(metadata: new Dictionary<string, string>
        {
            ["dependencyDisabledActions"] = "blocked_action"
        });

        var results = service.Evaluate(profile, session);
        results.Should().ContainSingle();
        results[0].ReasonCode.Should().Be("dependency_soft_blocked");
    }

    #endregion

    #region ActionReliabilityService — mode constraints

    [Fact]
    public void Evaluate_ShouldReturnUnavailable_WhenModeIsUnknownAndStrictBundle()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["spawn_unit_helper"] = BuildAction(mode: RuntimeMode.Unknown, kind: ExecutionKind.Helper)
        });
        var session = BuildSession(mode: RuntimeMode.Unknown, metadata: new Dictionary<string, string>
        {
            ["helperBridgeState"] = "ready"
        });

        var results = service.Evaluate(profile, session);
        results.Should().ContainSingle();
        results[0].ReasonCode.Should().Be("mode_unknown_strict_gate");
    }

    [Fact]
    public void Evaluate_ShouldReturnUnavailable_WhenModeMismatch()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["galactic_only"] = BuildAction(mode: RuntimeMode.Galactic)
        });
        var session = BuildSession(mode: RuntimeMode.TacticalLand);

        var results = service.Evaluate(profile, session);
        results.Should().ContainSingle();
        results[0].ReasonCode.Should().Be("mode_mismatch");
    }

    [Fact]
    public void Evaluate_ShouldAllowAnyTactical_WhenModeIsTacticalLand()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["tac_action"] = BuildAction(mode: RuntimeMode.AnyTactical)
        });
        var session = BuildSession(mode: RuntimeMode.TacticalLand);

        var results = service.Evaluate(profile, session);
        results.Should().ContainSingle();
        results[0].State.Should().NotBe(ActionReliabilityState.Unavailable);
    }

    [Fact]
    public void Evaluate_ShouldReturnUnavailable_WhenModeUnknownAndActionRequiresMode()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["needs_mode"] = BuildAction(mode: RuntimeMode.Galactic)
        });
        var session = BuildSession(mode: RuntimeMode.Unknown);

        var results = service.Evaluate(profile, session);
        results.Should().ContainSingle();
        results[0].ReasonCode.Should().Be("mode_unknown_strict_gate");
    }

    #endregion

    #region ActionReliabilityService — helper bridge

    [Fact]
    public void Evaluate_ShouldReturnUnavailable_WhenHelperBridgeNotReady()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["helper_action"] = BuildAction(kind: ExecutionKind.Helper)
        });
        var session = BuildSession(mode: RuntimeMode.Galactic);

        var results = service.Evaluate(profile, session);
        results.Should().ContainSingle();
        results[0].ReasonCode.Should().Be("helper_bridge_unavailable");
    }

    [Fact]
    public void Evaluate_ShouldReturnUnavailable_WhenSpawnCatalogUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["spawn_unit_helper"] = BuildAction(mode: RuntimeMode.Galactic, kind: ExecutionKind.Helper)
        });
        var session = BuildSession(mode: RuntimeMode.Galactic, metadata: new Dictionary<string, string>
        {
            ["helperBridgeState"] = "ready"
        });

        // No catalog provided
        var results = service.Evaluate(profile, session, null);
        results.Should().ContainSingle();
        results[0].ReasonCode.Should().Be("catalog_unavailable");
    }

    [Fact]
    public void Evaluate_ShouldReturnUnavailable_WhenBuildingCatalogUnavailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["place_planet_building"] = BuildAction(mode: RuntimeMode.Galactic, kind: ExecutionKind.Helper)
        });
        var session = BuildSession(mode: RuntimeMode.Galactic, metadata: new Dictionary<string, string>
        {
            ["helperBridgeState"] = "ready"
        });

        var results = service.Evaluate(profile, session, null);
        results.Should().ContainSingle();
        results[0].ReasonCode.Should().Be("building_catalog_unavailable");
    }

    [Fact]
    public void Evaluate_ShouldReturnStable_WhenHelperReadyAndCatalogAvailable()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["spawn_unit_helper"] = BuildAction(mode: RuntimeMode.Galactic, kind: ExecutionKind.Helper)
        });
        var session = BuildSession(mode: RuntimeMode.Galactic, metadata: new Dictionary<string, string>
        {
            ["helperBridgeState"] = "ready"
        });
        var catalog = new Dictionary<string, IReadOnlyList<string>>
        {
            ["unit_catalog"] = new[] { "rebel_soldier" },
            ["faction_catalog"] = new[] { "rebel" }
        };

        var results = service.Evaluate(profile, session, catalog);
        results.Should().ContainSingle();
        results[0].State.Should().Be(ActionReliabilityState.Stable);
        results[0].ReasonCode.Should().Be("helper_ready");
    }

    #endregion

    #region ActionReliabilityService — symbol evaluation

    [Fact]
    public void Evaluate_ShouldReturnExperimental_WhenSymbolHintMissing()
    {
        var service = new ActionReliabilityService();
        var schema = new JsonObject { ["required"] = new JsonArray("symbol") };
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["unknown_symbol_action"] = BuildAction(payloadSchema: schema)
        });
        var session = BuildSession(mode: RuntimeMode.Galactic);

        var results = service.Evaluate(profile, session);
        results.Should().ContainSingle();
        results[0].ReasonCode.Should().Be("symbol_hint_missing");
    }

    [Fact]
    public void Evaluate_ShouldReturnUnavailable_WhenSymbolIsUnresolved()
    {
        var service = new ActionReliabilityService();
        var schema = new JsonObject { ["required"] = new JsonArray("symbol") };
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["set_credits"] = BuildAction(payloadSchema: schema)
        });
        var session = BuildSession(mode: RuntimeMode.Galactic); // no symbols

        var results = service.Evaluate(profile, session);
        results.Should().ContainSingle();
        results[0].ReasonCode.Should().Be("symbol_unresolved");
    }

    [Fact]
    public void Evaluate_ShouldReturnStable_WhenSymbolIsHealthy()
    {
        var service = new ActionReliabilityService();
        var schema = new JsonObject { ["required"] = new JsonArray("symbol") };
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["set_credits"] = BuildAction(payloadSchema: schema)
        });
        var session = BuildSession(
            mode: RuntimeMode.Galactic,
            symbols: new[]
            {
                new SymbolInfo("credits", new nint(0x100), SymbolValueType.Int32, AddressSource.Signature,
                    Confidence: 0.95d, HealthStatus: SymbolHealthStatus.Healthy)
            });

        var results = service.Evaluate(profile, session);
        results.Should().ContainSingle();
        results[0].State.Should().Be(ActionReliabilityState.Stable);
    }

    [Fact]
    public void Evaluate_ShouldReturnExperimental_WhenSymbolIsDegraded()
    {
        var service = new ActionReliabilityService();
        var schema = new JsonObject { ["required"] = new JsonArray("symbol") };
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["set_credits"] = BuildAction(payloadSchema: schema)
        });
        var session = BuildSession(
            mode: RuntimeMode.Galactic,
            symbols: new[]
            {
                new SymbolInfo("credits", new nint(0x100), SymbolValueType.Int32, AddressSource.Fallback,
                    Confidence: 0.6d, HealthStatus: SymbolHealthStatus.Degraded)
            });

        var results = service.Evaluate(profile, session);
        results.Should().ContainSingle();
        results[0].State.Should().Be(ActionReliabilityState.Experimental);
        results[0].ReasonCode.Should().Be("fallback_or_degraded");
    }

    [Fact]
    public void Evaluate_ShouldReturnUnavailable_WhenCriticalSymbolIsDegraded()
    {
        var service = new ActionReliabilityService();
        var schema = new JsonObject { ["required"] = new JsonArray("symbol") };
        var profile = BuildProfile(
            actions: new Dictionary<string, ActionSpec>
            {
                ["set_credits"] = BuildAction(payloadSchema: schema)
            },
            metadata: new Dictionary<string, string>
            {
                ["criticalSymbols"] = "credits"
            });
        var session = BuildSession(
            mode: RuntimeMode.Galactic,
            symbols: new[]
            {
                new SymbolInfo("credits", new nint(0x100), SymbolValueType.Int32, AddressSource.Signature,
                    Confidence: 0.8d, HealthStatus: SymbolHealthStatus.Degraded)
            });

        var results = service.Evaluate(profile, session);
        results.Should().ContainSingle();
        results[0].State.Should().Be(ActionReliabilityState.Unavailable);
        results[0].ReasonCode.Should().Be("critical_symbol_degraded");
    }

    [Fact]
    public void Evaluate_ShouldReturnStable_WhenActionDoesNotRequireSymbol()
    {
        var service = new ActionReliabilityService();
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["no_symbol"] = BuildAction()
        });
        var session = BuildSession(mode: RuntimeMode.Galactic);

        var results = service.Evaluate(profile, session);
        results.Should().ContainSingle();
        results[0].State.Should().Be(ActionReliabilityState.Stable);
        results[0].ReasonCode.Should().Be("non_symbol_action");
    }

    #endregion

    #region ClampConfidence edge cases

    [Fact]
    public void Evaluate_ShouldClampNaNConfidence()
    {
        var service = new ActionReliabilityService();
        var mechanicService2 = new StubModMechanicDetectionService(new ModMechanicReport(
            ProfileId: "test",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            DependenciesSatisfied: true,
            HelperBridgeReady: true,
            ActionSupport: new[]
            {
                new ModMechanicSupport("nan_action", Supported: false, RuntimeReasonCode.UNKNOWN, "nan conf", double.NaN)
            },
            Diagnostics: new Dictionary<string, object?>()));

        var svc = new ActionReliabilityService(mechanicService2);
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["nan_action"] = BuildAction()
        });

        var results = svc.Evaluate(profile, BuildSession(), null);
        results.Should().ContainSingle();
        results[0].Confidence.Should().Be(0.50d);
    }

    [Fact]
    public void Evaluate_ShouldClampNegativeConfidence()
    {
        var mechanicService2 = new StubModMechanicDetectionService(new ModMechanicReport(
            ProfileId: "test",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            DependenciesSatisfied: true,
            HelperBridgeReady: true,
            ActionSupport: new[]
            {
                new ModMechanicSupport("neg_action", Supported: false, RuntimeReasonCode.UNKNOWN, "neg", -5.0d)
            },
            Diagnostics: new Dictionary<string, object?>()));

        var svc = new ActionReliabilityService(mechanicService2);
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["neg_action"] = BuildAction()
        });

        var results = svc.Evaluate(profile, BuildSession(), null);
        results.Should().ContainSingle();
        results[0].Confidence.Should().Be(0d);
    }

    [Fact]
    public void Evaluate_ShouldClampInfinityConfidence()
    {
        var mechanicService2 = new StubModMechanicDetectionService(new ModMechanicReport(
            ProfileId: "test",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            DependenciesSatisfied: true,
            HelperBridgeReady: true,
            ActionSupport: new[]
            {
                new ModMechanicSupport("inf_action", Supported: false, RuntimeReasonCode.UNKNOWN, "inf", double.PositiveInfinity)
            },
            Diagnostics: new Dictionary<string, object?>()));

        var svc = new ActionReliabilityService(mechanicService2);
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["inf_action"] = BuildAction()
        });

        var results = svc.Evaluate(profile, BuildSession(), null);
        results.Should().ContainSingle();
        results[0].Confidence.Should().Be(0.50d);
    }

    [Fact]
    public void Evaluate_ShouldClampConfidenceAbove1()
    {
        var mechanicService2 = new StubModMechanicDetectionService(new ModMechanicReport(
            ProfileId: "test",
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            DependenciesSatisfied: true,
            HelperBridgeReady: true,
            ActionSupport: new[]
            {
                new ModMechanicSupport("over_action", Supported: false, RuntimeReasonCode.UNKNOWN, "over", 5.0d)
            },
            Diagnostics: new Dictionary<string, object?>()));

        var svc = new ActionReliabilityService(mechanicService2);
        var profile = BuildProfile(actions: new Dictionary<string, ActionSpec>
        {
            ["over_action"] = BuildAction()
        });

        var results = svc.Evaluate(profile, BuildSession(), null);
        results.Should().ContainSingle();
        results[0].Confidence.Should().Be(1d);
    }

    #endregion

    #region ActionSymbolRegistry

    [Fact]
    public void TryGetSymbol_ShouldReturnTrue_ForKnownAction()
    {
        ActionSymbolRegistry.TryGetSymbol("set_credits", out var symbol).Should().BeTrue();
        symbol.Should().Be("credits");
    }

    [Fact]
    public void TryGetSymbol_ShouldReturnFalse_ForUnknownAction()
    {
        ActionSymbolRegistry.TryGetSymbol("does_not_exist", out var symbol).Should().BeFalse();
        symbol.Should().BeEmpty();
    }

    [Fact]
    public void TryGetSymbol_ShouldThrow_WhenActionIdIsNull()
    {
        var act = () => ActionSymbolRegistry.TryGetSymbol(null!, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryGetSymbol_ShouldBeCaseInsensitive()
    {
        ActionSymbolRegistry.TryGetSymbol("SET_CREDITS", out var symbol).Should().BeTrue();
        symbol.Should().Be("credits");
    }

    #endregion

    #region SelectedUnitDraft.IsEmpty

    [Fact]
    public void IsEmpty_ShouldBeTrue_WhenAllFieldsAreNull()
    {
        var draft = new SelectedUnitDraft();
        draft.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void IsEmpty_ShouldBeFalse_WhenHpIsSet()
    {
        var draft = new SelectedUnitDraft(Hp: 100f);
        draft.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_ShouldBeFalse_WhenShieldIsSet()
    {
        var draft = new SelectedUnitDraft(Shield: 50f);
        draft.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_ShouldBeFalse_WhenSpeedIsSet()
    {
        var draft = new SelectedUnitDraft(Speed: 10f);
        draft.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_ShouldBeFalse_WhenDamageMultiplierIsSet()
    {
        var draft = new SelectedUnitDraft(DamageMultiplier: 2f);
        draft.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_ShouldBeFalse_WhenCooldownMultiplierIsSet()
    {
        var draft = new SelectedUnitDraft(CooldownMultiplier: 0.5f);
        draft.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_ShouldBeFalse_WhenVeterancyIsSet()
    {
        var draft = new SelectedUnitDraft(Veterancy: 3);
        draft.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void IsEmpty_ShouldBeFalse_WhenOwnerFactionIsSet()
    {
        var draft = new SelectedUnitDraft(OwnerFaction: 1);
        draft.IsEmpty.Should().BeFalse();
    }

    #endregion

    #region Helpers

    private static TrainerProfile BuildProfile(
        Dictionary<string, ActionSpec>? actions = null,
        Dictionary<string, bool>? featureFlags = null,
        Dictionary<string, string>? metadata = null)
    {
        return new TrainerProfile(
            Id: "test_profile",
            DisplayName: "Test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) },
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: actions ?? new Dictionary<string, ActionSpec>(),
            FeatureFlags: featureFlags ?? new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata);
    }

    private static AttachSession BuildSession(
        RuntimeMode mode = RuntimeMode.Galactic,
        IReadOnlyDictionary<string, string>? metadata = null,
        IReadOnlyList<SymbolInfo>? symbols = null)
    {
        var symbolMap = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in symbols ?? Array.Empty<SymbolInfo>())
        {
            symbolMap[s.Name] = s;
        }

        return new AttachSession(
            ProfileId: "test_profile",
            Process: new ProcessMetadata(
                ProcessId: 1234,
                ProcessName: "StarWarsG.exe",
                ProcessPath: @"C:\Games\StarWarsG.exe",
                CommandLine: null,
                ExeTarget: ExeTarget.Swfoc,
                Mode: mode,
                Metadata: metadata),
            Build: new ProfileBuild("test_profile", "1.0", @"C:\Games\StarWarsG.exe", ExeTarget.Swfoc),
            Symbols: new SymbolMap(symbolMap),
            AttachedAt: DateTimeOffset.UtcNow);
    }

    private static ActionSpec BuildAction(
        RuntimeMode mode = RuntimeMode.Unknown,
        ExecutionKind kind = ExecutionKind.Memory,
        JsonObject? payloadSchema = null)
    {
        return new ActionSpec(
            Id: "action",
            Category: ActionCategory.Global,
            Mode: mode,
            ExecutionKind: kind,
            PayloadSchema: payloadSchema ?? new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0);
    }

    private sealed class StubModMechanicDetectionService : IModMechanicDetectionService
    {
        private readonly ModMechanicReport _report;

        public StubModMechanicDetectionService(ModMechanicReport report)
        {
            _report = report;
        }

        public Task<ModMechanicReport> DetectAsync(
            TrainerProfile profile, AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_report);
        }

        public Task<ModMechanicReport> DetectAsync(
            TrainerProfile profile, AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
        {
            return Task.FromResult(_report);
        }
    }

    private sealed class ThrowingModMechanicDetectionService : IModMechanicDetectionService
    {
        private readonly Exception _exception;

        public ThrowingModMechanicDetectionService(Exception exception)
        {
            _exception = exception;
        }

        public Task<ModMechanicReport> DetectAsync(
            TrainerProfile profile, AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
            CancellationToken cancellationToken)
        {
            throw _exception;
        }

        public Task<ModMechanicReport> DetectAsync(
            TrainerProfile profile, AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
        {
            throw _exception;
        }
    }

    #endregion
}
