using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Simulator.E2E;

/// <summary>
/// 2026-04-27 (iter 32) — Overlay Feature 3 (planet flip with
/// convert/kick modes) validated through the simulator. Pre-builds the
/// bridge capability the overlay's right-click radial menu will eventually
/// expose, so when the overlay ships, the bridge contract is already
/// validated.
/// </summary>
/// <remarks>
/// The three modes mirror the live-engine choice the operator makes when
/// flipping a planet:
/// <list type="bullet">
///   <item><b>default</b> — kick foreign units off the planet but keep
///     them alive (engine's standard post-conquest behaviour).</item>
///   <item><b>convert</b> — re-team foreign units to the new owner.</item>
///   <item><b>pure_kick</b> — destroy foreign units outright.</item>
/// </list>
/// Each test seeds Hoth with mixed garrison ownership, fires the flip,
/// and asserts the per-unit fate matches the chosen mode.
/// </remarks>
public sealed class PlanetFlipModeTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter, FakeGameState state) NewSession()
    {
        var state = new FakeGameStateBuilder()
            .Galactic()
            // 5 Rebel troopers garrisoning Hoth (slot 0 = REBEL).
            .WithUnit("Rebel_Trooper_Squad", slot: 0, count: 5)
            // 3 Empire ATSTs that have happened to be on Hoth (operator
            // perhaps placed them via the editor's spawn tab).
            .WithUnit("Empire_AT_ST", slot: 1, count: 3)
            .Build();
        // Anchor garrison units to Hoth.
        foreach (var u in state.Units) u.OnPlanet = "Hoth";
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, connectTimeoutMs: 1500, readTimeoutMs: 1500);
        return (sim, new V2BridgeAdapter(pipe), state);
    }

    [Fact]
    public async Task DefaultMode_KicksForeignUnitsOffPlanetButKeepsThemAlive()
    {
        var (sim, adapter, state) = NewSession();
        using var _ = sim;

        // Initial state: 5 Rebel + 3 Empire, all on Hoth, all alive.
        state.Units.Should().HaveCount(8);
        state.Units.Where(u => u.OnPlanet == "Hoth").Should().HaveCount(8);

        // Hoth flips from REBEL to EMPIRE with default kick mode.
        var round = await adapter.SendRawAsync(
            "return SWFOC_ChangePlanetOwnerWithMode('Hoth', 'EMPIRE', 'default')",
            CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().StartWith("ok:");

        // Empire ATSTs already aligned with the new owner — stay put.
        state.Units.Where(u => u.OwnerSlot == 1)
            .All(u => u.OnPlanet == "Hoth")
            .Should().BeTrue("aligned units stay garrisoned");

        // Rebel troopers were the foreign units — kicked off Hoth (OnPlanet=""),
        // but they're still alive in the world.
        var rebels = state.Units.Where(u => u.OwnerSlot == 0).ToList();
        rebels.Should().HaveCount(5, "no Rebel unit was destroyed");
        rebels.All(u => u.Alive).Should().BeTrue("kick keeps them alive");
        rebels.All(u => u.OnPlanet == string.Empty).Should().BeTrue(
            "kick removes them from the planet but doesn't destroy them");

        state.EventQueue.Should().Contain(e => e.StartsWith("PLANET_FLIP_KICK:Hoth"));
    }

    [Fact]
    public async Task ConvertMode_ReTeamsForeignUnitsToNewOwner()
    {
        var (sim, adapter, state) = NewSession();
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_ChangePlanetOwnerWithMode('Hoth', 'EMPIRE', 'convert')",
            CancellationToken.None);

        round.Succeeded.Should().BeTrue();

        // All 8 units should now be owned by slot 1 (EMPIRE) and stay on Hoth.
        state.Units.Should().HaveCount(8, "convert mode preserves all units");
        state.Units.All(u => u.OwnerSlot == 1).Should().BeTrue(
            "all foreign units re-teamed to EMPIRE");
        state.Units.All(u => u.OnPlanet == "Hoth").Should().BeTrue(
            "convert keeps them stationed on Hoth");
        state.EventQueue.Should().Contain(e => e.StartsWith("PLANET_FLIP_CONVERT:Hoth"));
    }

    [Fact]
    public async Task PureKickMode_RemovesForeignUnitsFromWorld()
    {
        var (sim, adapter, state) = NewSession();
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_ChangePlanetOwnerWithMode('Hoth', 'EMPIRE', 'pure_kick')",
            CancellationToken.None);

        round.Succeeded.Should().BeTrue();

        // The 5 Rebel troopers are gone from state.Units entirely.
        state.Units.Where(u => u.OwnerSlot == 0).Should().BeEmpty(
            "pure_kick removes foreign units from the world");
        state.Units.Where(u => u.OwnerSlot == 1).Should().HaveCount(3,
            "Empire units are aligned with the new owner — they stay");
        state.EventQueue.Should().Contain(e => e.StartsWith("PLANET_FLIP_PUREKICK:Hoth"));
    }

    [Fact]
    public async Task UnknownMode_ReturnsErrorAndRollsBackOwnership()
    {
        var (sim, adapter, state) = NewSession();
        using var _ = sim;

        var hoth = state.Planets.First(p => p.Name == "Hoth");
        var prevOwner = hoth.OwnerFaction; // "REBEL"

        var round = await adapter.SendRawAsync(
            "return SWFOC_ChangePlanetOwnerWithMode('Hoth', 'EMPIRE', 'launch_into_sun')",
            CancellationToken.None);

        round.Succeeded.Should().BeFalse();
        round.ErrorMessage.Should().Contain("unknown mode");
        hoth.OwnerFaction.Should().Be(prevOwner,
            "ownership change is rolled back when mode is invalid");
        state.Units.Should().HaveCount(8, "no units are touched");
    }

    [Fact]
    public async Task ConvertMode_ReportsAffectedCountInResponse()
    {
        var (sim, adapter, state) = NewSession();
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_ChangePlanetOwnerWithMode('Hoth', 'EMPIRE', 'convert')",
            CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().Be("ok:5",
            "5 Rebel units were converted; the 3 Empire units were already aligned");
    }

    // ==================================================================
    // Overlay Feature 2 — galactic story-hook spawn
    // ==================================================================

    [Fact]
    public async Task SpawnAsStoryArrival_AnchorsUnitToPlanetAndOwner()
    {
        var (sim, adapter, state) = NewSession();
        using var _ = sim;

        // Make sure type+planet+faction are all valid in the seeded scenario.
        state.KnownTypeNames.Add("Rebel_T2A_Tank");
        var initialUnitCount = state.Units.Count;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SpawnAsStoryArrival('Rebel_T2A_Tank', 'Yavin', 'REBEL')",
            CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().StartWith("ok:");

        var spawned = state.Units.LastOrDefault();
        spawned.Should().NotBeNull();
        spawned!.TypeName.Should().Be("Rebel_T2A_Tank");
        spawned.OnPlanet.Should().Be("Yavin");
        spawned.OwnerSlot.Should().Be(0, "REBEL is slot 0 in the seeded campaign");
        state.Units.Count.Should().Be(initialUnitCount + 1);
        state.EventQueue.Should().Contain(e =>
            e.StartsWith("STORY_ARRIVAL:Rebel_T2A_Tank@Yavin"));
    }

    [Fact]
    public async Task SpawnAsStoryArrival_RejectsUnknownType()
    {
        var (sim, adapter, state) = NewSession();
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SpawnAsStoryArrival('Made_Up_Unit', 'Yavin', 'REBEL')",
            CancellationToken.None);

        round.Succeeded.Should().BeFalse();
        round.ErrorMessage.Should().Contain("unknown type");
    }

    [Fact]
    public async Task SpawnAsStoryArrival_RejectsFactionNotInScenario()
    {
        var (sim, adapter, state) = NewSession();
        using var _ = sim;

        state.KnownTypeNames.Add("Rebel_T2A_Tank");

        var round = await adapter.SendRawAsync(
            "return SWFOC_SpawnAsStoryArrival('Rebel_T2A_Tank', 'Yavin', 'YUUZHAN_VONG')",
            CancellationToken.None);

        round.Succeeded.Should().BeFalse();
        round.ErrorMessage.Should().Contain("no such faction");
    }
}
