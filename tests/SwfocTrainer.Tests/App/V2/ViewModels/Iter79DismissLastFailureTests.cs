using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 79) — pins the Diagnostics "dismiss last failure"
/// behavior. The iter-51 LastFailureSummary callout is sticky by design,
/// but operators need to acknowledge they've seen a failure without
/// losing visibility on NEW failures. The dismiss command records a
/// timestamp; failures with Timestamp ≤ dismissal are hidden, and any
/// newer failure overrides the dismissal automatically.
/// </summary>
public sealed class Iter79DismissLastFailureTests
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

    private static BridgeActivityEntry FailureEntry(string lua, DateTimeOffset stamp)
    {
        return new BridgeActivityEntry(
            Timestamp: stamp,
            LuaCommand: lua,
            Succeeded: false,
            ResponseOrError: "ERR: probe failed",
            DurationMs: 12);
    }

    private static BridgeActivityEntry SuccessEntry(string lua, DateTimeOffset stamp)
    {
        return new BridgeActivityEntry(
            Timestamp: stamp,
            LuaCommand: lua,
            Succeeded: true,
            ResponseOrError: "OK",
            DurationMs: 2);
    }

    [Fact]
    public void DismissCommand_IsExposed()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;

        vm.DismissLastFailureCommand.Should().NotBeNull();
    }

    [Fact]
    public void Dismiss_HidesCallout_WhenFailureExists()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        // Inject a failure 5 seconds ago — captured before dismissal.
        var fiveSecondsAgo = DateTimeOffset.UtcNow.AddSeconds(-5);
        adapter.RecordForTest(FailureEntry("return SWFOC_DoString(\"bogus()\")", fiveSecondsAgo));
        vm.HasRecentFailure.Should().BeTrue("callout must show before dismiss");
        vm.LastFailureSummary.Should().NotBeNullOrEmpty();

        vm.DismissLastFailureCommand.Execute(null);

        vm.HasRecentFailure.Should().BeFalse("dismissal hides the callout");
        vm.LastFailureSummary.Should().BeNull("summary returns null when dismissed");
    }

    [Fact]
    public void Dismiss_DoesNotHide_WhenNewFailureArrivesAfterDismissal()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        var oldFailure = DateTimeOffset.UtcNow.AddSeconds(-30);
        adapter.RecordForTest(FailureEntry("return SWFOC_X(1)", oldFailure));
        vm.DismissLastFailureCommand.Execute(null);
        vm.HasRecentFailure.Should().BeFalse("dismissal hides the old failure");

        // A NEW failure arrives — its Timestamp is AFTER dismissal.
        var newFailure = DateTimeOffset.UtcNow.AddSeconds(2);
        adapter.RecordForTest(FailureEntry("return SWFOC_Y(2)", newFailure));

        vm.HasRecentFailure.Should().BeTrue("new failure overrides dismissal");
        vm.LastFailureSummary.Should().Contain("SWFOC_Y(2)");
    }

    [Fact]
    public void Dismiss_HasNoEffect_WhenThereAreNoFailures()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        // Only a success entry — no failure to dismiss.
        adapter.RecordForTest(SuccessEntry("return SWFOC_GetVersion()", DateTimeOffset.UtcNow));
        vm.HasRecentFailure.Should().BeFalse();
        vm.LastFailureSummary.Should().BeNull();

        vm.DismissLastFailureCommand.Execute(null);

        // Still no failure visible.
        vm.HasRecentFailure.Should().BeFalse();
        vm.LastFailureSummary.Should().BeNull();
    }

    [Fact]
    public void Dismiss_PreservesSuccessEntries_OnlyHidesFailureCallout()
    {
        // Scope discipline: dismissal MUST NOT remove activity log entries
        // or affect the iter-48 stats line. Operator's audit log stays
        // intact; only the callout visibility changes.
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(SuccessEntry("return SWFOC_OK()", DateTimeOffset.UtcNow.AddSeconds(-10)));
        adapter.RecordForTest(FailureEntry("return SWFOC_Bad()", DateTimeOffset.UtcNow.AddSeconds(-5)));
        var preDismissCount = adapter.RecentCalls.Count;

        vm.DismissLastFailureCommand.Execute(null);

        adapter.RecentCalls.Count.Should().Be(preDismissCount, "dismissal must not delete log entries");
        adapter.RecentCalls.Any(e => e.LuaCommand.Contains("SWFOC_Bad")).Should().BeTrue("the failed entry stays in the log for audit");
    }
}
