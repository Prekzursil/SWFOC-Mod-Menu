using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 58) — pins per-button capability metadata on the
/// Spawning tab. The primary "Spawn" button now routes through
/// SWFOC_SpawnUnitLua, so the operator-facing path is live instead of the
/// old SWFOC_SpawnUnit Phase-1 mirror.
/// </summary>
public sealed class SpawningTabViewModelCapabilityTests
{
    private static SpawningTabViewModel NewVm(out SwfocSimulator sim)
    {
        sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return new SpawningTabViewModel(adapter);
    }

    [Fact]
    public void Spawn_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.Spawn.Badge.Should().Be("LIVE",
            "the primary Spawn button is now backed by SWFOC_SpawnUnitLua");
    }

    [Fact]
    public void RefreshFromLiveGame_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.RefreshFromLiveGame.Badge.Should().Be("LIVE",
            "SWFOC_BatchTypeExists is a read-only Find_Object_Type probe — engine-verified");
    }

    [Fact]
    public void HasPhase2PendingAction_FalseForSpawningTab()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.HasPhase2PendingAction.Should().BeFalse();
    }

    [Fact]
    public void Phase2PendingWarning_IsEmptyWhenAllActionsAreLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.Phase2PendingWarning.Should().BeEmpty();
    }

    [Fact]
    public void AllActions_EnumeratesSpawnThenRefreshThenSpawnLua()
    {
        // 2026-04-29 (iter 119): added SWFOC_SpawnUnitLua.
        // 2026-05-05 (iter 195): +3 spawn variants (Reinforce, SpawnFromPool, CreateGenericObject).
        // 2026-05-05 (iter 203): +4 discovery helpers (FindObjectType, FindPlanet,
        // FindFirstObject, FindNearest). Spawning tab now spans 10 LIVE actions.
        // 2026-05-05 (iter 206): +1 discovery extension (FindAllObjectsOfType).
        var vm = NewVm(out var sim); using var _ = sim;
        vm.AllActions.Should().HaveCount(11);
        vm.AllActions[0].Should().BeSameAs(vm.Spawn);
        vm.AllActions[1].Should().BeSameAs(vm.RefreshFromLiveGame);
        vm.AllActions[2].Should().BeSameAs(vm.SpawnUnitLua);
        vm.AllActions[3].Should().BeSameAs(vm.ReinforceUnitLua);
        vm.AllActions[4].Should().BeSameAs(vm.SpawnFromReinforcementPoolLua);
        vm.AllActions[5].Should().BeSameAs(vm.CreateGenericObjectLua);
        vm.AllActions[6].Should().BeSameAs(vm.FindObjectTypeLua);
        vm.AllActions[7].Should().BeSameAs(vm.FindPlanetLua);
        vm.AllActions[8].Should().BeSameAs(vm.FindFirstObjectLua);
        vm.AllActions[9].Should().BeSameAs(vm.FindNearestLua);
        vm.AllActions[10].Should().BeSameAs(vm.FindAllObjectsOfTypeLua);
    }
}
