using FluentAssertions;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.V2Vm;

/// <summary>
/// Recording stub for IEconomyDispatcher. Captures every call into a
/// list so tests can assert which bridge actions fired in which order.
/// </summary>
internal sealed class RecordingEconomyDispatcher : IEconomyDispatcher
{
    public List<string> Calls { get; } = new();
    public bool ReturnValue { get; set; } = true;

    public Task<bool> SetCreditsAsync(int slot, double amount, CancellationToken ct)
    { Calls.Add($"SetCredits({slot},{amount:0})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetTechAsync(int slot, int level, CancellationToken ct)
    { Calls.Add($"SetTech({slot},{level})"); return Task.FromResult(ReturnValue); }
    public Task<bool> DrainEnemyCreditsAsync(CancellationToken ct)
    { Calls.Add("DrainEnemy"); return Task.FromResult(ReturnValue); }
    public Task<bool> UncapCreditsAsync(CancellationToken ct)
    { Calls.Add("Uncap"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetIncomeMultiplierAsync(int slot, float mult, CancellationToken ct)
    { Calls.Add($"SetIncomeMult({slot},{mult:0.00})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetBuildSpeedAsync(int slot, float mult, CancellationToken ct)
    { Calls.Add($"SetBuildSpeed({slot},{mult:0.00})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetBuildCostAsync(int slot, float mult, CancellationToken ct)
    { Calls.Add($"SetBuildCost({slot},{mult:0.00})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetFreezeCreditsAsync(int slot, bool enable, double target, CancellationToken ct)
    { Calls.Add($"SetFreeze({slot},{enable},{target:0})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetInstantBuildAsync(bool enable, CancellationToken ct)
    { Calls.Add($"SetInstantBuild({enable})"); return Task.FromResult(ReturnValue); }
    public Task<bool> SetFreeBuildAsync(bool enable, CancellationToken ct)
    { Calls.Add($"SetFreeBuild({enable})"); return Task.FromResult(ReturnValue); }
}

public sealed class EconomyTabStateTests
{
    private (EconomyTabState state, RecordingEconomyDispatcher dispatcher,
             RecordingFeedbackSink sink, FeatureToggleCoordinator coord) Build()
    {
        var dispatcher = new RecordingEconomyDispatcher();
        var sink = new RecordingFeedbackSink();
        var coord = new FeatureToggleCoordinator(sink);
        var state = new EconomyTabState(dispatcher, sink, coord);
        return (state, dispatcher, sink, coord);
    }

    // ── SetCredits ────────────────────────────────────────────

    [Fact]
    public async Task SetCredits_DispatchesAndEmitsSuccess()
    {
        var (state, d, s, _) = Build();
        state.Slot = 1;
        state.CreditsAmount = 50000;
        var fb = await state.SetCreditsAsync();
        d.Calls.Should().ContainSingle().Which.Should().Be("SetCredits(1,50000)");
        fb.Severity.Should().Be(UxSeverity.Success);
        s.Last!.Severity.Should().Be(UxSeverity.Success);
    }

    [Fact]
    public async Task SetCredits_NegativeAmount_RejectsBeforeDispatch()
    {
        var (state, d, _, _) = Build();
        state.CreditsAmount = -1;
        var fb = await state.SetCreditsAsync();
        fb.Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetCredits_DispatcherFailure_EmitsError()
    {
        var (state, d, s, _) = Build();
        d.ReturnValue = false;
        var fb = await state.SetCreditsAsync();
        fb.Severity.Should().Be(UxSeverity.Error);
        s.BySeverity(UxSeverity.Error).Should().HaveCount(1);
    }

    // ── SetTech ────────────────────────────────────────────────

    [Fact]
    public async Task SetTech_OutOfRange_Rejected()
    {
        var (state, d, _, _) = Build();
        state.TechLevel = 6;
        (await state.SetTechAsync()).Severity.Should().Be(UxSeverity.Error);
        state.TechLevel = 0;
        (await state.SetTechAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task SetTech_InRange_Dispatches()
    {
        var (state, d, _, _) = Build();
        state.Slot = 2;
        state.TechLevel = 4;
        (await state.SetTechAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("SetTech(2,4)");
    }

    // ── DrainEnemy / Uncap (parameterless) ────────────────────

    [Fact]
    public async Task DrainEnemy_DispatchesAndSucceeds()
    {
        var (state, d, _, _) = Build();
        (await state.DrainEnemyCreditsAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("DrainEnemy");
    }

    [Fact]
    public async Task UncapCredits_DispatchesAndSucceeds()
    {
        var (state, d, _, _) = Build();
        (await state.UncapCreditsAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("Uncap");
    }

    // ── Multipliers (income / build-speed / build-cost) ───────

    [Fact]
    public async Task IncomeMult_NegativeRejected()
    {
        var (state, d, _, _) = Build();
        state.IncomeMultiplier = -0.5f;
        (await state.SetIncomeMultiplierAsync()).Severity.Should().Be(UxSeverity.Error);
        d.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task IncomeMult_Zero_Allowed_ForRoutines()
    {
        // Setting income mult to 0 == "freeze income at 0" — a valid
        // configuration for "no income" routines.
        var (state, d, _, _) = Build();
        state.IncomeMultiplier = 0;
        (await state.SetIncomeMultiplierAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("SetIncomeMult(-1,0.00)");
    }

    [Fact]
    public async Task BuildSpeed_PositiveDispatches()
    {
        var (state, d, _, _) = Build();
        state.Slot = 3;
        state.BuildSpeedMultiplier = 5.0f;
        (await state.SetBuildSpeedAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("SetBuildSpeed(3,5.00)");
    }

    [Fact]
    public async Task BuildCost_FreeBuildShortcut_Zero_Allowed()
    {
        var (state, d, _, _) = Build();
        state.BuildCostMultiplier = 0;
        (await state.SetBuildCostAsync()).Severity.Should().Be(UxSeverity.Success);
        d.Calls.Should().Contain("SetBuildCost(-1,0.00)");
    }

    // ── Toggles with cleanup-on-disable ────────────────────────

    [Fact]
    public async Task ToggleFreezeCredits_EnableThenCleanup_DispatchesDisable()
    {
        var (state, d, s, coord) = Build();
        state.Slot = 1;
        state.FreezeCreditsTarget = 99999;

        await state.ToggleFreezeCreditsAsync(true);
        coord.IsEnabled("freeze_credits").Should().BeTrue();
        d.Calls.Should().Contain("SetFreeze(1,True,99999)");

        await coord.CleanupAllAsync();
        coord.IsEnabled("freeze_credits").Should().BeFalse();
        d.Calls.Should().Contain(c => c.StartsWith("SetFreeze(1,False"));
    }

    [Fact]
    public async Task ToggleInstantBuild_EnableThenManualDisable_NoDoubleClean()
    {
        var (state, d, _, coord) = Build();

        await state.ToggleInstantBuildAsync(true);
        await state.ToggleInstantBuildAsync(false);
        coord.IsEnabled("instant_build").Should().BeFalse();

        // Cleanup runs no extra disable because the state is already disabled.
        var cleaned = await coord.CleanupAllAsync();
        cleaned.Should().Be(0);
        d.Calls.Where(c => c.Contains("SetInstantBuild")).Should().HaveCount(2);
    }

    [Fact]
    public async Task ToggleFreeBuild_DispatcherFailure_StateRecordsErrorFeedback()
    {
        var (state, d, s, coord) = Build();
        d.ReturnValue = false;
        await state.ToggleFreeBuildAsync(true);
        s.BySeverity(UxSeverity.Error).Should().HaveCount(1);
        // State coordinator records the toggle as enabled (action was
        // called even though it returned false). The error severity
        // gives the operator a clear "this didn't work" signal.
        coord.IsEnabled("free_build").Should().BeTrue();
    }
}
