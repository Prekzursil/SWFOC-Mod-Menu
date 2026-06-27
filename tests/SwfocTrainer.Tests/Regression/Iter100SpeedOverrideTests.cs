using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-28 (iter 100, master ralph loop) — pins the iter 100 LIVE wire
/// for <c>SWFOC_SetUnitSpeed</c>, <c>SWFOC_SetPerFactionSpeedMultiplier</c>,
/// and <c>SWFOC_ClearUnitSpeedOverride</c>. The real bridge calls the
/// engine's <c>SetSpeedOverride @ RVA 0x3A8C90</c> directly to write the
/// locomotor override field at <c>+0x2A0</c> and set the active flag at
/// <c>+0x29C</c>. The simulator mirrors by writing <see cref="FakeUnit.Speed"/>.
///
/// RED-GREEN pair (one file, three concerns):
///   RED  — the per-faction handler used to parse a faction STRING
///          ("REBEL") even though the dispatcher emits an INT slot.
///          Iter 100 fixed the wire format AND made it apply per-unit.
///   RED  — SWFOC_ClearUnitSpeedOverride didn't exist in iter 99 — the
///          editor had no way to revert without a full restart.
///   GREEN — both helpers now LIVE-wired through the simulator's
///          dispatcher and verified end-to-end.
/// </summary>
public sealed class Iter100SpeedOverrideTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        // Two Rebel units owned by slot 0, one Empire by slot 1, so we
        // can prove the per-faction filter actually filters.
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0, Speed = 100f, MaxSpeed = 100f });
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Plex_Soldier_Squad", OwnerSlot = 0, Speed = 100f, MaxSpeed = 100f });
        state.Units.Add(new FakeUnit { TypeName = "Empire_AT_AT", OwnerSlot = 1, Speed = 100f, MaxSpeed = 100f });
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, adapter);
    }

    [Fact]
    public async Task SetUnitSpeed_AppliesEngineOverrideToTargetUnit()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        var rebel = sim.GameState.Units.First(u => u.OwnerSlot == 0);

        var lua = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "return SWFOC_SetUnitSpeed({0}, 250)", rebel.Id);
        var result = await adapter.SendRawAsync(lua, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().Be("ok");
        rebel.Speed.Should().Be(250f);
        rebel.MaxSpeed.Should().BeGreaterOrEqualTo(rebel.Speed,
            "MaxSpeed must auto-bump so the override stays clamp-valid");
    }

    [Fact]
    public async Task SetUnitSpeed_DoesNotAffectOtherUnits()
    {
        // Symmetric isolation guard — the per-unit call must NOT apply
        // to other units even if they share a slot.
        var (sim, adapter) = NewSession();
        using var _ = sim;
        var rebel0 = sim.GameState.Units.First(u => u.OwnerSlot == 0);
        var rebel1 = sim.GameState.Units.Where(u => u.OwnerSlot == 0).Skip(1).First();
        var initialRebel1Speed = rebel1.Speed;

        var lua = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "return SWFOC_SetUnitSpeed({0}, 500)", rebel0.Id);
        await adapter.SendRawAsync(lua, CancellationToken.None);

        rebel0.Speed.Should().Be(500f);
        rebel1.Speed.Should().Be(initialRebel1Speed,
            "per-unit override must not bleed across siblings");
    }

    [Fact]
    public async Task SetPerFactionSpeedMultiplier_AppliesToEveryUnitOwnedBySlot()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        var rebelUnits = sim.GameState.Units.Where(u => u.OwnerSlot == 0).ToList();
        var empireUnit = sim.GameState.Units.First(u => u.OwnerSlot == 1);
        var initialEmpireSpeed = empireUnit.Speed;

        // Slot 0 = rebels. Apply absolute speed override 350.
        var result = await adapter.SendRawAsync(
            "return SWFOC_SetPerFactionSpeedMultiplier(0, 350)", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().Be("ok");
        rebelUnits.Should().AllSatisfy(u => u.Speed.Should().Be(350f),
            "every unit owned by slot 0 must receive the speed override");
        empireUnit.Speed.Should().Be(initialEmpireSpeed,
            "units owned by slot 1 must NOT be affected by the slot-0 call");
    }

    [Fact]
    public async Task SetPerFactionSpeedMultiplier_StoresValueOnGameStateMap()
    {
        // Cache mirror — Diagnostics tab can show "what's currently
        // applied per slot" by reading PerFactionSpeed.
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_SetPerFactionSpeedMultiplier(0, 500)", CancellationToken.None);

        sim.GameState.PerFactionSpeed.Should().ContainKey("0");
        sim.GameState.PerFactionSpeed["0"].Should().Be(500f);
    }

    [Fact]
    public async Task ClearUnitSpeedOverride_RevertsSpeedToMaxSpeed()
    {
        // After SetUnitSpeed bumps MaxSpeed to match Speed, the engine's
        // ClearSpeedOverride reverts to natural max speed. Simulator
        // models this by setting Speed back to MaxSpeed.
        var (sim, adapter) = NewSession();
        using var _ = sim;
        var unit = sim.GameState.Units.First(u => u.OwnerSlot == 0);
        unit.MaxSpeed = 100f;
        unit.Speed = 100f;

        var setLua = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "return SWFOC_SetUnitSpeed({0}, 250)", unit.Id);
        await adapter.SendRawAsync(setLua, CancellationToken.None);
        unit.Speed.Should().Be(250f);

        // After SetUnitSpeed, MaxSpeed got bumped to 250 to keep clamp valid.
        // Now revert MaxSpeed back to engine-default before clear, so the
        // post-clear value reflects "engine default" (100) not the bumped 250.
        unit.MaxSpeed = 100f;

        var clrLua = string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "return SWFOC_ClearUnitSpeedOverride({0})", unit.Id);
        var result = await adapter.SendRawAsync(clrLua, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().Be("ok");
        unit.Speed.Should().Be(100f, "ClearSpeedOverride reverts to natural max speed");
    }

    [Fact]
    public void Catalog_MarksSetUnitSpeed_AsLive()
    {
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_SetUnitSpeed");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 100 wired SetSpeedOverride engine call; this helper is LIVE");
        entry.Note.Should().Contain("SetSpeedOverride");
    }

    [Fact]
    public void Catalog_MarksSetPerFactionSpeedMultiplier_AsLive()
    {
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_SetPerFactionSpeedMultiplier");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 100 wired per-unit SetSpeedOverride enumeration; this helper is LIVE");
        entry.Note.Should().Contain("SetSpeedOverride");
    }

    [Fact]
    public void Catalog_MarksClearUnitSpeedOverride_AsLive()
    {
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_ClearUnitSpeedOverride");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 100 added the revert helper; this helper is LIVE");
        entry.Note.Should().Contain("ClearSpeedOverride");
    }

    [Fact]
    public void Catalog_MarksGetUnitSpeed_AsLive()
    {
        // Iter 100 also flipped GetUnitSpeed to LIVE — it now reads the
        // engine's locomotor override directly when active.
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_GetUnitSpeed");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 100 added engine-locomotor read at +0x2A0; LIVE");
    }
}
