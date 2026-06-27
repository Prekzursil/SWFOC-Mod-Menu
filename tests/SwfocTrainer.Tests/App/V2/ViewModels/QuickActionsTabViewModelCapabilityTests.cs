using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 54) — pins the Quick Actions tab's capability-awareness
/// contract:
/// <list type="bullet">
///   <item>Each composite resolves its primitives against
///         <c>CapabilityStatusCatalog</c> and exposes a per-composite
///         badge (LIVE / MIXED / PHASE 2 PENDING).</item>
///   <item>Operator-facing composites avoid PHASE 2 primitives, so a
///         successful quick action means each primitive has live backing.</item>
/// </list>
/// Pure VM tests — no simulator wiring; uses the same lightweight session
/// pattern as the existing scenario tests but ignores the bridge.
/// </summary>
public sealed class QuickActionsTabViewModelCapabilityTests
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
    public void OperatorGodMode_AllPrimitivesLive_BadgeIsLive()
    {
        var vm = NewVm(out var sim);
        using var _ = sim;
        vm.OperatorGodMode.Badge.Should().Be("LIVE",
            "GodMode + HealAllLocal + UncapCredits are all catalogued LIVE");
        vm.OperatorGodMode.IsAllLive.Should().BeTrue();
        vm.OperatorGodMode.IsMixed.Should().BeFalse();
    }

    [Fact]
    public void DrainEnemies_SinglePrimitiveLive_BadgeIsLive()
    {
        var vm = NewVm(out var sim);
        using var _ = sim;
        vm.DrainEnemies.Badge.Should().Be("LIVE",
            "SWFOC_DrainEnemyCredits is catalogued LIVE");
        vm.DrainEnemies.IsAllLive.Should().BeTrue();
    }

    [Fact]
    public void RevealGalaxy_AllPrimitivesLive_BadgeIsLive()
    {
        var vm = NewVm(out var sim);
        using var _ = sim;
        // RevealAll, GetPlanets, and GetAllPlayers are all LIVE after the
        // iter-296 GetPlanets promotion.
        vm.RevealGalaxy.Badge.Should().Be("LIVE",
            "GetPlanets now uses the live galactic planet enumeration path");
        vm.RevealGalaxy.IsMixed.Should().BeFalse();
        vm.RevealGalaxy.IsAllLive.Should().BeTrue();
    }

    [Fact]
    public void ResetToggles_Phase2PrimitivesRemoved_BadgeIsLive()
    {
        var vm = NewVm(out var sim);
        using var _ = sim;
        vm.ResetToggles.Badge.Should().Be("LIVE",
            "Phase-2-only toggles were removed from the quick action");
        vm.ResetToggles.IsAllLive.Should().BeTrue();
        vm.ResetToggles.IsMixed.Should().BeFalse();
    }

    [Fact]
    public void HasMixedComposite_FalseWhenQuickActionsAreLiveOnly()
    {
        var vm = NewVm(out var sim);
        using var _ = sim;
        vm.HasMixedComposite.Should().BeFalse(
            "quick actions now omit Phase-2 primitives instead of mixing them");
    }

    [Fact]
    public void MixedCompositeWarning_IsEmptyWhenNoCompositeIsMixed()
    {
        var vm = NewVm(out var sim);
        using var _ = sim;
        vm.MixedCompositeWarning.Should().BeEmpty();
    }

    [Fact]
    public void AllComposites_AreLiveOnly()
    {
        var vm = NewVm(out var sim);
        using var _ = sim;
        vm.AllComposites.Should().OnlyContain(c => c.IsAllLive);
    }

    [Fact]
    public void AllComposites_EnumeratesEveryCompositeInDeclaredOrder()
    {
        // 2026-04-28 (iter 77 → updated iter 80): updated again to reflect
        // iter 80's TournamentSetup / SandboxSetup / StreamingSetup
        // additions. The order-pin discipline still applies — composites
        // appear in the order they're declared in the VM constructor and
        // surfaced in the XAML wrap panel.
        var vm = NewVm(out var sim);
        using var _ = sim;
        vm.AllComposites.Should().HaveCount(9);
        vm.AllComposites[0].Should().BeSameAs(vm.OperatorGodMode);
        vm.AllComposites[1].Should().BeSameAs(vm.DrainEnemies);
        vm.AllComposites[2].Should().BeSameAs(vm.RevealGalaxy);
        vm.AllComposites[3].Should().BeSameAs(vm.ResetToggles);
        vm.AllComposites[4].Should().BeSameAs(vm.BattleSetup);
        vm.AllComposites[5].Should().BeSameAs(vm.FilmingSetup);
        vm.AllComposites[6].Should().BeSameAs(vm.TournamentSetup);
        vm.AllComposites[7].Should().BeSameAs(vm.SandboxSetup);
        vm.AllComposites[8].Should().BeSameAs(vm.StreamingSetup);
    }

    [Theory]
    [InlineData("return SWFOC_GodMode(1)", "SWFOC_GodMode")]
    [InlineData("return SWFOC_HealAllLocal()", "SWFOC_HealAllLocal")]
    [InlineData("return SWFOC_RevealAll(1)", "SWFOC_RevealAll")]
    [InlineData("return SWFOC_DrainEnemyCredits()", "SWFOC_DrainEnemyCredits")]
    [InlineData("", "")]
    public void ExtractHelperName_StripsReturnPrefixAndArgList(string lua, string expected)
    {
        CapabilityAwareAction.ExtractHelperName(lua).Should().Be(expected);
    }
}
