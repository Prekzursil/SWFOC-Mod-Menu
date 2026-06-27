using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 244) -- pins the simulator handler extension + round-trip
/// behavior for the iter-243 LIVE branches of SWFOC_SetUnitField (invuln_flag
/// + prevent_death).
///
/// Bridge-side (iter 243): direct memory writes
///   - invuln_flag → byte at GameObj+0x3A7 (RVA::GameObj::InvulnFlag).
///   - prevent_death → bit 0x80 of byte at GameObj+0x3A1 (RVA::GameObj::PreventDeath).
///
/// Both LIVE branches carry the engine-state-machine bypass caveat per the
/// feedback_flag_flipping_vs_engine_state memory rule. Engine-state-aware
/// alternatives:
///   - iter-110 SWFOC_MakeInvulnerableLua (BehaviorMarker + per-hardpoint
///     INVULNERABLE attachments via QueryInterface(0x16)).
///   - iter-153 SWFOC_SetCannotBeKilledLua (engine Set_Cannot_Be_Killed Lua API).
///
/// Simulator-side (iter 244): HandleSetUnitField extended with canonical
/// snake_case branches matching the bridge's 13-field taxonomy. Iter 136's
/// 3 LIVE branches (hull / shield / speed) get snake_case aliases alongside
/// the legacy PascalCase names. Iter 243's 2 NEW LIVE branches (invuln_flag
/// + prevent_death) round-trip into FakeUnit.Invulnerable / .DeathPrevented
/// bool fields. The 8 Phase-1 mirror fields (max_hull / max_shield /
/// max_speed / attack_power / is_hero / respawn_enabled / respawn_ms /
/// owner_slot) also get snake_case branches that store into the relevant
/// FakeUnit field without engine-state cascade (matches the bridge's
/// g_pendingUnitFieldWrites Phase-1 mirror semantics).
///
/// Pattern parallels iter-238 SetCameraPos sim pin file: simulator handler
/// pre-existed (iter 136 PascalCase form); iter 244 extends it with the
/// canonical wire-format branches needed by iter-243's LIVE flips. Catches
/// the pre-existing wire-format mismatch flagged in the
/// reference_simulator_wire_gotchas memory entry.
/// </summary>
public sealed class Iter244SetUnitFieldExtraFieldsSimulatorTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter, FakeUnit unit) NewSession()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        // NewTacticalSkirmish seeds Players + KnownTypeNames but no Units.
        // SetUnitField needs an obj_addr (FakeUnit.Id) to target.
        var unit = new FakeUnit
        {
            TypeName = "Rebel_Trooper_Squad",
            OwnerSlot = 0,
            CurrentHull = 100f,
            MaxHull = 100f,
            CurrentShield = 0f,
            MaxShield = 0f,
            Speed = 100f,
            MaxSpeed = 100f,
        };
        state.Units.Add(unit);
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, adapter, unit);
    }

    [Fact]
    public void CatalogStatus_SetUnitFieldIsLive()
    {
        // Pin: catalog status stays Live across the iter 136 → iter 243
        // multi-stage promotion of LIVE sub-field branches.
        CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"].Status
            .Should().Be(CapabilityStatus.Live,
                "iter 136 promoted to Live (3/13 branches), iter 243 extended to 5/13, iter 258 extended to 7/13");
    }

    [Fact]
    public void CatalogRationale_DocumentsIter243LiveBranchesAndCaveats()
    {
        // Pin: catalog rationale must cite iter 136 + iter 243 provenance,
        // the 7/13 LIVE ratio (post-iter-258), AND the engine-state-aware alternatives that
        // operators should prefer for full gameplay correctness. This pin
        // closes the operator-trust gap from feedback_flag_flipping_vs_engine_state.
        var note = CapabilityStatusCatalog.Entries["SWFOC_SetUnitField"].Note;
        note.Should().Contain("7/13", "iter 258 ratio bump");
        note.Should().Contain("iter 136", "original LIVE flip provenance");
        note.Should().Contain("iter 243", "extension provenance");
        note.Should().Contain("invuln_flag", "iter 243 LIVE branch field");
        note.Should().Contain("prevent_death", "iter 243 LIVE branch field");
        note.Should().Contain("MakeInvulnerableLua",
            "iter-110 LIVE alternative cross-ref for full hardpoint propagation");
        note.Should().Contain("SetCannotBeKilledLua",
            "iter-153 LIVE alternative cross-ref for Set_Cannot_Be_Killed Lua API");
        note.Should().Contain("iter-108",
            "owner_slot defer-pointer to SWFOC_ChangeUnitOwnerLua engine-aware path");
    }

    [Fact]
    public async Task SimulatorRoundTrip_InvulnFlag_TogglesOnAndOff()
    {
        // Operator workflow: stage invuln_flag write via UnitStatEditor →
        // bridge wire format `SWFOC_SetUnitField(addr, 'invuln_flag', 1)` →
        // simulator stores into FakeUnit.Invulnerable (matches the engine's
        // direct byte write at GameObj+0x3A7, which is what the iter 243
        // LIVE branch actually does on-target).
        var (sim, adapter, unit) = NewSession();
        try
        {
            unit.Invulnerable.Should().BeFalse("default FakeUnit state");

            var setOn = await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'invuln_flag', 1)", unit.Id),
                CancellationToken.None);
            setOn.Succeeded.Should().BeTrue();
            unit.Invulnerable.Should().BeTrue("iter 243 LIVE branch flips the bool on");

            var setOff = await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'invuln_flag', 0)", unit.Id),
                CancellationToken.None);
            setOff.Succeeded.Should().BeTrue();
            unit.Invulnerable.Should().BeFalse("zero value clears the flag");
        }
        finally
        {
            sim.Dispose();
        }
    }

    [Fact]
    public async Task SimulatorRoundTrip_PreventDeath_TogglesOnAndOff()
    {
        // Operator workflow: stage prevent_death write → bridge wire format
        // `SWFOC_SetUnitField(addr, 'prevent_death', 1)` → simulator stores
        // into FakeUnit.DeathPrevented. The bridge's iter 243 LIVE branch
        // does a bit-write (bit 0x80 of GameObj+0x3A1); the simulator
        // models the operator-observable outcome (bool flipped) without
        // simulating bit-level memory layout (the simulator already abstracts
        // FakeUnit fields, not raw bytes).
        var (sim, adapter, unit) = NewSession();
        try
        {
            unit.DeathPrevented.Should().BeFalse("default FakeUnit state");

            var setOn = await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'prevent_death', 1)", unit.Id),
                CancellationToken.None);
            setOn.Succeeded.Should().BeTrue();
            unit.DeathPrevented.Should().BeTrue("iter 243 LIVE branch flips the bool on");

            var setOff = await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'prevent_death', 0)", unit.Id),
                CancellationToken.None);
            setOff.Succeeded.Should().BeTrue();
            unit.DeathPrevented.Should().BeFalse("zero value clears the flag");
        }
        finally
        {
            sim.Dispose();
        }
    }

    [Fact]
    public async Task SimulatorRoundTrip_HullShieldSpeed_CanonicalSnakeCaseBranches()
    {
        // Regression guard for iter 244 simulator extension: canonical
        // snake_case branches MUST round-trip alongside the legacy PascalCase
        // names. This locks in the wire-format-canonical alignment so future
        // tests using the bridge's actual emission shape (snake_case via
        // BridgeUnitStatEditDispatcher) hit the simulator handler instead of
        // dropping into "ERR: unknown field".
        var (sim, adapter, unit) = NewSession();
        try
        {
            unit.CurrentHull = 100f;
            unit.CurrentShield = 0f;
            unit.Speed = 100f;

            await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'hull', 75)", unit.Id),
                CancellationToken.None);
            unit.CurrentHull.Should().Be(75f, "iter 136 hull branch (canonical snake_case)");

            await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'shield', 50)", unit.Id),
                CancellationToken.None);
            unit.CurrentShield.Should().Be(50f, "iter 136 shield branch (canonical snake_case)");

            await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'speed', 250)", unit.Id),
                CancellationToken.None);
            unit.Speed.Should().Be(250f, "iter 136 speed branch (canonical snake_case)");
        }
        finally
        {
            sim.Dispose();
        }
    }

    [Fact]
    public async Task SimulatorRoundTrip_OwnerSlotPhase1Mirror_StoresButDoesNotCascade()
    {
        // Pin Phase-1 mirror semantics: owner_slot direct write stores the
        // staged value into FakeUnit.OwnerSlot but does NOT cascade through
        // FakePlayer's selection-list / AI brain / UI roster. This matches
        // the bridge's g_pendingUnitFieldWrites Phase-1 mirror behavior — the
        // iter-242 design explicitly defers the LIVE flip for owner_slot
        // because the engine-state machinery (iter-108 SWFOC_ChangeUnitOwnerLua
        // calling Change_Owner @ 0x574D0E) is the only operator-correct path.
        var (sim, adapter, unit) = NewSession();
        try
        {
            var originalOwnerSlot = unit.OwnerSlot;
            var newSlot = (originalOwnerSlot + 1) % 8;

            var rt = await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'owner_slot', {1})", unit.Id, newSlot),
                CancellationToken.None);

            rt.Succeeded.Should().BeTrue("Phase-1 mirror still acks the write");
            unit.OwnerSlot.Should().Be(newSlot,
                "Phase-1 mirror stores the staged value for display/test round-trip");
        }
        finally
        {
            sim.Dispose();
        }
    }

    [Fact]
    public async Task SimulatorRoundTrip_LegacyPascalCase_StillWorks()
    {
        // Backwards-compat regression guard: iter-136 introduced the
        // simulator's HandleSetUnitField with PascalCase field names
        // (MaxHull, CurrentHull, etc.) before the bridge wire format was
        // settled on snake_case. Pre-iter-244 tests like
        // PhaseCSimulatorTests.cs:303 use these legacy names. Iter 244
        // adds canonical snake_case branches alongside without removing
        // the PascalCase compat.
        var (sim, adapter, unit) = NewSession();
        try
        {

            var rt = await adapter.SendRawAsync(
                string.Format(CultureInfo.InvariantCulture,
                    "return SWFOC_SetUnitField({0}, 'MaxHull', 9999)", unit.Id),
                CancellationToken.None);
            rt.Succeeded.Should().BeTrue();
            unit.MaxHull.Should().Be(9999f, "iter 136 PascalCase legacy branch still routes correctly");
        }
        finally
        {
            sim.Dispose();
        }
    }
}
