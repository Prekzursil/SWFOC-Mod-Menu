using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-28 (iter 108, master ralph loop) — pins the iter 108 LIVE wire
/// for <c>SWFOC_ChangeUnitOwner</c>. The bridge composes
/// <c>(&lt;unit_expr&gt;):Change_Owner(&lt;player_expr&gt;)</c> and dispatches via
/// DoString into the engine's Lua-registered <c>Change_Owner</c> method
/// on GameObjectWrapper. Engine internally calls sub_140574D0E (RVA
/// 0x574D0E, "Phase 2 RE"-pinned per docs/rvas.md). The handler updates
/// ownership, fires UI events, plays audio, processes corruption, and
/// updates AI budgets — the full "swap sides" engine behaviour that the
/// editor's earlier Phase-1-mirror version couldn't replicate.
///
/// Same iter-99/100/107 pattern: engine API call routed through existing
/// primitives (DoString), no MinHook detour, no struct-offset write,
/// no new RVA pin.
///
/// RED-GREEN pair:
///   RED   — <c>SWFOC_ChangeUnitOwner</c> didn't exist before iter 108.
///   GREEN — iter 108 ships the LIVE wire that splices both expressions
///           into Change_Owner and dispatches it.
/// </summary>
public sealed class Iter108ChangeUnitOwnerTests
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
    public async Task ChangeUnitOwner_WithFindFirstObject_DispatchesLiveOk()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var result = await adapter.SendRawAsync(
            "return SWFOC_ChangeUnitOwner('Find_First_Object(\"Empire_AT_AT\")', 'Find_Player(\"REBEL\")')",
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().StartWith("OK:");
        result.Response.Should().Contain("LIVE");
        sim.GameState.LastChangeUnitOwnerUnit
            .Should().Be("Find_First_Object(\"Empire_AT_AT\")");
        sim.GameState.LastChangeUnitOwnerPlayer
            .Should().Be("Find_Player(\"REBEL\")");
    }

    [Fact]
    public async Task ChangeUnitOwner_PreservesLastCall_AcrossMultipleDispatch()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_ChangeUnitOwner('Find_First_Object(\"Empire_AT_ST\")', 'Find_Player(\"UNDERWORLD\")')",
            CancellationToken.None);
        sim.GameState.LastChangeUnitOwnerPlayer.Should().Contain("UNDERWORLD");

        await adapter.SendRawAsync(
            "return SWFOC_ChangeUnitOwner('Find_First_Object(\"Rebel_Trooper_Squad\")', 'Find_Player(\"EMPIRE\")')",
            CancellationToken.None);
        sim.GameState.LastChangeUnitOwnerPlayer.Should().Contain("EMPIRE");
        sim.GameState.LastChangeUnitOwnerPlayer.Should().NotContain("UNDERWORLD",
            "second call must override the first");
        sim.GameState.LastChangeUnitOwnerUnit.Should().Contain("Rebel_Trooper_Squad");
    }

    [Fact]
    public void Catalog_MarksChangeUnitOwner_AsLive()
    {
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_ChangeUnitOwner");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 108 wired Change_Owner via DoString — LIVE");
        entry.Note.Should().Contain("Change_Owner");
    }
}
