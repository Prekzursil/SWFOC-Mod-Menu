using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 56) — pins per-button capability metadata on the
/// Combat tab. Replaces the previously-misleading
/// <c>"Toggles (LIVE — engine effect verified)"</c> group header with
/// per-button accuracy: GodMode + OneHitKill are LIVE today; the four
/// scalar setters and target-filter, plus OHK-AttackPower + Area-Damage,
/// are PHASE 2 PENDING (Phase-1-mirror only).
/// </summary>
public sealed class CombatTabViewModelCapabilityTests
{
    private static CombatTabViewModel NewVm(out SwfocSimulator sim)
    {
        sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return new CombatTabViewModel(adapter, new V2UnitMutationDispatcher(adapter));
    }

    [Fact]
    public void ToggleGodMode_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ToggleGodMode.Badge.Should().Be("LIVE");
        vm.ToggleGodMode.IsAllLive.Should().BeTrue();
    }

    [Fact]
    public void ToggleOhk_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ToggleOhk.Badge.Should().Be("LIVE");
    }

    [Fact]
    public void ToggleOhkAttackPower_BadgeIsPhase2Pending()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ToggleOhkAttackPower.Badge.Should().Be("PHASE 2 PENDING");
    }

    [Fact]
    public void ToggleAreaDamage_BadgeIsPhase2Pending()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ToggleAreaDamage.Badge.Should().Be("PHASE 2 PENDING");
    }

    [Fact]
    public void ScalarSetters_AllPhase2PendingExceptShield()
    {
        // 2026-04-29 (iter 129): SetUnitShield flipped Phase2Pending → LIVE
        // via SetFrontShield + SetRearShield engine helpers (RVA 0x3A8630
        // + 0x3A91E0). Iter 105's "XML-attribute-only, defer" finding
        // was wrong; iter 128 callgraph re-audit caught the engine
        // helpers already in the verified ledger.
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SetDamageMultiplier.Badge.Should().Be("PHASE 2 PENDING");
        vm.SetUnitShield.Badge.Should().Be("LIVE",
            "iter 129 flipped SetUnitShield LIVE via SetFrontShield/SetRearShield");
        vm.SetFireRate.Badge.Should().Be("PHASE 2 PENDING");
        vm.SetTargetFilter.Badge.Should().Be("PHASE 2 PENDING");
    }

    [Fact]
    public void HasPhase2PendingAction_TrueForCombatTab()
    {
        // 2026-04-29 (iter 129): SetUnitShield flipped LIVE — Combat tab now
        // has 5 of 9 Phase2Pending (down from 6 of 9). Still has at least
        // one PHASE 2 PENDING action so the warning surface still fires.
        var vm = NewVm(out var sim); using var _ = sim;
        vm.HasPhase2PendingAction.Should().BeTrue(
            "5 of the 9 Combat actions are still PHASE 2 PENDING after iter 129");
    }

    [Fact]
    public void Phase2PendingWarning_NamesEveryNonLiveButton()
    {
        // 2026-04-29 (iter 129): "Set unit shield" no longer appears in
        // the warning — flipped LIVE. Iter 100 also removed
        // "Set damage multiplier (GLOBAL)" pattern from this list, but
        // the per-slot SetDamageMultiplier remains Phase2Pending.
        var vm = NewVm(out var sim); using var _ = sim;
        var warning = vm.Phase2PendingWarning;
        warning.Should().Contain("Toggle OHK attack-power");
        warning.Should().Contain("Toggle area damage");
        warning.Should().Contain("Set damage multiplier");
        warning.Should().NotContain("Set unit shield",
            "iter 129 flipped SetUnitShield LIVE — must NOT appear in pending warning");
        warning.Should().Contain("Set fire rate");
        warning.Should().Contain("Set target filter");
        warning.Should().NotContain("Toggle god mode",
            "uniformly LIVE buttons must NOT appear in the warning");
        warning.Should().NotContain("Toggle one-hit-kill");
    }

    [Fact]
    public void AllActions_EnumeratesEveryActionInDeclaredOrder()
    {
        // 2026-04-28 (iter 100): added SetDamageMultiplierGlobal — 9 total.
        // 2026-05-05 (iter 193): added 4 per-unit Lua actions — 13 total.
        // 2026-05-06 (iter 219): added SuspendAiLua cinematic helper — 14 total.
        // 2026-05-06 (iter 227): added SetFireRateMultiplierGlobal +
        // GetFireRateMultiplierGlobal pair (closes A1.3 after 124-day deferral) — 16 total.
        var vm = NewVm(out var sim); using var _ = sim;
        vm.AllActions.Should().HaveCount(16);
        vm.AllActions[0].Should().BeSameAs(vm.ToggleGodMode);
        vm.AllActions[1].Should().BeSameAs(vm.ToggleOhk);
        vm.AllActions[2].Should().BeSameAs(vm.ToggleOhkAttackPower);
        vm.AllActions[3].Should().BeSameAs(vm.ToggleAreaDamage);
        vm.AllActions[4].Should().BeSameAs(vm.SetDamageMultiplier);
        vm.AllActions[5].Should().BeSameAs(vm.SetDamageMultiplierGlobal);
        vm.AllActions[6].Should().BeSameAs(vm.SetUnitShield);
        vm.AllActions[7].Should().BeSameAs(vm.SetFireRate);
        vm.AllActions[8].Should().BeSameAs(vm.SetTargetFilter);
        vm.AllActions[9].Should().BeSameAs(vm.HealUnitLua);
        vm.AllActions[10].Should().BeSameAs(vm.TakeDamageLua);
        vm.AllActions[11].Should().BeSameAs(vm.SetDamageModifierLua);
        vm.AllActions[12].Should().BeSameAs(vm.SetRateOfFireModifierLua);
        // iter 219: Suspend_AI cinematic helper (closes iter-216 queue)
        vm.AllActions[13].Should().BeSameAs(vm.SuspendAiLua);
        // iter 227: GLOBAL fire-rate multiplier pair (closes A1.3 after 124-day deferral)
        vm.AllActions[14].Should().BeSameAs(vm.SetFireRateMultiplierGlobal);
        vm.AllActions[15].Should().BeSameAs(vm.GetFireRateMultiplierGlobal);
    }

    [Fact]
    public void SetDamageMultiplierGlobal_BadgeIsLive()
    {
        // 2026-04-28 (iter 100): global multiplier LIVE via Take_Damage_Outer.
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SetDamageMultiplierGlobal.Badge.Should().Be("LIVE",
            "iter 96 wired the Take_Damage_Outer detour; LIVE");
    }
}
