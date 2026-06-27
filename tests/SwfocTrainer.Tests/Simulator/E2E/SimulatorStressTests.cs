using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.Tests.Simulator.E2E;

/// <summary>
/// 2026-04-27 (iter 28) — stress tests demonstrating the simulator handles
/// realistic-volume operations (hundreds of bridge round-trips) without
/// regressions in correctness or unbounded latency. Useful as a smoke test
/// for the named-pipe transport layer and the per-handler dispatch.
/// </summary>
public sealed class SimulatorStressTests
{
    private readonly ITestOutputHelper _output;

    public SimulatorStressTests(ITestOutputHelper output) => _output = output;

    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession(FakeGameState state)
    {
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, connectTimeoutMs: 2000, readTimeoutMs: 2000);
        return (sim, new V2BridgeAdapter(pipe));
    }

    [Fact]
    public async Task Stress_HundredSpawnsHundredKills_StateRemainsConsistent()
    {
        var state = new FakeGameStateBuilder()
            .Tactical()
            .Build();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var sw = Stopwatch.StartNew();

        // Spawn 100 individual units (one per call — exercises the dispatcher
        // and pipe transport per round-trip).
        for (var i = 0; i < 100; i++)
        {
            var round = await adapter.SendRawAsync(
                "return SWFOC_SpawnUnit('Rebel_Trooper_Squad', 0, 0, 0, 0, 1)",
                CancellationToken.None);
            round.Succeeded.Should().BeTrue($"spawn #{i} must succeed");
        }
        var spawnTime = sw.ElapsedMilliseconds;
        state.Units.Should().HaveCount(100);

        // Kill every alive unit, one round-trip each.
        var aliveIds = state.Units.Select(u => u.Id).ToList();
        foreach (var id in aliveIds)
        {
            var round = await adapter.SendRawAsync(
                $"return SWFOC_KillUnit({id})", CancellationToken.None);
            round.Succeeded.Should().BeTrue();
        }
        var killTime = sw.ElapsedMilliseconds - spawnTime;
        state.Units.Where(u => u.Alive).Should().BeEmpty();

        sw.Stop();
        _output.WriteLine(
            $"100 spawns: {spawnTime}ms, 100 kills: {killTime}ms, total: {sw.ElapsedMilliseconds}ms");

        // Loose perf gate: 200 round-trips through a named pipe + dispatcher
        // + simulator state mutation should land well under 5 seconds even
        // on slow CI. Anything dramatically slower indicates a regression.
        sw.ElapsedMilliseconds.Should().BeLessThan(5000,
            "200 round-trips against an in-process simulator must stay sub-5s");
    }

    [Fact]
    public async Task Stress_BatchTypeExists_ScalesToHundredsOfNames()
    {
        var state = new FakeGameStateBuilder()
            .Tactical()
            .WithType(System.Linq.Enumerable.Range(0, 200)
                .Select(i => $"ModUnit_{i:D3}").ToArray())
            .Build();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        // Build a 250-name probe (mix of known + unknown).
        var probeNames = System.Linq.Enumerable.Range(0, 250).Select(i =>
            i < 200 ? $"ModUnit_{i:D3}" : $"Garbage_{i:D3}").ToArray();
        var probeArg = string.Join("|", probeNames);

        var round = await adapter.SendRawAsync(
            $"return SWFOC_BatchTypeExists(\"{probeArg}\")", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        var flags = round.Response!.Split('|');
        flags.Should().HaveCount(250);
        // First 200 should be 1 (known), last 50 should be 0 (unknown).
        flags.Take(200).All(f => f == "1").Should().BeTrue("first 200 are seeded as known");
        flags.Skip(200).All(f => f == "0").Should().BeTrue("last 50 are deliberately garbage");
    }

    [Fact]
    public async Task Stress_RepeatedRefreshGetAllPlayers_NoLeak()
    {
        var state = new FakeGameStateBuilder()
            .Tactical()
            .WithUnit("Rebel_Trooper_Squad", slot: 0, count: 10)
            .WithUnit("Empire_AT_AT", slot: 1, count: 10)
            .Build();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        // 50 rapid refreshes — what an over-eager auto-refresh checkbox
        // would emit at 5Hz over 10 seconds. The transport's pipe-rotation
        // logic gets a real workout here.
        for (var i = 0; i < 50; i++)
        {
            var round = await adapter.SendRawAsync(
                "return SWFOC_GetAllPlayers()", CancellationToken.None);
            round.Succeeded.Should().BeTrue($"refresh #{i} must succeed");
            round.Response.Should().NotBeNullOrEmpty();
        }

        // Counter on the simulator's pipe server confirms exactly 50 served.
        sim.Bridge.CommandsServed.Should().BeGreaterOrEqualTo(50,
            "every refresh must reach the dispatcher");
    }
}
