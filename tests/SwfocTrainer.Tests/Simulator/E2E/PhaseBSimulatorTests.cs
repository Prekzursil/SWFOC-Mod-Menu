using System;
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
/// 2026-04-27 (iter 23) — Phase B handlers covering Combat scalars,
/// Speed, Hero Lab, AI brain, Diplomacy, Economy, Event stream, and
/// Galactic specifics. Each test exercises one bridge function through
/// the real adapter stack and asserts the simulated game state mutated
/// the way the live engine would.
/// </summary>
/// <remarks>
/// <para>
/// The original PHASE 2 PENDING badges on Combat scalar controls
/// (damage/shield/firerate) and per-faction Speed were the user's
/// specific concern: "operator will think they're broken (or worse,
/// will think they're working when they're not)". These tests prove
/// the bridge call paths are wired correctly through to a stateful
/// engine.
/// </para>
/// </remarks>
public sealed class PhaseBSimulatorTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession(FakeGameState state)
    {
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, connectTimeoutMs: 1500, readTimeoutMs: 1500);
        return (sim, new V2BridgeAdapter(pipe));
    }

    // ==================================================================
    // Combat scalars
    // ==================================================================

    [Fact]
    public async Task SetDamageMultiplier_PerSlot_UpdatesScalarOnAllSlotUnits()
    {
        // Dispatcher emits SWFOC_SetDamageMultiplier(slot, mult) — per-slot
        // scaling, not per-unit. Every alive unit owned by that slot picks
        // up the new scalar; the per-slot dictionary is also updated for
        // late-spawn semantics (newly spawned units inherit the scalar).
        var state = FakeGameState.NewTacticalSkirmish();
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0, DamageScalar = 1f });
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Plex_Soldier_Squad", OwnerSlot = 0, DamageScalar = 1f });
        state.Units.Add(new FakeUnit { TypeName = "Empire_AT_AT", OwnerSlot = 1, DamageScalar = 1f });
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SetDamageMultiplier(0, 2.5)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.PerSlotDamageMultiplier[0].Should().Be(2.5f);
        state.Units.Where(u => u.OwnerSlot == 0).All(u => u.DamageScalar == 2.5f).Should().BeTrue();
        state.Units.First(u => u.OwnerSlot == 1).DamageScalar.Should().Be(1f, "other slots untouched");
    }

    [Fact]
    public async Task SetDamageMultiplier_RejectsUnknownSlot()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SetDamageMultiplier(99, 3.0)", CancellationToken.None);

        round.Succeeded.Should().BeFalse();
        round.ErrorMessage.Should().Contain("no such slot");
    }

    [Fact]
    public async Task SetFireRate_PerSlot_UpdatesScalarOnAllSlotUnits()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0, FireRateScalar = 1f });
        state.Units.Add(new FakeUnit { TypeName = "Empire_AT_AT", OwnerSlot = 1, FireRateScalar = 1f });
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SetFireRate(0, 1.75)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.PerSlotFireRateMultiplier[0].Should().Be(1.75f);
        state.Units.First(u => u.OwnerSlot == 0).FireRateScalar.Should().Be(1.75f);
        state.Units.First(u => u.OwnerSlot == 1).FireRateScalar.Should().Be(1f, "other slots untouched");
    }

    [Fact]
    public async Task SetUnitShield_RaisesMaxShield_WhenExceeded()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var unit = new FakeUnit { TypeName = "Empire_AT_AT", OwnerSlot = 1, MaxShield = 100, CurrentShield = 100 };
        state.Units.Add(unit);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            $"return SWFOC_SetUnitShield({unit.Id}, 500)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        unit.CurrentShield.Should().Be(500);
        unit.MaxShield.Should().Be(500, "exceeding MaxShield should bump it up");
    }

    [Fact]
    public async Task OneHitKill_SetsAliveFalse_AndZeroesShields()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var unit = new FakeUnit { TypeName = "Empire_AT_AT", OwnerSlot = 1, CurrentHull = 9999, CurrentShield = 9999 };
        state.Units.Add(unit);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            $"return SWFOC_OneHitKill({unit.Id})", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        unit.Alive.Should().BeFalse();
        unit.CurrentHull.Should().Be(0);
        unit.CurrentShield.Should().Be(0);
    }

    // ==================================================================
    // Speed
    // ==================================================================

    [Fact]
    public async Task SetGameSpeed_UpdatesGlobalMultiplier()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SetGameSpeed(2.5)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.GameSpeed.Should().Be(2.5f);
    }

    [Fact]
    public async Task SetPerFactionSpeedMultiplier_UpdatesPerFactionDictionary()
    {
        // 2026-04-28 (iter 100, master ralph loop): bridge wire format is
        // `(slot, mult)` (int slot), not `(faction_name, mult)`. The
        // simulator handler now applies the override per-unit (mirroring
        // the engine's SetSpeedOverride enumeration).
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SetPerFactionSpeedMultiplier(0, 1.75)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.PerFactionSpeed.Should().ContainKey("0",
            "iter 100: per-faction speed map keyed by slot string");
        state.PerFactionSpeed["0"].Should().Be(1.75f);
    }

    [Fact]
    public async Task SetUnitSpeed_RaisesMaxSpeed_WhenExceeded()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var unit = new FakeUnit { TypeName = "Empire_AT_ST", OwnerSlot = 1, Speed = 100, MaxSpeed = 100 };
        state.Units.Add(unit);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            $"return SWFOC_SetUnitSpeed({unit.Id}, 350)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        unit.Speed.Should().Be(350);
        unit.MaxSpeed.Should().Be(350, "exceeding MaxSpeed should bump it up");
    }

    // ==================================================================
    // Hero Lab
    // ==================================================================

    [Fact]
    public async Task ListHeroes_ReturnsOnlyHeroes()
    {
        // BridgeHeroLabDispatcher.ListHeroesAsync parses NEWLINE-separated rows
        // (not pipe). Format: addr;type;owner;alive;respawn_ms;respawn_enabled.
        var state = FakeGameState.NewTacticalSkirmish();
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0 });
        state.Units.Add(new FakeUnit { TypeName = "Han_Solo", OwnerSlot = 0, IsHero = true });
        state.Units.Add(new FakeUnit { TypeName = "Luke_Skywalker", OwnerSlot = 0, IsHero = true });
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_ListHeroes()", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        var rows = round.Response!.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        rows.Should().HaveCount(2, "only hero-flagged units appear");
        rows.Any(r => r.Contains("Han_Solo")).Should().BeTrue();
        rows.Any(r => r.Contains("Luke_Skywalker")).Should().BeTrue();
        rows.Any(r => r.Contains("Trooper")).Should().BeFalse();
        // Each row has 6 semicolon-separated fields (the dispatcher's contract).
        rows.All(r => r.Split(';').Length == 6).Should().BeTrue();
    }

    [Fact]
    public async Task HeroInstantRespawn_RevivesDeadHero()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var hero = new FakeUnit
        {
            TypeName = "Han_Solo",
            OwnerSlot = 0,
            IsHero = true,
            MaxHull = 200,
            CurrentHull = 0,
            Alive = false,
        };
        state.Units.Add(hero);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            $"return SWFOC_HeroInstantRespawn({hero.Id})", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        hero.Alive.Should().BeTrue();
        hero.CurrentHull.Should().Be(200);
    }

    [Fact]
    public async Task HeroInstantRespawn_RejectsNonHero()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var trooper = new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0, Alive = false };
        state.Units.Add(trooper);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            $"return SWFOC_HeroInstantRespawn({trooper.Id})", CancellationToken.None);

        round.Succeeded.Should().BeFalse();
        round.ErrorMessage.Should().Contain("not a hero");
    }

    [Fact]
    public async Task HeroStatEdit_UpdatesNamedStat()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var hero = new FakeUnit
        {
            TypeName = "Han_Solo",
            OwnerSlot = 0,
            IsHero = true,
            MaxHull = 200,
            CurrentHull = 200,
        };
        state.Units.Add(hero);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            $"return SWFOC_HeroStatEdit({hero.Id}, \"MaxHull\", 9999)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        hero.MaxHull.Should().Be(9999);
    }

    [Fact]
    public async Task SetHeroRespawn_UpdatesGlobalTimer()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SetHeroRespawn(15)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.HeroRespawnSeconds.Should().Be(15);
    }

    [Fact]
    public async Task SetPermadeath_FlipsGlobalToggle()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SetPermadeath(1)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.Permadeath.Should().BeTrue();
    }

    // ==================================================================
    // AI brain
    // ==================================================================

    [Fact]
    public async Task NullAiBrain_ClearsAiFlag()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        state.GetPlayer(1)!.HasAiBrain.Should().BeTrue();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_NullAiBrain(1)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.GetPlayer(1)!.HasAiBrain.Should().BeFalse();
    }

    [Fact]
    public async Task AttachAiBrain_RestoresAiFlag()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        state.GetPlayer(0)!.HasAiBrain.Should().BeFalse("slot 0 starts as human, no AI brain");
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_AttachAiBrain(0)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.GetPlayer(0)!.HasAiBrain.Should().BeTrue();
    }

    [Fact]
    public async Task FreezeAi_DisablesGlobalAi()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        state.AiEnabled.Should().BeTrue();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_FreezeAI(1)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.AiEnabled.Should().BeFalse();
    }

    // ==================================================================
    // Diplomacy
    // ==================================================================

    [Fact]
    public async Task SetDiplomacy_AlliesTwoSlots()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SetDiplomacy(0, 1, \"Allied\")", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.Diplomacy.Should().ContainKey("0:1");
        state.Diplomacy["0:1"].Should().Be("Allied");
    }

    [Fact]
    public async Task SetDiplomacy_RejectsBadRelationToken()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SetDiplomacy(0, 1, \"Friendly\")", CancellationToken.None);

        round.Succeeded.Should().BeFalse();
        round.ErrorMessage.Should().Contain("bad relation");
    }

    // ==================================================================
    // Economy
    // ==================================================================

    [Fact]
    public async Task SetIncomeMultiplier_StoresPerFactionScalar()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_SetIncomeMultiplier(\"REBEL\", 5.0)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.PerFactionIncome["REBEL"].Should().Be(5f);
    }

    [Fact]
    public async Task DrainEnemyCredits_ZeroesOtherSlotsOnly()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        // Sanity: every slot starts with 5000.
        state.Players.All(p => p.Credits == 5000).Should().BeTrue();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_DrainEnemyCredits(0)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.GetPlayer(0)!.Credits.Should().Be(5000, "the operator's own slot is preserved");
        state.Players.Where(p => p.Slot != 0).All(p => p.Credits == 0).Should().BeTrue();
    }

    // ==================================================================
    // Galactic
    // ==================================================================

    [Fact]
    public async Task ChangePlanetOwner_UpdatesOwner()
    {
        var state = FakeGameState.NewGalacticCampaign();
        var hoth = state.Planets.First(p => p.Name == "Hoth");
        hoth.OwnerSlot.Should().Be(0);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_ChangePlanetOwner(\"Hoth\", 1)", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        hoth.OwnerSlot.Should().Be(1);
    }

    [Fact]
    public async Task InstantBuild_IncrementsStructureCount()
    {
        var state = FakeGameState.NewGalacticCampaign();
        var hoth = state.Planets.First(p => p.Name == "Hoth");
        hoth.Structures.Should().Be(0);
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_InstantBuild(\"Hoth\")", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        hoth.Structures.Should().Be(1);
    }

    // ==================================================================
    // Event stream
    // ==================================================================

    [Fact]
    public async Task EventStreamDrain_ReturnsAndClearsQueue()
    {
        var state = FakeGameState.NewGalacticCampaign();
        state.EventQueue.Enqueue("PLANET_OWNED:Hoth:1");
        state.EventQueue.Enqueue("STORY_FIRED:DEATH_STAR_BUILT");
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_EventStreamDrain()", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        round.Response.Should().Contain("PLANET_OWNED:Hoth:1");
        round.Response.Should().Contain("STORY_FIRED:DEATH_STAR_BUILT");
        state.EventQueue.Should().BeEmpty("drain must consume the queue");
    }

    [Fact]
    public async Task EventControl_Clear_EmptiesQueue()
    {
        var state = FakeGameState.NewGalacticCampaign();
        state.EventQueue.Enqueue("noise1");
        state.EventQueue.Enqueue("noise2");
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_EventControl(\"clear\")", CancellationToken.None);

        round.Succeeded.Should().BeTrue();
        state.EventQueue.Should().BeEmpty();
    }

    [Fact]
    public async Task FireStoryEvent_AlsoEmitsToEventQueue()
    {
        // Phase B contribution: story-event firings now feed the event queue
        // as well as the StoryFlags set, so the Event Stream tab can show them.
        var state = FakeGameState.NewGalacticCampaign();
        var (sim, adapter) = NewSession(state);
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_FireStoryEvent(\"REBEL_VICTORY\")", CancellationToken.None);

        state.StoryFlags.Should().Contain("REBEL_VICTORY");
        state.EventQueue.Should().Contain("STORY_FIRED:REBEL_VICTORY");
    }
}
