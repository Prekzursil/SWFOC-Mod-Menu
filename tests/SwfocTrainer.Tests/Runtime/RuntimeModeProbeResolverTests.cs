using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class RuntimeModeProbeResolverTests
{
    [Fact]
    public void Evaluate_Should_Return_Tactical_When_TacticalScore_Is_Dominant()
    {
        var observations = new[]
        {
            new RuntimeModeProbeObservation("tactical_god_mode", true, "1", 1.0, "bool_ok"),
            new RuntimeModeProbeObservation("tactical_one_hit_mode", true, "0", 1.0, "bool_ok"),
            new RuntimeModeProbeObservation("selected_hp", true, "1200", 1.0, "float_range_ok"),
            new RuntimeModeProbeObservation("planet_owner", true, "9", 0.0, "int_out_of_range")
        };

        var result = RuntimeModeProbeResolver.Evaluate(RuntimeMode.Unknown, observations);

        result.EffectiveMode.Should().Be(RuntimeMode.Tactical);
        result.ReasonCode.Should().Be("probe_tactical_dominant");
        result.TacticalScore.Should().BeGreaterThan(result.GalacticScore);
    }

    [Fact]
    public void Evaluate_Should_Return_Galactic_When_GalacticScore_Is_Dominant()
    {
        var observations = new[]
        {
            new RuntimeModeProbeObservation("planet_owner", true, "3", 2.0, "int_range_ok"),
            new RuntimeModeProbeObservation("hero_respawn_timer", true, "300", 1.0, "int_range_ok"),
            new RuntimeModeProbeObservation("tactical_god_mode", true, "7", 0.25, "bool_out_of_range"),
            new RuntimeModeProbeObservation("selected_hp", false, "read_error", 0.0, "read_error")
        };

        var result = RuntimeModeProbeResolver.Evaluate(RuntimeMode.Unknown, observations);

        result.EffectiveMode.Should().Be(RuntimeMode.Galactic);
        result.ReasonCode.Should().Be("probe_galactic_dominant");
        result.GalacticScore.Should().BeGreaterThan(result.TacticalScore);
    }

    [Fact]
    public void Evaluate_Should_Fallback_To_Hint_When_Inconclusive()
    {
        var observations = new[]
        {
            new RuntimeModeProbeObservation("tactical_god_mode", true, "7", 0.25, "bool_out_of_range"),
            new RuntimeModeProbeObservation("planet_owner", true, "20", 0.0, "int_out_of_range")
        };

        var result = RuntimeModeProbeResolver.Evaluate(RuntimeMode.Galactic, observations);

        result.EffectiveMode.Should().Be(RuntimeMode.Galactic);
        result.ReasonCode.Should().Be("probe_inconclusive_hint_fallback");
    }

    [Fact]
    public void Evaluate_Should_Return_Unknown_When_Inconclusive_And_No_Hint()
    {
        var observations = new[]
        {
            new RuntimeModeProbeObservation("tactical_god_mode", false, "missing", 0.0, "symbol_missing"),
            new RuntimeModeProbeObservation("planet_owner", false, "missing", 0.0, "symbol_missing")
        };

        var result = RuntimeModeProbeResolver.Evaluate(RuntimeMode.Unknown, observations);

        result.EffectiveMode.Should().Be(RuntimeMode.Unknown);
        result.ReasonCode.Should().Be("probe_inconclusive_unknown");
    }
}
