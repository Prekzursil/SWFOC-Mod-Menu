using System.Threading;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.Infrastructure;

/// <summary>
/// 2026-04-28 (iter 74) — pins the new
/// <see cref="V2BridgeAdapter.ComputeCommandSummaries"/> aggregator that
/// powers the Diagnostics group-by-command DataGrid.
/// </summary>
public sealed class Iter74CommandSummaryAggregationTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        return (sim, new V2BridgeAdapter(pipe));
    }

    [Fact]
    public void ComputeCommandSummaries_EmptyBuffer_ReturnsEmpty()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        adapter.ComputeCommandSummaries().Should().BeEmpty();
    }

    [Fact]
    public async Task ComputeCommandSummaries_AggregatesByCommandName()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_DiagSelfTest()", CancellationToken.None);

        var summaries = adapter.ComputeCommandSummaries();
        summaries.Should().HaveCount(2,
            "two distinct command strings");
        summaries[0].CallCount.Should().Be(2,
            "GetVersion has 2 calls — sorts to top by descending count");
        summaries[0].Command.Should().Be("return SWFOC_GetVersion()");
        summaries[1].Command.Should().Be("return SWFOC_DiagSelfTest()");
        summaries[1].CallCount.Should().Be(1);
    }

    [Fact]
    public async Task ComputeCommandSummaries_SortsByCallCountDescending_ThenAlphabetically()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        // Three commands; A x1, B x3, C x2. Expected order: B, C, A.
        await adapter.SendRawAsync("return SWFOC_A()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_B()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_B()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_B()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_C()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_C()", CancellationToken.None);

        var summaries = adapter.ComputeCommandSummaries();
        summaries.Select(s => s.Command).Should().Equal(
            "return SWFOC_B()",
            "return SWFOC_C()",
            "return SWFOC_A()");
    }

    [Fact]
    public async Task ComputeCommandSummaries_AlphaTieBreak_WhenCountsEqual()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        // Two commands, equal count → alphabetical secondary sort.
        await adapter.SendRawAsync("return SWFOC_Zebra()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_Apple()", CancellationToken.None);

        var summaries = adapter.ComputeCommandSummaries();
        summaries[0].Command.Should().Be("return SWFOC_Apple()",
            "alpha tie-break puts Apple before Zebra at equal count");
        summaries[1].Command.Should().Be("return SWFOC_Zebra()");
    }

    [Fact]
    public async Task ComputeCommandSummaries_TracksSuccessAndFailureCounts()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        // Real call (succeeds) + bogus call (returns ERR / fails).
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_NotARealHelper_iter74()", CancellationToken.None);

        var summaries = adapter.ComputeCommandSummaries();
        var version = summaries.First(s => s.Command.Contains("GetVersion"));
        version.CallCount.Should().Be(2);
        version.SuccessCount.Should().Be(2);
        version.FailureCount.Should().Be(0);
        version.SuccessRate.Should().Be(1.0d);
    }

    [Fact]
    public async Task ComputeCommandSummaries_ComputesAvgAndMaxDuration()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        // Sim is fast; mostly catching that Avg/Max are non-negative
        // and correctly bracketed (Avg ≤ Max).
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);
        await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);

        var summary = adapter.ComputeCommandSummaries().Single();
        summary.AverageDurationMs.Should().BeGreaterThanOrEqualTo(0);
        summary.MaxDurationMs.Should().BeGreaterThanOrEqualTo(0);
        summary.AverageDurationMs.Should().BeLessThanOrEqualTo(summary.MaxDurationMs,
            "avg must always be at-or-below max");
    }

    [Fact]
    public void SuccessRate_ZeroCallCount_ReturnsZeroNoDivideByZero()
    {
        var summary = new BridgeCommandSummary(
            Command: "X",
            CallCount: 0,
            SuccessCount: 0,
            FailureCount: 0,
            AverageDurationMs: 0,
            MaxDurationMs: 0);
        summary.SuccessRate.Should().Be(0d,
            "guard against divide-by-zero when CallCount is 0");
    }
}
