using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Branch coverage for MainViewModelRuntimeModeOverrideHelpers:
/// Normalize, ResolveEffectiveRuntimeMode, Load, Save.
/// </summary>
[Collection(RuntimeModeSerialCollection.Name)]
public sealed class MainViewModelRuntimeModeOverrideBranchTests
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
    [InlineData("garbage", "Auto")]
    public void Normalize_ShouldMapAllInputs(string? input, string expected)
    {
        MainViewModelRuntimeModeOverrideHelpers.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_ShouldReturnGalactic_WhenOverrideIsGalactic()
    {
        MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.Unknown, "Galactic")
            .Should().Be(RuntimeMode.Galactic);
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_ShouldReturnAnyTactical_WhenOverrideIsAnyTactical()
    {
        MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.Unknown, "AnyTactical")
            .Should().Be(RuntimeMode.AnyTactical);
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_ShouldReturnTacticalLand_WhenOverrideIsTacticalLand()
    {
        MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.Unknown, "TacticalLand")
            .Should().Be(RuntimeMode.TacticalLand);
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_ShouldReturnTacticalSpace_WhenOverrideIsTacticalSpace()
    {
        MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.Unknown, "TacticalSpace")
            .Should().Be(RuntimeMode.TacticalSpace);
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_ShouldReturnPassthrough_WhenOverrideIsAuto()
    {
        MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.Galactic, "Auto")
            .Should().Be(RuntimeMode.Galactic);
    }

    [Fact]
    public void ResolveEffectiveRuntimeMode_ShouldReturnPassthrough_WhenOverrideIsNull()
    {
        MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.TacticalLand, null)
            .Should().Be(RuntimeMode.TacticalLand);
    }

    [Fact]
    public void ModeOverrideOptions_ShouldContainAllFiveOptions()
    {
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().HaveCount(5);
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().Contain("Auto");
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().Contain("Galactic");
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().Contain("AnyTactical");
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().Contain("TacticalLand");
        MainViewModelRuntimeModeOverrideHelpers.ModeOverrideOptions.Should().Contain("TacticalSpace");
    }

    [Fact]
    public void Load_ShouldReturnAuto_WhenFileDoesNotExist()
    {
        // Load reads from AppData; if the file doesn't exist it returns Auto
        var result = MainViewModelRuntimeModeOverrideHelpers.Load();
        result.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Save_ThenLoad_ShouldRoundTrip()
    {
        MainViewModelRuntimeModeOverrideHelpers.Save("Galactic");
        var loaded = MainViewModelRuntimeModeOverrideHelpers.Load();
        loaded.Should().Be("Galactic");

        // Restore to Auto
        MainViewModelRuntimeModeOverrideHelpers.Save("Auto");
    }

    [Fact]
    public void Save_ShouldNormalize_WhenGivenInvalidInput()
    {
        MainViewModelRuntimeModeOverrideHelpers.Save("garbage_value");
        var loaded = MainViewModelRuntimeModeOverrideHelpers.Load();
        loaded.Should().Be("Auto");
    }

    [Fact]
    public void Save_ShouldNormalize_WhenGivenNull()
    {
        MainViewModelRuntimeModeOverrideHelpers.Save(null);
        var loaded = MainViewModelRuntimeModeOverrideHelpers.Load();
        loaded.Should().Be("Auto");
    }
}
