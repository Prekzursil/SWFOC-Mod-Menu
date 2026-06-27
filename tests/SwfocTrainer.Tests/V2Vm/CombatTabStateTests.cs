using FluentAssertions;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.V2Vm;

internal sealed class RecordingCombatDispatcher : ICombatDispatcher
{
    public List<string> Calls { get; } = new();
    public bool ReturnValue { get; set; } = true;

    public Task<bool> SetGodModeAsync(bool e, CancellationToken ct)
    { Calls.Add($"GodMode({e})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetOhkAsync(bool e, CancellationToken ct)
    { Calls.Add($"Ohk({e})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetOhkAttackPowerAsync(bool e, CancellationToken ct)
    { Calls.Add($"OhkAtk({e})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetAreaDamageAsync(bool e, CancellationToken ct)
    { Calls.Add($"Area({e})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetDamageMultiplierAsync(int s, float m, CancellationToken ct)
    { Calls.Add($"DmgMult({s},{m:0.00})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetUnitShieldAsync(long addr, float v, CancellationToken ct)
    { Calls.Add($"Shield(0x{addr:X},{v:0})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetFireRateAsync(int s, float m, CancellationToken ct)
    { Calls.Add($"Fire({s},{m:0.00})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetTargetFilterAsync(int s, int b, CancellationToken ct)
    { Calls.Add($"Filter({s},0x{b:X})"); return Task.FromResult(ReturnValue); }

    // 2026-04-28 (iter 100): global damage multiplier (LIVE).
    public float StoredGlobalDamageMultiplier { get; private set; } = 1.0f;
    public Task<bool> SetDamageMultiplierGlobalAsync(float m, CancellationToken ct)
    {
        Calls.Add($"GlobalDmg({m:0.00})");
        StoredGlobalDamageMultiplier = m;
        return Task.FromResult(ReturnValue);
    }
    public Task<float> GetDamageMultiplierGlobalAsync(CancellationToken ct)
        => Task.FromResult(StoredGlobalDamageMultiplier);
}

public sealed class CombatTabStateTests
{
    private (CombatTabState state, RecordingCombatDispatcher dispatcher,
             RecordingFeedbackSink sink, FeatureToggleCoordinator coord) Build()
    {
        var dispatcher = new RecordingCombatDispatcher();
        var sink = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(sink);
        var state = new CombatTabState(dispatcher, sink, coord);
        return (state, dispatcher, sink, coord);
    }

    [Fact]
    public async Task ToggleGodMode_EnableThenCleanup_DispatchesDisable()
    {
        var (state, d, _, coord) = Build();
        await state.ToggleGodModeAsync(true);
        coord.IsEnabled("god_mode").Should().BeTrue();
        await coord.CleanupAllAsync();
        coord.IsEnabled("god_mode").Should().BeFalse();
        d.Calls.Should().ContainInOrder("GodMode(True)", "GodMode(False)");
    }

    [Fact]
    public async Task ToggleCombinedGodAndOhk_FlipsBoth_OnEnableAndCleanup()
    {
        var (state, d, _, coord) = Build();
        await state.ToggleCombinedGodAndOhkAsync(true);
        d.Calls.Should().ContainInOrder("GodMode(True)", "Ohk(True)");

        await coord.CleanupAllAsync();
        d.Calls.Should().ContainInOrder("GodMode(False)", "Ohk(False)");
        coord.IsEnabled("combined_god_ohk").Should().BeFalse();
    }

    [Fact]
    public async Task ToggleCombined_PartialFailure_EmitsError()
    {
        var (state, d, sink, _) = Build();
        // Use a custom dispatcher where GodMode succeeds but OHK fails.
        var custom = new PartialDispatcher();
        var sink2 = new RecordingFeedbackSink();
        var coord2 = new FeatureToggleCoordinator(sink2);
        var state2 = new CombatTabState(custom, sink2, coord2);
        var fb = await state2.ToggleCombinedGodAndOhkAsync(true);
        fb.Severity.Should().Be(UxSeverity.Error);
        fb.Message.Should().Contain("god=True");
        fb.Message.Should().Contain("ohk=False");
    }

    private sealed class PartialDispatcher : ICombatDispatcher
    {
        public Task<bool> SetGodModeAsync(bool e, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetOhkAsync(bool e, CancellationToken ct) => Task.FromResult(false);
        public Task<bool> SetOhkAttackPowerAsync(bool e, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetAreaDamageAsync(bool e, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetDamageMultiplierAsync(int s, float m, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetUnitShieldAsync(long a, float v, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetFireRateAsync(int s, float m, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetTargetFilterAsync(int s, int b, CancellationToken ct) => Task.FromResult(true);
    }

    [Fact]
    public async Task SetDamageMultiplier_NegativeRejected()
    {
        var (state, d, _, _) = Build();
        state.DamageMultiplier = -0.5f;
        (await state.SetDamageMultiplierAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetDamageMultiplier_Zero_Allowed_ForInvincibleUnits()
    {
        // Damage mult 0 == "incoming damage scaled to 0" — valid.
        var (state, d, _, _) = Build();
        state.Slot = 2;
        state.DamageMultiplier = 0;
        (await state.SetDamageMultiplierAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("DmgMult(2,0.00)");
    }

    [Fact]
    public async Task SetUnitShield_NoSelection_Rejected()
    {
        var (state, d, _, _) = Build();
        state.SelectedObjAddr = 0;
        state.ShieldValue = 100;
        (await state.SetUnitShieldAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetUnitShield_NegativeValue_Rejected()
    {
        var (state, d, _, _) = Build();
        state.SelectedObjAddr = 0xABCD;
        state.ShieldValue = -10;
        (await state.SetUnitShieldAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetUnitShield_ValidInput_Dispatches()
    {
        var (state, d, _, _) = Build();
        state.SelectedObjAddr = 0xABCD;
        state.ShieldValue = 250;
        (await state.SetUnitShieldAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("Shield(0xABCD,250)");
    }

    [Fact]
    public async Task SetFireRate_ZeroOrNegativeRejected()
    {
        var (state, d, _, _) = Build();
        state.FireRateMultiplier = 0;
        (await state.SetFireRateAsync()).Severity.Should().Be(UxSeverity.Error);
        state.FireRateMultiplier = -1;
        (await state.SetFireRateAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0x7u, "ENEMY|FRIENDLY|NEUTRAL")]
    [InlineData(0x1u, "ENEMY")]
    [InlineData(0x2u, "FRIENDLY")]
    [InlineData(0x4u, "NEUTRAL")]
    [InlineData(0x3u, "ENEMY|FRIENDLY")]
    [InlineData(0x0u, "DISARM")]
    public async Task SetTargetFilter_LabelsBitmaskHumanReadable(int mask, string expectedLabel)
    {
        var (state, d, sink, _) = Build();
        state.TargetFilterBitmask = mask;
        var fb = await state.SetTargetFilterAsync();
        fb.Severity.Should().Be(UxSeverity.Success);
        fb.Message.Should().Contain(expectedLabel);
    }

    [Fact]
    public async Task ToggleArea_ThenToggleOff_StateClean()
    {
        var (state, d, _, coord) = Build();
        await state.ToggleAreaDamageAsync(true);
        coord.IsEnabled("area_damage").Should().BeTrue();
        await state.ToggleAreaDamageAsync(false);
        coord.IsEnabled("area_damage").Should().BeFalse();
        d.Calls.Should().ContainInOrder("Area(True)", "Area(False)");
    }
}
