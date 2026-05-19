using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-28 (iter 112, master ralph loop) — three more per-unit Lua
/// no-arg method LIVE wires sharing <c>Lua_DispatchUnitNoArgMethod</c>:
/// <list type="bullet">
///   <item><c>SWFOC_DespawnUnitLua</c> → <c>Despawn()</c></item>
///   <item><c>SWFOC_StopUnitLua</c> → <c>Stop()</c></item>
///   <item><c>SWFOC_RetreatUnitLua</c> → <c>Retreat()</c></item>
/// </list>
/// Same DoString-into-engine-Lua-API pattern, even smaller marginal
/// cost: the helper is shared so each new wire is ~3 lines of bridge
/// code + 1 catalog line + 1 simulator handler line + 1 test.
/// </summary>
public sealed class Iter112UnitNoArgMethodBatchTests
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
    public async Task DespawnUnitLua_DispatchesLiveOk()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        var result = await adapter.SendRawAsync(
            "return SWFOC_DespawnUnitLua('Find_First_Object(\"Empire_AT_AT\")')",
            CancellationToken.None);
        result.Succeeded.Should().BeTrue();
        result.Response.Should().Contain("Despawn dispatched");
        sim.GameState.LastUnitNoArgMethodCalls["Despawn"]
            .Should().Be("Find_First_Object(\"Empire_AT_AT\")");
    }

    [Fact]
    public async Task StopUnitLua_DispatchesLiveOk()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        var result = await adapter.SendRawAsync(
            "return SWFOC_StopUnitLua('Find_First_Object(\"Rebel_Trooper_Squad\")')",
            CancellationToken.None);
        result.Succeeded.Should().BeTrue();
        result.Response.Should().Contain("Stop dispatched");
        sim.GameState.LastUnitNoArgMethodCalls["Stop"]
            .Should().Be("Find_First_Object(\"Rebel_Trooper_Squad\")");
    }

    [Fact]
    public async Task RetreatUnitLua_DispatchesLiveOk()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;
        var result = await adapter.SendRawAsync(
            "return SWFOC_RetreatUnitLua('Find_First_Object(\"Empire_AT_ST\")')",
            CancellationToken.None);
        result.Succeeded.Should().BeTrue();
        result.Response.Should().Contain("Retreat dispatched");
        sim.GameState.LastUnitNoArgMethodCalls["Retreat"]
            .Should().Be("Find_First_Object(\"Empire_AT_ST\")");
    }

    [Fact]
    public void Catalog_MarksAllThreeBatchEntries_AsLive()
    {
        CapabilityStatusCatalog.Lookup("SWFOC_DespawnUnitLua").Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Lookup("SWFOC_StopUnitLua").Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Lookup("SWFOC_RetreatUnitLua").Status
            .Should().Be(CapabilityStatus.Live);
    }
}
