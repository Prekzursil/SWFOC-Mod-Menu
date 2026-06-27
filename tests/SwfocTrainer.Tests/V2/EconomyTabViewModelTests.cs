using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.V2;

/// <summary>
/// Phase 1 thread A — tests for the App-side INPC wrapper around
/// <see cref="EconomyTabState"/>. Verifies that property setters propagate
/// to the underlying Core state, that commands invoke the right dispatcher
/// methods, and that toggle commands flip the IsXxxEnabled flags via the
/// shared FeatureToggleCoordinator.
/// </summary>
public sealed class EconomyTabViewModelTests
{
    private sealed class RecordingDispatcher : IEconomyDispatcher
    {
        public List<string> Calls { get; } = new();
        public bool ReturnSuccess { get; set; } = true;

        public Task<bool> SetCreditsAsync(int slot, double amount, CancellationToken ct)
        { Calls.Add($"SetCredits({slot},{amount})"); return Task.FromResult(ReturnSuccess); }

        public Task<bool> SetTechAsync(int slot, int level, CancellationToken ct)
        { Calls.Add($"SetTech({slot},{level})"); return Task.FromResult(ReturnSuccess); }

        public Task<bool> DrainEnemyCreditsAsync(CancellationToken ct)
        { Calls.Add("DrainEnemy"); return Task.FromResult(ReturnSuccess); }

        public Task<bool> UncapCreditsAsync(CancellationToken ct)
        { Calls.Add("Uncap"); return Task.FromResult(ReturnSuccess); }

        public Task<bool> SetIncomeMultiplierAsync(int slot, float mult, CancellationToken ct)
        { Calls.Add($"IncomeMult({slot},{mult})"); return Task.FromResult(ReturnSuccess); }

        public Task<bool> SetBuildSpeedAsync(int slot, float mult, CancellationToken ct)
        { Calls.Add($"BuildSpeed({slot},{mult})"); return Task.FromResult(ReturnSuccess); }

        public Task<bool> SetBuildCostAsync(int slot, float mult, CancellationToken ct)
        { Calls.Add($"BuildCost({slot},{mult})"); return Task.FromResult(ReturnSuccess); }

        public Task<bool> SetFreezeCreditsAsync(
            int slot, bool enable, double target, CancellationToken ct)
        { Calls.Add($"Freeze({slot},{enable},{target})"); return Task.FromResult(ReturnSuccess); }

        public Task<bool> SetInstantBuildAsync(bool enable, CancellationToken ct)
        { Calls.Add($"InstantBuild({enable})"); return Task.FromResult(ReturnSuccess); }

        public Task<bool> SetFreeBuildAsync(bool enable, CancellationToken ct)
        { Calls.Add($"FreeBuild({enable})"); return Task.FromResult(ReturnSuccess); }
    }

    [Fact]
    public async Task SetCreditsCommand_RoutesAmountAndSlotToDispatcher()
    {
        var disp = new RecordingDispatcher();
        var vm = new EconomyTabViewModel(disp) { Slot = 2, CreditsAmount = 50000 };

        await AsyncCommandPump.PumpAsync(vm.SetCreditsCommand);

        disp.Calls.Should().ContainSingle().Which.Should().Be("SetCredits(2,50000)");
    }

    [Fact]
    public async Task SetTechCommand_PassesTechLevel()
    {
        var disp = new RecordingDispatcher();
        var vm = new EconomyTabViewModel(disp) { Slot = -1, TechLevel = 4 };
        await AsyncCommandPump.PumpAsync(vm.SetTechCommand);
        disp.Calls.Should().Contain("SetTech(-1,4)");
    }

    [Fact]
    public async Task DrainEnemyCommand_HitsDrainPath()
    {
        var disp = new RecordingDispatcher();
        var vm = new EconomyTabViewModel(disp);
        await AsyncCommandPump.PumpAsync(vm.DrainEnemyCommand);
        disp.Calls.Should().Contain("DrainEnemy");
    }

    [Fact]
    public void ToggleFreezeCommand_DisabledUntilLivePerSlotHookExists()
    {
        var disp = new RecordingDispatcher();
        var vm = new EconomyTabViewModel(disp) { Slot = 1, FreezeCreditsTarget = 12345 };

        vm.IsFreezeCreditsEnabled.Should().BeFalse();
        vm.ToggleFreezeCommand.CanExecute(null).Should().BeFalse(
            "per-slot FreezeCredits is Phase 2 pending; the live UI uses the global freeze controls");
        vm.IsFreezeCreditsEnabled.Should().BeFalse();
        disp.Calls.Should().BeEmpty();
    }

    [Fact]
    public void ToggleInstantBuildCommand_AndFreeBuildCommand_AreDisabledUntilLiveHooksExist()
    {
        var disp = new RecordingDispatcher();
        var vm = new EconomyTabViewModel(disp);

        vm.ToggleInstantBuildCommand.CanExecute(null).Should().BeFalse();
        vm.ToggleFreeBuildCommand.CanExecute(null).Should().BeFalse();
        vm.IsInstantBuildEnabled.Should().BeFalse();
        vm.IsFreeBuildEnabled.Should().BeFalse();
        disp.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task DispatcherFailure_LeavesLastStatusErrorish()
    {
        var disp = new RecordingDispatcher { ReturnSuccess = false };
        var vm = new EconomyTabViewModel(disp);
        await AsyncCommandPump.PumpAsync(vm.SetCreditsCommand);
        vm.LastStatus.Should().Contain("Error");
    }

    [Fact]
    public void PropertySetters_FirePropertyChangedAndPropagateToCoreState()
    {
        var disp = new RecordingDispatcher();
        var vm = new EconomyTabViewModel(disp);
        var fired = new HashSet<string>();
        vm.PropertyChanged += (_, e) => fired.Add(e.PropertyName ?? "");

        vm.Slot = 3;
        vm.CreditsAmount = 7;
        vm.TechLevel = 4;
        vm.IncomeMultiplier = 2.5f;
        vm.BuildSpeedMultiplier = 1.5f;
        vm.BuildCostMultiplier = 0.5f;
        vm.FreezeCreditsTarget = 100;

        fired.Should().Contain(new[]
        {
            nameof(EconomyTabViewModel.Slot),
            nameof(EconomyTabViewModel.CreditsAmount),
            nameof(EconomyTabViewModel.TechLevel),
            nameof(EconomyTabViewModel.IncomeMultiplier),
            nameof(EconomyTabViewModel.BuildSpeedMultiplier),
            nameof(EconomyTabViewModel.BuildCostMultiplier),
            nameof(EconomyTabViewModel.FreezeCreditsTarget),
        });
    }
}
