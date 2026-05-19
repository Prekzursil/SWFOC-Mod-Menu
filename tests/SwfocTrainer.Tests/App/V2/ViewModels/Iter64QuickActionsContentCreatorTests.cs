using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 64) — pins the 2 new content-creator workflow
/// composites added to the Quick Actions tab: Battle setup + Filming
/// setup. Both compose existing primitives so the test verifies badge
/// derivation + AllComposites ordering rather than re-verifying simulator
/// flag-flip behavior (already covered for the existing 4 composites by
/// QuickActionsViewModelScenarioTests).
/// </summary>
public sealed class Iter64QuickActionsContentCreatorTests
{
    private static QuickActionsTabViewModel NewVm(out SwfocSimulator sim)
    {
        sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return new QuickActionsTabViewModel(adapter);
    }

    [Fact]
    public void BattleSetup_AllPrimitivesLive_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        // GodMode + HealAllLocal + UncapCredits + DrainEnemyCredits
        // are all catalogued LIVE — operator can rely on this composite
        // having full engine effect.
        vm.BattleSetup.Badge.Should().Be("LIVE");
        vm.BattleSetup.IsAllLive.Should().BeTrue();
        vm.BattleSetup.HelperNames.Should().Equal(
            "SWFOC_GodMode", "SWFOC_HealAllLocal", "SWFOC_UncapCredits", "SWFOC_DrainEnemyCredits");
    }

    [Fact]
    public void FilmingSetup_Phase2PrimitivesRemoved_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.FilmingSetup.Badge.Should().Be("LIVE");
        vm.FilmingSetup.IsAllLive.Should().BeTrue();
        vm.FilmingSetup.IsMixed.Should().BeFalse();
    }

    [Fact]
    public void BattleSetupCommand_AndFilmingSetupCommand_AreExposed()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.BattleSetupCommand.Should().NotBeNull();
        vm.FilmingSetupCommand.Should().NotBeNull();
    }

    [Fact]
    public void AllComposites_NowIncludesSixComposites()
    {
        // 2026-04-28 (iter 80): updated count to 9 — iter 80 added Tournament,
        // Sandbox, Streaming. Test name kept for historical clarity (it pins the
        // iter 64 contribution: BattleSetup + FilmingSetup at indices 4-5).
        var vm = NewVm(out var sim); using var _ = sim;
        vm.AllComposites.Should().HaveCount(9,
            "iter 53 added 4 composites; iter 64 added Battle + Filming setup; iter 80 added Tournament + Sandbox + Streaming → 9 total");
        vm.AllComposites[4].Should().BeSameAs(vm.BattleSetup);
        vm.AllComposites[5].Should().BeSameAs(vm.FilmingSetup);
    }

    [Fact]
    public void HasMixedComposite_IsFalseAfterPhase2QuickActionCleanup()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.HasMixedComposite.Should().BeFalse(
            "Phase-2 primitives are omitted from quick actions instead of being mixed in");
        vm.MixedCompositeWarning.Should().BeEmpty();
    }

    [Fact]
    public void BattleSetup_UniformlyLive_NoteSurfacesEveryHelpersJustification()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        // Note propagation should show all 4 helper notes joined.
        vm.BattleSetup.Note.Should().Contain("Hardpoint-behavior sweep",
            "GodMode's note");
        vm.BattleSetup.Note.Should().Contain("Engine call sweep over local units",
            "HealAllLocal's note");
        vm.BattleSetup.Note.Should().Contain("Sets max-credits cap",
            "UncapCredits's note");
        vm.BattleSetup.Note.Should().Contain("non-local players setting credits=0",
            "DrainEnemyCredits's note");
    }
}
