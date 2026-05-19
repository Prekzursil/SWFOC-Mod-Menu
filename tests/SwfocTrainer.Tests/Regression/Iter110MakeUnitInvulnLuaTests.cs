using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-28 (iter 110, master ralph loop) — pins the LIVE wire for
/// <c>SWFOC_MakeUnitInvulnLua</c>. Bridge composes
/// <c>(&lt;unit&gt;):Make_Invulnerable(&lt;bool&gt;)</c> and dispatches via
/// DoString into the engine's per-unit Lua method on GameObjectWrapper.
///
/// Engine wrapper at RVA 0x57D550 propagates via QueryInterface(22) →
/// HardpointCount/HardpointGet loop → BehaviorAttach(hp, "INVULNERABLE", 0)
/// per hardpoint (verified ledger fact
/// <c>fact_make_invulnerable_hardpoint_propagation</c>). So the per-unit
/// `Make_Invulnerable(true)` Lua call flips the entire unit including
/// its hardpoints — operator's expectation matches engine semantics.
///
/// 5th LIVE wire of the session via the engine-Lua-API + DoString
/// pattern (after iter 100/107/108/109).
/// </summary>
public sealed class Iter110MakeUnitInvulnLuaTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, adapter);
    }

    [Fact]
    public async Task MakeUnitInvulnLua_WithFindFirstObjectAndTrue_DispatchesLiveOk()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var result = await adapter.SendRawAsync(
            "return SWFOC_MakeUnitInvulnLua('Find_First_Object(\"Empire_AT_AT\")', 'true')",
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().StartWith("OK:");
        result.Response.Should().Contain("LIVE");
        sim.GameState.LastMakeUnitInvulnLuaUnit
            .Should().Be("Find_First_Object(\"Empire_AT_AT\")");
        sim.GameState.LastMakeUnitInvulnLuaBool.Should().Be("true");
    }

    [Fact]
    public async Task MakeUnitInvulnLua_WithFalse_RoundTripsCorrectBoolLiteral()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_MakeUnitInvulnLua('Find_First_Object(\"Rebel_Trooper_Squad\")', 'false')",
            CancellationToken.None);

        sim.GameState.LastMakeUnitInvulnLuaBool.Should().Be("false",
            "the bool expression must round-trip verbatim — Make_Invulnerable(false) "
            + "is the engine's idiomatic way to revert per-unit invulnerability");
    }

    [Fact]
    public void Catalog_MarksMakeUnitInvulnLua_AsLive()
    {
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_MakeUnitInvulnLua");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 110 wired Make_Invulnerable via DoString — LIVE");
        entry.Note.Should().Contain("Make_Invulnerable");
    }
}
