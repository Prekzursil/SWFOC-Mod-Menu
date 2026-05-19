using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 82) — pins the activity-stats-line "(N failed)"
/// suffix introduced in iter 82. When the ring buffer has at least one
/// failed entry, the stats line includes an explicit count so operators
/// don't have to mentally compute failed = total × (1 − OK%). When
/// everything's clean, the suffix hides to avoid clutter.
/// </summary>
public sealed class Iter82ActivityStatsLineFailedCountTests
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
    public void ActivityStatsLine_NoCalls_ReturnsIdleString()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.ActivityStatsLine.Should().Be("(no recent calls)");
    }

    [Fact]
    public void ActivityStatsLine_AllSuccesses_NoFailedSuffix()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "return SWFOC_OK1()", true, "OK", 5));
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "return SWFOC_OK2()", true, "OK", 7));

        var line = vm.ActivityStatsLine;

        line.Should().Contain("100 % OK");
        line.Should().NotContain("failed", "no failures means no failed-count suffix");
    }

    [Fact]
    public void ActivityStatsLine_SingleFailure_ShowsOneFailedSuffix()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "return SWFOC_OK()", true, "OK", 5));
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "return SWFOC_Bad()", false, "ERR: probe failed", 12));

        var line = vm.ActivityStatsLine;

        line.Should().Contain("(1 failed)", "exactly one failure → suffix shows '1 failed'");
        line.Should().Contain("50 % OK", "1 of 2 calls succeeded");
    }

    [Fact]
    public void ActivityStatsLine_MultipleFailures_ShowsTotalCount()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "return SWFOC_OK1()", true, "OK", 5));
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "return SWFOC_OK2()", true, "OK", 5));
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "return SWFOC_Bad1()", false, "ERR1", 12));
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "return SWFOC_Bad2()", false, "ERR2", 12));
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "return SWFOC_Bad3()", false, "ERR3", 12));

        var line = vm.ActivityStatsLine;

        line.Should().Contain("(3 failed)", "3 failures across 5 calls");
        line.Should().Contain("5 calls");
    }

    [Fact]
    public void ActivityStatsLine_AllFailures_ShowsAllAsFailed()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "return SWFOC_Bad1()", false, "ERR1", 12));
        adapter.RecordForTest(new BridgeActivityEntry(
            DateTimeOffset.UtcNow, "return SWFOC_Bad2()", false, "ERR2", 12));

        var line = vm.ActivityStatsLine;

        line.Should().Contain("0 % OK");
        line.Should().Contain("(2 failed)");
    }
}
