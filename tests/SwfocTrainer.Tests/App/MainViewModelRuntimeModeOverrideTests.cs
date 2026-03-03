using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelRuntimeModeOverrideTests
{
    [Fact]
    public void ModeOverrideOptions_ShouldContainStrictModes()
    {
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().ContainInOrder(
            "Auto",
            "Galactic",
            "AnyTactical",
            "TacticalLand",
            "TacticalSpace");
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_ShouldRemainUnknown_WhenAutoWithUnknownHint()
    {
        var effectiveMode = MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.Unknown, "Auto");

        effectiveMode.Should().Be(RuntimeMode.Unknown);
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_ShouldUseAnyTacticalOverride_WhenHintUnknown()
    {
        var effectiveMode = MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.Unknown, "AnyTactical");

        effectiveMode.Should().Be(RuntimeMode.AnyTactical);
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_ShouldUseLandOverride()
    {
        var effectiveMode = MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.Unknown, "TacticalLand");

        effectiveMode.Should().Be(RuntimeMode.TacticalLand);
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_ShouldUseSpaceOverride()
    {
        var effectiveMode = MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.Unknown, "TacticalSpace");

        effectiveMode.Should().Be(RuntimeMode.TacticalSpace);
    }

    [Fact]
    public void Normalize_ShouldMapKnownValuesCaseInsensitive()
    {
        MainViewModelRuntimeModeOverrideHelpers.Normalize("galactic").Should().Be("Galactic");
        MainViewModelRuntimeModeOverrideHelpers.Normalize("anytactical").Should().Be("AnyTactical");
        MainViewModelRuntimeModeOverrideHelpers.Normalize("tacticalland").Should().Be("TacticalLand");
        MainViewModelRuntimeModeOverrideHelpers.Normalize("tacticalspace").Should().Be("TacticalSpace");
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_ShouldUseTacticalSpaceOverride_WhenHintIsGalactic()
    {
        var effectiveMode = MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.Galactic, "TacticalSpace");

        effectiveMode.Should().Be(RuntimeMode.TacticalSpace);
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_ShouldUseGalacticOverride_WhenHintIsTacticalLand()
    {
        var effectiveMode = MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.TacticalLand, "Galactic");

        effectiveMode.Should().Be(RuntimeMode.Galactic);
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_ShouldKeepRuntimeHint_WhenOverrideIsAuto()
    {
        var effectiveMode = MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.TacticalSpace, "Auto");

        effectiveMode.Should().Be(RuntimeMode.TacticalSpace);
    }

    [Theory]
    [InlineData("galactic", "Galactic")]
    [InlineData("anytactical", "AnyTactical")]
    [InlineData("tacticalland", "TacticalLand")]
    [InlineData("tacticalspace", "TacticalSpace")]
    public void Normalize_ShouldMapKnownOverrides_CaseInsensitive(string rawOverride, string expected)
    {
        MainViewModelRuntimeModeOverrideHelpers.Normalize(rawOverride).Should().Be(expected);
    }

    [Fact]
    public void ModeOverrideOptions_ShouldExposeExpectedOverrideSequence()
    {
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions
            .Should()
            .Equal("Auto", "Galactic", "AnyTactical", "TacticalLand", "TacticalSpace");
    }

    [Fact]
    public void Normalize_ShouldFallbackToAuto_ForUnknownOverrideValues()
    {
        MainViewModelRuntimeModeOverrideHelpers.Normalize("invalid_mode").Should().Be("Auto");
    }
}
