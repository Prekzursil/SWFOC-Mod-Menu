using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 152) — pins SWFOC_GalacticSpawnUnit LIVE wire.
/// Galactic-mode complement to iter 109 SWFOC_SpawnUnitLua (tactical).
/// 3-arg shape mirrors iter 109; the third arg is a PlanetWrapper
/// (FindPlanet) instead of a position userdata.
///
/// LIVE flip #33. Master loop now at 33 LIVE wires.
/// </summary>
public sealed class Iter152GalacticSpawnUnitTests
{
    [Fact]
    public void GalacticSpawnUnit_StatusIsLive()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_GalacticSpawnUnit"];
        entry.Status.Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void GalacticSpawnUnit_NoteCitesEngineApi()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_GalacticSpawnUnit"];
        entry.Note.Should().Contain("Galactic_Spawn_Unit");
        entry.Note.Should().Contain("DoString");
        entry.Note.Should().Contain("iter 109");
    }

    [Fact]
    public async System.Threading.Tasks.Task GalacticSpawnUnit_DispatchedExpressionsRoundTrip()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewGalacticCampaign());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var resp = await adapter.SendRawAsync(
            "return SWFOC_GalacticSpawnUnit('Find_Player(\"REBEL\")', 'Find_Object_Type(\"Rebel_Trooper_Squad\")', 'FindPlanet(\"Yavin\")')",
            System.Threading.CancellationToken.None);

        resp.Succeeded.Should().BeTrue();
        resp.Response.Should().Contain("OK").And.Contain("LIVE");
        sim.GameState.LastGalacticSpawnPlayer.Should().Contain("REBEL");
        sim.GameState.LastGalacticSpawnType.Should().Contain("Rebel_Trooper_Squad");
        sim.GameState.LastGalacticSpawnPlanet.Should().Contain("Yavin");
    }

    [Fact]
    public void GalacticSpawnUnit_ComposedBadgeReportsLive()
    {
        CapabilityStatusCatalog.ComposeBadge("SWFOC_GalacticSpawnUnit")
            .Should().Be("LIVE");
    }
}
