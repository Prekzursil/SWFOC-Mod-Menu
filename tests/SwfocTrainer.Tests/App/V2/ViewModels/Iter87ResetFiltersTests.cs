using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 87) — pins the activity-log Reset filters command.
/// Single click clears all three composable filters introduced across
/// iter 46 (errors-only), iter 66 (substring), and iter 86 (time window).
/// </summary>
public sealed class Iter87ResetFiltersTests
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
    public void ResetFiltersCommand_IsExposed()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.ResetActivityLogFiltersCommand.Should().NotBeNull();
    }

    [Fact]
    public void ResetFilters_ClearsAllThreeFilters()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;
        vm.ActivityLogErrorsOnly = true;
        vm.ActivityLogCommandFilter = "GodMode";
        vm.ActivityLogTimeWindowMinutes = 5;

        vm.ResetActivityLogFiltersCommand.Execute(null);

        vm.ActivityLogErrorsOnly.Should().BeFalse();
        vm.ActivityLogCommandFilter.Should().BeEmpty();
        vm.ActivityLogTimeWindowMinutes.Should().BeNull();
    }

    [Fact]
    public void ResetFilters_AllEntriesVisibleAfterReset()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        // Inject 3 entries with different shapes so each filter would
        // hide at least one if active.
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow.AddSeconds(-30), "return SWFOC_GodMode(1)", true, "OK", 5));
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow.AddMinutes(-10), "return SWFOC_OneHitKill(1)", true, "OK", 5));
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow.AddSeconds(-30), "return SWFOC_Bad()", false, "ERR", 5));

        // Apply restrictive filters first.
        vm.ActivityLogErrorsOnly = true;
        vm.ActivityLogTimeWindowMinutes = 5;
        vm.ActivityLogCommandFilter = "Nonexistent";
        vm.RecentBridgeCalls.Should().BeEmpty("all 3 filters together exclude every entry");

        vm.ResetActivityLogFiltersCommand.Execute(null);

        vm.RecentBridgeCalls.Should().HaveCount(3, "after reset, all entries are visible again");
    }

    [Fact]
    public void ResetFilters_NoOpWhenAlreadyDefault()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;
        // VM starts in default state — Reset should be safe and idempotent.

        vm.ResetActivityLogFiltersCommand.Execute(null);

        vm.ActivityLogErrorsOnly.Should().BeFalse();
        vm.ActivityLogCommandFilter.Should().BeEmpty();
        vm.ActivityLogTimeWindowMinutes.Should().BeNull();
    }

    [Fact]
    public void ResetFilters_DoesNotTouchPinnedOrSavedSettings()
    {
        // Scope discipline: Reset filters is for the 3 view filters only.
        // Pinned activity entries (iter 75) and the editor's V2Settings
        // are independent state and must not be cleared.
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        var entry = new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "return SWFOC_X()", true, "OK", 5);
        adapter.RecordForTest(entry);
        adapter.PinActivity(entry);
        var pinnedCountBefore = adapter.PinnedCalls.Count;

        vm.ResetActivityLogFiltersCommand.Execute(null);

        adapter.PinnedCalls.Count.Should().Be(pinnedCountBefore,
            "Reset filters MUST NOT clear pinned bookmarks");
    }
}
