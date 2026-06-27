using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 153) — pins SWFOC_SetCannotBeKilledLua + SWFOC_EnableStealthLua.
/// Same iter-111 bool-arg dispatch pattern. Two LIVE flips in one iter
/// using the existing Lua_DispatchUnitBoolMethod helper. ~30 LoC bridge
/// + tests + catalog.
///
/// LIVE flips #34-35. Master loop now at 35 LIVE wires.
/// </summary>
public sealed class Iter153BoolUnitMethodBatchTests
{
    [Fact]
    public void SetCannotBeKilled_StatusIsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_SetCannotBeKilledLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void EnableStealth_StatusIsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_EnableStealthLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void SetCannotBeKilled_NoteCitesEngineMethodAndIter111Pattern()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetCannotBeKilledLua"];
        entry.Note.Should().Contain("Set_Cannot_Be_Killed");
        entry.Note.Should().Contain("DoString");
        entry.Note.Should().Contain("iter-111");
    }

    [Fact]
    public void EnableStealth_NoteCitesEngineMethod()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_EnableStealthLua"];
        entry.Note.Should().Contain("Enable_Stealth");
        entry.Note.Should().Contain("DoString");
    }

    [Fact]
    public async System.Threading.Tasks.Task SetCannotBeKilled_DispatchesOk()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var resp = await adapter.SendRawAsync(
            "return SWFOC_SetCannotBeKilledLua('Find_First_Object(\"Empire_AT_AT\")', 'true')",
            System.Threading.CancellationToken.None);

        resp.Succeeded.Should().BeTrue();
        resp.Response.Should().Contain("OK");
    }

    [Fact]
    public async System.Threading.Tasks.Task EnableStealth_DispatchesOk()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var resp = await adapter.SendRawAsync(
            "return SWFOC_EnableStealthLua('Find_First_Object(\"Rebel_T2A_Tank\")', 'true')",
            System.Threading.CancellationToken.None);

        resp.Succeeded.Should().BeTrue();
        resp.Response.Should().Contain("OK");
    }

    [Fact]
    public void BoolMethodPair_BothLive_InvariantPin()
    {
        // Iter 153 — both bool-arg unit methods must remain LIVE as a unit.
        // Future regression in either flips this assertion.
        CapabilityStatusCatalog.Entries["SWFOC_SetCannotBeKilledLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_EnableStealthLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }
}
