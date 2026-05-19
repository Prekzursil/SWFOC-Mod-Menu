using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// Headless state model for V2 Tab 1 (Economy). Pure Core, no WPF
/// dependencies — bind a thin adapter in the App project that maps
/// WPF property-changed notifications onto the methods here. This
/// keeps the entire economy-tab logic unit-testable.
///
/// Task #145 — wires every economy service into one cohesive
/// state-machine: credits, tech, drain-enemy, uncap, income mult,
/// freeze credits, build speed, build cost, instant build, free build.
///
/// Action dispatch goes through <see cref="IEconomyDispatcher"/> so
/// tests can swap in a recording stub instead of a real bridge call.
/// </summary>
public sealed class EconomyTabState
{
    private readonly IEconomyDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;
    private readonly FeatureToggleCoordinator _toggles;

    public EconomyTabState(
        IEconomyDispatcher dispatcher,
        IUxFeedbackSink feedback,
        FeatureToggleCoordinator toggles)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(toggles);
        _dispatcher = dispatcher;
        _feedback = feedback;
        _toggles = toggles;
    }

    // ── Bindable inputs ────────────────────────────────────────

    /// <summary>Slot index, -1 = global.</summary>
    public int Slot { get; set; } = -1;

    public double CreditsAmount { get; set; } = 100000;
    public int TechLevel { get; set; } = 5;
    public float IncomeMultiplier { get; set; } = 1.0f;
    public float BuildSpeedMultiplier { get; set; } = 1.0f;
    public float BuildCostMultiplier { get; set; } = 1.0f;

    public double FreezeCreditsTarget { get; set; } = 99999;

    // ── Commands ───────────────────────────────────────────────

    /// <summary>Apply credit value. Wraps SetCredits / SetCreditsForSlot.</summary>
    public async Task<UxFeedback> SetCreditsAsync(CancellationToken ct = default)
    {
        if (CreditsAmount < 0)
        {
            return EmitAndReturn(UxFeedback.Error(
                "set_credits", $"credits must be >= 0 (got {CreditsAmount})", "set_credits"));
        }
        var ok = await _dispatcher.SetCreditsAsync(Slot, CreditsAmount, ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("set_credits", $"slot={Slot} → {CreditsAmount:0}", "set_credits")
            : UxFeedback.Error("set_credits", "bridge rejected the write", "set_credits"));
    }

    public async Task<UxFeedback> SetTechAsync(CancellationToken ct = default)
    {
        if (TechLevel < 1 || TechLevel > 5)
        {
            return EmitAndReturn(UxFeedback.Error(
                "set_tech", $"tech must be in [1,5], got {TechLevel}", "set_tech"));
        }
        var ok = await _dispatcher.SetTechAsync(Slot, TechLevel, ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("set_tech", $"slot={Slot} → tech {TechLevel}", "set_tech")
            : UxFeedback.Error("set_tech", "bridge rejected the write", "set_tech"));
    }

    public async Task<UxFeedback> DrainEnemyCreditsAsync(CancellationToken ct = default)
    {
        var ok = await _dispatcher.DrainEnemyCreditsAsync(ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("drain_enemy", "every non-local slot drained to 0", "drain_enemy")
            : UxFeedback.Error("drain_enemy", "drain rejected", "drain_enemy"));
    }

    public async Task<UxFeedback> UncapCreditsAsync(CancellationToken ct = default)
    {
        var ok = await _dispatcher.UncapCreditsAsync(ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("uncap_credits", "engine cap removed", "uncap_credits")
            : UxFeedback.Error("uncap_credits", "uncap rejected", "uncap_credits"));
    }

    public async Task<UxFeedback> SetIncomeMultiplierAsync(CancellationToken ct = default)
    {
        if (IncomeMultiplier < 0)
        {
            return EmitAndReturn(UxFeedback.Error(
                "set_income_mult", $"multiplier must be >= 0 (got {IncomeMultiplier})",
                "set_income_mult"));
        }
        var ok = await _dispatcher.SetIncomeMultiplierAsync(Slot, IncomeMultiplier, ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("set_income_mult", $"slot={Slot} → {IncomeMultiplier:0.00}×",
                "set_income_mult")
            : UxFeedback.Error("set_income_mult", "bridge rejected", "set_income_mult"));
    }

    public async Task<UxFeedback> SetBuildSpeedAsync(CancellationToken ct = default)
    {
        if (BuildSpeedMultiplier < 0)
        {
            return EmitAndReturn(UxFeedback.Error(
                "set_build_speed", $"multiplier must be >= 0 (got {BuildSpeedMultiplier})",
                "set_build_speed"));
        }
        var ok = await _dispatcher.SetBuildSpeedAsync(Slot, BuildSpeedMultiplier, ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("set_build_speed", $"slot={Slot} → {BuildSpeedMultiplier:0.00}×",
                "set_build_speed")
            : UxFeedback.Error("set_build_speed", "bridge rejected", "set_build_speed"));
    }

    public async Task<UxFeedback> SetBuildCostAsync(CancellationToken ct = default)
    {
        if (BuildCostMultiplier < 0)
        {
            return EmitAndReturn(UxFeedback.Error(
                "set_build_cost", $"multiplier must be >= 0 (got {BuildCostMultiplier})",
                "set_build_cost"));
        }
        var ok = await _dispatcher.SetBuildCostAsync(Slot, BuildCostMultiplier, ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("set_build_cost", $"slot={Slot} → {BuildCostMultiplier:0.00}×",
                "set_build_cost")
            : UxFeedback.Error("set_build_cost", "bridge rejected", "set_build_cost"));
    }

    /// <summary>
    /// Toggle FreezeCredits. The toggle goes through the coordinator
    /// so the disable callback runs automatically on detach.
    /// </summary>
    public Task<UxFeedback> ToggleFreezeCreditsAsync(bool enable, CancellationToken ct = default)
    {
        return _toggles.ToggleAsync("freeze_credits", enable,
            action: async cancel =>
            {
                var ok = await _dispatcher.SetFreezeCreditsAsync(
                    Slot, enable, FreezeCreditsTarget, cancel);
                return ok
                    ? UxFeedback.Success("freeze_credits",
                        enable ? $"slot={Slot} pinned at {FreezeCreditsTarget:0}" : "released",
                        "freeze_credits")
                    : UxFeedback.Error("freeze_credits", "bridge rejected", "freeze_credits");
            },
            disableAction: enable
                ? async cancel =>
                {
                    var ok = await _dispatcher.SetFreezeCreditsAsync(Slot, false, 0, cancel);
                    return ok
                        ? UxFeedback.Info("freeze_credits", "released (cleanup)", "freeze_credits")
                        : UxFeedback.Warning("freeze_credits", "release failed", "freeze_credits");
                }
        : null,
            cancellationToken: ct);
    }

    /// <summary>
    /// Toggle Instant Build. The Phase 2 AOB patch is engine-wide so
    /// this isn't per-slot.
    /// </summary>
    public Task<UxFeedback> ToggleInstantBuildAsync(bool enable, CancellationToken ct = default)
    {
        return _toggles.ToggleAsync("instant_build", enable,
            action: async cancel =>
            {
                var ok = await _dispatcher.SetInstantBuildAsync(enable, cancel);
                return ok
                    ? UxFeedback.Success("instant_build",
                        enable ? "queued builds complete on next tick" : "normal build pace restored",
                        "instant_build")
                    : UxFeedback.Error("instant_build", "bridge rejected", "instant_build");
            },
            disableAction: enable
                ? async cancel =>
                {
                    var ok = await _dispatcher.SetInstantBuildAsync(false, cancel);
                    return ok
                        ? UxFeedback.Info("instant_build", "normal build pace restored (cleanup)",
                            "instant_build")
                        : UxFeedback.Warning("instant_build", "cleanup-disable failed", "instant_build");
                }
        : null,
            cancellationToken: ct);
    }

    public Task<UxFeedback> ToggleFreeBuildAsync(bool enable, CancellationToken ct = default)
    {
        return _toggles.ToggleAsync("free_build", enable,
            action: async cancel =>
            {
                var ok = await _dispatcher.SetFreeBuildAsync(enable, cancel);
                return ok
                    ? UxFeedback.Success("free_build",
                        enable ? "build cost waived" : "build cost restored", "free_build")
                    : UxFeedback.Error("free_build", "bridge rejected", "free_build");
            },
            disableAction: enable
                ? async cancel =>
                {
                    var ok = await _dispatcher.SetFreeBuildAsync(false, cancel);
                    return ok
                        ? UxFeedback.Info("free_build", "cost restored (cleanup)", "free_build")
                        : UxFeedback.Warning("free_build", "cleanup-disable failed", "free_build");
                }
        : null,
            cancellationToken: ct);
    }

    // ── 2026-05-06 (iter 233): GLOBAL credits freeze + multiplier ─────
    // LIVE via AddCredits MinHook detour (iter 231). Distinct surface
    // from per-slot SetCredits/SetIncomeMultiplier (those stay PHASE 2
    // PENDING). Pattern parallels iter-227 SetFireRateMultiplierGlobal
    // wrappers on CombatTabState.

    /// <summary>
    /// 2026-05-06 (iter 233): toggle the GLOBAL credits freeze.
    /// LIVE via AddCredits @ 0x27F370 MinHook detour. Wins-over-mult
    /// precedence per iter-230 RE design.
    /// </summary>
    public bool GlobalCreditsFreezeStaged { get; set; }

    public async Task<UxFeedback> SetCreditsFreezeGlobalAsync(CancellationToken ct = default)
    {
        var ok = await _dispatcher.SetCreditsFreezeGlobalAsync(GlobalCreditsFreezeStaged, ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("set_credits_freeze_global",
                GlobalCreditsFreezeStaged ? "credits frozen (GLOBAL)" : "credits unfrozen (GLOBAL)",
                "set_credits_freeze_global")
            : UxFeedback.Error("set_credits_freeze_global",
                "bridge rejected", "set_credits_freeze_global"));
    }

    public Task<bool> GetCreditsFreezeGlobalAsync(CancellationToken ct = default)
        => _dispatcher.GetCreditsFreezeGlobalAsync(ct);

    /// <summary>
    /// 2026-05-06 (iter 233): GLOBAL credits multiplier on AddCredits delta.
    /// Sanity clamp [0.0, 100.0] applied bridge-side. mult=2 → 2x both
    /// income/spend; mult=0.5 → halved both. Per iter-230 caveats.
    /// </summary>
    public float GlobalCreditsMultiplierStaged { get; set; } = 1.0f;

    public async Task<UxFeedback> SetCreditsMultiplierGlobalAsync(CancellationToken ct = default)
    {
        if (GlobalCreditsMultiplierStaged < 0)
        {
            return EmitAndReturn(UxFeedback.Error("set_credits_mult_global",
                $"multiplier must be >= 0 (got {GlobalCreditsMultiplierStaged})",
                "set_credits_mult_global"));
        }
        var ok = await _dispatcher.SetCreditsMultiplierGlobalAsync(GlobalCreditsMultiplierStaged, ct);
        return EmitAndReturn(ok
            ? UxFeedback.Success("set_credits_mult_global",
                $"global credits mult → {GlobalCreditsMultiplierStaged:0.00}×",
                "set_credits_mult_global")
            : UxFeedback.Error("set_credits_mult_global",
                "bridge rejected", "set_credits_mult_global"));
    }

    public Task<float> GetCreditsMultiplierGlobalAsync(CancellationToken ct = default)
        => _dispatcher.GetCreditsMultiplierGlobalAsync(ct);

    private UxFeedback EmitAndReturn(UxFeedback fb)
    {
        _feedback.Emit(fb);
        return fb;
    }
}

/// <summary>
/// Dispatch surface for the economy tab. The App project provides
/// an implementation that proxies to TrainerOrchestrator.ExecuteAsync
/// with the right action ids; tests provide a recording stub.
/// </summary>
public interface IEconomyDispatcher
{
    Task<bool> SetCreditsAsync(int slot, double amount, CancellationToken ct);
    Task<bool> SetTechAsync(int slot, int level, CancellationToken ct);
    Task<bool> DrainEnemyCreditsAsync(CancellationToken ct);
    Task<bool> UncapCreditsAsync(CancellationToken ct);
    Task<bool> SetIncomeMultiplierAsync(int slot, float mult, CancellationToken ct);
    Task<bool> SetBuildSpeedAsync(int slot, float mult, CancellationToken ct);
    Task<bool> SetBuildCostAsync(int slot, float mult, CancellationToken ct);
    Task<bool> SetFreezeCreditsAsync(int slot, bool enable, double target, CancellationToken ct);
    Task<bool> SetInstantBuildAsync(bool enable, CancellationToken ct);
    Task<bool> SetFreeBuildAsync(bool enable, CancellationToken ct);

    // 2026-05-06 (iter 231/233): GLOBAL credits freeze + multiplier — LIVE via
    // AddCredits @ 0x27F370 MinHook detour. Pattern parallels iter-96 + iter-225.
    // Default impls keep older mocks compiling. Per iter-230 RE design:
    // bool freeze short-circuits AddCredits entirely (precedence over mult);
    // mult scales delta arg with [0.0, 100.0] clamp.
    Task<bool> SetCreditsFreezeGlobalAsync(bool freeze, CancellationToken ct)
        => Task.FromResult(false);
    Task<bool> GetCreditsFreezeGlobalAsync(CancellationToken ct)
        => Task.FromResult(false);
    Task<bool> SetCreditsMultiplierGlobalAsync(float mult, CancellationToken ct)
        => Task.FromResult(false);
    Task<float> GetCreditsMultiplierGlobalAsync(CancellationToken ct)
        => Task.FromResult(1.0f);
}
