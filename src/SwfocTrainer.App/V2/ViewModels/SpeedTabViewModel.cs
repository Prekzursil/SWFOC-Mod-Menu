using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Speed tab) — INPC wrapper around SpeedTabState.
/// Three apply-on-click commands for the three speed surfaces (global game
/// tick, per-faction locomotor multiplier, per-unit speed).
/// </summary>
public sealed class SpeedTabViewModel : ObservableBase
{
    private readonly SpeedTabState _state;
    private readonly RecordingFeedbackSink _sink;

    private float _globalGameSpeed = 1.0f;
    private int _factionSlot = -1;
    private float _factionMoveSpeedMultiplier = 1.0f;
    private long _selectedObjAddr;
    private float _unitSpeed = 5.0f;
    private string _lastStatus = "(idle)";

    public SpeedTabViewModel(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _sink = new RecordingFeedbackSink();
        var dispatcher = new BridgeSpeedDispatcher(bridge);
        _state = new SpeedTabState(dispatcher, _sink);

        SetGameSpeedCommand = new AsyncRelayCommand(SetGameSpeedCore, () => false, HandleError);
        SetFactionSpeedCommand = new AsyncRelayCommand(SetFactionSpeedCore, onError: HandleError);
        SetUnitSpeedCommand = new AsyncRelayCommand(SetUnitSpeedCore, onError: HandleError);
        // 2026-04-28 (iter 100): revert per-unit override.
        ClearUnitSpeedOverrideCommand = new AsyncRelayCommand(
            ClearUnitSpeedOverrideCore, onError: HandleError);

        // 2026-04-27: quick presets (Pause / Slow / Real / Fast / Faster / Hyper).
        // Each AsyncRelayCommand wraps the same SetGlobalGameSpeed flow so the
        // operator gets one-click access to common speed settings without
        // typing into the textbox.
        ApplyPauseCommand = new AsyncRelayCommand(() => ApplyPresetAsync(0.0f), () => false, HandleError);
        ApplyHalfCommand = new AsyncRelayCommand(() => ApplyPresetAsync(0.5f), () => false, HandleError);
        ApplyRealtimeCommand = new AsyncRelayCommand(() => ApplyPresetAsync(1.0f), () => false, HandleError);
        Apply2xCommand = new AsyncRelayCommand(() => ApplyPresetAsync(2.0f), () => false, HandleError);
        Apply4xCommand = new AsyncRelayCommand(() => ApplyPresetAsync(4.0f), () => false, HandleError);

        // 2026-04-28 (iter 77): per-faction and per-unit speed presets.
        // Mirrors iter 76's Combat preset pattern. The existing global
        // game speed presets above sit on the SetGameSpeed surface; these
        // four sit on the per-faction multiplier surface and another four
        // on the per-unit speed surface. All 8 fire the existing
        // SetFactionSpeedCore / SetUnitSpeedCore so the operator gets the
        // same status feedback as a manual click. The underlying
        // SWFOC_SetPerFactionSpeedMultiplier and SWFOC_SetUnitSpeed
        // helpers are PHASE 2 PENDING — replay-mirror today, real engine
        // effect once the IDA pin completes.
        ApplyFactionSnailCommand = new AsyncRelayCommand(() => ApplyFactionPresetAsync(0.25f), onError: HandleError);
        ApplyFactionSlowCommand = new AsyncRelayCommand(() => ApplyFactionPresetAsync(0.5f), onError: HandleError);
        ApplyFactionNormalCommand = new AsyncRelayCommand(() => ApplyFactionPresetAsync(1.0f), onError: HandleError);
        ApplyFactionFastCommand = new AsyncRelayCommand(() => ApplyFactionPresetAsync(2.0f), onError: HandleError);

        ApplyUnitSlowCommand = new AsyncRelayCommand(() => ApplyUnitPresetAsync(2.5f), onError: HandleError);
        ApplyUnitNormalCommand = new AsyncRelayCommand(() => ApplyUnitPresetAsync(5.0f), onError: HandleError);
        ApplyUnitFastCommand = new AsyncRelayCommand(() => ApplyUnitPresetAsync(10.0f), onError: HandleError);
        ApplyUnitSprintCommand = new AsyncRelayCommand(() => ApplyUnitPresetAsync(20.0f), onError: HandleError);

        // 2026-04-27 (iter 56): per-button capability metadata for the 3
        // speed surfaces. All 3 are PHASE 2 PENDING today (Phase-1-mirror
        // only). The amber tab-level banner makes that explicit alongside
        // the per-button stacked badges.
        SetGameSpeed = new CapabilityAwareAction("Set global game speed", "SWFOC_SetGameSpeed");
        SetFactionSpeed = new CapabilityAwareAction("Set faction move-speed multiplier",
            "SWFOC_SetPerFactionSpeedMultiplier");
        SetUnitSpeed = new CapabilityAwareAction("Set unit speed", "SWFOC_SetUnitSpeed");
        // 2026-04-28 (iter 100): revert helper LIVE-wired.
        ClearUnitSpeedOverride = new CapabilityAwareAction(
            "Revert unit speed override", "SWFOC_ClearUnitSpeedOverride");
    }

    public CapabilityAwareAction SetGameSpeed { get; }
    public CapabilityAwareAction SetFactionSpeed { get; }
    public CapabilityAwareAction SetUnitSpeed { get; }
    public CapabilityAwareAction ClearUnitSpeedOverride { get; }

    public IReadOnlyList<CapabilityAwareAction> AllActions => new[]
    {
        SetGameSpeed, SetFactionSpeed, SetUnitSpeed, ClearUnitSpeedOverride,
    };

    public bool HasPhase2PendingAction => AllActions.Any(a => !a.IsAllLive);

    public string Phase2PendingWarning
    {
        get
        {
            var pending = AllActions.Where(a => !a.IsAllLive).ToList();
            if (pending.Count == 0) return string.Empty;
            var parts = pending.Select(a => $"{a.Name} ({a.Badge})");
            return "Some actions on this tab are PHASE 2 PENDING; their buttons are disabled "
                + "until a live engine hook exists. Affected: "
                + string.Join("; ", parts);
        }
    }

    private async Task ApplyPresetAsync(float value)
    {
        GlobalGameSpeed = value; // updates the textbox + the state via the setter
        await SetGameSpeedCore().ConfigureAwait(true);
    }

    /// <summary>
    /// 2026-04-28 (iter 77): per-faction speed preset. Sets the bound
    /// FactionMoveSpeedMultiplier then fires the existing
    /// SetFactionSpeedCore so the operator gets the same status feedback
    /// as a manual click. Uses the currently-selected FactionSlot — the
    /// operator must pick a faction first; otherwise the dispatcher
    /// fires with FactionSlot=-1 (caller's responsibility).
    /// </summary>
    internal async Task ApplyFactionPresetAsync(float multiplier)
    {
        FactionMoveSpeedMultiplier = multiplier;
        await SetFactionSpeedCore().ConfigureAwait(true);
    }

    /// <summary>
    /// 2026-04-28 (iter 77): per-unit speed preset. Sets the bound
    /// UnitSpeed then fires the existing SetUnitSpeedCore. Operator must
    /// pick an obj_addr first; the SelectedObjAddr=0 case will result in
    /// a no-op-flavored Lua call but not crash.
    /// </summary>
    internal async Task ApplyUnitPresetAsync(float speed)
    {
        UnitSpeed = speed;
        await SetUnitSpeedCore().ConfigureAwait(true);
    }

    public float GlobalGameSpeed
    {
        get => _globalGameSpeed;
        set { if (SetField(ref _globalGameSpeed, value)) _state.GlobalGameSpeed = value; }
    }

    public int FactionSlot
    {
        get => _factionSlot;
        set { if (SetField(ref _factionSlot, value)) _state.FactionSlot = value; }
    }

    public float FactionMoveSpeedMultiplier
    {
        get => _factionMoveSpeedMultiplier;
        set { if (SetField(ref _factionMoveSpeedMultiplier, value)) _state.FactionMoveSpeedMultiplier = value; }
    }

    public long SelectedObjAddr
    {
        get => _selectedObjAddr;
        set { if (SetField(ref _selectedObjAddr, value)) _state.SelectedObjAddr = value; }
    }

    public float UnitSpeed
    {
        get => _unitSpeed;
        set { if (SetField(ref _unitSpeed, value)) _state.UnitSpeed = value; }
    }

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge(
        "SWFOC_SetGameSpeed", "SWFOC_SetPerFactionSpeedMultiplier",
        "SWFOC_SetUnitSpeed", "SWFOC_ClearUnitSpeedOverride");

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand SetGameSpeedCommand { get; }
    public ICommand SetFactionSpeedCommand { get; }
    public ICommand SetUnitSpeedCommand { get; }

    /// <summary>
    /// 2026-04-28 (iter 100): revert per-unit override. Calls
    /// SWFOC_ClearUnitSpeedOverride(SelectedObjAddr).
    /// </summary>
    public ICommand ClearUnitSpeedOverrideCommand { get; }

    /// <summary>2026-04-27: one-click "Pause" preset (sets game speed to 0).</summary>
    public ICommand ApplyPauseCommand { get; }
    /// <summary>2026-04-27: one-click "0.5x" slow-mo preset.</summary>
    public ICommand ApplyHalfCommand { get; }
    /// <summary>2026-04-27: one-click "1.0x" real-time preset.</summary>
    public ICommand ApplyRealtimeCommand { get; }
    /// <summary>2026-04-27: one-click "2x" fast-forward preset.</summary>
    public ICommand Apply2xCommand { get; }
    /// <summary>2026-04-27: one-click "4x" hyper preset.</summary>
    public ICommand Apply4xCommand { get; }

    /// <summary>2026-04-28 (iter 77): per-faction "Snail" (0.25×) preset — picks the currently-selected faction slot.</summary>
    public ICommand ApplyFactionSnailCommand { get; }
    /// <summary>2026-04-28 (iter 77): per-faction "Slow" (0.5×) preset.</summary>
    public ICommand ApplyFactionSlowCommand { get; }
    /// <summary>2026-04-28 (iter 77): per-faction "Normal" (1.0×) preset — canonical reset.</summary>
    public ICommand ApplyFactionNormalCommand { get; }
    /// <summary>2026-04-28 (iter 77): per-faction "Fast" (2.0×) preset.</summary>
    public ICommand ApplyFactionFastCommand { get; }

    /// <summary>2026-04-28 (iter 77): per-unit "Slow" (2.5) preset — slowed infantry feel for the selected obj_addr.</summary>
    public ICommand ApplyUnitSlowCommand { get; }
    /// <summary>2026-04-28 (iter 77): per-unit "Normal" (5.0) preset — default unit speed.</summary>
    public ICommand ApplyUnitNormalCommand { get; }
    /// <summary>2026-04-28 (iter 77): per-unit "Fast" (10.0) preset — speeder-bike feel.</summary>
    public ICommand ApplyUnitFastCommand { get; }
    /// <summary>2026-04-28 (iter 77): per-unit "Sprint" (20.0) preset — vehicle stress test.</summary>
    public ICommand ApplyUnitSprintCommand { get; }

    private async Task SetGameSpeedCore() => ApplyFeedback(await _state.SetGlobalGameSpeedAsync());
    private async Task SetFactionSpeedCore() => ApplyFeedback(await _state.SetFactionMoveSpeedAsync());
    private async Task SetUnitSpeedCore() => ApplyFeedback(await _state.SetUnitSpeedAsync());
    private async Task ClearUnitSpeedOverrideCore() =>
        ApplyFeedback(await _state.ClearUnitSpeedOverrideAsync());

    private void ApplyFeedback(UxFeedback fb)
    {
        LastStatus = string.Format(CultureInfo.InvariantCulture,
            "{0}: {1} — {2}", fb.Severity, fb.Title, fb.Message);
    }

    private void HandleError(Exception ex)
    {
        LastStatus = $"command failed: {ex.Message}";
    }
}
