using System.Globalization;
using System.Windows;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Cross-Faction Recruitment tab) — INPC wrapper around
/// CrossFactionRecruitmentState. The operator enters the source obj_addr
/// (decimal or hex), the source slot (for safety classification — must be
/// LocalPlayer-marked), and the target slot. The State enforces:
///   - source must exist and be local-owned (IsLocal=true)
///   - target slot must be 0..N
///   - source.OwnerSlot != target slot (no-op rejected)
/// </summary>
public sealed class CrossFactionRecruitmentTabViewModel : ObservableBase
{
    private readonly V2BridgeAdapter _bridge;
    private readonly CrossFactionRecruitmentState _state;
    private readonly RecordingFeedbackSink _sink;

    private string _objAddrInput = "0";
    private int _sourceOwnerSlot = -1;
    private int _targetSlot = -1;
    private bool _sourceIsLocal = true;
    private string _lastStatus = "(idle — paste a unit obj_addr and click Recruit)";

    public CrossFactionRecruitmentTabViewModel(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;
        _sink = new RecordingFeedbackSink();
        var dispatcher = new BridgeCrossFactionDispatcher(bridge);
        _state = new CrossFactionRecruitmentState(dispatcher, _sink);

        RecruitCommand = new AsyncRelayCommand(RecruitCore, onError: HandleError);
        // 2026-04-27: workflow shortcut — read SWFOC_GetSelectedUnit and
        // pre-fill the source obj_addr + owner-slot fields. Saves the
        // operator from copying hex pointers between tabs.
        AutoFillFromSelectedCommand = new AsyncRelayCommand(
            AutoFillFromSelectedCore, onError: HandleError);

        // 2026-04-27 (iter 60): per-button capability metadata. Recruit
        // routes through SWFOC_DoString to call Switch_Sides per-unit
        // (engine-native; LIVE). AutoFill reads SWFOC_GetSelectedUnit
        // (LIVE).
        Recruit = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Recruit cross-faction", "SWFOC_DoString");
        AutoFillFromSelected = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Auto-fill from selected", "SWFOC_GetSelectedUnit");
    }

    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction Recruit { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction AutoFillFromSelected { get; }
    public IReadOnlyList<SwfocTrainer.Core.Diagnostics.CapabilityAwareAction> AllActions =>
        new[] { Recruit, AutoFillFromSelected };

    private async Task AutoFillFromSelectedCore()
    {
        var round = await _bridge
            .SendRawAsync("return SWFOC_GetSelectedUnit()", CancellationToken.None)
            .ConfigureAwait(true);
        if (!round.Succeeded || string.IsNullOrEmpty(round.Response))
        {
            LastStatus = $"AutoFill: bridge error — {round.ErrorMessage ?? "no response"}";
            return;
        }
        // Wire format: "addr_decimal,owner_slot,is_local" (compatible with
        // SWFOC_GetSelectedUnit. Falls back to single-int form for older
        // bridge builds.)
        var parts = round.Response.Split(',');
        if (!long.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var addr) || addr <= 0)
        {
            LastStatus = $"AutoFill: nothing selected in-game (got '{round.Response}').";
            return;
        }
        ObjAddrInput = addr.ToString(CultureInfo.InvariantCulture);
        if (parts.Length >= 2 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var slot))
        {
            SourceOwnerSlot = slot;
        }
        if (parts.Length >= 3 && int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var local))
        {
            SourceIsLocal = local != 0;
        }
        LastStatus = $"AutoFill: addr=0x{addr:X}, slot={SourceOwnerSlot}, local={SourceIsLocal}.";
    }

    /// <summary>
    /// 2026-04-27: pulls the in-game selection and pre-fills the source
    /// obj_addr / owner-slot / is-local fields. Eliminates the
    /// copy-hex-from-Tactical-Units-paste-here workflow.
    /// </summary>
    public ICommand AutoFillFromSelectedCommand { get; }

    public string ObjAddrInput
    {
        get => _objAddrInput;
        set
        {
            if (SetField(ref _objAddrInput, value ?? "0"))
            {
                RebuildSourceUnit();
                OnPropertyChanged(nameof(RecruitPreview));
                OnPropertyChanged(nameof(WarningVisibility));
                OnPropertyChanged(nameof(InfoVisibility));
            }
        }
    }

    public int SourceOwnerSlot
    {
        get => _sourceOwnerSlot;
        set
        {
            if (SetField(ref _sourceOwnerSlot, value))
            {
                RebuildSourceUnit();
                OnPropertyChanged(nameof(RecruitPreview));
                OnPropertyChanged(nameof(WarningVisibility));
                OnPropertyChanged(nameof(InfoVisibility));
            }
        }
    }

    public int TargetSlot
    {
        get => _targetSlot;
        set
        {
            if (SetField(ref _targetSlot, value))
            {
                _state.TargetSlot = value;
                OnPropertyChanged(nameof(RecruitPreview));
                OnPropertyChanged(nameof(WarningVisibility));
                OnPropertyChanged(nameof(InfoVisibility));
            }
        }
    }

    /// <summary>
    /// 2026-04-27: live preview that surfaces no-op / invalid-input
    /// conditions BEFORE the operator clicks Recruit. The State still
    /// enforces the same rules (source must be local, target != source);
    /// this just lets the operator see what's wrong without round-tripping.
    /// </summary>
    public string RecruitPreview =>
        BuildRecruitPreview(_objAddrInput, _sourceOwnerSlot, _targetSlot, _sourceIsLocal);

    /// <summary>
    /// 2026-04-27 (iter 14): pure-static formatter so tests can pin the
    /// preview string for every input combo without constructing the VM
    /// (which needs a real V2BridgeAdapter via DI).
    /// </summary>
    internal static string BuildRecruitPreview(
        string objAddrInput, int sourceOwnerSlot, int targetSlot, bool sourceIsLocal)
    {
        long.TryParse(objAddrInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var addr);
        if (addr <= 0)
        {
            long.TryParse(objAddrInput, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr);
        }
        if (addr <= 0)
        {
            return "Source obj_addr is empty / invalid — paste a decimal or hex pointer.";
        }
        if (sourceOwnerSlot < 0)
        {
            return $"Source addr=0x{addr:X} (slot unknown — set source-owner-slot for safety classification).";
        }
        if (targetSlot < 0)
        {
            return $"Source addr=0x{addr:X}, slot {sourceOwnerSlot} → target slot not set yet.";
        }
        if (sourceOwnerSlot == targetSlot)
        {
            return $"⚠ source.OwnerSlot ({sourceOwnerSlot}) == target ({targetSlot}); recruitment will be a no-op.";
        }
        if (!sourceIsLocal)
        {
            return $"⚠ Source flagged as NOT local; the State will reject it (use Tactical Units to confirm).";
        }
        return $"Will transfer addr=0x{addr:X} from slot {sourceOwnerSlot} → slot {targetSlot}.";
    }

    /// <summary>
    /// Visibility for the warning banner. Visible when the preview starts
    /// with a "⚠" or input-empty marker, Collapsed otherwise. Exposing
    /// Visibility directly here avoids needing a BooleanToVisibilityConverter
    /// in app resources.
    /// </summary>
    public Visibility WarningVisibility
        => IsWarning ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>Visibility complement of <see cref="WarningVisibility"/>.</summary>
    public Visibility InfoVisibility
        => IsWarning ? Visibility.Collapsed : Visibility.Visible;

    private bool IsWarning => RecruitPreview.StartsWith("⚠", StringComparison.Ordinal)
                           || RecruitPreview.StartsWith("Source obj_addr", StringComparison.Ordinal);

    public bool SourceIsLocal
    {
        get => _sourceIsLocal;
        set
        {
            if (SetField(ref _sourceIsLocal, value))
            {
                RebuildSourceUnit();
                OnPropertyChanged(nameof(RecruitPreview));
                OnPropertyChanged(nameof(WarningVisibility));
                OnPropertyChanged(nameof(InfoVisibility));
            }
        }
    }

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge("SWFOC_DoString");

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand RecruitCommand { get; }

    private void RebuildSourceUnit()
    {
        long.TryParse(_objAddrInput, NumberStyles.Integer, CultureInfo.InvariantCulture, out var addr);
        if (addr <= 0)
        {
            long.TryParse(_objAddrInput, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr);
        }
        _state.SourceUnit = addr > 0
            ? new TacticalUnitRow(
                ObjAddr: addr,
                OwnerSlot: _sourceOwnerSlot,
                Hull: 0f,
                InvulnFlag: 0,
                PreventDeath: 0,
                IsLocal: _sourceIsLocal,
                IsSelected: false)
            : null;
    }

    private async Task RecruitCore() => ApplyFeedback(await _state.RecruitAsync());

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
