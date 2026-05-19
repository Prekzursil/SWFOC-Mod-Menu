using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.V2Vm;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 60) — pins per-action capability metadata across the
/// last 5 V2 tabs (UnitControl, PlayerState, Economy, CrossFaction,
/// UnitStatEditor) so every bridge-using V2 tab now exposes
/// <see cref="CapabilityAwareAction"/> properties keyed to the catalog.
/// </summary>
public sealed class Iter60CapabilityCoverageTests
{
    [Fact]
    public void UnitControl_MixedActions_ExposeCorrectBadges()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var settings = new V2Settings();
        var godMode = new GodModeService(adapter, NullLogger<GodModeService>.Instance);
        var oneHitKill = new OneHitKillService(adapter, NullLogger<OneHitKillService>.Instance);
        var unitInspector = new UnitInspectorService(adapter, NullLogger<UnitInspectorService>.Instance);
        var hardpoints = new HardpointService(adapter, NullLogger<HardpointService>.Instance);
        var enhancedSpawn = new EnhancedSpawnService(adapter, NullLogger<EnhancedSpawnService>.Instance);
        var unitMutator = new V2UnitMutationDispatcher(adapter);
        var factionRegistry = new V2FactionRegistry();

        var vm = new UnitControlTabViewModel(
            adapter, settings, godMode, oneHitKill, unitInspector, hardpoints,
            enhancedSpawn, unitMutator, factionRegistry);

        vm.ToggleGodMode.Badge.Should().Be("LIVE");
        vm.ToggleOneHitKill.Badge.Should().Be("LIVE");
        vm.SetUnitHull.Badge.Should().Be("LIVE");
        vm.SetUnitInvuln.Badge.Should().Be("LIVE");
        vm.SetPreventDeath.Badge.Should().Be("LIVE");
        vm.InspectUnit.Badge.Should().Be("LIVE ONLY",
            "SWFOC_InspectUnit needs a running game session");
        vm.GetHardpoints.Badge.Should().Be("LIVE ONLY");
        vm.SpawnUnit.Badge.Should().Be("PHASE 2 PENDING",
            "SWFOC_SpawnUnit is the same Phase-1-mirror as the Spawning tab");
        vm.UseSelected.Badge.Should().Be("LIVE");
        vm.RefreshSelection.Badge.Should().Be("LIVE");

        vm.HasNonLiveAction.Should().BeTrue();
        vm.CapabilityNoteLine.Should().Contain("Inspect unit");
        vm.CapabilityNoteLine.Should().Contain("Spawn unit");
    }

    [Fact]
    public void PlayerState_AllEconomyActionsAreLive()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var economy = new EconomyService(adapter, NullLogger<EconomyService>.Instance);
        var heroRespawn = new HeroRespawnService(adapter, NullLogger<HeroRespawnService>.Instance);
        var factionSwitch = new FactionSwitchService(adapter, NullLogger<FactionSwitchService>.Instance);
        var unitMutator = new V2UnitMutationDispatcher(adapter);
        var factionRegistry = new V2FactionRegistry();
        var settings = new V2Settings();

        var vm = new PlayerStateTabViewModel(
            adapter, settings, economy, heroRespawn, factionSwitch, unitMutator, factionRegistry);

        vm.GetCredits.Badge.Should().Be("LIVE");
        vm.SetCredits.Badge.Should().Be("LIVE");
        vm.UncapCredits.Badge.Should().Be("LIVE");
        vm.DrainEnemyCredits.Badge.Should().Be("LIVE");
        vm.SwitchToSlot.Badge.Should().Be("LIVE",
            "SetHumanPlayer_v3 is engine-verified");
        vm.NullAiBrain.Badge.Should().Be("LIVE");
        vm.AttachAiBrain.Badge.Should().Be("LIVE");

        vm.SetRespawn.Badge.Should().Be("LIVE",
            "SetRespawn uses the global SWFOC_SetHeroRespawn helper, which is engine-verified");
        vm.HasPhase2PendingAction.Should().BeFalse();
        vm.Phase2PendingWarning.Should().BeEmpty();
        vm.Phase2PendingWarning.Should().NotContain("Set credits");
    }

    [Fact]
    public void Economy_MixedActions_ExposeCorrectBadges()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var vm = new EconomyTabViewModel(adapter);

        vm.SetCredits.Badge.Should().Be("LIVE");
        vm.SetTech.Badge.Should().Be("LIVE");
        vm.DrainEnemy.Badge.Should().Be("LIVE");
        vm.UncapCredits.Badge.Should().Be("LIVE");
        vm.SetIncomeMult.Badge.Should().Be("PHASE 2 PENDING");
        vm.SetBuildSpeed.Badge.Should().Be("PHASE 2 PENDING");
        vm.SetBuildCost.Badge.Should().Be("PHASE 2 PENDING");
        vm.ToggleFreeze.Badge.Should().Be("PHASE 2 PENDING",
            "FreezeCredits is Phase-1-mirror only");
        vm.ToggleInstantBuild.Badge.Should().Be("PHASE 2 PENDING");
        vm.ToggleFreeBuild.Badge.Should().Be("PHASE 2 PENDING");

        vm.HasPhase2PendingAction.Should().BeTrue();
        var warning = vm.Phase2PendingWarning;
        warning.Should().Contain("Set income multiplier");
        warning.Should().NotContain("Set credits");
    }

    [Fact]
    public void CrossFaction_BothActionsAreLive()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var vm = new CrossFactionRecruitmentTabViewModel(adapter);

        vm.Recruit.Badge.Should().Be("LIVE",
            "Recruit routes via SWFOC_DoString — engine-native escape hatch");
        vm.AutoFillFromSelected.Badge.Should().Be("LIVE");
        vm.AllActions.Should().HaveCount(2);
    }

    [Fact]
    public void UnitStatEditor_ApplyAllIsLive()
    {
        // 2026-04-29 (iter 136) — SWFOC_SetUnitField bridge gained
        // per-field LIVE branches for hull/shield/speed (mirroring
        // HeroStatEdit's iter 100/129 pattern). 3/13 fields LIVE; 10
        // remain Phase-1 pending RTTI-driven offset table. The single
        // tab badge follows the catalog flip → LIVE.
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        var vm = new UnitStatEditorTabViewModel(adapter);

        vm.ApplyAll.Badge.Should().Be("LIVE",
            "iter 136 mirrored HeroStatEdit's per-field LIVE branches into Lua_SetUnitField");
        vm.HasPhase2PendingAction.Should().BeFalse(
            "ApplyAll is the only action on UnitStatEditor; iter 136 flipped it LIVE");
    }
}
