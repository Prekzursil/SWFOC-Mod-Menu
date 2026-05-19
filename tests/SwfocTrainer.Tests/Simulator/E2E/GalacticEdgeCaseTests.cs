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
/// 2026-04-27 (iter 37) — boundary + edge-case coverage for the iter
/// 32-34 Galactic features. Catches the "operator typed something
/// unexpected" failure modes the happy-path tests miss.
/// </summary>
public sealed class GalacticEdgeCaseTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter, FakeGameState state) NewSession()
    {
        var state = FakeGameState.NewGalacticCampaign();
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, connectTimeoutMs: 1500, readTimeoutMs: 1500);
        return (sim, new V2BridgeAdapter(pipe), state);
    }

    // ==================================================================
    // ChangePlanetOwnerWithMode — boundaries
    // ==================================================================

    [Fact]
    public async Task ChangePlanetOwnerWithMode_NonExistentPlanet_ReturnsErrorAndPreservesState()
    {
        var (sim, adapter, state) = NewSession();
        using var _ = sim;
        var snapshotBefore = state.Planets.Select(p => (p.Name, p.OwnerFaction)).ToList();

        var round = await adapter.SendRawAsync(
            "return SWFOC_ChangePlanetOwnerWithMode('Tatooine', 'EMPIRE', 'convert')",
            CancellationToken.None);

        round.Succeeded.Should().BeFalse();
        round.ErrorMessage.Should().Contain("no such planet");
        // No planet's owner should have changed.
        var snapshotAfter = state.Planets.Select(p => (p.Name, p.OwnerFaction)).ToList();
        snapshotAfter.Should().BeEquivalentTo(snapshotBefore,
            "an error must not silently mutate other planets");
    }

    [Fact]
    public async Task ChangePlanetOwnerWithMode_ConvertWithEmptyGarrison_ReportsZeroAffected()
    {
        // Hoth starts with NO units in NewGalacticCampaign.
        var (sim, adapter, state) = NewSession();
        using var _ = sim;
        var hothUnitsBefore = state.Units.Count(u => u.OnPlanet == "Hoth");
        hothUnitsBefore.Should().Be(0, "fixture pre-condition");

        var round = await adapter.SendRawAsync(
            "return SWFOC_ChangePlanetOwnerWithMode('Hoth', 'EMPIRE', 'convert')",
            CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().Be("ok:0", "convert with no garrison reports 0 affected");
        var hoth = state.Planets.First(p => p.Name == "Hoth");
        hoth.OwnerFaction.Should().Be("EMPIRE", "ownership still flips even when no garrison exists");
    }

    [Fact]
    public async Task ChangePlanetOwnerWithMode_RepeatedConvert_IsIdempotent()
    {
        var (sim, adapter, state) = NewSession();
        using var _ = sim;
        // Seed a Rebel garrison on Hoth.
        for (var i = 0; i < 3; i++)
        {
            state.Units.Add(new FakeUnit
            {
                TypeName = "Rebel_Trooper_Squad",
                OwnerSlot = 0,
                OnPlanet = "Hoth",
            });
        }

        // First convert: 3 affected.
        var first = await adapter.SendRawAsync(
            "return SWFOC_ChangePlanetOwnerWithMode('Hoth', 'EMPIRE', 'convert')",
            CancellationToken.None);
        first.Response.Should().Be("ok:3");

        // Second convert: 0 affected because the garrison already aligns.
        var second = await adapter.SendRawAsync(
            "return SWFOC_ChangePlanetOwnerWithMode('Hoth', 'EMPIRE', 'convert')",
            CancellationToken.None);
        second.Succeeded.Should().BeTrue();
        second.Response.Should().Be("ok:0", "second call is a no-op when ownership already matches");
    }

    [Fact]
    public async Task ChangePlanetOwnerWithMode_PureKickThenSpawn_LandsOnEmptyPlanet()
    {
        var (sim, adapter, state) = NewSession();
        using var _ = sim;
        for (var i = 0; i < 5; i++)
        {
            state.Units.Add(new FakeUnit
            {
                TypeName = "Rebel_Trooper_Squad",
                OwnerSlot = 0,
                OnPlanet = "Hoth",
            });
        }
        state.KnownTypeNames.Add("Empire_AT_AT");

        // Pure-kick clears Hoth.
        await adapter.SendRawAsync(
            "return SWFOC_ChangePlanetOwnerWithMode('Hoth', 'EMPIRE', 'pure_kick')",
            CancellationToken.None);
        state.Units.Where(u => u.OnPlanet == "Hoth").Should().BeEmpty();

        // Story-arrival spawn after the wipe lands cleanly on the now-empty planet.
        var spawn = await adapter.SendRawAsync(
            "return SWFOC_SpawnAsStoryArrival('Empire_AT_AT', 'Hoth', 'EMPIRE')",
            CancellationToken.None);
        spawn.Succeeded.Should().BeTrue();
        var newGarrison = state.Units.Where(u => u.OnPlanet == "Hoth").ToList();
        newGarrison.Should().HaveCount(1);
        newGarrison[0].OwnerSlot.Should().Be(1, "EMPIRE owns the new garrison");
    }

    // ==================================================================
    // SpawnAsStoryArrival — boundaries
    // ==================================================================

    [Fact]
    public async Task SpawnAsStoryArrival_NonExistentPlanet_ReturnsError()
    {
        var (sim, adapter, state) = NewSession();
        using var _ = sim;
        state.KnownTypeNames.Add("Rebel_T2A_Tank");

        var round = await adapter.SendRawAsync(
            "return SWFOC_SpawnAsStoryArrival('Rebel_T2A_Tank', 'Tatooine', 'REBEL')",
            CancellationToken.None);

        round.Succeeded.Should().BeFalse();
        round.ErrorMessage.Should().Contain("no such planet");
    }

    [Fact]
    public async Task SpawnAsStoryArrival_EmptyArgs_RejectsAtBridge()
    {
        var (sim, adapter, state) = NewSession();
        using var _ = sim;

        // Empty single-quoted strings are valid Lua syntax but the simulator
        // should refuse them rather than spawning a unit named "" on planet "".
        var round = await adapter.SendRawAsync(
            "return SWFOC_SpawnAsStoryArrival('', '', '')",
            CancellationToken.None);

        round.Succeeded.Should().BeFalse();
        round.ErrorMessage.Should().StartWith("ERR:");
        state.Units.Should().BeEmpty();
    }

    // ==================================================================
    // Sequencing — multiple ops in a single session
    // ==================================================================

    [Fact]
    public async Task GalacticOperatorJourney_FlipMultiplePlanetsAndSpawnReinforcements()
    {
        // Realistic journey: operator marches across the galaxy, flipping
        // Empire planets to Rebel, converting their garrisons, and dropping
        // a Rebel reinforcement at each newly-captured planet.
        var (sim, adapter, state) = NewSession();
        using var _ = sim;
        state.KnownTypeNames.Add("Rebel_T2A_Tank");
        // Seed garrisons on Empire-owned planets.
        foreach (var planet in new[] { "Coruscant", "Kuat" })
        {
            for (var i = 0; i < 2; i++)
            {
                state.Units.Add(new FakeUnit
                {
                    TypeName = "Empire_AT_AT",
                    OwnerSlot = 1,
                    OnPlanet = planet,
                });
            }
        }

        // Capture each Empire planet by converting, then drop reinforcements.
        foreach (var planet in new[] { "Coruscant", "Kuat" })
        {
            var flip = await adapter.SendRawAsync(
                $"return SWFOC_ChangePlanetOwnerWithMode('{planet}', 'REBEL', 'convert')",
                CancellationToken.None);
            flip.Succeeded.Should().BeTrue();

            var spawn = await adapter.SendRawAsync(
                $"return SWFOC_SpawnAsStoryArrival('Rebel_T2A_Tank', '{planet}', 'REBEL')",
                CancellationToken.None);
            spawn.Succeeded.Should().BeTrue();
        }

        // Final state assertions.
        var rebelPlanets = state.Planets.Where(p => p.OwnerFaction == "REBEL").Select(p => p.Name).ToList();
        rebelPlanets.Should().Contain(new[] { "Yavin", "Hoth", "Coruscant", "Kuat" });

        // Every unit on captured planets is now Rebel.
        foreach (var planet in new[] { "Coruscant", "Kuat" })
        {
            var garrison = state.Units.Where(u => u.OnPlanet == planet).ToList();
            garrison.Should().NotBeEmpty();
            garrison.All(u => u.OwnerSlot == 0).Should().BeTrue(
                $"after capture+convert, all garrison units on {planet} should be Rebel");
        }

        // Event queue captured every transition.
        state.EventQueue.Count(e => e.StartsWith("PLANET_FLIP_CONVERT")).Should().Be(2);
        state.EventQueue.Count(e => e.StartsWith("STORY_ARRIVAL")).Should().Be(2);
    }
}
