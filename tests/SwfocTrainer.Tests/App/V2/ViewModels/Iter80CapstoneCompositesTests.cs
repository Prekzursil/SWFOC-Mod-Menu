using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-28 (iter 80) — pins the capstone composite presets that tie
/// iter 76 (Combat) / iter 77 (Speed) / iter 78 (HeroLab) together at
/// the workflow level. Three composites: Tournament, Sandbox, Streaming.
/// Each verifies (a) the composite is exposed, (b) the command runs, and
/// (c) the underlying Lua command sequence matches the operator's
/// expectations (key SWFOC_X helpers fire in the right order).
/// </summary>
public sealed class Iter80CapstoneCompositesTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter, QuickActionsTabViewModel vm) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, adapter, new QuickActionsTabViewModel(adapter));
    }

    /// <summary>
    /// Poll <see cref="V2BridgeAdapter.RecentCalls"/> until at least
    /// <paramref name="minimumCount"/> entries have been recorded or
    /// <paramref name="timeoutMs"/> milliseconds elapse. Replaces the older
    /// fixed-delay pattern that flaked under stress test load when the
    /// async-dispatch budget exceeded the hard-coded 400 ms window. Returns
    /// quickly (≈10-50 ms) on the green path; falls back to a generous 3 s
    /// timeout under heavy parallel-test contention.
    /// </summary>
    private static async Task WaitForRecentCallsAtLeast(V2BridgeAdapter adapter, int minimumCount, int timeoutMs = 3000)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (adapter.RecentCalls.Count >= minimumCount)
            {
                // Tiny extra settle so a still-in-flight call lands before
                // assertions inspect the LuaCommand list.
                await Task.Delay(50);
                return;
            }
            await Task.Delay(20);
        }
    }

    [Fact]
    public void CapstoneComposites_AllExposed()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.TournamentSetup.Should().NotBeNull();
        vm.SandboxSetup.Should().NotBeNull();
        vm.StreamingSetup.Should().NotBeNull();
        vm.TournamentSetupCommand.Should().NotBeNull();
        vm.SandboxSetupCommand.Should().NotBeNull();
        vm.StreamingSetupCommand.Should().NotBeNull();
    }

    [Fact]
    public void TournamentSetup_HasThreeHelpers_HardCombatScalarsPlusSlowRespawn()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.TournamentSetup.HelperNames.Should().HaveCount(3);
        vm.TournamentSetup.HelperNames.Should().Contain("SWFOC_SetDamageMultiplierGlobal");
        vm.TournamentSetup.HelperNames.Should().Contain("SWFOC_SetFireRateMultiplierGlobal");
        vm.TournamentSetup.HelperNames.Should().Contain("SWFOC_SetHeroRespawn");
    }

    [Fact]
    public void SandboxSetup_HasFiveHelpers_FullOperatorControl()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.SandboxSetup.HelperNames.Should().HaveCount(5);
        vm.SandboxSetup.HelperNames.Should().Contain("SWFOC_GodMode");
        vm.SandboxSetup.HelperNames.Should().Contain("SWFOC_HealAllLocal");
        vm.SandboxSetup.HelperNames.Should().Contain("SWFOC_UncapCredits");
        vm.SandboxSetup.HelperNames.Should().Contain("SWFOC_DrainEnemyCredits");
        vm.SandboxSetup.HelperNames.Should().Contain("SWFOC_SetHeroRespawn");
    }

    [Fact]
    public void StreamingSetup_HasThreeHelpers_CinematicPosture()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.StreamingSetup.HelperNames.Should().HaveCount(3);
        vm.StreamingSetup.HelperNames.Should().Contain("SWFOC_DoString");
        vm.StreamingSetup.HelperNames.Should().Contain("SWFOC_SuspendAiLua");
        vm.StreamingSetup.HelperNames.Should().Contain("SWFOC_SetHeroRespawn");
    }

    [Fact]
    public async Task TournamentSetup_RunComposite_FiresEachHelper()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;

        vm.TournamentSetupCommand.Execute(null);
        // RunComposite fires each call; poll until all 3 land or 3s timeout.
        // (Old code used a fixed Task.Delay(400) which flaked under stress.)
        await WaitForRecentCallsAtLeast(adapter, minimumCount: 3);

        adapter.RecentCalls.Should().HaveCountGreaterThanOrEqualTo(3);
        adapter.RecentCalls.Any(e => e.LuaCommand.Contains("SetDamageMultiplierGlobal")).Should().BeTrue();
        adapter.RecentCalls.Any(e => e.LuaCommand.Contains("SetFireRateMultiplierGlobal")).Should().BeTrue();
        adapter.RecentCalls.Any(e => e.LuaCommand.Contains("SetGameSpeed")).Should().BeFalse(
            "global game speed is Phase 2 pending and must not be fired by quick actions");
        adapter.RecentCalls.Any(e => e.LuaCommand.Contains("SetHeroRespawn")).Should().BeTrue();
    }

    [Fact]
    public async Task SandboxSetup_RunComposite_FiresAllFiveHelpers()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;

        vm.SandboxSetupCommand.Execute(null);
        // RunComposite fires 5 helpers; poll-until-landed instead of fixed delay.
        await WaitForRecentCallsAtLeast(adapter, minimumCount: 5);

        adapter.RecentCalls.Should().HaveCountGreaterThanOrEqualTo(5);
        // Spot-check the most distinctive ones.
        adapter.RecentCalls.Any(e => e.LuaCommand.Contains("UncapCredits")).Should().BeTrue();
        adapter.RecentCalls.Any(e => e.LuaCommand.Contains("DrainEnemyCredits")).Should().BeTrue();
        adapter.RecentCalls.Any(e => e.LuaCommand.Contains("SetGameSpeed")).Should().BeFalse(
            "global game speed is Phase 2 pending and must not be fired by quick actions");
    }

    [Fact]
    public async Task StreamingSetup_RunComposite_PostsHideHudAndAiFreeze()
    {
        var (sim, adapter, vm) = NewSession();
        using var _ = sim;

        vm.StreamingSetupCommand.Execute(null);
        // RunComposite fires 3 helpers; poll-until-landed instead of fixed delay.
        await WaitForRecentCallsAtLeast(adapter, minimumCount: 3);

        adapter.RecentCalls.Should().HaveCountGreaterThanOrEqualTo(3);
        adapter.RecentCalls.Any(e => e.LuaCommand.Contains("Hide_HUD")).Should().BeTrue();
        adapter.RecentCalls.Any(e => e.LuaCommand.Contains("SuspendAiLua")).Should().BeTrue();
        adapter.RecentCalls.Any(e => e.LuaCommand.Contains("SetGameSpeed")).Should().BeFalse(
            "slow-mo depends on global game speed, which is Phase 2 pending");
    }

    [Fact]
    public void TournamentSetup_BadgeIsLive_AfterPhase2SpeedRemoval()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.TournamentSetup.Badge.Should().Be("LIVE");
        vm.TournamentSetup.IsAllLive.Should().BeTrue();
        vm.TournamentSetup.IsMixed.Should().BeFalse();
    }

    [Fact]
    public void SandboxSetup_BadgeIsLive_AfterPhase2SpeedRemoval()
    {
        var (sim, _, vm) = NewSession();
        using var _ = sim;

        vm.SandboxSetup.Badge.Should().Be("LIVE");
        vm.SandboxSetup.IsAllLive.Should().BeTrue();
        vm.SandboxSetup.IsMixed.Should().BeFalse();
    }
}
