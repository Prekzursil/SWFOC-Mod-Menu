using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelRuntimeModeOverrideTests
{
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
    public void Normalize_ShouldFallbackToAuto_ForUnknownOverrideValues()
    {
        MainViewModelRuntimeModeOverrideHelpers.Normalize("invalid_mode").Should().Be("Auto");
    }
}
