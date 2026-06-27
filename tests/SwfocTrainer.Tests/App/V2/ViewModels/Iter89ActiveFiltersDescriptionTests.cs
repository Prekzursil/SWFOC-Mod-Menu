using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 89) — pins the active-filters status line that
/// surfaces which of the 3 composable filters are currently narrowing
/// the activity log. Pairs with iter-87 Reset filters button —
/// operator sees what's active AND has a clear path to widen.
/// </summary>
public sealed class Iter89ActiveFiltersDescriptionTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter, DiagnosticsTabViewModel vm) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var settings = new V2Settings();
        return (sim, adapter, new DiagnosticsTabViewModel(adapter, settings));
    }

    [Fact]
    public void HasActiveFilters_DefaultIsFalse()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.HasActiveFilters.Should().BeFalse();
        vm.ActiveFiltersDescription.Should().BeEmpty();
    }

    [Fact]
    public void HasActiveFilters_True_WhenErrorsOnlyOn()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.ActivityLogErrorsOnly = true;

        vm.HasActiveFilters.Should().BeTrue();
        vm.ActiveFiltersDescription.Should().Be("Active filters: errors-only");
    }

    [Fact]
    public void HasActiveFilters_True_WhenTimeWindowSet()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.ActivityLogTimeWindowMinutes = 5;

        vm.HasActiveFilters.Should().BeTrue();
        vm.ActiveFiltersDescription.Should().Be("Active filters: window 5 min");
    }

    [Fact]
    public void HasActiveFilters_True_WhenSubstringSet()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.ActivityLogCommandFilter = "GodMode";

        vm.HasActiveFilters.Should().BeTrue();
        vm.ActiveFiltersDescription.Should().Be("Active filters: 'GodMode'");
    }

    [Fact]
    public void Description_AllThreeFiltersOn_ListsInOrder()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.ActivityLogErrorsOnly = true;
        vm.ActivityLogTimeWindowMinutes = 1;
        vm.ActivityLogCommandFilter = "Spawn";

        vm.HasActiveFilters.Should().BeTrue();
        vm.ActiveFiltersDescription.Should().Be(
            "Active filters: errors-only · window 1 min · 'Spawn'");
    }

    [Fact]
    public void HasActiveFilters_False_AfterReset()
    {
        // Pairs with iter-87 Reset: after pressing Reset, the description
        // and the HasActiveFilters flag both flip back to clean state.
        var (sim, _, vm) = NewSession();
        using var _ = sim;
        vm.ActivityLogErrorsOnly = true;
        vm.ActivityLogTimeWindowMinutes = 5;
        vm.ActivityLogCommandFilter = "X";

        vm.ResetActivityLogFiltersCommand.Execute(null);

        vm.HasActiveFilters.Should().BeFalse();
        vm.ActiveFiltersDescription.Should().BeEmpty();
    }

    [Fact]
    public void Description_ChangingAnyFilter_FiresPropertyChanged()
    {
        // Bound TextBlock visibility (HasActiveFilters) and text
        // (ActiveFiltersDescription) must both notify when ANY of the 3
        // filters change.
        var (sim, _, vm) = NewSession();
        using var _ = sim;
        var hasActiveFired = false;
        var descFired = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.HasActiveFilters)) hasActiveFired = true;
            if (e.PropertyName == nameof(vm.ActiveFiltersDescription)) descFired = true;
        };

        vm.ActivityLogTimeWindowMinutes = 5;

        hasActiveFired.Should().BeTrue("HasActiveFilters must fire on any filter change");
        descFired.Should().BeTrue("ActiveFiltersDescription must fire on any filter change");
    }
}
