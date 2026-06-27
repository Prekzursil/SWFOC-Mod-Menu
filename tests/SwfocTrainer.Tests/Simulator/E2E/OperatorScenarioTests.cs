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
/// 2026-04-27 (iter 22) — multi-step operator scenarios proving the
/// simulator validates real workflows, not just isolated function calls.
/// Each test models a concrete journey through the editor's V2 surface
/// and asserts that the simulated game state ends up in the expected
/// shape.
/// </summary>
/// <remarks>
/// <para>
/// These complement <see cref="SwfocSimulatorEndToEndTests"/>: those test
/// individual bridge functions, these test sequences-of-actions like a
/// human operator would perform. They catch ordering bugs that
/// per-function tests can't, e.g. "spawning depends on the slot already
/// existing in GetAllPlayers".
/// </para>
/// </remarks>
public sealed class OperatorScenarioTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession(FakeGameState state)
    {
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, connectTimeoutMs: 1500, readTimeoutMs: 1500);
        return (sim, new V2BridgeAdapter(pipe));
    }

    [Fact]
    public async Task TacticalSkirmish_SpawnArmy_KillEnemies_FactionSwitch_FullJourney()
    {
        // Initial state: 4-player tactical skirmish, REBEL human in slot 0,
        // EMPIRE/UNDERWORLD/NEUTRAL AIs.
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        // 1. Operator connects → editor probes slot map.
        var slotMap = await adapter.SendRawAsync("return SWFOC_GetAllPlayers()", CancellationToken.None);
        slotMap.Succeeded.Should().BeTrue();
        slotMap.Response!.Split('|').Should().HaveCount(4);

        // 2. Operator spawns a Rebel army for themselves.
        var spawn1 = await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Rebel_Trooper_Squad\", 0, 5)", CancellationToken.None);
        spawn1.Succeeded.Should().BeTrue();
        spawn1.Response.Should().Be("ok:5");

        // 3. Operator spawns 3 enemy AT-ATs in slot 1 (EMPIRE) for stress-testing.
        var spawn2 = await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Empire_AT_AT\", 1, 3)", CancellationToken.None);
        spawn2.Succeeded.Should().BeTrue();
        spawn2.Response.Should().Be("ok:3");

        state.Units.Should().HaveCount(8, "5 Rebel + 3 Empire");

        // 4. Operator selects all enemy units and kills them via Kill all enemies.
        var enemyIds = state.Units.Where(u => u.OwnerSlot == 1).Select(u => u.Id).ToList();
        foreach (var id in enemyIds)
        {
            var kill = await adapter.SendRawAsync(
                $"return SWFOC_KillUnit({id})", CancellationToken.None);
            kill.Succeeded.Should().BeTrue();
        }
        state.Units.Count(u => u.Alive).Should().Be(5, "only Rebel units remain alive");

        // 5. Operator switches to EMPIRE (slot 1) for cross-faction control.
        var swap = await adapter.SendRawAsync(
            "return SWFOC_SetHumanPlayer_v3(1)", CancellationToken.None);
        swap.Succeeded.Should().BeTrue();

        // 6. Verify: human flag is on slot 1 now, slot 0 has the AI brain.
        var newMap = await adapter.SendRawAsync("return SWFOC_GetAllPlayers()", CancellationToken.None);
        newMap.Succeeded.Should().BeTrue();
        var rows = newMap.Response!.Split('|').ToDictionary(
            r => r.Split(';')[0], r => r.Split(';'));
        rows["0"][3].Should().Be("0", "old slot is no longer human");
        rows["0"][4].Should().Be("1", "old slot has AI brain attached");
        rows["1"][3].Should().Be("1", "new slot is now human");
        rows["1"][5].Should().Be("1", "new slot is now local");

        // 7. Operator spawns more units under their NEW slot (Empire control).
        var spawn3 = await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Empire_AT_ST\", 1, 2)", CancellationToken.None);
        spawn3.Succeeded.Should().BeTrue();

        var slot1Alive = state.Units.Count(u => u.OwnerSlot == 1 && u.Alive);
        slot1Alive.Should().Be(2, "old Empire units were killed; only the 2 new AT-ST count");
    }

    [Fact]
    public async Task GalacticCampaign_RevealAll_FireStoryEvent_AdjustCredits_Sequence()
    {
        var state = FakeGameState.NewGalacticCampaign();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        state.Planets.Should().NotBeEmpty();
        state.Planets.All(p => !p.IsRevealed).Should().BeTrue("planets start hidden");

        // 1. Reveal galaxy.
        var reveal = await adapter.SendRawAsync("return SWFOC_RevealAll()", CancellationToken.None);
        reveal.Succeeded.Should().BeTrue();
        state.Planets.All(p => p.IsRevealed).Should().BeTrue();

        // 2. Fire story event.
        var fire = await adapter.SendRawAsync(
            "return SWFOC_FireStoryEvent(\"DEATH_STAR_BUILT\")", CancellationToken.None);
        fire.Succeeded.Should().BeTrue();
        state.StoryFlags.Should().Contain("DEATH_STAR_BUILT");

        // 3. Set credits across 3 slots.
        for (var slot = 0; slot < 3; slot++)
        {
            var set = await adapter.SendRawAsync(
                $"return SWFOC_SetCredits({slot}, {(slot + 1) * 50000})", CancellationToken.None);
            set.Succeeded.Should().BeTrue();
        }
        state.GetPlayer(0)!.Credits.Should().Be(50000);
        state.GetPlayer(1)!.Credits.Should().Be(100000);
        state.GetPlayer(2)!.Credits.Should().Be(150000);
    }

    [Fact]
    public async Task UnitControl_InvulnThenAttemptKill_RemainsAlive()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var unit = new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0, MaxHull = 100, CurrentHull = 100 };
        state.Units.Add(unit);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        // Make invulnerable, then try to kill via the engine path.
        var invuln = await adapter.SendRawAsync(
            $"return SWFOC_SetUnitInvuln({unit.Id}, 1)", CancellationToken.None);
        invuln.Succeeded.Should().BeTrue();
        unit.Invulnerable.Should().BeTrue();

        // KillUnit is a direct kill, not damage-based. The simulator models
        // the live engine: KillUnit ignores invulnerability. So the operator
        // can still force-kill an invuln'd unit (matches the engine's actual
        // behaviour — invuln blocks DAMAGE, not engine-level destroy).
        var kill = await adapter.SendRawAsync(
            $"return SWFOC_KillUnit({unit.Id})", CancellationToken.None);
        kill.Succeeded.Should().BeTrue();
        unit.Alive.Should().BeFalse("KillUnit is direct, not damage-based");

        // But if we revive and apply damage, invuln still blocks it.
        unit.Revive();
        unit.Invulnerable.Should().BeTrue("invuln flag survived KillUnit + Revive");
        unit.ApplyDamage(999f);
        unit.CurrentHull.Should().Be(100f, "invuln blocks damage");
        unit.Alive.Should().BeTrue();
    }

    [Fact]
    public async Task ListTacticalUnits_ReflectsLiveSpawnsAndKills()
    {
        // Note: the live bridge always has at least the local player's units
        // present, so the "0 tactical units" case never reaches the wire
        // protocol in a real game session. The bridge transport treats a
        // 0-byte payload as connection failure, so we don't test that path
        // here — the meaningful coverage is the count-after-mutation flow.
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        // Spawn 3.
        await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Rebel_Trooper_Squad\", 0, 3)", CancellationToken.None);
        var listAfterSpawn = await adapter.SendRawAsync(
            "return SWFOC_ListTacticalUnits()", CancellationToken.None);
        listAfterSpawn.Succeeded.Should().BeTrue();
        listAfterSpawn.Response!.Split('|').Should().HaveCount(3);

        // Kill the first one.
        var firstId = state.Units[0].Id;
        await adapter.SendRawAsync($"return SWFOC_KillUnit({firstId})", CancellationToken.None);

        var listAfterKill = await adapter.SendRawAsync(
            "return SWFOC_ListTacticalUnits()", CancellationToken.None);
        listAfterKill.Succeeded.Should().BeTrue();
        listAfterKill.Response!.Split('|').Should().HaveCount(2,
            "the dead unit should not appear in the live list");
    }
}
