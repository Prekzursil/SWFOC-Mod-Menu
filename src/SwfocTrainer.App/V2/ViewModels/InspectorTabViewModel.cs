using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Inspector tab) — INPC wrapper around InspectorTabState.
///
/// The operator types a hex obj_addr (or pastes one from the Tactical Units
/// tab) and clicks Refresh. The dispatcher pulls live state from the
/// bridge and we surface every available field — the missing ones (TypeName,
/// MaxHull, MaxShield, MaxSpeed, IsHero) come back zero/empty until the
/// bridge gets an extended inspect helper.
/// </summary>
public sealed class InspectorTabViewModel : ObservableBase, IDisposable
{
    // 2026-04-27 (iter 17): auto-refresh delegated to the shared
    // PeriodicAutoRefreshDriver. 2 sec is fast enough to feel live for
    // hull tracking + slow enough to not hammer the bridge pipe.
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    private readonly InspectorTabState _state;
    private readonly RecordingFeedbackSink _sink;
    private readonly PeriodicAutoRefreshDriver _autoRefresh;
    private readonly V2UnitMutationDispatcher _unitMutator;
    private string _objAddrInput = "0";
    private string _lastStatus = "(idle — paste a unit obj_addr and click Refresh)";
    private bool _isAutoRefreshEnabled;
    // 2026-05-05 (iter 191): default unit Lua expression for the new
    // read-side native UX buttons. Operator can override to anything that
    // resolves to an obj receiver (Find_First_Object("Empire_AT_AT") is a
    // stable hand-tested probe target).
    private string _unitLuaExpr = "Find_First_Object(\"Empire_AT_AT\")";
    // 2026-05-05 (iter 198): second arg field for iter-173 arg-getter wires.
    // Operator types a property/category/ability name OR a target unit Lua
    // expression depending on which button. Default empty so the operator
    // sees the "(no arg)" feedback when they click without typing.
    private string _unitArgExpr = string.Empty;

    public InspectorTabViewModel(V2BridgeAdapter bridge, V2UnitMutationDispatcher unitMutator)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(unitMutator);
        _sink = new RecordingFeedbackSink();
        var dispatcher = new BridgeInspectorDispatcher(bridge);
        _state = new InspectorTabState(dispatcher, _sink);
        _unitMutator = unitMutator;
        RefreshCommand = new AsyncRelayCommand(RefreshCore, onError: HandleError);
        ClearCommand = new RelayCommand(() =>
        {
            _state.Clear();
            ObjAddrInput = "0";
            OnSnapshotChanged();
        });
        // 2026-04-27: copy-snapshot for bug reports + offline analysis.
        CopySnapshotCommand = new RelayCommand(CopySnapshotToClipboard);
        // 2026-04-28 (iter 90): one-click copy of just the obj_addr hex.
        // Operators reading the inspector pane often want to paste the
        // address into another tab (Player State, Combat scalars, etc.)
        // without retyping it. CopySnapshot copies the full blob; this
        // is the focused alternative.
        CopyObjAddrCommand = new RelayCommand(CopyObjAddrToClipboard);
        // 2026-04-27 (iter 17): shared auto-refresh driver.
        _autoRefresh = new PeriodicAutoRefreshDriver(
            interval: TimeSpan.FromSeconds(2),
            refreshAsync: async _ => await RefreshCore().ConfigureAwait(false),
            // Skip refresh when no addr is set — saves a meaningless
            // bridge probe + an "(empty)" status update.
            canRefresh: () => _state.SelectedUnit is not null,
            onError: ex => LastStatus = $"auto-refresh error: {ex.Message}");

        // 2026-04-27 (iter 59): per-button capability metadata. Inspect is
        // RequiresLiveSwfoc — the offline harness can't exercise it because
        // there's no live unit object to read. Operator must have a running
        // game session for Refresh to return real data.
        Refresh = new CapabilityAwareAction("Refresh inspector", "SWFOC_InspectUnit");

        // 2026-05-05 (iter 191): read-side native UX for iter-168/169/170
        // unit-receiver wires — first surfacing of Inspector-class state queries
        // beyond the Refresh snapshot. Each button takes the SAME UnitLuaExpr
        // input (e.g. Find_First_Object("Empire_AT_AT")) and dispatches via the
        // iter-167 unit-getter helper. Result lands in LastStatus.
        ReadUnitTypeLuaCommand = new AsyncRelayCommand(GetTypeLuaAsync, onError: HandleError);
        ReadUnitOwnerLuaCommand = new AsyncRelayCommand(GetOwnerLuaAsync, onError: HandleError);
        ReadHasAttackTargetLuaCommand = new AsyncRelayCommand(HasAttackTargetLuaAsync, onError: HandleError);
        ReadAreEnginesOnlineLuaCommand = new AsyncRelayCommand(AreEnginesOnlineLuaAsync, onError: HandleError);
        ReadUnitTypeLuaAction = new CapabilityAwareAction("Read unit type (Lua)", "SWFOC_GetTypeLua");
        ReadUnitOwnerLuaAction = new CapabilityAwareAction("Read unit owner (Lua)", "SWFOC_GetOwnerLua");
        ReadHasAttackTargetLuaAction = new CapabilityAwareAction("Read has attack target (Lua)", "SWFOC_HasAttackTargetLua");
        ReadAreEnginesOnlineLuaAction = new CapabilityAwareAction("Read engines online (Lua)", "SWFOC_AreEnginesOnlineLua");

        // 2026-05-05 (iter 197): Inspector tab read-side EXTENSION for iter-171/172
        // unit-receiver wires. Same UnitLuaExpr field; 6 more buttons. iter-191
        // GroupBox grows from 4 → 10 buttons total. All 6 are no-arg unit getters.
        ReadParentObjectLuaCommand = new AsyncRelayCommand(GetParentObjectLuaAsync, onError: HandleError);
        ReadAttackTargetLuaCommand = new AsyncRelayCommand(GetAttackTargetLuaAsync, onError: HandleError);
        ReadDamageModifierLuaCommand = new AsyncRelayCommand(GetDamageModifierLuaAsync, onError: HandleError);
        ReadContainedObjectCountLuaCommand = new AsyncRelayCommand(GetContainedObjectCountLuaAsync, onError: HandleError);
        ReadBehaviorIdLuaCommand = new AsyncRelayCommand(GetBehaviorIdLuaAsync, onError: HandleError);
        ReadRateOfFireModifierLuaCommand = new AsyncRelayCommand(GetRateOfFireModifierLuaAsync, onError: HandleError);
        ReadParentObjectLuaAction = new CapabilityAwareAction("Read parent object (Lua)", "SWFOC_GetParentObjectLua");
        ReadAttackTargetLuaAction = new CapabilityAwareAction("Read attack target (Lua)", "SWFOC_GetAttackTargetLua");
        ReadDamageModifierLuaAction = new CapabilityAwareAction("Read damage mod (Lua)", "SWFOC_GetDamageModifierLua");
        ReadContainedObjectCountLuaAction = new CapabilityAwareAction("Read contained count (Lua)", "SWFOC_GetContainedObjectCountLua");
        ReadBehaviorIdLuaAction = new CapabilityAwareAction("Read behavior id (Lua)", "SWFOC_GetBehaviorIdLua");
        ReadRateOfFireModifierLuaAction = new CapabilityAwareAction("Read RoF mod (Lua)", "SWFOC_GetRateOfFireModifierLua");

        // 2026-05-05 (iter 198): iter-173 arg-getter extension. All 4 take
        // unit + 1 string arg via the iter-173 helper. UnitArgExpr field used
        // for property/category/ability name OR target unit Lua expression.
        IsAbilityActiveLuaCommand = new AsyncRelayCommand(IsAbilityActiveLuaAsync, onError: HandleError);
        HasPropertyLuaCommand = new AsyncRelayCommand(HasPropertyLuaAsync, onError: HandleError);
        IsCategoryLuaCommand = new AsyncRelayCommand(IsCategoryLuaAsync, onError: HandleError);
        GetDistanceLuaCommand = new AsyncRelayCommand(GetDistanceLuaAsync, onError: HandleError);
        IsAbilityActiveLuaAction = new CapabilityAwareAction("Is ability active? (Lua)", "SWFOC_IsAbilityActiveLua");
        HasPropertyLuaAction = new CapabilityAwareAction("Has property? (Lua)", "SWFOC_HasPropertyLua");
        IsCategoryLuaAction = new CapabilityAwareAction("Is category? (Lua)", "SWFOC_IsCategoryLua");
        GetDistanceLuaAction = new CapabilityAwareAction("Get distance to target (Lua)", "SWFOC_GetDistanceLua");

        // 2026-05-06 (iter 214): cross-receiver arg-getter extension (iter-174
        // 4 wires). All 4 use the same UnitLuaExpr + UnitArgExpr field pair
        // (operator types receiver Lua expression — unit/player/TaskForce —
        // into UnitLuaExpr, and second arg into UnitArgExpr). Helper is fully
        // receiver-agnostic so the field reuse works across all 3 receiver
        // types. Catalog rationales document the receiver type per wire.
        GetBonePositionLuaCommand = new AsyncRelayCommand(GetBonePositionLuaAsync, onError: HandleError);
        ContainsObjectTypeLuaCommand = new AsyncRelayCommand(ContainsObjectTypeLuaAsync, onError: HandleError);
        GetSpaceStationLevelLuaCommand = new AsyncRelayCommand(GetSpaceStationLevelLuaAsync, onError: HandleError);
        GetTypeOfUnitLuaCommand = new AsyncRelayCommand(GetTypeOfUnitLuaAsync, onError: HandleError);
        GetBonePositionLuaAction = new CapabilityAwareAction("Get bone position (Lua)", "SWFOC_GetBonePositionLua");
        ContainsObjectTypeLuaAction = new CapabilityAwareAction("Contains object type? (Lua)", "SWFOC_ContainsObjectTypeLua");
        GetSpaceStationLevelLuaAction = new CapabilityAwareAction("Get space station level (Lua)", "SWFOC_GetSpaceStationLevelLua");
        GetTypeOfUnitLuaAction = new CapabilityAwareAction("Get type of unit at index (Lua)", "SWFOC_GetTypeOfUnitLua");
    }

    public CapabilityAwareAction Refresh { get; }

    // 2026-05-05 (iter 191): read-side native UX commands + capability actions.
    public ICommand ReadUnitTypeLuaCommand { get; }
    public ICommand ReadUnitOwnerLuaCommand { get; }
    public ICommand ReadHasAttackTargetLuaCommand { get; }
    public ICommand ReadAreEnginesOnlineLuaCommand { get; }
    public CapabilityAwareAction ReadUnitTypeLuaAction { get; }
    public CapabilityAwareAction ReadUnitOwnerLuaAction { get; }
    public CapabilityAwareAction ReadHasAttackTargetLuaAction { get; }
    public CapabilityAwareAction ReadAreEnginesOnlineLuaAction { get; }

    // 2026-05-05 (iter 197): Inspector read-side extension commands + actions.
    public ICommand ReadParentObjectLuaCommand { get; }
    public ICommand ReadAttackTargetLuaCommand { get; }
    public ICommand ReadDamageModifierLuaCommand { get; }
    public ICommand ReadContainedObjectCountLuaCommand { get; }
    public ICommand ReadBehaviorIdLuaCommand { get; }
    public ICommand ReadRateOfFireModifierLuaCommand { get; }
    public CapabilityAwareAction ReadParentObjectLuaAction { get; }
    public CapabilityAwareAction ReadAttackTargetLuaAction { get; }
    public CapabilityAwareAction ReadDamageModifierLuaAction { get; }
    public CapabilityAwareAction ReadContainedObjectCountLuaAction { get; }
    public CapabilityAwareAction ReadBehaviorIdLuaAction { get; }
    public CapabilityAwareAction ReadRateOfFireModifierLuaAction { get; }

    // 2026-05-05 (iter 198): Inspector arg-getter extension commands + actions.
    public ICommand IsAbilityActiveLuaCommand { get; }
    public ICommand HasPropertyLuaCommand { get; }
    public ICommand IsCategoryLuaCommand { get; }
    public ICommand GetDistanceLuaCommand { get; }
    public CapabilityAwareAction IsAbilityActiveLuaAction { get; }
    public CapabilityAwareAction HasPropertyLuaAction { get; }
    public CapabilityAwareAction IsCategoryLuaAction { get; }
    public CapabilityAwareAction GetDistanceLuaAction { get; }

    // 2026-05-06 (iter 214): Inspector cross-receiver arg-getter commands + actions.
    public ICommand GetBonePositionLuaCommand { get; }
    public ICommand ContainsObjectTypeLuaCommand { get; }
    public ICommand GetSpaceStationLevelLuaCommand { get; }
    public ICommand GetTypeOfUnitLuaCommand { get; }
    public CapabilityAwareAction GetBonePositionLuaAction { get; }
    public CapabilityAwareAction ContainsObjectTypeLuaAction { get; }
    public CapabilityAwareAction GetSpaceStationLevelLuaAction { get; }
    public CapabilityAwareAction GetTypeOfUnitLuaAction { get; }

    /// <summary>
    /// 2026-05-05 (iter 198): second arg field for iter-173 unit-receiver
    /// arg-getter wires. Operator types property/category/ability name OR a
    /// target unit Lua expression depending on which button is clicked.
    /// </summary>
    public string UnitArgExpr
    {
        get => _unitArgExpr;
        set => SetField(ref _unitArgExpr, value ?? string.Empty);
    }

    public string UnitLuaExpr
    {
        get => _unitLuaExpr;
        set => SetField(ref _unitLuaExpr, value ?? string.Empty);
    }

    public IReadOnlyList<CapabilityAwareAction> AllActions => new[]
    {
        Refresh,
        ReadUnitTypeLuaAction,
        ReadUnitOwnerLuaAction,
        ReadHasAttackTargetLuaAction,
        ReadAreEnginesOnlineLuaAction,
        // iter 197: Inspector tab read-side extension
        ReadParentObjectLuaAction,
        ReadAttackTargetLuaAction,
        ReadDamageModifierLuaAction,
        ReadContainedObjectCountLuaAction,
        ReadBehaviorIdLuaAction,
        ReadRateOfFireModifierLuaAction,
        // iter 198: Inspector tab arg-getter extension
        IsAbilityActiveLuaAction,
        HasPropertyLuaAction,
        IsCategoryLuaAction,
        GetDistanceLuaAction,
        // iter 214: Inspector tab cross-receiver arg-getter extension
        GetBonePositionLuaAction,
        ContainsObjectTypeLuaAction,
        GetSpaceStationLevelLuaAction,
        GetTypeOfUnitLuaAction,
    };

    public bool HasNonLiveAction => AllActions.Any(a => !a.IsAllLive);

    /// <summary>
    /// Inspector's only bridge-using action is <see cref="Refresh"/>, which
    /// the catalog marks <c>RequiresLiveSwfoc</c> ("LIVE ONLY"). Surface the
    /// distinction from PHASE 2 PENDING — operator should know the call IS
    /// engine-effective, just only when a real game session is running.
    /// </summary>
    public string CapabilityNoteLine
    {
        get
        {
            var pending = AllActions.Where(a => !a.IsAllLive).ToList();
            if (pending.Count == 0) return string.Empty;
            var parts = pending.Select(a => $"{a.Name} ({a.Badge})");
            return "ℹ Refresh requires a running SWFOC session — the offline "
                + "harness can't return real unit data because there's no live "
                + "unit object to read. State: "
                + string.Join("; ", parts);
        }
    }

    /// <summary>
    /// 2026-04-27: serialise the current snapshot to a JSON-ish blob and
    /// put it on the clipboard. Empty snapshot → "(no snapshot)" so the
    /// operator gets feedback either way.
    /// </summary>
    public ICommand CopySnapshotCommand { get; }

    /// <summary>
    /// 2026-04-28 (iter 90) — copy just the current obj_addr as a hex
    /// string ("0x12345678") to clipboard. Operators often want to
    /// paste the address into another tab without grabbing the full
    /// snapshot blob.
    /// </summary>
    public ICommand CopyObjAddrCommand { get; }

    private void CopySnapshotToClipboard()
    {
        var snap = _state.CurrentSnapshot;
        string blob;
        if (snap is null)
        {
            blob = "(no snapshot — click Refresh first)";
        }
        else
        {
            // InspectorDetailSnapshot fields per Core/V2Vm/InspectorTabState.cs:
            // ObjAddr, TypeName, OwnerSlot, Hull, MaxHull, Shield, MaxShield,
            // Speed, MaxSpeed, IsHero, InvulnFlag, PreventDeath. (No IsLocal /
            // IsSelected — those are TacticalUnitRow fields, not Inspector.)
            blob = string.Format(Inv,
                "{{\n" +
                "  \"capturedAt\": \"{0:yyyy-MM-ddTHH:mm:ss}\",\n" +
                "  \"objAddrHex\": \"0x{1:X}\",\n" +
                "  \"objAddrDecimal\": {1},\n" +
                "  \"typeName\": \"{2}\",\n" +
                "  \"ownerSlot\": {3},\n" +
                "  \"hull\": {4:0.000},\n" +
                "  \"maxHull\": {5:0.000},\n" +
                "  \"shield\": {6:0.000},\n" +
                "  \"maxShield\": {7:0.000},\n" +
                "  \"speed\": {8:0.000},\n" +
                "  \"maxSpeed\": {9:0.000},\n" +
                "  \"isHero\": {10},\n" +
                "  \"invulnFlag\": {11},\n" +
                "  \"preventDeath\": {12}\n" +
                "}}",
                DateTime.Now,
                snap.ObjAddr,
                snap.TypeName,
                snap.OwnerSlot,
                snap.Hull,
                snap.MaxHull,
                snap.Shield,
                snap.MaxShield,
                snap.Speed,
                snap.MaxSpeed,
                snap.IsHero ? "true" : "false",
                snap.InvulnFlag ? "true" : "false",
                snap.PreventDeath ? "true" : "false");
        }
        try
        {
            System.Windows.Clipboard.SetText(blob);
            LastStatus = "Snapshot copied to clipboard.";
        }
        catch (Exception ex)
        {
            LastStatus = $"Clipboard copy failed: {ex.Message}";
        }
    }

    /// <summary>
    /// 2026-04-28 (iter 90) — copy just the obj_addr (hex format with
    /// 0x prefix) to the clipboard. Pulls from the snapshot if present;
    /// falls back to the parsed input if no snapshot. Empty / 0 input
    /// yields a clear status message instead of an empty clipboard.
    /// </summary>
    private void CopyObjAddrToClipboard()
    {
        var hex = BuildObjAddrHex();
        if (string.IsNullOrEmpty(hex))
        {
            LastStatus = "(no obj_addr to copy — paste an address first)";
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(hex);
            LastStatus = $"obj_addr copied: {hex}";
        }
        catch (Exception ex)
        {
            LastStatus = $"Clipboard copy failed: {ex.Message}";
        }
    }

    /// <summary>
    /// 2026-04-28 (iter 90) — testable hex builder. Prefers the live
    /// snapshot's ObjAddr; falls back to parsing <see cref="ObjAddrInput"/>
    /// the same way the property setter does (decimal then hex). Returns
    /// empty string when nothing is set.
    /// </summary>
    internal string BuildObjAddrHex()
    {
        var snap = _state.CurrentSnapshot;
        if (snap is not null && snap.ObjAddr > 0)
        {
            return string.Format(Inv, "0x{0:X}", snap.ObjAddr);
        }
        if (long.TryParse(_objAddrInput, NumberStyles.Integer, Inv, out var dec) && dec > 0)
        {
            return string.Format(Inv, "0x{0:X}", dec);
        }
        if (long.TryParse(_objAddrInput, NumberStyles.HexNumber, Inv, out var hex) && hex > 0)
        {
            return string.Format(Inv, "0x{0:X}", hex);
        }
        return string.Empty;
    }

    public string ObjAddrInput
    {
        get => _objAddrInput;
        set
        {
            if (SetField(ref _objAddrInput, value ?? "0"))
            {
                long.TryParse(_objAddrInput, NumberStyles.Integer, Inv, out var addr);
                if (addr <= 0)
                {
                    long.TryParse(_objAddrInput, NumberStyles.HexNumber, Inv, out addr);
                }
                _state.SelectedUnit = addr > 0
                    ? new TacticalUnitRow(addr, OwnerSlot: -1, Hull: 0f, InvulnFlag: 0,
                          PreventDeath: 0, IsLocal: false, IsSelected: false)
                    : null;
            }
        }
    }

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge("SWFOC_InspectUnit");

    public string SnapshotHull => _state.CurrentSnapshot?.Hull.ToString("0.00", Inv) ?? "—";
    public string SnapshotOwner => _state.CurrentSnapshot?.OwnerSlot.ToString(Inv) ?? "—";
    public string SnapshotInvuln => _state.CurrentSnapshot?.InvulnFlag.ToString() ?? "—";
    public string SnapshotPreventDeath => _state.CurrentSnapshot?.PreventDeath.ToString() ?? "—";
    public string SnapshotAddrHex => _state.CurrentSnapshot is { } s
        ? $"0x{s.ObjAddr:X}" : "—";

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand RefreshCommand { get; }
    public ICommand ClearCommand { get; }

    /// <summary>
    /// 2026-04-27: live observation toggle. When true, a background
    /// PeriodicTimer fires the Refresh command every 2 seconds so the
    /// Inspector tracks live hull / shield without manual clicking.
    /// Stops automatically when the operator unchecks, navigates away,
    /// or the editor closes.
    /// </summary>
    public bool IsAutoRefreshEnabled
    {
        get => _isAutoRefreshEnabled;
        set
        {
            if (SetField(ref _isAutoRefreshEnabled, value))
            {
                if (_isAutoRefreshEnabled) _autoRefresh.Start();
                else _autoRefresh.Stop();
            }
        }
    }

    public void Dispose()
    {
        _autoRefresh.Dispose();
    }

    private async Task RefreshCore()
    {
        var fb = await _state.RefreshAsync().ConfigureAwait(true);
        LastStatus = string.Format(Inv, "{0}: {1} — {2}",
            fb.Severity, fb.Title, fb.Message);
        OnSnapshotChanged();
    }

    private void OnSnapshotChanged()
    {
        OnPropertyChanged(nameof(SnapshotHull));
        OnPropertyChanged(nameof(SnapshotOwner));
        OnPropertyChanged(nameof(SnapshotInvuln));
        OnPropertyChanged(nameof(SnapshotPreventDeath));
        OnPropertyChanged(nameof(SnapshotAddrHex));
    }

    private void HandleError(Exception ex)
    {
        LastStatus = $"command failed: {ex.Message}";
    }

    // 2026-05-05 (iter 191): read-side native UX async handlers. Each one
    // validates the UnitLuaExpr is non-empty, then dispatches via
    // V2UnitMutationDispatcher (iter-167 unit-getter helper) and surfaces the
    // engine return value in LastStatus. Pattern mirrors iter-189 PlayerState
    // tab; the only difference is we land in single-line LastStatus instead
    // of an Output ListBox.
    private async Task GetTypeLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        var round = await _unitMutator.GetTypeLuaAsync(_unitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" GetTypeLua({_unitLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task GetOwnerLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        var round = await _unitMutator.GetOwnerLuaAsync(_unitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" GetOwnerLua({_unitLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task HasAttackTargetLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        var round = await _unitMutator.HasAttackTargetLuaAsync(_unitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" HasAttackTargetLua({_unitLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task AreEnginesOnlineLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        var round = await _unitMutator.AreEnginesOnlineLuaAsync(_unitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" AreEnginesOnlineLua({_unitLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    // 2026-05-05 (iter 197): Inspector tab read-side extension async handlers.
    // Each one validates UnitLuaExpr non-empty, dispatches via V2UnitMutationDispatcher
    // (iter-167 unit-getter helper), surfaces engine return value in LastStatus.
    private async Task GetParentObjectLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        var round = await _unitMutator.GetParentObjectLuaAsync(_unitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" GetParentObjectLua({_unitLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task GetAttackTargetLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        var round = await _unitMutator.GetAttackTargetLuaAsync(_unitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" GetAttackTargetLua({_unitLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task GetDamageModifierLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        var round = await _unitMutator.GetDamageModifierLuaAsync(_unitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" GetDamageModifierLua({_unitLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task GetContainedObjectCountLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        var round = await _unitMutator.GetContainedObjectCountLuaAsync(_unitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" GetContainedObjectCountLua({_unitLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task GetBehaviorIdLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        var round = await _unitMutator.GetBehaviorIdLuaAsync(_unitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" GetBehaviorIdLua({_unitLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task GetRateOfFireModifierLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        var round = await _unitMutator.GetRateOfFireModifierLuaAsync(_unitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" GetRateOfFireModifierLua({_unitLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    // 2026-05-05 (iter 198): Inspector arg-getter async handlers. Each
    // validates UnitLuaExpr + UnitArgExpr non-empty and dispatches via the
    // iter-173 helper. Result lands in LastStatus.
    private async Task IsAbilityActiveLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        if (string.IsNullOrWhiteSpace(_unitArgExpr))
        {
            LastStatus = "(no ability name — type one into the Arg field first)";
            return;
        }
        var round = await _unitMutator.IsAbilityActiveLuaAsync(
            _unitLuaExpr, _unitArgExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" IsAbilityActiveLua({_unitLuaExpr}, {_unitArgExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task HasPropertyLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        if (string.IsNullOrWhiteSpace(_unitArgExpr))
        {
            LastStatus = "(no property name — type one into the Arg field first)";
            return;
        }
        var round = await _unitMutator.HasPropertyLuaAsync(
            _unitLuaExpr, _unitArgExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" HasPropertyLua({_unitLuaExpr}, {_unitArgExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task IsCategoryLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        if (string.IsNullOrWhiteSpace(_unitArgExpr))
        {
            LastStatus = "(no category name — type one into the Arg field first)";
            return;
        }
        var round = await _unitMutator.IsCategoryLuaAsync(
            _unitLuaExpr, _unitArgExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" IsCategoryLua({_unitLuaExpr}, {_unitArgExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task GetDistanceLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        if (string.IsNullOrWhiteSpace(_unitArgExpr))
        {
            LastStatus = "(no target — type a unit Lua expression into the Arg field first)";
            return;
        }
        var round = await _unitMutator.GetDistanceLuaAsync(
            _unitLuaExpr, _unitArgExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" GetDistanceLua({_unitLuaExpr}, {_unitArgExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    // 2026-05-06 (iter 214): cross-receiver arg-getter handlers (iter-174 wires).
    // All four reuse the iter-198 UnitLuaExpr + UnitArgExpr field pair. The
    // FIRST arg's receiver type varies per wire (unit/player/TaskForce) — the
    // operator types the appropriate Lua expression into UnitLuaExpr; helper
    // is shape-agnostic so the dispatch works regardless.
    private async Task GetBonePositionLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        if (string.IsNullOrWhiteSpace(_unitArgExpr))
        {
            LastStatus = "(no bone-name — type a bone-name Lua expression into the Arg field first)";
            return;
        }
        var round = await _unitMutator.GetBonePositionLuaAsync(
            _unitLuaExpr, _unitArgExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" GetBonePositionLua({_unitLuaExpr}, {_unitArgExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task ContainsObjectTypeLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        if (string.IsNullOrWhiteSpace(_unitArgExpr))
        {
            LastStatus = "(no child-type — type a child-type Lua expression into the Arg field first)";
            return;
        }
        var round = await _unitMutator.ContainsObjectTypeLuaAsync(
            _unitLuaExpr, _unitArgExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" ContainsObjectTypeLua({_unitLuaExpr}, {_unitArgExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task GetSpaceStationLevelLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no player Lua expression — type one above first; first arg is player handle)";
            return;
        }
        if (string.IsNullOrWhiteSpace(_unitArgExpr))
        {
            LastStatus = "(no planet — type a planet Lua expression into the Arg field first)";
            return;
        }
        var round = await _unitMutator.GetSpaceStationLevelLuaAsync(
            _unitLuaExpr, _unitArgExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" GetSpaceStationLevelLua({_unitLuaExpr}, {_unitArgExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task GetTypeOfUnitLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_unitLuaExpr))
        {
            LastStatus = "(no TaskForce Lua expression — type one above first; first arg is TaskForce handle)";
            return;
        }
        if (string.IsNullOrWhiteSpace(_unitArgExpr))
        {
            LastStatus = "(no index — type an index Lua expression into the Arg field first)";
            return;
        }
        var round = await _unitMutator.GetTypeOfUnitLuaAsync(
            _unitLuaExpr, _unitArgExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" GetTypeOfUnitLua({_unitLuaExpr}, {_unitArgExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }
}
