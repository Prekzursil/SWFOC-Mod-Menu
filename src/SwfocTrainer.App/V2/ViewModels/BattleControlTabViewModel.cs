using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Battle Control tab) — INPC wrapper around
/// BattleControlTabState. Content-creator one-click commands: freeze AI per
/// slot (toggle with cleanup), kill all enemies (composed Lua loop),
/// heal all local, set unit cap, clear unit cap.
/// </summary>
public sealed class BattleControlTabViewModel : ObservableBase
{
    private readonly BattleControlTabState _state;
    private readonly RecordingFeedbackSink _sink;
    private readonly FeatureToggleCoordinator _toggles;

    private int _targetSlot = -1;
    private int _unitCap = 100;
    private string _lastStatus = "(idle)";

    public BattleControlTabViewModel(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _sink = new RecordingFeedbackSink();
        _toggles = new FeatureToggleCoordinator(_sink);
        var dispatcher = new BridgeBattleControlDispatcher(bridge);
        _state = new BattleControlTabState(dispatcher, _sink, _toggles);

        ToggleFreezeAiCommand = new AsyncRelayCommand(ToggleFreezeAiCore, onError: HandleError);
        // 2026-04-27: KillAll wraps the core in a confirmation prompt
        // since the agent A audit flagged it as "destructive, easy to fire
        // accidentally". Heal-all stays uncomfirmed (it's safe).
        KillAllEnemiesCommand = new AsyncRelayCommand(KillAllEnemiesWithConfirmAsync, onError: HandleError);
        HealAllLocalCommand = new AsyncRelayCommand(HealAllLocalCore, onError: HandleError);
        SetUnitCapCommand = new AsyncRelayCommand(SetUnitCapCore, () => false, HandleError);
        ClearUnitCapCommand = new AsyncRelayCommand(ClearUnitCapCore, () => false, HandleError);
        // 2026-04-27: composite "Instant win" — heal all local first
        // (so the operator's units don't die mid-rampage), then kill all
        // enemies. Identical to two manual clicks in sequence; the value
        // is one-button discoverability for content creators / streamers.
        InstantWinCommand = new AsyncRelayCommand(InstantWinCore, onError: HandleError);

        // 2026-04-27 (iter 55): per-button capability metadata. Each action
        // gets a badge derived from the catalog so the operator sees
        // LIVE / PHASE 2 PENDING per button instead of just a tab-level
        // "MIXED" rollup. Freeze AI routes through the live Suspend_AI Lua
        // helper; Set/ClearUnitCap stay disabled until the engine hook lands.
        ToggleFreezeAi = new CapabilityAwareAction("Toggle freeze AI", "SWFOC_SuspendAiLua");
        KillAllEnemies = new CapabilityAwareAction("Kill all enemies", "SWFOC_ListTacticalUnits", "SWFOC_KillUnit");
        HealAllLocal = new CapabilityAwareAction("Heal all local", "SWFOC_HealAllLocal");
        InstantWin = new CapabilityAwareAction("Instant win",
            "SWFOC_HealAllLocal", "SWFOC_ListTacticalUnits", "SWFOC_KillUnit");
        SetUnitCap = new CapabilityAwareAction("Set unit cap override", "SWFOC_SetUnitCapOverride");
        ClearUnitCap = new CapabilityAwareAction("Clear unit cap override", "SWFOC_SetUnitCapOverride");
    }

    public CapabilityAwareAction ToggleFreezeAi { get; }
    public CapabilityAwareAction KillAllEnemies { get; }
    public CapabilityAwareAction HealAllLocal { get; }
    public CapabilityAwareAction InstantWin { get; }
    public CapabilityAwareAction SetUnitCap { get; }
    public CapabilityAwareAction ClearUnitCap { get; }

    public IReadOnlyList<CapabilityAwareAction> AllActions => new[]
    {
        ToggleFreezeAi, KillAllEnemies, HealAllLocal, InstantWin, SetUnitCap, ClearUnitCap,
    };

    /// <summary>
    /// True when the tab contains both LIVE and non-LIVE actions. Drives
    /// the tab-level amber banner — operator must understand which
    /// buttons currently have engine effect vs. which are Phase-1-mirror.
    /// </summary>
    public bool HasPhase2PendingAction =>
        AllActions.Any(a => !a.IsAllLive);

    public string Phase2PendingWarning
    {
        get
        {
            var pending = AllActions.Where(a => !a.IsAllLive).ToList();
            if (pending.Count == 0) return string.Empty;
            var parts = pending.Select(a => $"{a.Name} ({a.Badge})");
            return "⚠ Some actions on this tab are PHASE 2 PENDING and are disabled "
                + "until a live engine hook exists. Affected: "
                + string.Join("; ", parts);
        }
    }

    private async Task InstantWinCore()
    {
        // 2026-04-27: same confirmation as the bare Kill-all path. The
        // composite is even more destructive (heals locals + kills every
        // enemy), so an accidental click is high-impact.
        if (!ConfirmDestructive(
            "Instant win?",
            "This will heal every local unit AND kill every enemy on the current battle. Proceed?"))
        {
            LastStatus = "Instant win cancelled.";
            return;
        }
        await HealAllLocalCore().ConfigureAwait(true);
        await KillAllEnemiesCore().ConfigureAwait(true);
    }

    private async Task KillAllEnemiesWithConfirmAsync()
    {
        if (!ConfirmDestructive(
            "Kill all enemies?",
            "This sends a kill command to every non-local unit on the current battle. Proceed?"))
        {
            LastStatus = "Kill-all cancelled.";
            return;
        }
        await KillAllEnemiesCore().ConfigureAwait(true);
    }

    /// <summary>
    /// 2026-04-27: shared confirmation prompt. The class is sealed today;
    /// kept private/non-virtual but factored into a single method so a
    /// future refactor that needs to inject a fake confirm only has to
    /// touch one place. WPF's MessageBox needs a UI dispatcher so direct
    /// unit tests on this method aren't useful — exercised via the
    /// go-live smoke checklist instead.
    /// </summary>
    private static bool ConfirmDestructive(string title, string body)
    {
        var result = System.Windows.MessageBox.Show(
            body,
            title,
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No /* default = cancel for safety */);
        return result == System.Windows.MessageBoxResult.Yes;
    }

    public int TargetSlot
    {
        get => _targetSlot;
        set
        {
            if (SetField(ref _targetSlot, value))
            {
                _state.TargetSlot = value;
                OnPropertyChanged(nameof(IsFreezeAiEnabled));
            }
        }
    }

    public int UnitCap
    {
        get => _unitCap;
        set
        {
            if (SetField(ref _unitCap, value))
            {
                _state.UnitCap = value;
                OnPropertyChanged(nameof(UnitCapHint));
            }
        }
    }

    /// <summary>
    /// 2026-04-27: live hint for the cap input. Renders an empty string
    /// for sane values, a warning otherwise. The XAML binds a faded
    /// TextBlock next to the cap textbox so the operator sees what their
    /// value will actually mean before clicking Apply.
    /// </summary>
    public string UnitCapHint => BuildUnitCapHint(_unitCap);

    /// <summary>
    /// 2026-04-27 (iter 14): pure-static formatter so tests can pin the
    /// hint shape without constructing the full VM (which needs a real
    /// V2BridgeAdapter via DI).
    /// </summary>
    internal static string BuildUnitCapHint(int unitCap) => unitCap switch
    {
        -1 => "(unlimited — cap removed)",
        0 => "⚠ 0 means no units may exist; the engine treats this as 'wipe everything that tries to spawn'",
        < -1 => "⚠ negative cap (other than -1) is undefined behaviour",
        > 9999 => "(very high — engine may struggle with > 10k units per slot)",
        _ => $"({unitCap} units max per slot)",
    };

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge(
        "SWFOC_SuspendAiLua", "SWFOC_HealAllLocal", "SWFOC_KillUnit", "SWFOC_SetUnitCapOverride");

    public bool IsFreezeAiEnabled => _toggles.IsEnabled($"freeze_ai_slot{_targetSlot}");

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand ToggleFreezeAiCommand { get; }
    public ICommand KillAllEnemiesCommand { get; }
    public ICommand HealAllLocalCommand { get; }
    /// <summary>
    /// 2026-04-27: composite "Instant win" — Heal all local + Kill all
    /// enemies in sequence. Order matters: heal first so the operator's
    /// units survive any ongoing damage during the kill sweep.
    /// </summary>
    public ICommand InstantWinCommand { get; }
    public ICommand SetUnitCapCommand { get; }
    public ICommand ClearUnitCapCommand { get; }

    private async Task ToggleFreezeAiCore()
    {
        var next = !IsFreezeAiEnabled;
        ApplyFeedback(await _state.ToggleFreezeAiAsync(next));
        OnPropertyChanged(nameof(IsFreezeAiEnabled));
    }

    private async Task KillAllEnemiesCore() => ApplyFeedback(await _state.KillAllEnemiesAsync());
    private async Task HealAllLocalCore() => ApplyFeedback(await _state.HealAllLocalAsync());
    private async Task SetUnitCapCore() => ApplyFeedback(await _state.SetUnitCapAsync());
    private async Task ClearUnitCapCore() => ApplyFeedback(await _state.ClearUnitCapAsync());

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
