using System.Text.Json.Nodes;
using FluentAssertions;
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
}
