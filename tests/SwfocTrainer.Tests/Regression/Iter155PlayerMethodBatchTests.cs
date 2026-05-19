using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 155) — pins player-method LIVE batch.
/// Three player-method wires (Give_Money / Set_Tech_Level / Unlock_Tech)
/// shipped via the iter-154 generic 2-arg dispatcher. Helper composes
/// (obj):method(arg) regardless of arg type — splice is verbatim, so it
/// works for player handles + numeric/string args identically.
///
/// LIVE flips #40-42. Master loop now at 42 LIVE wires.
/// </summary>
public sealed class Iter155PlayerMethodBatchTests
{
    [Fact]
    public void GiveMoney_StatusIsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_PlayerGiveMoneyLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void SetTechLevel_StatusIsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_PlayerSetTechLevelLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void UnlockTech_StatusIsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_PlayerUnlockTechLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public async System.Threading.Tasks.Task GiveMoney_DispatchesOk()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var resp = await adapter.SendRawAsync(
            "return SWFOC_PlayerGiveMoneyLua('Find_Player(\"REBEL\")', '50000')",
            System.Threading.CancellationToken.None);
        resp.Succeeded.Should().BeTrue();
        resp.Response.Should().Contain("OK");
    }

    [Fact]
    public async System.Threading.Tasks.Task SetTechLevel_DispatchesOk()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var resp = await adapter.SendRawAsync(
            "return SWFOC_PlayerSetTechLevelLua('Find_Player(\"EMPIRE\")', '5')",
            System.Threading.CancellationToken.None);
        resp.Succeeded.Should().BeTrue();
        resp.Response.Should().Contain("OK");
    }

    [Fact]
    public async System.Threading.Tasks.Task UnlockTech_DispatchesOk()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var resp = await adapter.SendRawAsync(
            "return SWFOC_PlayerUnlockTechLua('Find_Player(\"EMPIRE\")', 'Find_Object_Type(\"DEATH_STAR\")')",
            System.Threading.CancellationToken.None);
        resp.Succeeded.Should().BeTrue();
        resp.Response.Should().Contain("OK");
    }

    [Fact]
    public void PlayerBatch_AllThree_LiveInvariant()
    {
        CapabilityStatusCatalog.Entries["SWFOC_PlayerGiveMoneyLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_PlayerSetTechLevelLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_PlayerUnlockTechLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }
}
