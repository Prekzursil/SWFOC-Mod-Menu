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
/// 2026-04-27 (iter 22) — end-to-end functional verification of editor
/// features against the in-memory <see cref="SwfocSimulator"/>. The test
/// fixture pattern is: spin up a fresh simulator, point a real
/// <see cref="V2BridgeAdapter"/> at its pipe, drive bridge commands, and
/// assert that <see cref="FakeGameState"/> mutated in the way the
/// feature claims to mutate the live engine.
/// </summary>
/// <remarks>
/// <para>
/// These are TRUE functional tests — same wire bytes the editor would
/// emit to powrprof.dll, but answered by an in-process state machine.
/// They prove that "Spawn 3 Rebel_Trooper_Squad in slot 0" actually
/// produces 3 alive, owned-by-slot-0 units, and "SetHumanPlayer_v3(1)"
/// flips the IsLocal/IsHuman flags on slots correctly.
/// </para>
/// <para>
/// <b>Why this matters</b>: previously the only verification path for
/// editor features was "alt-tab into the running game and eyeball it".
/// That gates every change on a live SWFOC session, prevents CI, and
/// can't catch silent regressions. This harness gives the same
/// confidence at unit-test speed.
/// </para>
/// <para>
/// Phase A coverage: spawning, faction switch, invulnerability,
/// prevent-death, set-hull, kill, planet reveal, credits, story flags.
/// </para>
/// </remarks>
public sealed class SwfocSimulatorEndToEndTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession(FakeGameState state)
    {
        var sim = new SwfocSimulator(state);
        sim.Start();
        // Tighter timeouts than production — we're in-process and want
        // tests to fail fast if the simulator handler is wrong.
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, connectTimeoutMs: 1500, readTimeoutMs: 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, adapter);
    }

    [Fact]
    public async Task GetVersion_ReturnsSimulatorBanner()
    {
        var (sim, adapter) = NewSession(FakeGameState.NewTacticalSkirmish());
        using var _ = sim;

        var round = await adapter.SendRawAsync("return SWFOC_GetVersion()", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().Contain("simulator", "the simulator must self-identify");
    }

    [Fact]
    public async Task GetAllPlayers_EmitsSlotMapInRealWireFormat()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync("return SWFOC_GetAllPlayers()", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        var rows = round.Response!.Split('|');
        rows.Should().HaveCount(state.Players.Count);

        // Verify slot 0 is REBEL human local — same parse path as
        // PlayerStateTabViewModel.RefreshSlotMapAsync.
        var first = rows[0].Split(';');
        first[0].Should().Be("0");
        first[1].Should().Be("REBEL");
        first[3].Should().Be("1", "slot 0 is human");
        first[5].Should().Be("1", "slot 0 is local");
    }

    [Fact]
    public async Task SetHumanPlayerV3_FlipsLocalAndHumanFlags()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        // Switch from REBEL (slot 0) to EMPIRE (slot 1).
        var round = await adapter.SendRawAsync(
            "return SWFOC_SetHumanPlayer_v3(1)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().Contain("EMPIRE");

        var oldHuman = state.GetPlayer(0)!;
        var newHuman = state.GetPlayer(1)!;
        oldHuman.IsHuman.Should().BeFalse();
        oldHuman.IsLocal.Should().BeFalse();
        oldHuman.HasAiBrain.Should().BeTrue("v3's contribution: old slot gets the AI brain");
        newHuman.IsHuman.Should().BeTrue();
        newHuman.IsLocal.Should().BeTrue();
        newHuman.HasAiBrain.Should().BeFalse("v3 swap: new slot must NOT keep the old AI brain (the dual-control bug)");
    }

    [Fact]
    public async Task BatchTypeExists_ReturnsCorrectFlagsPerName()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        // Two known, one unknown.
        var round = await adapter.SendRawAsync(
            "return SWFOC_BatchTypeExists(\"Rebel_Trooper_Squad|Empire_AT_AT|Garbage_Type_X\")",
            CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().Be("1|1|0");
    }

    [Fact]
    public async Task SpawnUnit_AddsAliveUnitsToWorld()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        state.Units.Should().BeEmpty("world starts empty");

        var round = await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Rebel_Trooper_Squad\", 0, 3)", CancellationToken.None);
        round.Succeeded.Should().BeTrue();
        round.Response.Should().Be("ok:3");

        state.Units.Should().HaveCount(3);
        state.Units.All(u => u.TypeName == "Rebel_Trooper_Squad").Should().BeTrue();
        state.Units.All(u => u.OwnerSlot == 0).Should().BeTrue();
        state.Units.All(u => u.Alive).Should().BeTrue();
    }

    [Fact]
    public async Task SpawnUnit_RejectsUnknownTypeName()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SpawnUnit(\"Garbage_Type\", 0, 1)", CancellationToken.None);

        // NamedPipeLuaBridgeClient classifies any "ERR:"-prefixed response as
        // a transport-level failure (line 168 in the production code). The
        // simulator returns "ERR: unknown type", so Succeeded must be false
        // and the error text is in ErrorMessage. This is the same contract
        // the editor's VM error handlers see against the real bridge.
        round.Succeeded.Should().BeFalse("ERR-prefixed replies are surfaced as failures by the client");
        round.ErrorMessage.Should().StartWith("ERR:");
        state.Units.Should().BeEmpty("nothing should be added when the type is rejected");
    }

    [Fact]
    public async Task SetUnitInvuln_BlocksDamage()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var unit = new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0, MaxHull = 100, CurrentHull = 100 };
        state.Units.Add(unit);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            $"return SWFOC_SetUnitInvuln({unit.Id}, 1)", CancellationToken.None);
        round.Succeeded.Should().BeTrue();

        // Apply 999 damage — engine call site would be a separate path, but
        // the simulator's ApplyDamage models it. The flag was set via the bridge.
        var actual = unit.ApplyDamage(999f);

        actual.Should().Be(0f, "invulnerable units take zero damage");
        unit.Alive.Should().BeTrue();
        unit.CurrentHull.Should().Be(100f);
    }

    [Fact]
    public async Task PreventUnitDeath_CapsHullAtOne()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var unit = new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0, MaxHull = 100, CurrentHull = 100 };
        state.Units.Add(unit);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            $"return SWFOC_PreventUnitDeath({unit.Id}, 1)", CancellationToken.None);
        round.Succeeded.Should().BeTrue();

        unit.ApplyDamage(999f);

        unit.Alive.Should().BeTrue("prevent-death keeps the unit alive at 1 hull");
        unit.CurrentHull.Should().Be(1f);
    }

    [Fact]
    public async Task KillUnit_FlipsAliveFalse()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var unit = new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0 };
        state.Units.Add(unit);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            $"return SWFOC_KillUnit({unit.Id})", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().Be("ok");
        unit.Alive.Should().BeFalse();
        unit.CurrentHull.Should().Be(0f);
    }

    [Fact]
    public async Task SetCredits_PersistsToGameState()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SetCredits(0, 99999)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.GetPlayer(0)!.Credits.Should().Be(99999);
    }

    [Fact]
    public async Task RevealAll_FlipsEveryPlanetVisible()
    {
        var state = FakeGameState.NewGalacticCampaign();
        state.Planets.All(p => !p.IsRevealed).Should().BeTrue("planets start hidden");
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync("return SWFOC_RevealAll()", CancellationToken.None);
        round.Succeeded.Should().BeTrue();

        state.Planets.All(p => p.IsRevealed).Should().BeTrue("RevealAll should flip every planet");
    }

    [Fact]
    public async Task FireStoryEvent_AddsFlagToState()
    {
        var state = FakeGameState.NewGalacticCampaign();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_FireStoryEvent(\"REBEL_VICTORY\")", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.StoryFlags.Should().Contain("REBEL_VICTORY");
    }

    [Fact]
    public async Task ListTacticalUnits_OnlyEmitsAliveOnes()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0 });
        state.Units.Add(new FakeUnit { TypeName = "Empire_AT_ST", OwnerSlot = 1, Alive = false });
        state.Units.Add(new FakeUnit { TypeName = "Empire_AT_AT", OwnerSlot = 1 });
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_ListTacticalUnits()", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        var rows = round.Response!.Split('|');
        rows.Should().HaveCount(2, "dead units must not appear in the live list");
        rows.Any(r => r.Contains("Rebel_Trooper_Squad")).Should().BeTrue();
        rows.Any(r => r.Contains("Empire_AT_AT")).Should().BeTrue();
        rows.Any(r => r.Contains("AT_ST")).Should().BeFalse();
    }
}
