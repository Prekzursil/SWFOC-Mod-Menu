using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// V2 Tab 8 (Battle Control). Task #152 — auto-win, instant-build,
/// free-build, unit-cap override, freeze AI, kill-all/heal-all, no-fog,
/// build/game-speed (the speed parts overlap with Tab 3 but are
/// re-bound here for content-creator workflows).
/// </summary>
public sealed class BattleControlTabState
{
    private readonly IBattleControlDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;
    private readonly FeatureToggleCoordinator _toggles;

    public BattleControlTabState(IBattleControlDispatcher dispatcher,
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

    public int TargetSlot { get; set; } = -1;
    public int UnitCap { get; set; } = 100;

    public Task<UxFeedback> ToggleFreezeAiAsync(bool enable, CancellationToken ct = default)
    {
        var key = $"freeze_ai_slot{TargetSlot}";
        return _toggles.ToggleAsync(key, enable,
            action: async cancel =>
            {
                var ok = await _dispatcher.SetFreezeAiAsync(TargetSlot, enable, cancel);
                return ok
                    ? UxFeedback.Success(key,
                        $"slot {TargetSlot} AI {(enable ? "frozen" : "unfrozen")}", key)
                    : UxFeedback.Error(key, "bridge rejected", key);
            },
            disableAction: enable
                ? async cancel =>
                {
                    var ok = await _dispatcher.SetFreezeAiAsync(TargetSlot, false, cancel);
                    return ok
                        ? UxFeedback.Info(key, "AI unfrozen (cleanup)", key)
                        : UxFeedback.Warning(key, "cleanup failed", key);
                }
        : null,
            cancellationToken: ct);
    }

    public async Task<UxFeedback> KillAllEnemiesAsync(CancellationToken ct = default)
    {
        var ok = await _dispatcher.KillAllEnemiesAsync(ct);
        return Emit(ok
            ? UxFeedback.Success("kill_all_enemies",
                "every non-local unit reduced to 0 hull", "kill_all_enemies")
            : UxFeedback.Error("kill_all_enemies", "bridge rejected", "kill_all_enemies"));
    }

    public async Task<UxFeedback> HealAllLocalAsync(CancellationToken ct = default)
    {
        var ok = await _dispatcher.HealAllLocalAsync(ct);
        return Emit(ok
            ? UxFeedback.Success("heal_all_local",
                "every local unit restored to max_hull", "heal_all_local")
            : UxFeedback.Error("heal_all_local", "bridge rejected", "heal_all_local"));
    }

    public async Task<UxFeedback> SetUnitCapAsync(CancellationToken ct = default)
    {
        if (TargetSlot < 0)
        {
            return Emit(UxFeedback.Error("set_unit_cap",
                "slot must be >= 0", "set_unit_cap"));
        }
        if (UnitCap < -1)
        {
            return Emit(UxFeedback.Error("set_unit_cap",
                $"cap must be -1 (unlimited) or >= 0, got {UnitCap}", "set_unit_cap"));
        }
        var ok = await _dispatcher.SetUnitCapOverrideAsync(TargetSlot, UnitCap, ct);
        return Emit(ok
            ? UxFeedback.Success("set_unit_cap",
                $"slot={TargetSlot} cap → {(UnitCap == -1 ? "∞" : UnitCap.ToString())}",
                "set_unit_cap")
            : UxFeedback.Error("set_unit_cap", "bridge rejected", "set_unit_cap"));
    }

    public async Task<UxFeedback> ClearUnitCapAsync(CancellationToken ct = default)
    {
        if (TargetSlot < 0)
        {
            return Emit(UxFeedback.Error("clear_unit_cap",
                "slot must be >= 0", "clear_unit_cap"));
        }
        var ok = await _dispatcher.ClearUnitCapOverrideAsync(TargetSlot, ct);
        return Emit(ok
            ? UxFeedback.Success("clear_unit_cap",
                $"slot={TargetSlot} cap reverted to engine default", "clear_unit_cap")
            : UxFeedback.Error("clear_unit_cap", "bridge rejected", "clear_unit_cap"));
    }

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

public interface IBattleControlDispatcher
{
    Task<bool> SetFreezeAiAsync(int slot, bool enable, CancellationToken ct);
    Task<bool> KillAllEnemiesAsync(CancellationToken ct);
    Task<bool> HealAllLocalAsync(CancellationToken ct);
    Task<bool> SetUnitCapOverrideAsync(int slot, int cap, CancellationToken ct);
    Task<bool> ClearUnitCapOverrideAsync(int slot, CancellationToken ct);
}
