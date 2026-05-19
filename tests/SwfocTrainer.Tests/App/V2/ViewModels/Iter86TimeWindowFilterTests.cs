using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 86) — pins the activity-log time-window filter:
/// when <see cref="DiagnosticsTabViewModel.ActivityLogTimeWindowMinutes"/>
/// is non-null, RecentBridgeCalls drops entries older than now - N
/// minutes. Composes with existing Errors-only + substring filters.
/// </summary>
public sealed class Iter86TimeWindowFilterTests
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

    private static BridgeActivityEntry EntryAt(string lua, DateTimeOffset stamp) =>
        new BridgeActivityEntry(
            Timestamp: stamp,
            LuaCommand: lua,
            Succeeded: true,
            ResponseOrError: "OK",
            DurationMs: 5);

    [Fact]
    public void ActivityLogTimeWindowMinutes_DefaultsToNull()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.ActivityLogTimeWindowMinutes.Should().BeNull("default = no time-window filter");
    }

    [Fact]
    public void TimeWindow_Null_IncludesAllEntries()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(EntryAt("recent", DateTimeOffset.UtcNow.AddSeconds(-10)));
        adapter.RecordForTest(EntryAt("oldish", DateTimeOffset.UtcNow.AddMinutes(-30)));

        vm.ActivityLogTimeWindowMinutes = null;

        vm.RecentBridgeCalls.Should().HaveCount(2);
    }

    [Fact]
    public void TimeWindow_OneMinute_DropsEntriesOlderThanOneMinute()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(EntryAt("recent", DateTimeOffset.UtcNow.AddSeconds(-30)));
        adapter.RecordForTest(EntryAt("old", DateTimeOffset.UtcNow.AddMinutes(-2)));

        vm.ActivityLogTimeWindowMinutes = 1;

        vm.RecentBridgeCalls.Should().HaveCount(1);
        vm.RecentBridgeCalls[0].LuaCommand.Should().Be("recent");
    }

    [Fact]
    public void TimeWindow_FiveMinutes_KeepsThreeMinuteOldEntry()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(EntryAt("recent", DateTimeOffset.UtcNow.AddSeconds(-30)));
        adapter.RecordForTest(EntryAt("threeMinAgo", DateTimeOffset.UtcNow.AddMinutes(-3)));
        adapter.RecordForTest(EntryAt("tenMinAgo", DateTimeOffset.UtcNow.AddMinutes(-10)));

        vm.ActivityLogTimeWindowMinutes = 5;

        vm.RecentBridgeCalls.Should().HaveCount(2,
            "5-min window keeps recent + 3-min-old, drops 10-min-old");
        vm.RecentBridgeCalls.Should().NotContain(e => e.LuaCommand == "tenMinAgo");
    }

    [Fact]
    public void TimeWindow_ComposesWithErrorsOnly()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow.AddSeconds(-30), "recent_ok", true, "OK", 5));
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow.AddSeconds(-30), "recent_fail", false, "ERR", 5));
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow.AddMinutes(-10), "old_fail", false, "ERR", 5));

        vm.ActivityLogTimeWindowMinutes = 5;
        vm.ActivityLogErrorsOnly = true;

        vm.RecentBridgeCalls.Should().HaveCount(1,
            "errors-only AND last-5-min keeps only the recent failure");
        vm.RecentBridgeCalls[0].LuaCommand.Should().Be("recent_fail");
    }

    [Fact]
    public void TimeWindow_ComposesWithSubstringFilter()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(EntryAt("return SWFOC_GodMode(1)", DateTimeOffset.UtcNow.AddSeconds(-30)));
        adapter.RecordForTest(EntryAt("return SWFOC_OneHitKill(1)", DateTimeOffset.UtcNow.AddSeconds(-30)));
        adapter.RecordForTest(EntryAt("return SWFOC_GodMode(0)", DateTimeOffset.UtcNow.AddMinutes(-10)));

        vm.ActivityLogTimeWindowMinutes = 5;
        vm.ActivityLogCommandFilter = "GodMode";

        vm.RecentBridgeCalls.Should().HaveCount(1,
            "5-min window AND 'GodMode' filter keeps only the recent GodMode call");
        vm.RecentBridgeCalls[0].LuaCommand.Should().Contain("GodMode(1)");
    }

    [Fact]
    public void TimeWindow_PropertyChange_FiresRecentBridgeCallsNotification()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;
        var fired = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.RecentBridgeCalls))
                fired = true;
        };

        vm.ActivityLogTimeWindowMinutes = 5;

        fired.Should().BeTrue("setting the window must invalidate the bound list");
    }
}
