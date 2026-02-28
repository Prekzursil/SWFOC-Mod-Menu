using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelRuntimeModeOverrideTests
{
    [Fact]
    public void TryBuildBatchInputs_ShouldBlock_WhenModeUnknownWithoutOverride()
    {
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                SelectedProfileId: "base_swfoc",
                SelectedSpawnPreset: BuildPreset(),
                RuntimeMode: RuntimeMode.Unknown,
                SpawnQuantity: "1",
                SpawnDelayMs: "125"));

        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("runtime mode is unknown");
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldAllow_WhenModeOverriddenToTactical()
    {
        var effectiveMode = MainViewModelRuntimeModeOverrideHelpers.ResolveEffectiveRuntimeMode(RuntimeMode.Unknown, "Tactical");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                SelectedProfileId: "base_swfoc",
                SelectedSpawnPreset: BuildPreset(),
                RuntimeMode: effectiveMode,
                SpawnQuantity: "1",
                SpawnDelayMs: "125"));

        result.Succeeded.Should().BeTrue();
        result.FailureStatus.Should().BeEmpty();
    }

    private static SpawnPresetViewItem BuildPreset() => new(
        "test",
        "Test",
        "stormtrooper_squad",
        "EMPIRE",
        "AUTO",
        1,
        125,
        "");
}
