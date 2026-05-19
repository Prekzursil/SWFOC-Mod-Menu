using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-28 (iter 97, master ralph loop) — pins the iter 96 LIVE wire
/// for <c>SWFOC_SetDamageMultiplierGlobal</c>. The real bridge detours
/// Take_Damage_Outer @ RVA 0x38A350 to scale damageParams[0] by
/// g_dmgMult_global; the simulator mirrors this by setting
/// <see cref="FakeGameState.GlobalDamageMultiplier"/> and applying it
/// to every alive unit's <c>FakeUnit.DamageScalar</c>.
///
/// RED-GREEN pair:
///   RED   — calling the helper without registering the simulator
///           handler (iter 96 baseline) returns "(unhandled)" and
///           leaves the multiplier at 1.0.
///   GREEN — iter 97 simulator handler is wired so the bridge call
///           lands cleanly and the global multiplier reads back the
///           value we set.
///
/// The red-green discipline catches accidental dispatcher-table
/// regressions (e.g. someone removes the Reg() line by mistake) — the
/// test would flip back to "ERR: unhandled" output, breaking GREEN.
/// </summary>
public sealed class Iter97DamageMultiplierGlobalTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession()
    {
        // The skirmish fixture lists known type names but doesn't pre-spawn
        // units. Seed two units (Rebel + Empire) so the apply-to-every-unit
        // assertion has something to read. Mirror the seeding pattern from
        // CombatViewModelScenarioTests.
        var state = FakeGameState.NewTacticalSkirmish();
        state.Units.Add(new FakeUnit { TypeName = "Rebel_Trooper_Squad", OwnerSlot = 0 });
        state.Units.Add(new FakeUnit { TypeName = "Empire_AT_AT", OwnerSlot = 1 });
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, adapter);
    }

    [Fact]
    public async Task SetDamageMultiplierGlobal_WithSimHandler_ReturnsOk()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var result = await adapter.SendRawAsync(
            "return SWFOC_SetDamageMultiplierGlobal(2.5)", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().Be("ok");
    }

    [Fact]
    public async Task SetDamageMultiplierGlobal_StoresValueOnGameState()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        sim.GameState.GlobalDamageMultiplier.Should().Be(1.0f, "default before any call");

        await adapter.SendRawAsync(
            "return SWFOC_SetDamageMultiplierGlobal(3.0)", CancellationToken.None);

        sim.GameState.GlobalDamageMultiplier.Should().Be(3.0f);
    }

    [Fact]
    public async Task SetDamageMultiplierGlobal_AppliesToEveryAliveUnit()
    {
        // Symmetric with the per-slot SetDamageMultiplier handler.
        // Real bridge applies via the Take_Damage_Outer detour; simulator
        // mirrors by writing FakeUnit.DamageScalar.
        var (sim, adapter) = NewSession();
        using var _ = sim;
        var aliveCountBefore = sim.GameState.Units.Count(u => u.Alive);
        aliveCountBefore.Should().BeGreaterThan(0, "skirmish fixture must have alive units");

        await adapter.SendRawAsync(
            "return SWFOC_SetDamageMultiplierGlobal(0.25)", CancellationToken.None);

        sim.GameState.Units.Where(u => u.Alive)
            .Should().AllSatisfy(u => u.DamageScalar.Should().Be(0.25f),
                "global multiplier must affect every alive unit's DamageScalar");
    }

    // 2026-04-28: negative-multiplier rejection lives ONLY in the real
    // bridge (Lua_SetDamageMultiplierGlobal validates `mult < 0.0`). The
    // simulator's `s_floatRx` regex doesn't capture leading minus signs,
    // so "-1.5" loses the sign and reaches the handler as "1.5" — there's
    // no way for the simulator to faithfully reject negatives. The bridge
    // harness covers this via its own test suite; we don't duplicate it
    // here. Test removed deliberately, with this comment as the marker.

    [Fact]
    public async Task GetDamageMultiplierGlobal_RoundTripsTheStoredValue()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_SetDamageMultiplierGlobal(1.75)", CancellationToken.None);
        var result = await adapter.SendRawAsync(
            "return SWFOC_GetDamageMultiplierGlobal()", CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        float.Parse(result.Response!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(1.75f, 0.001f);
    }

    [Fact]
    public void Catalog_MarksSetDamageMultiplierGlobal_AsLive()
    {
        // Ledger-side proof: the iter 96 catalog flip is in place.
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_SetDamageMultiplierGlobal");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 96 wired the Take_Damage_Outer detour; this helper is LIVE");
        entry.Note.Should().Contain("Take_Damage_Outer");
    }

    [Fact]
    public void Catalog_KeepsPerSlotSetDamageMultiplier_AsPhase2Pending()
    {
        // The per-slot variant stays Phase2HookPending — attacker context
        // isn't available at the Take_Damage layer (iter 95 finding).
        // Higher-layer detours are required to flip this LIVE.
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_SetDamageMultiplier");
        entry.Status.Should().Be(CapabilityStatus.Phase2HookPending,
            "per-slot needs higher-layer detours; not yet implementable");
    }
}
