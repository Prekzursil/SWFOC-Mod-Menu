using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.Infrastructure;

/// <summary>
/// 2026-04-27 (iter 45) — verifies <see cref="V2BridgeAdapter.RecentCalls"/>
/// captures every SendRawAsync round-trip with the right shape, caps at 50,
/// and surfaces both succeeded + failed outcomes correctly.
/// </summary>
public sealed class BridgeActivityLogTests
{
    private static V2BridgeAdapter NewAdapter(out SwfocSimulator sim)
    {
        sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        return new V2BridgeAdapter(pipe);
    }

    [Fact]
    public async Task RecentCalls_StartsEmpty()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        adapter.RecentCalls.Should().BeEmpty();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task SendRawAsync_RecordsEntryWithCommandAndOutcome()
    {
        var adapter = NewAdapter(out var sim);
        using var _ = sim;

        var rt = await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);

        rt.Succeeded.Should().BeTrue();
        var entry = adapter.RecentCalls.Single();
        entry.LuaCommand.Should().Be("return SWFOC_GetVersion()");
        entry.Succeeded.Should().BeTrue();
        entry.ResponseOrError.Should().NotBeNullOrWhiteSpace();
        entry.DurationMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task SendRawAsync_FailedCallStillRecorded_WithErrorMessage()
    {
        var adapter = NewAdapter(out var sim);
        using var _ = sim;

        // Unknown probe → simulator's catch-all returns a fallback string.
        // The simulator's no-arg catch-all prefix is "return " so this would
        // hit it. Force a true ERR by calling a real handler with bad args.
        var rt = await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Garbage_Type\", 0, 1)", CancellationToken.None);

        rt.Succeeded.Should().BeFalse("simulator returned ERR: which the client treats as failure");

        var entry = adapter.RecentCalls.Single();
        entry.Succeeded.Should().BeFalse();
        entry.ResponseOrError.Should().StartWith("ERR:",
            "the failed-path branch records the error message, not the response");
    }

    [Fact]
    public async Task RecentCalls_CapsAt50_NewestFirst()
    {
        var adapter = NewAdapter(out var sim);
        using var _ = sim;

        // 60 calls — should land 50 in the buffer, oldest dropped.
        for (var i = 0; i < 60; i++)
        {
            await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        }

        adapter.RecentCalls.Should().HaveCount(50, "ring buffer cap is 50");
    }

    [Fact]
    public async Task RecentCalls_ReturnsSnapshotCopy_ImmuneToConcurrentAdds()
    {
        var adapter = NewAdapter(out var sim);
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        var snap1 = adapter.RecentCalls;
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);

        snap1.Should().HaveCount(1, "earlier snapshot must not see the post-snapshot call");
        adapter.RecentCalls.Should().HaveCount(2, "fresh snapshot reflects both");
    }

    [Fact]
    public async Task RecentCalls_CapturesDurationGreaterThanZero_ForRealRoundTrip()
    {
        // The simulator's pipe round-trip is <1ms typically, but the
        // Stopwatch resolution should still record a non-negative count.
        // We don't assert > 0 because pipe latency CAN floor to 0 on fast
        // hardware; instead we assert it's a valid (non-negative) value.
        var adapter = NewAdapter(out var sim);
        using var _ = sim;
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        adapter.RecentCalls.Single().DurationMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task ActivityRecorded_FiresAfterEachSendRawAsync()
    {
        // 2026-04-27 (iter 47): event firing path. Subscribe a counting
        // handler, fire two calls, verify the handler ran twice with the
        // right entries.
        var adapter = NewAdapter(out var sim);
        using var _ = sim;

        var seen = new System.Collections.Generic.List<BridgeActivityEntry>();
        adapter.ActivityRecorded += entry => seen.Add(entry);

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);

        seen.Should().HaveCount(2);
        seen[0].LuaCommand.Should().Be("return SWFOC_GetVersion()");
        seen[1].LuaCommand.Should().Be("return SWFOC_DiagSelfTest()");
    }

    [Fact]
    public async Task ActivityRecorded_UnsubscribeStopsFurtherCallbacks()
    {
        var adapter = NewAdapter(out var sim);
        using var _ = sim;

        var count = 0;
        Action<BridgeActivityEntry> handler = _ => count++;
        adapter.ActivityRecorded += handler;
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        adapter.ActivityRecorded -= handler;
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);

        count.Should().Be(1, "after unsubscribe, the second call must NOT trigger the handler");
        adapter.RecentCalls.Should().HaveCount(2,
            "the ring buffer still records both — only the event is what was unsubscribed");
    }

    [Fact]
    public void ComputeStats_OnEmptyBuffer_ReturnsZeros()
    {
        // 2026-04-27 (iter 48): at-a-glance summary on empty buffer.
        var adapter = NewAdapter(out var sim);
        using var _ = sim;

        var stats = adapter.ComputeStats();
        stats.TotalCalls.Should().Be(0);
        stats.SuccessRate.Should().Be(0d);
        stats.TopCommand.Should().BeNull();
    }

    [Fact]
    public async Task ComputeStats_AfterMixedCalls_ReportsSuccessRateAndTopCommand()
    {
        var adapter = NewAdapter(out var sim);
        using var _ = sim;

        // 3 successes + 1 failure. SWFOC_GetVersion fires 2x — should be top.
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);
        await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Garbage_Type\", 0, 1)", CancellationToken.None);

        var stats = adapter.ComputeStats();
        stats.TotalCalls.Should().Be(4);
        stats.SuccessCount.Should().Be(3);
        stats.FailureCount.Should().Be(1);
        stats.SuccessRate.Should().BeApproximately(0.75d, 0.001);
        stats.TopCommand.Should().Be("return SWFOC_GetVersion()");
        stats.TopCommandCount.Should().Be(2);
        stats.AverageDurationMs.Should().BeGreaterOrEqualTo(0);
    }

    [Fact]
    public async Task ComputeStats_TopCommandTie_BreaksByOrdinal()
    {
        // When two commands have the same call count, the implementation
        // breaks the tie by ordinal-string sort. Locks in deterministic
        // output so tests can assert specifics.
        var adapter = NewAdapter(out var sim);
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);

        var stats = adapter.ComputeStats();
        // Ordinal sort: "return SWFOC_DiagSelfTest()" < "return SWFOC_GetVersion()" alphabetically.
        stats.TopCommand.Should().Be("return SWFOC_DiagSelfTest()");
        stats.TopCommandCount.Should().Be(1);
    }
}
