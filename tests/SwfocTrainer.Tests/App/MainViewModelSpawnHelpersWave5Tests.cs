using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 5 branch coverage for MainViewModelSpawnHelpers:
/// TryBuildBatchInputs all failure branches (null profile/preset, unknown runtime,
/// invalid quantity, invalid delay, zero quantity, negative delay).
/// </summary>
public sealed class MainViewModelSpawnHelpersWave5Tests
{
    [Fact]
    public void TryBuildBatchInputs_NullProfileId_ShouldFail()
    {
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
            null, BuildPreset(), RuntimeMode.Galactic, "1", "100");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("select profile and preset");
    }

    [Fact]
    public void TryBuildBatchInputs_NullPreset_ShouldFail()
    {
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
            "profile", null, RuntimeMode.Galactic, "1", "100");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("select profile and preset");
    }

    [Fact]
    public void TryBuildBatchInputs_UnknownRuntimeMode_ShouldFail()
    {
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
            "profile", BuildPreset(), RuntimeMode.Unknown, "1", "100");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("runtime mode is unknown");
    }

    [Fact]
    public void TryBuildBatchInputs_InvalidQuantity_ShouldFail()
    {
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
            "profile", BuildPreset(), RuntimeMode.Galactic, "abc", "100");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("Invalid spawn quantity");
    }

    [Fact]
    public void TryBuildBatchInputs_ZeroQuantity_ShouldFail()
    {
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
            "profile", BuildPreset(), RuntimeMode.Galactic, "0", "100");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("Invalid spawn quantity");
    }

    [Fact]
    public void TryBuildBatchInputs_NegativeQuantity_ShouldFail()
    {
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
            "profile", BuildPreset(), RuntimeMode.Galactic, "-1", "100");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("Invalid spawn quantity");
    }

    [Fact]
    public void TryBuildBatchInputs_InvalidDelay_ShouldFail()
    {
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
            "profile", BuildPreset(), RuntimeMode.Galactic, "5", "abc");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("Invalid spawn delay");
    }

    [Fact]
    public void TryBuildBatchInputs_NegativeDelay_ShouldFail()
    {
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
            "profile", BuildPreset(), RuntimeMode.Galactic, "5", "-1");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("Invalid spawn delay");
    }

    [Fact]
    public void TryBuildBatchInputs_ValidInputs_ShouldSucceed()
    {
        var preset = BuildPreset();
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
            "profile", preset, RuntimeMode.Galactic, "10", "250");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeTrue();
        result.ProfileId.Should().Be("profile");
        result.SelectedPreset.Should().BeSameAs(preset);
        result.Quantity.Should().Be(10);
        result.DelayMs.Should().Be(250);
        result.FailureStatus.Should().BeEmpty();
    }

    [Fact]
    public void TryBuildBatchInputs_ZeroDelay_ShouldSucceed()
    {
        var request = new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
            "profile", BuildPreset(), RuntimeMode.Galactic, "1", "0");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(request);
        result.Succeeded.Should().BeTrue();
        result.DelayMs.Should().Be(0);
    }

    private static SpawnPresetViewItem BuildPreset()
    {
        return new SpawnPresetViewItem(
            Id: "preset1",
            Name: "Test Preset",
            UnitId: "unit_stormtrooper",
            Faction: "EMPIRE",
            EntryMarker: "AUTO",
            DefaultQuantity: 5,
            DefaultDelayMs: 125,
            Description: "Test");
    }
}
