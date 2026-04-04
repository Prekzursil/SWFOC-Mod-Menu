using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Branch coverage for MainViewModelSpawnHelpers.TryBuildBatchInputs.
/// </summary>
public sealed class MainViewModelSpawnHelpersBranchTests
{
    [Fact]
    public void TryBuildBatchInputs_ShouldThrow_WhenRequestIsNull()
    {
        var act = () => MainViewModelSpawnHelpers.TryBuildBatchInputs(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldFail_WhenProfileIdIsNull()
    {
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                null, BuildPreset(), RuntimeMode.AnyTactical, "1", "0"));
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("select profile");
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldFail_WhenPresetIsNull()
    {
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                "profile", null, RuntimeMode.AnyTactical, "1", "0"));
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("select profile");
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldFail_WhenRuntimeModeIsUnknown()
    {
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                "profile", BuildPreset(), RuntimeMode.Unknown, "1", "0"));
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("runtime mode");
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldFail_WhenQuantityIsNonNumeric()
    {
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                "profile", BuildPreset(), RuntimeMode.AnyTactical, "abc", "0"));
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("quantity");
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldFail_WhenQuantityIsZero()
    {
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                "profile", BuildPreset(), RuntimeMode.AnyTactical, "0", "0"));
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("quantity");
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldFail_WhenQuantityIsNegative()
    {
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                "profile", BuildPreset(), RuntimeMode.AnyTactical, "-1", "0"));
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("quantity");
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldFail_WhenDelayIsNonNumeric()
    {
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                "profile", BuildPreset(), RuntimeMode.AnyTactical, "5", "abc"));
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("delay");
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldFail_WhenDelayIsNegative()
    {
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                "profile", BuildPreset(), RuntimeMode.AnyTactical, "5", "-1"));
        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("delay");
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldSucceed_WithValidInputs()
    {
        var preset = BuildPreset();
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                "profile", preset, RuntimeMode.TacticalLand, "10", "250"));
        result.Succeeded.Should().BeTrue();
        result.ProfileId.Should().Be("profile");
        result.SelectedPreset.Should().BeSameAs(preset);
        result.Quantity.Should().Be(10);
        result.DelayMs.Should().Be(250);
        result.FailureStatus.Should().BeEmpty();
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldSucceed_WithZeroDelay()
    {
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                "profile", BuildPreset(), RuntimeMode.AnyTactical, "1", "0"));
        result.Succeeded.Should().BeTrue();
        result.DelayMs.Should().Be(0);
    }

    private static SpawnPresetViewItem BuildPreset()
    {
        return new SpawnPresetViewItem("test_unit", "Test Unit", "test_unit_id", "Empire", "TacticalLand", 1, 0, "Test spawn preset");
    }
}
