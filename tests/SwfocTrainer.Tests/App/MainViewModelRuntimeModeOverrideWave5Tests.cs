using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 5 branch coverage for MainViewModelRuntimeModeOverrideHelpers:
/// Normalize all branches, ResolveEffectiveRuntimeMode all overrides,
/// ModeOverrideOptions list.
/// </summary>
[Collection(RuntimeModeSerialCollection.Name)]
public sealed class MainViewModelRuntimeModeOverrideWave5Tests
{
    [Theory]
    [InlineData(null, "Auto")]
    [InlineData("", "Auto")]
    [InlineData("auto", "Auto")]
    [InlineData("Auto", "Auto")]
    [InlineData("Galactic", "Galactic")]
    [InlineData("galactic", "Galactic")]
    [InlineData("AnyTactical", "AnyTactical")]
    [InlineData("anytactical", "AnyTactical")]
    [InlineData("TacticalLand", "TacticalLand")]
    [InlineData("tacticalland", "TacticalLand")]
    [InlineData("TacticalSpace", "TacticalSpace")]
    [InlineData("tacticalspace", "TacticalSpace")]
    [InlineData("UnknownValue", "Auto")]
    [InlineData("random_text", "Auto")]
    public void Normalize_ShouldMapToExpectedValue(string? input, string expected)
    {
        MainViewModelRuntimeModeOverrideHelpers.Normalize(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("Galactic", RuntimeMode.Unknown, RuntimeMode.Galactic)]
    [InlineData("AnyTactical", RuntimeMode.Unknown, RuntimeMode.AnyTactical)]
    [InlineData("TacticalLand", RuntimeMode.Unknown, RuntimeMode.TacticalLand)]
    [InlineData("TacticalSpace", RuntimeMode.Unknown, RuntimeMode.TacticalSpace)]
    [InlineData("Auto", RuntimeMode.Galactic, RuntimeMode.Galactic)]
    [InlineData("Auto", RuntimeMode.Unknown, RuntimeMode.Unknown)]
    [InlineData(null, RuntimeMode.Galactic, RuntimeMode.Galactic)]
    public void ResolveEffectiveRuntimeMode_ShouldReturnExpected(
        string? modeOverride, RuntimeMode runtimeMode, RuntimeMode expected)
    {
        MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(runtimeMode, modeOverride)
            .Should().Be(expected);
    }

    [Fact]
    public void ModeOverrideOptions_ShouldContainAllFiveOptions()
    {
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions
            .Should().HaveCount(5)
            .And.Contain("Auto")
            .And.Contain("Galactic")
            .And.Contain("AnyTactical")
            .And.Contain("TacticalLand")
            .And.Contain("TacticalSpace");
    }
}
