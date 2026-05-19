using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-28 (iter 109, master ralph loop) — pins the LIVE wire for
/// <c>SWFOC_SpawnUnitLua</c>. Bridge composes
/// <c>Spawn_Unit(&lt;player&gt;, &lt;type&gt;, &lt;position&gt;)</c> and dispatches via
/// DoString into the engine's Lua-registered <c>Spawn_Unit</c> API
/// (per <c>docs/lua-api.md</c>: "creates a unit of the given type owned
/// by the given player at the given world position"). Closes the
/// long-standing Spawning Phase-2-mirror by routing through the engine
/// API instead of the editor's offline simulator-only path.
///
/// Same iter 99/100/107/108 pattern: engine Lua API + caller-supplied
/// expressions + DoString. No MinHook detour, no struct-offset write.
/// </summary>
public sealed class Iter109SpawnUnitLuaTests
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
    public async Task SpawnUnitLua_WithFindPlayerTypeAndPosition_DispatchesLiveOk()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var result = await adapter.SendRawAsync(
            "return SWFOC_SpawnUnitLua("
            + "'Find_Player(\"REBEL\")', "
            + "'Find_Object_Type(\"Rebel_Trooper_Squad\")', "
            + "'Create_Position(0, 0, 0)')",
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().StartWith("OK:");
        result.Response.Should().Contain("LIVE");
        sim.GameState.LastSpawnUnitLuaPlayer
            .Should().Be("Find_Player(\"REBEL\")");
        sim.GameState.LastSpawnUnitLuaType
            .Should().Be("Find_Object_Type(\"Rebel_Trooper_Squad\")");
        sim.GameState.LastSpawnUnitLuaPosition
            .Should().Be("Create_Position(0, 0, 0)");
    }

    [Fact]
    public async Task SpawnUnitLua_DifferentPlayerTypePosition_OverridesPreviousCall()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_SpawnUnitLua('Find_Player(\"EMPIRE\")', 'Find_Object_Type(\"Empire_AT_AT\")', 'Create_Position(100, 0, 0)')",
            CancellationToken.None);
        sim.GameState.LastSpawnUnitLuaPlayer.Should().Contain("EMPIRE");

        await adapter.SendRawAsync(
            "return SWFOC_SpawnUnitLua('Find_Player(\"UNDERWORLD\")', 'Find_Object_Type(\"Underworld_Mercenary_Squad\")', 'Create_Position(200, 50, 0)')",
            CancellationToken.None);
        sim.GameState.LastSpawnUnitLuaPlayer.Should().Contain("UNDERWORLD");
        sim.GameState.LastSpawnUnitLuaPlayer.Should().NotContain("EMPIRE",
            "second call must override the first");
        sim.GameState.LastSpawnUnitLuaType.Should().Contain("Underworld_Mercenary_Squad");
        sim.GameState.LastSpawnUnitLuaPosition.Should().Contain("200");
    }

    [Fact]
    public void Catalog_MarksSpawnUnitLua_AsLive()
    {
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_SpawnUnitLua");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 109 wired Spawn_Unit via DoString — LIVE");
        entry.Note.Should().Contain("Spawn_Unit");
    }
}
