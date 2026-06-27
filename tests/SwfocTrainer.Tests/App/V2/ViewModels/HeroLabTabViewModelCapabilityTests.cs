using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 57) — pins per-button capability metadata on Hero
/// Lab. KillUnit + ReviveUnit + global SetHeroRespawn are LIVE; ListHeroes
/// and SetPermadeath are PHASE 2 PENDING.
///
/// 2026-04-29 (iter 135) — HeroStatEdit catalog drift fix: bridge always
/// routed hull/shield/speed through engine helpers (LIVE) but catalog
/// said Phase 1 mirror. EditStat badge now LIVE (3/4 sub-fields LIVE;
/// respawn_ms still Phase-1).
/// </summary>
public sealed class HeroLabTabViewModelCapabilityTests
{
    private static HeroLabTabViewModel NewVm(out SwfocSimulator sim)
    {
        sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return new HeroLabTabViewModel(adapter);
    }

    [Fact]
    public void KillHero_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.KillHero.Badge.Should().Be("LIVE");
    }

    [Fact]
    public void ReviveHero_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ReviveHero.Badge.Should().Be("LIVE");
    }

    [Fact]
    public void ReviveAllHeroes_SamePrimitiveAsReviveHero_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ReviveAllHeroes.Badge.Should().Be("LIVE",
            "ReviveAll fires SWFOC_ReviveUnit per addr — same LIVE primitive as ReviveHero");
    }

    [Fact]
    public void RefreshHeroes_BadgeIsPhase2Pending()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.RefreshHeroes.Badge.Should().Be("PHASE 2 PENDING",
            "SWFOC_ListHeroes is Phase-1-mirror only — needs hero detection table IDA pin");
    }

    [Fact]
    public void SetRespawn_BadgeIsLiveGlobalHelper()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SetRespawn.Badge.Should().Be("LIVE",
            "Hero Lab respawn uses the live global SWFOC_SetHeroRespawn helper, not the pending per-hero timer hook");
    }

    [Fact]
    public void TogglePermadeath_BadgeIsPhase2Pending()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.TogglePermadeath.Badge.Should().Be("PHASE 2 PENDING");
    }

    [Fact]
    public void EditStat_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.EditStat.Badge.Should().Be("LIVE",
            "iter 135 catalog drift fix: bridge always routed hull/shield/speed through engine helpers (LIVE iter 100/129); only respawn_ms is still Phase-1 mirror");
    }

    [Fact]
    public void HasPhase2PendingAction_TrueForHeroLabTab()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.HasPhase2PendingAction.Should().BeTrue();
    }

    [Fact]
    public void Phase2PendingWarning_NamesNonLiveActionsOnly()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        var warning = vm.Phase2PendingWarning;
        warning.Should().Contain("Refresh heroes");
        warning.Should().NotContain("Set global respawn timer");
        warning.Should().Contain("Toggle permadeath");
        warning.Should().NotContain("Kill hero");
        warning.Should().NotContain("Revive hero");
        warning.Should().NotContain("Revive all heroes");
        warning.Should().NotContain("Edit hero stat",
            "iter 135 — HeroStatEdit flipped LIVE since 3/4 sub-fields are LIVE");
    }

    [Fact]
    public void AllActions_EnumeratesEveryActionInDeclaredOrder()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.AllActions.Should().HaveCount(7);
        vm.AllActions[0].Should().BeSameAs(vm.RefreshHeroes);
        vm.AllActions[1].Should().BeSameAs(vm.SetRespawn);
        vm.AllActions[2].Should().BeSameAs(vm.TogglePermadeath);
        vm.AllActions[3].Should().BeSameAs(vm.KillHero);
        vm.AllActions[4].Should().BeSameAs(vm.ReviveHero);
        vm.AllActions[5].Should().BeSameAs(vm.EditStat);
        vm.AllActions[6].Should().BeSameAs(vm.ReviveAllHeroes);
    }
}
