using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Unit Stat Editor tab) — INPC wrapper around
/// UnitStatEditorState. Composed flow: stage (field, value) edits, point at
/// a list of obj_addrs (comma- or newline-separated), apply all in one click.
///
/// The selection model is owned by the VM (independent from the
/// TacticalUnits tab) — operators paste obj_addrs from the Tactical Units
/// tab into TargetObjAddrsInput. All pasted addresses are assumed local
/// for the safety filter; the operator is responsible for not pasting
/// enemy obj_addrs.
/// </summary>
public sealed class UnitStatEditorTabViewModel : ObservableBase
{
    private readonly UnitStatEditorState _state;
    private readonly RecordingFeedbackSink _sink;
    private readonly TacticalUnitSelection _selection;
    private readonly ObservableCollection<StatEdit> _staged = new();

    private string _editField = "hull";
    private float _editValue;
    private string _targetObjAddrsInput = string.Empty;
    private string _lastStatus = "(idle)";

    public UnitStatEditorTabViewModel(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _sink = new RecordingFeedbackSink();
        _selection = new TacticalUnitSelection();
        var dispatcher = new BridgeUnitStatEditDispatcher(bridge);
        _state = new UnitStatEditorState(dispatcher, _sink, _selection);

        StageEditCommand = new RelayCommand(StageEdit);
        ClearStagedCommand = new RelayCommand(ClearStaged);
        ApplyAllCommand = new AsyncRelayCommand(ApplyAllCore, onError: HandleError);

        // 2026-04-27 (iter 60): per-button capability metadata. Apply
        // routes through SWFOC_SetUnitField — Phase-1-mirror only
        // (PHASE 2 PENDING). Stage/Clear are pure VM-state ops.
        ApplyAll = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Apply staged edits", "SWFOC_SetUnitField");
    }

    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ApplyAll { get; }
    public IReadOnlyList<SwfocTrainer.Core.Diagnostics.CapabilityAwareAction> AllActions =>
        new[] { ApplyAll };

    public bool HasPhase2PendingAction => AllActions.Any(a => !a.IsAllLive);

    public string Phase2PendingWarning
    {
        get
        {
            var pending = AllActions.Where(a => !a.IsAllLive).ToList();
            if (pending.Count == 0) return string.Empty;
            var parts = pending.Select(a => $"{a.Name} ({a.Badge})");
            return "⚠ Apply staged edits is PHASE 2 PENDING — Phase-1-mirror only. "
                + "State: " + string.Join("; ", parts);
        }
    }

    public string EditField
    {
        get => _editField;
        set => SetField(ref _editField, value ?? "hull");
    }

    public float EditValue
    {
        get => _editValue;
        set => SetField(ref _editValue, value);
    }

    public string TargetObjAddrsInput
    {
        get => _targetObjAddrsInput;
        set
        {
            if (SetField(ref _targetObjAddrsInput, value ?? string.Empty))
            {
                RebuildSelection();
            }
        }
    }

    // 12 of the 13 SWFOC_SetUnitField sub-fields. Routes through
    // BridgeUnitStatEditDispatcher → SWFOC_SetUnitField wire format.
    //
    // LIVE branches (engine-effective writes):
    //   hull / shield / speed (iter 136)
    //   invuln_flag / prevent_death (iter 243; display-only direct writes —
    //     pair with iter-110 SWFOC_MakeInvulnerableLua + iter-153
    //     SWFOC_SetCannotBeKilledLua for engine-state-aware paths).
    //   max_hull / max_shield (iter 258; TYPE-LEVEL writes via GameObj+0x298 →
    //     UnitType chain — affects EVERY unit of this type for the session,
    //     NOT per-instance. Operator should be aware that buff/nerf is
    //     global-by-type. The staging UI input fields already existed since
    //     iter-245 as Phase-1 mirrors; iter-258 promoted the bridge branches
    //     to LIVE without touching the UI. Iter-260 verification iter pins
    //     this seamless promotion.).
    //
    // Phase-1 mirror with HONEST DEFER (iter 267-268 — semantic verification per
    // iter-256 memory rule confirmed no TYPE-LEVEL max_speed offset; Override_Max_Speed
    // @ 0x57E590 walks unit+0x60 locomotor NOT unit+0x298 UnitType; iter-99
    // SWFOC_SetUnitSpeed + iter-100 SWFOC_SetPerFactionSpeedMultiplier already cover
    // per-instance + per-faction LIVE; routing max_speed through this dispatcher would
    // sacrifice iter-258 TYPE-LEVEL semantic consistency):
    //   max_speed.
    //
    // Phase-1 mirror with HONEST DEFER (iter 269-270 — semantic verification per
    // iter-256 memory rule EMPIRICALLY REAFFIRMED iter-94's rejection: combat path has
    // NO central per-unit attack_power read site; HardpointFire @ 0x387F50 inspection
    // shows param_1+0x28 is the hardpoint HP CONSUMER and damage is param_4 PASSED IN,
    // computed dynamically from per-weapon XML attributes at fire time. Operator has
    // 3 LIVE alternatives covering distinct damage scopes (alternative-set pattern):
    // iter-96 SWFOC_SetDamageMultiplierGlobal (global outgoing via Take_Damage_Outer
    // detour), iter-154 SWFOC_SetDamageModifierLua (per-instance via Set_Damage_Modifier
    // engine API), iter-225 SWFOC_SetFireRateMultiplierGlobal (global fire-rate via
    // WeaponTick detour). Adding a 4th attack_power LIVE branch would not add operator
    // capability and would sacrifice iter-258 TYPE-LEVEL semantic consistency):
    //   attack_power.
    //
    // Phase-1 mirror only (queued, no engine effect; pending future RTTI offset arcs):
    //   respawn_ms / is_hero / respawn_enabled.
    //
    // owner_slot is INTENTIONALLY EXCLUDED per iter-242 design:
    //   Direct write of GameObj+0x58 bypasses Change_Owner @ 0x574D0E +
    //   selection-list update + AI brain reassignment + UI roster refresh.
    //   Operator MUST use iter-108 SWFOC_ChangeUnitOwnerLua for engine-aware
    //   ownership change. Excluding owner_slot from this list prevents the
    //   "stage owner_slot edit + Apply" pattern that would silently desync
    //   ownership-derived caches. Iter 245 staging-UI verification iter
    //   pins this exclusion.
    public IReadOnlyList<string> EditFieldOptions { get; } = new[]
    {
        "hull", "max_hull", "shield", "max_shield", "speed", "max_speed",
        "attack_power", "respawn_ms", "invuln_flag", "prevent_death",
        "is_hero", "respawn_enabled",
    };

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge("SWFOC_SetUnitField");

    public ObservableCollection<StatEdit> Staged => _staged;

    public int SelectedUnitCount => _selection.SelectedRows.Count;

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand StageEditCommand { get; }
    public ICommand ClearStagedCommand { get; }
    public ICommand ApplyAllCommand { get; }

    private void StageEdit()
    {
        ApplyFeedback(_state.StageEdit(_editField, _editValue));
        RefreshStaged();
    }

    private void ClearStaged()
    {
        ApplyFeedback(_state.ClearStaged());
        RefreshStaged();
    }

    private async Task ApplyAllCore() => ApplyFeedback(await _state.ApplyAllAsync());

    private void RebuildSelection()
    {
        var rows = new List<TacticalUnitRow>();
        var separators = new[] { ',', ' ', '\n', '\r', '\t' };
        var addrSet = new HashSet<long>();
        foreach (var token in _targetObjAddrsInput.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            long addr;
            if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out addr))
            {
                long.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr);
            }
            if (addr > 0 && addrSet.Add(addr))
            {
                rows.Add(new TacticalUnitRow(
                    ObjAddr: addr,
                    OwnerSlot: -1,
                    Hull: 0f,
                    InvulnFlag: 0,
                    PreventDeath: 0,
                    IsLocal: true,        // operator-asserted; cross-faction edits go through CrossFactionRecruitment
                    IsSelected: true));
            }
        }
        _selection.LoadRows(rows);
        _selection.ApplySelection(addrSet);
        OnPropertyChanged(nameof(SelectedUnitCount));
    }

    private void RefreshStaged()
    {
        _staged.Clear();
        foreach (var edit in _state.PendingEdits) _staged.Add(edit);
    }

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
