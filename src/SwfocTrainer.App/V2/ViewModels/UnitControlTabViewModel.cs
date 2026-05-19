using System.Collections.ObjectModel;
using System.Globalization;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;

namespace SwfocTrainer.App.V2.ViewModels;

// ============================================================================
// Tab 3 — Unit Control
//
// God Mode / One Hit Kill / Set Unit Hull / Set Unit Invuln / Prevent Unit Death /
// Inspect Unit / Get Hardpoints / Spawn Unit.
//
// GodMode + OneHitKill go through their wrapped services (IGodModeService,
// IOneHitKillService) which already build the right Lua command.
// InspectUnit + GetHardpoints use their services (IUnitInspectorService,
// IHardpointService). The Unit Hull / Unit Invuln / Prevent Unit Death
// helpers have no C# service wrapper, so we build the Lua text inline and
// push it through V2BridgeAdapter. Spawn goes through IEnhancedSpawnService.
//
// 2026-04-11: the tab now also reads the currently-selected unit through
// SWFOC_GetSelectedUnit, so the user no longer has to type a hex pointer
// into the Obj addr field. Two entry points drive this:
//   - "Use selected" button: snapshots the selection into ObjAddrInput now
//   - "Auto-use selected" checkbox: every button resolves the selection at
//     click time and ignores the textbox
// Also fixes a Lua 5.0 parse error that was killing every SetUnit* button:
// the inline builder previously emitted `0x{addr:X}` (e.g. `0x0`) which
// Lua 5.0 can't parse as a number literal (hex literals were added in 5.1).
// Emitting decimal via InvariantCulture matches the existing service-side
// formatting and round-trips cleanly.
// ============================================================================

public sealed class UnitControlTabViewModel : ObservableBase
{
    private readonly V2BridgeAdapter _bridge;
    private readonly V2Settings _settings;
    private readonly IGodModeService _godMode;
    private readonly IOneHitKillService _oneHitKill;
    private readonly IUnitInspectorService _unitInspector;
    private readonly IHardpointService _hardpoints;
    private readonly IEnhancedSpawnService _enhancedSpawn;
    private readonly V2UnitMutationDispatcher _unitMutator;

    private readonly ObservableCollection<string> _output = new();
    private string _objAddrInput = "0x0";
    private string _hullHpInput = "99999";
    private string _spawnUnitId = "NEB_B_FRIGATE";
    private string _spawnFaction = "EMPIRE";
    private string _spawnCount = "1";
    private string _lastInspectOrHardpoint = string.Empty;
    private bool _autoUseSelected = true;
    private string _selectedUnitSummary = "(no selection read yet)";

    public UnitControlTabViewModel(
        V2BridgeAdapter bridge,
        V2Settings settings,
        IGodModeService godMode,
        IOneHitKillService oneHitKill,
        IUnitInspectorService unitInspector,
        IHardpointService hardpoints,
        IEnhancedSpawnService enhancedSpawn,
        V2UnitMutationDispatcher unitMutator,
        V2FactionRegistry factionRegistry)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(godMode);
        ArgumentNullException.ThrowIfNull(oneHitKill);
        ArgumentNullException.ThrowIfNull(unitInspector);
        ArgumentNullException.ThrowIfNull(hardpoints);
        ArgumentNullException.ThrowIfNull(enhancedSpawn);
        ArgumentNullException.ThrowIfNull(unitMutator);
        ArgumentNullException.ThrowIfNull(factionRegistry);

        _bridge = bridge;
        _settings = settings;
        _godMode = godMode;
        _oneHitKill = oneHitKill;
        _unitInspector = unitInspector;
        _hardpoints = hardpoints;
        _enhancedSpawn = enhancedSpawn;
        _unitMutator = unitMutator;

        // 2026-04-27: faction list now sourced from V2FactionRegistry
        // (live-merged from SWFOC_GetAllPlayers). Vanilla seed remains
        // as a fallback when the operator hasn't connected yet.
        Factions = factionRegistry.Factions;

        EnableGodModeCommand = Async(() => ToggleGodModeAsync(true));
        DisableGodModeCommand = Async(() => ToggleGodModeAsync(false));
        EnableOneHitKillCommand = Async(() => ToggleOneHitKillAsync(true));
        DisableOneHitKillCommand = Async(() => ToggleOneHitKillAsync(false));
        SetUnitHullCommand = Async(SetUnitHullAsync);
        EnableUnitInvulnCommand = Async(() => SetUnitInvulnAsync(true));
        DisableUnitInvulnCommand = Async(() => SetUnitInvulnAsync(false));
        EnablePreventDeathCommand = Async(() => SetPreventDeathAsync(true));
        DisablePreventDeathCommand = Async(() => SetPreventDeathAsync(false));
        InspectUnitCommand = Async(InspectUnitAsync);
        GetHardpointsCommand = Async(GetHardpointsAsync);
        SpawnUnitCommand = AsyncDisabled(SpawnUnitAsync);
        UseSelectedCommand = Async(UseSelectedAsync);
        RefreshSelectionCommand = Async(RefreshSelectionAsync);

        // 2026-04-28 (iter 117): per-unit Lua-method action commands.
        // Operator types a Lua expression that resolves to a unit handle
        // (e.g. Find_First_Object("Empire_AT_AT")) into SelectedUnitLuaExpr,
        // then clicks any of these buttons to fire the engine-Lua-API wire
        // shipped in iter 110-112.
        EnableMakeInvulnLuaCommand = Async(() => MakeInvulnLuaAsync(true));
        DisableMakeInvulnLuaCommand = Async(() => MakeInvulnLuaAsync(false));
        EnableHideUnitLuaCommand = Async(() => HideUnitLuaAsync(true));
        DisableHideUnitLuaCommand = Async(() => HideUnitLuaAsync(false));
        EnablePreventAiUsageLuaCommand = Async(() => PreventAiUsageLuaAsync(true));
        DisablePreventAiUsageLuaCommand = Async(() => PreventAiUsageLuaAsync(false));
        DisableSelectableLuaCommand = Async(() => SetSelectableLuaAsync(false));
        EnableSelectableLuaCommand = Async(() => SetSelectableLuaAsync(true));
        DespawnUnitLuaCommand = Async(DespawnUnitLuaAsync);
        StopUnitLuaCommand = Async(StopUnitLuaAsync);
        RetreatUnitLuaCommand = Async(RetreatUnitLuaAsync);
        ChangeUnitOwnerLuaCommand = Async(ChangeUnitOwnerLuaAsync);

        // 2026-05-05 (iter 188): read-side native UX for iter 167-172 wires.
        // Operator selects a unit (via SelectedUnitLuaExpr) then clicks any
        // of these buttons to read engine state. Result lands in the Bridge
        // responses ListBox below.
        ReadUnitHullLuaCommand = Async(GetHullLuaAsync);
        ReadUnitShieldLuaCommand = Async(GetShieldLuaAsync);
        ReadUnitPositionLuaCommand = Async(GetPositionLuaAsync);
        ReadUnitGarrisonLuaCommand = Async(GetGarrisonUnitsLuaAsync);

        // 2026-05-05 (iter 194): combat-order native UX for iter 163 wires.
        // Operator types a target unit (Attack/Guard) or position (Divert)
        // into TargetForCombatOrderLuaExpr, then clicks any of these buttons.
        AttackTargetLuaCommand = Async(AttackTargetLuaAsync);
        GuardTargetLuaCommand = Async(GuardTargetLuaAsync);
        DivertLuaCommand = Async(DivertLuaAsync);

        // 2026-05-06 (iter 211): unit-method extension batch (iter-156 wires).
        // Activate_Ability uses dedicated AbilityNameLuaExpr field for the
        // ability-name string; Disable_Capture/Set_Garrison_Spawn use hardcoded
        // bool args ("1"/"0" via on/off button pairs); Cancel_Hyperspace is
        // no-arg. All four anchor on shared SelectedUnitLuaExpr (with iter-117).
        ActivateAbilityLuaCommand = Async(ActivateAbilityLuaAsync);
        DisableCaptureOnLuaCommand = Async(() => DisableCaptureLuaAsync("1"));
        DisableCaptureOffLuaCommand = Async(() => DisableCaptureLuaAsync("0"));
        SetGarrisonSpawnOnLuaCommand = Async(() => SetGarrisonSpawnLuaAsync("1"));
        SetGarrisonSpawnOffLuaCommand = Async(() => SetGarrisonSpawnLuaAsync("0"));
        CancelHyperspaceLuaCommand = Async(CancelHyperspaceLuaAsync);

        // 2026-05-06 (iter 212): unit-method MEGA-batch (iter-157 6 wires).
        // Set_In_Limbo + Set_Check_Contested_Space: bool args via iter-204
        // on/off pattern. Sell: no-arg. Bribe: 1-arg (player) — reuses
        // iter-118 TargetPlayerLuaExpr (the target-player field is shared
        // with ChangeUnitOwner). Move_To: 1-arg position — reuses iter-194
        // TargetForCombatOrderLuaExpr (semantically same "where to go" as
        // iter-163 Divert). Fire_Special_Weapon: 1-arg slot — needs NEW
        // SpecialWeaponSlotLuaExpr field.
        SetInLimboOnLuaCommand = Async(() => SetInLimboLuaAsync("1"));
        SetInLimboOffLuaCommand = Async(() => SetInLimboLuaAsync("0"));
        SetCheckContestedSpaceOnLuaCommand = Async(() => SetCheckContestedSpaceLuaAsync("1"));
        SetCheckContestedSpaceOffLuaCommand = Async(() => SetCheckContestedSpaceLuaAsync("0"));
        SellUnitLuaCommand = Async(SellUnitLuaAsync);
        BribeLuaCommand = Async(BribeLuaAsync);
        MoveToLuaCommand = Async(MoveToLuaAsync);
        FireSpecialWeaponLuaCommand = Async(FireSpecialWeaponLuaAsync);

        // 2026-05-06 (iter 213): unit-method bool batch (iter-153 + iter-162
        // unit method). Set_Cannot_Be_Killed + Enable_Stealth: bool args via
        // iter-204 on/off pattern. Override_Max_Speed: 1-arg float (per-unit
        // speed override; complements iter-100 SetPerFactionSpeedMultiplier
        // global) — uses NEW MaxSpeedOverrideLuaExpr field.
        SetCannotBeKilledOnLuaCommand = Async(() => SetCannotBeKilledLuaAsync("1"));
        SetCannotBeKilledOffLuaCommand = Async(() => SetCannotBeKilledLuaAsync("0"));
        EnableStealthOnLuaCommand = Async(() => EnableStealthLuaAsync("1"));
        EnableStealthOffLuaCommand = Async(() => EnableStealthLuaAsync("0"));
        OverrideMaxSpeedLuaCommand = Async(OverrideMaxSpeedLuaAsync);

        // 2026-05-06 (iter 218): Corrupt button (iter-180 Underworld signature
        // ability). Unit-method 2-arg via iter-154 (helper shape-agnostic).
        // Pairs semantically with iter-212 Bribe — Bribe takes ownership,
        // Corrupt degrades hostility/loyalty without transferring ownership.
        // Anchors on iter-117 SelectedUnitLuaExpr; needs NEW CorruptAmountLuaExpr
        // field for the numeric amount arg.
        CorruptLuaCommand = Async(CorruptLuaAsync);
        CorruptLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Corrupt unit (Lua)", "SWFOC_CorruptLua");

        // 2026-04-27 (iter 60): per-button capability metadata. UnitControl
        // is the most capability-mixed tab in the editor — most actions
        // are LIVE (god/OHK/heal/kill/revive/hull/invuln/prevent-death/
        // AI brain), but Inspect + GetHardpoints need a running game
        // (LIVE ONLY) and SpawnUnit is PHASE 2 PENDING.
        ToggleGodMode = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Toggle god mode", "SWFOC_GodMode");
        ToggleOneHitKill = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Toggle one-hit-kill", "SWFOC_OneHitKill");
        SetUnitHull = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set unit hull", "SWFOC_SetUnitHull");
        SetUnitInvuln = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set unit invulnerability", "SWFOC_SetUnitInvuln");
        SetPreventDeath = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set prevent-death", "SWFOC_PreventUnitDeath");
        InspectUnit = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Inspect unit", "SWFOC_InspectUnit");
        GetHardpoints = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Get hardpoints", "SWFOC_GetHardpoints");
        SpawnUnit = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Spawn unit", "SWFOC_SpawnUnit");
        UseSelected = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Use selected", "SWFOC_GetSelectedUnit");
        RefreshSelection = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Refresh selection", "SWFOC_GetSelectedUnit");

        // 2026-04-28 (iter 117): Lua-method action capabilities — all LIVE
        // through the iter 110-112 engine-Lua-API + DoString wires.
        MakeInvulnLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Make unit invulnerable (Lua)", "SWFOC_MakeUnitInvulnLua");
        HideUnitLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Hide unit (Lua)", "SWFOC_HideUnitLua");
        PreventAiUsageLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Lock unit from AI (Lua)", "SWFOC_PreventAiUsageLua");
        SetSelectableLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set unit selectable (Lua)", "SWFOC_SetUnitSelectableLua");
        DespawnUnitLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Despawn unit (Lua)", "SWFOC_DespawnUnitLua");
        StopUnitLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Stop unit (Lua)", "SWFOC_StopUnitLua");
        RetreatUnitLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Retreat unit (Lua)", "SWFOC_RetreatUnitLua");

        // 2026-04-29 (iter 118): two-Lua-expr ownership change. Reuses
        // SelectedUnitLuaExpr for the unit; adds TargetPlayerLuaExpr
        // for the destination player.
        ChangeUnitOwnerLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Change unit owner (Lua)", "SWFOC_ChangeUnitOwner");

        // 2026-05-05 (iter 188): read-side action capabilities (iter 167-172 wires).
        ReadUnitHullLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Read unit hull (Lua)", "SWFOC_GetHullLua");
        ReadUnitShieldLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Read unit shield (Lua)", "SWFOC_GetShieldLua");
        ReadUnitPositionLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Read unit position (Lua)", "SWFOC_GetPositionLua");
        ReadUnitGarrisonLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Read unit garrison (Lua)", "SWFOC_GetGarrisonUnitsLua");

        // 2026-05-05 (iter 194): combat-order action capabilities (iter 163 wires).
        AttackTargetLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Attack target (Lua)", "SWFOC_AttackTargetLua");
        GuardTargetLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Guard target (Lua)", "SWFOC_GuardTargetLua");
        DivertLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Divert to position (Lua)", "SWFOC_DivertLua");

        // 2026-05-06 (iter 211): unit-method extension capabilities (iter 156 wires).
        ActivateAbilityLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Activate ability (Lua)", "SWFOC_ActivateAbilityLua");
        DisableCaptureOnLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Disable capture: on (Lua)", "SWFOC_DisableCaptureLua");
        DisableCaptureOffLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Disable capture: off (Lua)", "SWFOC_DisableCaptureLua");
        SetGarrisonSpawnOnLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Garrison spawn: on (Lua)", "SWFOC_SetGarrisonSpawnLua");
        SetGarrisonSpawnOffLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Garrison spawn: off (Lua)", "SWFOC_SetGarrisonSpawnLua");
        CancelHyperspaceLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Cancel hyperspace (Lua)", "SWFOC_CancelHyperspaceLua");

        // 2026-05-06 (iter 212): unit-method mega-batch capabilities (iter 157 wires).
        SetInLimboOnLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set in limbo: on (Lua)", "SWFOC_SetInLimboLua");
        SetInLimboOffLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Set in limbo: off (Lua)", "SWFOC_SetInLimboLua");
        SetCheckContestedSpaceOnLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Check contested space: on (Lua)", "SWFOC_SetCheckContestedSpaceLua");
        SetCheckContestedSpaceOffLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Check contested space: off (Lua)", "SWFOC_SetCheckContestedSpaceLua");
        SellUnitLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Sell unit (Lua)", "SWFOC_SellUnitLua");
        BribeLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Bribe unit (Lua)", "SWFOC_BribeLua");
        MoveToLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Move to position (Lua)", "SWFOC_MoveToLua");
        FireSpecialWeaponLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Fire special weapon (Lua)", "SWFOC_FireSpecialWeaponLua");

        // 2026-05-06 (iter 213): unit-method bool-batch capabilities (iter-153 + iter-162).
        SetCannotBeKilledOnLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Cannot be killed: on (Lua)", "SWFOC_SetCannotBeKilledLua");
        SetCannotBeKilledOffLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Cannot be killed: off (Lua)", "SWFOC_SetCannotBeKilledLua");
        EnableStealthOnLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Enable stealth: on (Lua)", "SWFOC_EnableStealthLua");
        EnableStealthOffLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Enable stealth: off (Lua)", "SWFOC_EnableStealthLua");
        OverrideMaxSpeedLuaAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Override max speed (Lua)", "SWFOC_OverrideMaxSpeedLua");
    }

    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction MakeInvulnLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction HideUnitLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction PreventAiUsageLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetSelectableLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction DespawnUnitLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction StopUnitLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction RetreatUnitLuaAction { get; }

    public AsyncRelayCommand EnableMakeInvulnLuaCommand { get; }
    public AsyncRelayCommand DisableMakeInvulnLuaCommand { get; }
    public AsyncRelayCommand EnableHideUnitLuaCommand { get; }
    public AsyncRelayCommand DisableHideUnitLuaCommand { get; }
    public AsyncRelayCommand EnablePreventAiUsageLuaCommand { get; }
    public AsyncRelayCommand DisablePreventAiUsageLuaCommand { get; }
    public AsyncRelayCommand EnableSelectableLuaCommand { get; }
    public AsyncRelayCommand DisableSelectableLuaCommand { get; }
    public AsyncRelayCommand DespawnUnitLuaCommand { get; }
    public AsyncRelayCommand StopUnitLuaCommand { get; }
    public AsyncRelayCommand RetreatUnitLuaCommand { get; }
    public AsyncRelayCommand ChangeUnitOwnerLuaCommand { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ChangeUnitOwnerLuaAction { get; }

    // 2026-05-05 (iter 188): read-side native UX (iter 167-172 wires).
    public AsyncRelayCommand ReadUnitHullLuaCommand { get; }
    public AsyncRelayCommand ReadUnitShieldLuaCommand { get; }
    public AsyncRelayCommand ReadUnitPositionLuaCommand { get; }
    public AsyncRelayCommand ReadUnitGarrisonLuaCommand { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ReadUnitHullLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ReadUnitShieldLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ReadUnitPositionLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ReadUnitGarrisonLuaAction { get; }

    // 2026-05-05 (iter 194): combat-order commands + capability actions.
    public AsyncRelayCommand AttackTargetLuaCommand { get; }
    public AsyncRelayCommand GuardTargetLuaCommand { get; }
    public AsyncRelayCommand DivertLuaCommand { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction AttackTargetLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction GuardTargetLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction DivertLuaAction { get; }

    // 2026-05-06 (iter 211): unit-method extension commands + capability actions.
    public AsyncRelayCommand ActivateAbilityLuaCommand { get; }
    public AsyncRelayCommand DisableCaptureOnLuaCommand { get; }
    public AsyncRelayCommand DisableCaptureOffLuaCommand { get; }
    public AsyncRelayCommand SetGarrisonSpawnOnLuaCommand { get; }
    public AsyncRelayCommand SetGarrisonSpawnOffLuaCommand { get; }
    public AsyncRelayCommand CancelHyperspaceLuaCommand { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ActivateAbilityLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction DisableCaptureOnLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction DisableCaptureOffLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetGarrisonSpawnOnLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetGarrisonSpawnOffLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction CancelHyperspaceLuaAction { get; }

    /// <summary>
    /// 2026-05-06 (iter 211): Lua ability-name expression for Activate_Ability.
    /// Operator types e.g. <c>"Tractor_Beam"</c> or <c>"Sensor_Jamming"</c>.
    /// Default empty so the operator sees the "(no ability-name)" feedback when
    /// they click without typing.
    /// </summary>
    private string _abilityNameLuaExpr = string.Empty;
    public string AbilityNameLuaExpr
    {
        get => _abilityNameLuaExpr;
        set => SetField(ref _abilityNameLuaExpr, value ?? string.Empty);
    }

    /// <summary>
    /// 2026-05-07 (iter 403): operator picks from
    /// <see cref="SwfocTrainer.Core.Diagnostics.KnownUnitAbilityNames"/>
    /// (69 names recovered from EnumConversionClass&lt;UnitAbilityType&gt; static
    /// initializer at RVA 0x5DEA20 via callgraph mining at iter-402). Setting
    /// this property auto-populates <see cref="AbilityNameLuaExpr"/> with the
    /// quoted Lua-string form so the operator never has to remember the
    /// surrounding double-quote convention.
    /// </summary>
    private string _abilityNamePresetSelection = string.Empty;
    public string AbilityNamePresetSelection
    {
        get => _abilityNamePresetSelection;
        set
        {
            var normalized = value ?? string.Empty;
            if (SetField(ref _abilityNamePresetSelection, normalized) && !string.IsNullOrWhiteSpace(normalized))
            {
                AbilityNameLuaExpr = "\"" + normalized + "\"";
            }
        }
    }

    // 2026-05-06 (iter 218): Corrupt command + capability action.
    public AsyncRelayCommand CorruptLuaCommand { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction CorruptLuaAction { get; }

    /// <summary>
    /// 2026-05-06 (iter 218): Lua numeric amount expression for Corrupt.
    /// Operator types e.g. <c>"50"</c> for corruption amount. Default empty
    /// so the operator sees the "(no corruption amount)" feedback when they
    /// click without typing.
    /// </summary>
    private string _corruptAmountLuaExpr = string.Empty;
    public string CorruptAmountLuaExpr
    {
        get => _corruptAmountLuaExpr;
        set => SetField(ref _corruptAmountLuaExpr, value ?? string.Empty);
    }

    // 2026-05-06 (iter 212): unit-method mega-batch commands + capability actions.
    public AsyncRelayCommand SetInLimboOnLuaCommand { get; }
    public AsyncRelayCommand SetInLimboOffLuaCommand { get; }
    public AsyncRelayCommand SetCheckContestedSpaceOnLuaCommand { get; }
    public AsyncRelayCommand SetCheckContestedSpaceOffLuaCommand { get; }
    public AsyncRelayCommand SellUnitLuaCommand { get; }
    public AsyncRelayCommand BribeLuaCommand { get; }
    public AsyncRelayCommand MoveToLuaCommand { get; }
    public AsyncRelayCommand FireSpecialWeaponLuaCommand { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetInLimboOnLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetInLimboOffLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetCheckContestedSpaceOnLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetCheckContestedSpaceOffLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SellUnitLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction BribeLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction MoveToLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction FireSpecialWeaponLuaAction { get; }

    /// <summary>
    /// 2026-05-06 (iter 212): Lua slot expression for Fire_Special_Weapon.
    /// Operator types e.g. <c>"0"</c> or <c>"1"</c> (slot index) or a named
    /// constant. Default empty so the operator sees the "(no slot)" feedback
    /// when they click without typing.
    /// </summary>
    private string _specialWeaponSlotLuaExpr = string.Empty;
    public string SpecialWeaponSlotLuaExpr
    {
        get => _specialWeaponSlotLuaExpr;
        set => SetField(ref _specialWeaponSlotLuaExpr, value ?? string.Empty);
    }

    // 2026-05-06 (iter 213): unit-method bool batch commands + capability actions.
    public AsyncRelayCommand SetCannotBeKilledOnLuaCommand { get; }
    public AsyncRelayCommand SetCannotBeKilledOffLuaCommand { get; }
    public AsyncRelayCommand EnableStealthOnLuaCommand { get; }
    public AsyncRelayCommand EnableStealthOffLuaCommand { get; }
    public AsyncRelayCommand OverrideMaxSpeedLuaCommand { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetCannotBeKilledOnLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetCannotBeKilledOffLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction EnableStealthOnLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction EnableStealthOffLuaAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction OverrideMaxSpeedLuaAction { get; }

    /// <summary>
    /// 2026-05-06 (iter 213): Lua numeric speed expression for Override_Max_Speed.
    /// Operator types e.g. <c>"100.0"</c> (units/sec) or a named constant.
    /// Per-unit speed override; complements iter-100 SetPerFactionSpeedMultiplier
    /// global. Default empty so the operator sees the "(no speed)" feedback
    /// when they click without typing.
    /// </summary>
    private string _maxSpeedOverrideLuaExpr = string.Empty;
    public string MaxSpeedOverrideLuaExpr
    {
        get => _maxSpeedOverrideLuaExpr;
        set => SetField(ref _maxSpeedOverrideLuaExpr, value ?? string.Empty);
    }

    /// <summary>
    /// 2026-05-05 (iter 194): Lua expression for the target unit (Attack/Guard)
    /// or position (Divert). Operator types e.g. <c>Find_First_Object("Rebel_Tank")</c>
    /// for combat orders, or a position handle for Divert.
    /// </summary>
    private string _targetForCombatOrderLuaExpr = string.Empty;
    public string TargetForCombatOrderLuaExpr
    {
        get => _targetForCombatOrderLuaExpr;
        set => SetField(ref _targetForCombatOrderLuaExpr, value ?? string.Empty);
    }

    /// <summary>
    /// 2026-04-29 (iter 118): Lua expression resolving to the destination
    /// player. Operator types e.g. <c>Find_Player("REBEL")</c> or
    /// <c>Find_Player("Hostile_Garrison")</c>. Paired with
    /// <see cref="SelectedUnitLuaExpr"/> for the iter 108 ChangeUnitOwner
    /// wire.
    /// </summary>
    private string _targetPlayerLuaExpr = string.Empty;
    public string TargetPlayerLuaExpr
    {
        get => _targetPlayerLuaExpr;
        set => SetField(ref _targetPlayerLuaExpr, value ?? string.Empty);
    }

    /// <summary>
    /// 2026-04-28 (iter 117): Lua expression that resolves to a unit
    /// handle. Operator types e.g. <c>Find_First_Object("Empire_AT_AT")</c>
    /// or <c>Find_Object_Type("Rebel_Trooper_Squad")[0]</c>. Used by
    /// every "(Lua)" suffixed action button.
    /// </summary>
    private string _selectedUnitLuaExpr = string.Empty;
    public string SelectedUnitLuaExpr
    {
        get => _selectedUnitLuaExpr;
        set => SetField(ref _selectedUnitLuaExpr, value ?? string.Empty);
    }

    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ToggleGodMode { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ToggleOneHitKill { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetUnitHull { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetUnitInvuln { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SetPreventDeath { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction InspectUnit { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction GetHardpoints { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction SpawnUnit { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction UseSelected { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction RefreshSelection { get; }

    public IReadOnlyList<SwfocTrainer.Core.Diagnostics.CapabilityAwareAction> AllActions => new[]
    {
        ToggleGodMode, ToggleOneHitKill, SetUnitHull, SetUnitInvuln, SetPreventDeath,
        InspectUnit, GetHardpoints, SpawnUnit, UseSelected, RefreshSelection,
        // iter 117 additions:
        MakeInvulnLuaAction, HideUnitLuaAction, PreventAiUsageLuaAction,
        SetSelectableLuaAction, DespawnUnitLuaAction, StopUnitLuaAction,
        RetreatUnitLuaAction,
        // iter 118 addition:
        ChangeUnitOwnerLuaAction,
    };

    public bool HasNonLiveAction => AllActions.Any(a => !a.IsAllLive);

    /// <summary>
    /// Surface every non-LIVE action by name so the operator can tell at a
    /// glance which buttons need a running game (LIVE ONLY) vs. which are
    /// Phase-1-mirror (PHASE 2 PENDING).
    /// </summary>
    public string CapabilityNoteLine
    {
        get
        {
            var nonLive = AllActions.Where(a => !a.IsAllLive).ToList();
            if (nonLive.Count == 0) return string.Empty;
            var parts = nonLive.Select(a => $"{a.Name} ({a.Badge})");
            return "Some actions are not uniformly LIVE; Inspect + Get hardpoints "
                + "require a running game (LIVE ONLY), and Spawn unit is disabled "
                + "until a live engine hook exists. "
                + "State: " + string.Join("; ", parts);
        }
    }

    public ObservableCollection<string> Factions { get; }

    public string ObjAddrInput
    {
        get => _objAddrInput;
        set => SetField(ref _objAddrInput, value);
    }

    public string HullHpInput
    {
        get => _hullHpInput;
        set => SetField(ref _hullHpInput, value);
    }

    public string SpawnUnitId
    {
        get => _spawnUnitId;
        set => SetField(ref _spawnUnitId, value);
    }

    public string SpawnFaction
    {
        get => _spawnFaction;
        set => SetField(ref _spawnFaction, value);
    }

    public string SpawnCount
    {
        get => _spawnCount;
        set => SetField(ref _spawnCount, value);
    }

    public string LastInspectOrHardpoint
    {
        get => _lastInspectOrHardpoint;
        private set => SetField(ref _lastInspectOrHardpoint, value);
    }

    /// <summary>
    /// When true, every per-unit button resolves the current selection at
    /// click time and ignores the <see cref="ObjAddrInput"/> textbox. When
    /// false, buttons use the textbox verbatim (the legacy behavior).
    /// Default: true — the common case is "act on whatever I have selected
    /// in-game" and the textbox is only for power users poking specific
    /// addresses returned from InspectUnit.
    /// </summary>
    public bool AutoUseSelected
    {
        get => _autoUseSelected;
        set => SetField(ref _autoUseSelected, value);
    }

    /// <summary>
    /// Human-readable summary of the last selection snapshot. Populated by
    /// <see cref="UseSelectedAsync"/> and <see cref="RefreshSelectionAsync"/>
    /// from the bridge's SWFOC_InspectUnit response.
    /// </summary>
    public string SelectedUnitSummary
    {
        get => _selectedUnitSummary;
        private set => SetField(ref _selectedUnitSummary, value);
    }

    public ObservableCollection<string> Output => _output;

    public AsyncRelayCommand EnableGodModeCommand { get; }
    public AsyncRelayCommand DisableGodModeCommand { get; }
    public AsyncRelayCommand EnableOneHitKillCommand { get; }
    public AsyncRelayCommand DisableOneHitKillCommand { get; }
    public AsyncRelayCommand SetUnitHullCommand { get; }
    public AsyncRelayCommand EnableUnitInvulnCommand { get; }
    public AsyncRelayCommand DisableUnitInvulnCommand { get; }
    public AsyncRelayCommand EnablePreventDeathCommand { get; }
    public AsyncRelayCommand DisablePreventDeathCommand { get; }
    public AsyncRelayCommand InspectUnitCommand { get; }
    public AsyncRelayCommand GetHardpointsCommand { get; }
    public AsyncRelayCommand SpawnUnitCommand { get; }
    public AsyncRelayCommand UseSelectedCommand { get; }
    public AsyncRelayCommand RefreshSelectionCommand { get; }

    private AsyncRelayCommand Async(Func<Task> run) =>
        new(run, onError: ex => Append($"[error] {ex.Message}", error: true));

    private AsyncRelayCommand AsyncDisabled(Func<Task> run) =>
        new(run, () => false, ex => Append($"[error] {ex.Message}", error: true));

    private async Task ToggleGodModeAsync(bool enable)
    {
        var result = await _godMode
            .SetGodModeAsync(_settings.ProfileId, enable, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult($"GodMode({(enable ? "on" : "off")})", result);
    }

    private async Task ToggleOneHitKillAsync(bool enable)
    {
        var result = await _oneHitKill
            .SetOneHitKillAsync(_settings.ProfileId, enable, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult($"OneHitKill({(enable ? "on" : "off")})", result);
    }

    private async Task SetUnitHullAsync()
    {
        var addr = await ResolveEffectiveAddressAsync().ConfigureAwait(true);
        if (addr == 0UL) return;

        if (!double.TryParse(_hullHpInput, NumberStyles.Any, CultureInfo.InvariantCulture, out var hp))
        {
            Append($"[error] Hull HP must be numeric, got '{_hullHpInput}'.", error: true);
            return;
        }

        // 2026-04-27: routed through V2UnitMutationDispatcher so a bridge
        // signature change fails visibly at one place. Lua 5.0 decimal
        // formatting + InvariantCulture handling lives in the dispatcher.
        var round = await _unitMutator.SetUnitHullAsync(addr, hp, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("SetUnitHull", round);
    }

    private async Task SetUnitInvulnAsync(bool enable)
    {
        var addr = await ResolveEffectiveAddressAsync().ConfigureAwait(true);
        if (addr == 0UL) return;

        var round = await _unitMutator.SetUnitInvulnAsync(addr, enable, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip($"SetUnitInvuln({(enable ? "on" : "off")})", round);
    }

    private async Task SetPreventDeathAsync(bool enable)
    {
        var addr = await ResolveEffectiveAddressAsync().ConfigureAwait(true);
        if (addr == 0UL) return;

        var round = await _unitMutator.PreventUnitDeathAsync(addr, enable, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip($"PreventUnitDeath({(enable ? "on" : "off")})", round);
    }

    // 2026-04-28 (iter 117): per-unit Lua-method action handlers. All
    // route through V2UnitMutationDispatcher.<X>LuaAsync, which composes
    // the bridge call as `return SWFOC_<X>('<unit_lua>', '<bool|>')`.
    // Each guards on empty SelectedUnitLuaExpr — operator must type a
    // unit handle expression first (e.g. Find_First_Object("Empire_AT_AT")).

    private async Task MakeInvulnLuaAsync(bool enable)
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first (e.g. Find_First_Object(\"Empire_AT_AT\")).", error: true);
            return;
        }
        var round = await _unitMutator.MakeUnitInvulnLuaAsync(
            _selectedUnitLuaExpr, enable, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip($"MakeUnitInvulnLua({(enable ? "on" : "off")})", round);
    }

    private async Task HideUnitLuaAsync(bool enable)
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.HideUnitLuaAsync(
            _selectedUnitLuaExpr, enable, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip($"HideUnitLua({(enable ? "on" : "off")})", round);
    }

    private async Task PreventAiUsageLuaAsync(bool enable)
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.PreventAiUsageLuaAsync(
            _selectedUnitLuaExpr, enable, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip($"PreventAiUsageLua({(enable ? "on" : "off")})", round);
    }

    private async Task SetSelectableLuaAsync(bool enable)
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.SetUnitSelectableLuaAsync(
            _selectedUnitLuaExpr, enable, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip($"SetUnitSelectableLua({(enable ? "on" : "off")})", round);
    }

    private async Task DespawnUnitLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.DespawnUnitLuaAsync(
            _selectedUnitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("DespawnUnitLua", round);
    }

    private async Task StopUnitLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.StopUnitLuaAsync(
            _selectedUnitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("StopUnitLua", round);
    }

    private async Task RetreatUnitLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.RetreatUnitLuaAsync(
            _selectedUnitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("RetreatUnitLua", round);
    }

    // 2026-05-05 (iter 188): read-side helpers — same pattern as the iter-117/118
    // mutators, but the bridge's response payload contains the engine value
    // (hull / shield% / position table-handle / garrison list) which gets shown
    // in the Bridge responses ListBox.
    private async Task GetHullLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.GetHullLuaAsync(
            _selectedUnitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("GetHullLua", round);
    }

    private async Task GetShieldLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.GetShieldLuaAsync(
            _selectedUnitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("GetShieldLua", round);
    }

    private async Task GetPositionLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.GetPositionLuaAsync(
            _selectedUnitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("GetPositionLua", round);
    }

    private async Task GetGarrisonUnitsLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.GetGarrisonUnitsLuaAsync(
            _selectedUnitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("GetGarrisonUnitsLua", round);
    }

    private async Task ChangeUnitOwnerLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_targetPlayerLuaExpr))
        {
            Append("[error] Type a target player Lua expression " +
                "(e.g. Find_Player(\"REBEL\")) first.", error: true);
            return;
        }
        var round = await _unitMutator.ChangeUnitOwnerLuaAsync(
            _selectedUnitLuaExpr, _targetPlayerLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("ChangeUnitOwnerLua", round);
    }

    // 2026-05-05 (iter 194): combat-order async handlers. Each takes a target
    // (unit Lua expr for Attack/Guard, position handle for Divert) via the
    // shared TargetForCombatOrderLuaExpr field.
    private async Task AttackTargetLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_targetForCombatOrderLuaExpr))
        {
            Append("[error] Type a target Lua expression " +
                "(e.g. Find_First_Object(\"Rebel_Tank\")) first.", error: true);
            return;
        }
        var round = await _unitMutator.AttackTargetLuaAsync(
            _selectedUnitLuaExpr, _targetForCombatOrderLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("AttackTargetLua", round);
    }

    private async Task GuardTargetLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_targetForCombatOrderLuaExpr))
        {
            Append("[error] Type a target Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.GuardTargetLuaAsync(
            _selectedUnitLuaExpr, _targetForCombatOrderLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("GuardTargetLua", round);
    }

    private async Task DivertLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_targetForCombatOrderLuaExpr))
        {
            Append("[error] Type a position Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.DivertLuaAsync(
            _selectedUnitLuaExpr, _targetForCombatOrderLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("DivertLua", round);
    }

    // 2026-05-06 (iter 211): unit-method extension async handlers (iter-156 wires).
    // Each guards on empty SelectedUnitLuaExpr (and AbilityNameLuaExpr for
    // Activate_Ability) — operator must populate the input(s) before clicking.
    private async Task ActivateAbilityLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_abilityNameLuaExpr))
        {
            Append("[error] Type an ability-name Lua expression " +
                "(e.g. \"Tractor_Beam\") into the ability field first.", error: true);
            return;
        }
        var round = await _unitMutator.ActivateAbilityLuaAsync(
            _selectedUnitLuaExpr, _abilityNameLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("ActivateAbilityLua", round);
    }

    private async Task DisableCaptureLuaAsync(string boolStringArg)
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.DisableCaptureLuaAsync(
            _selectedUnitLuaExpr, boolStringArg, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip($"DisableCaptureLua({boolStringArg})", round);
    }

    private async Task SetGarrisonSpawnLuaAsync(string boolStringArg)
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.SetGarrisonSpawnLuaAsync(
            _selectedUnitLuaExpr, boolStringArg, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip($"SetGarrisonSpawnLua({boolStringArg})", round);
    }

    private async Task CancelHyperspaceLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.CancelHyperspaceLuaAsync(
            _selectedUnitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("CancelHyperspaceLua", round);
    }

    // 2026-05-06 (iter 212): unit-method mega-batch async handlers (iter-157
    // wires). Bool pairs (Set_In_Limbo + Set_Check_Contested_Space) take
    // hardcoded bool string. Sell is no-arg. Bribe needs target player
    // (reuses iter-118 TargetPlayerLuaExpr). Move_To needs position (reuses
    // iter-194 TargetForCombatOrderLuaExpr). Fire_Special_Weapon needs slot
    // (uses NEW SpecialWeaponSlotLuaExpr field).
    private async Task SetInLimboLuaAsync(string boolStringArg)
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.SetInLimboLuaAsync(
            _selectedUnitLuaExpr, boolStringArg, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip($"SetInLimboLua({boolStringArg})", round);
    }

    private async Task SetCheckContestedSpaceLuaAsync(string boolStringArg)
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.SetCheckContestedSpaceLuaAsync(
            _selectedUnitLuaExpr, boolStringArg, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip($"SetCheckContestedSpaceLua({boolStringArg})", round);
    }

    private async Task SellUnitLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.SellUnitLuaAsync(
            _selectedUnitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("SellUnitLua", round);
    }

    private async Task BribeLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_targetPlayerLuaExpr))
        {
            Append("[error] Type a target-player Lua expression " +
                "(e.g. Find_Player(\"REBEL\")) into the target player field first.", error: true);
            return;
        }
        var round = await _unitMutator.BribeLuaAsync(
            _selectedUnitLuaExpr, _targetPlayerLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("BribeLua", round);
    }

    private async Task MoveToLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_targetForCombatOrderLuaExpr))
        {
            Append("[error] Type a position Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.MoveToLuaAsync(
            _selectedUnitLuaExpr, _targetForCombatOrderLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("MoveToLua", round);
    }

    private async Task FireSpecialWeaponLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_specialWeaponSlotLuaExpr))
        {
            Append("[error] Type a slot Lua expression " +
                "(e.g. \"0\" or \"1\") into the slot field first.", error: true);
            return;
        }
        var round = await _unitMutator.FireSpecialWeaponLuaAsync(
            _selectedUnitLuaExpr, _specialWeaponSlotLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("FireSpecialWeaponLua", round);
    }

    // 2026-05-06 (iter 213): unit-method bool-batch async handlers (iter-153 +
    // iter-162). SetCannotBeKilled + EnableStealth take hardcoded bool string.
    // OverrideMaxSpeed takes numeric speed value from MaxSpeedOverrideLuaExpr.
    private async Task SetCannotBeKilledLuaAsync(string boolStringArg)
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.SetCannotBeKilledLuaAsync(
            _selectedUnitLuaExpr, boolStringArg, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip($"SetCannotBeKilledLua({boolStringArg})", round);
    }

    private async Task EnableStealthLuaAsync(string boolStringArg)
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        var round = await _unitMutator.EnableStealthLuaAsync(
            _selectedUnitLuaExpr, boolStringArg, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip($"EnableStealthLua({boolStringArg})", round);
    }

    private async Task OverrideMaxSpeedLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_maxSpeedOverrideLuaExpr))
        {
            Append("[error] Type a numeric speed Lua expression " +
                "(e.g. \"100.0\") into the speed field first.", error: true);
            return;
        }
        var round = await _unitMutator.OverrideMaxSpeedLuaAsync(
            _selectedUnitLuaExpr, _maxSpeedOverrideLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("OverrideMaxSpeedLua", round);
    }

    // 2026-05-06 (iter 218): Corrupt async handler. Anchors on iter-117
    // SelectedUnitLuaExpr; uses iter-218 CorruptAmountLuaExpr for the numeric
    // amount arg. Pairs semantically with iter-212 Bribe (both Underworld
    // signature abilities) — Bribe takes ownership, Corrupt degrades.
    private async Task CorruptLuaAsync()
    {
        if (string.IsNullOrWhiteSpace(_selectedUnitLuaExpr))
        {
            Append("[error] Type a unit Lua expression first.", error: true);
            return;
        }
        if (string.IsNullOrWhiteSpace(_corruptAmountLuaExpr))
        {
            Append("[error] Type a numeric corruption amount " +
                "(e.g. \"50\") into the corrupt amount field first.", error: true);
            return;
        }
        var round = await _unitMutator.CorruptLuaAsync(
            _selectedUnitLuaExpr, _corruptAmountLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendRoundTrip("CorruptLua", round);
    }

    private async Task InspectUnitAsync()
    {
        var addr = await ResolveEffectiveAddressAsync().ConfigureAwait(true);
        if (addr == 0UL) return;

        var result = await _unitInspector
            .InspectUnitAsync(_settings.ProfileId, (long)addr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("InspectUnit", result);
        LastInspectOrHardpoint = result.Message;
    }

    private async Task GetHardpointsAsync()
    {
        var addr = await ResolveEffectiveAddressAsync().ConfigureAwait(true);
        if (addr == 0UL) return;

        var result = await _hardpoints
            .GetHardpointsAsync(_settings.ProfileId, (long)addr, CancellationToken.None)
            .ConfigureAwait(true);
        AppendResult("GetHardpoints", result);
        LastInspectOrHardpoint = result.Message;
    }

    // ------------------------------------------------------------------
    // Selection wiring (2026-04-11)
    // ------------------------------------------------------------------
    // UseSelectedAsync: snapshot the current selection into ObjAddrInput
    // and refresh the summary label.
    // RefreshSelectionAsync: only refresh the summary (without changing
    // the textbox), used for the "Refresh" button next to the label.
    // ResolveEffectiveAddressAsync: central policy -- if AutoUseSelected
    // is on, query the bridge fresh at click time; otherwise fall back to
    // the textbox. Returns 0 and logs an error if no address is resolvable.

    private async Task UseSelectedAsync()
    {
        var addr = await QuerySelectedAddressAsync().ConfigureAwait(true);
        if (addr == 0UL)
        {
            Append("[warn] Use selected: nothing currently selected in-game.", error: true);
            SelectedUnitSummary = "(no unit selected)";
            return;
        }

        // Write back to the textbox in hex form so the user can see it.
        // The textbox still accepts hex on parse (TryParseObjAddr handles
        // both 0x-prefixed and bare hex), and the Lua emitter converts to
        // decimal before sending, so there's no round-trip issue.
        ObjAddrInput = "0x" + addr.ToString("X", CultureInfo.InvariantCulture);
        Append($"[ok] Use selected -> 0x{addr:X}", error: false);
        await RefreshSelectionSummaryAsync(addr).ConfigureAwait(true);
    }

    private async Task RefreshSelectionAsync()
    {
        var addr = await QuerySelectedAddressAsync().ConfigureAwait(true);
        if (addr == 0UL)
        {
            SelectedUnitSummary = "(no unit selected)";
            Append("[info] Refresh selection -> no current selection", error: false);
            return;
        }
        await RefreshSelectionSummaryAsync(addr).ConfigureAwait(true);
    }

    private async Task<ulong> ResolveEffectiveAddressAsync()
    {
        if (_autoUseSelected)
        {
            var addr = await QuerySelectedAddressAsync().ConfigureAwait(true);
            if (addr != 0UL) return addr;

            Append(
                "[warn] Auto-use selected is ON but nothing is selected. " +
                "Either select a unit in-game or uncheck Auto-use selected.",
                error: true);
            return 0UL;
        }

        if (!TryParseObjAddr(out var manualAddr))
        {
            return 0UL;
        }
        return manualAddr;
    }

    private async Task<ulong> QuerySelectedAddressAsync()
    {
        var round = await _bridge
            .SendRawAsync("return SWFOC_GetSelectedUnit()", CancellationToken.None)
            .ConfigureAwait(true);
        if (!round.Succeeded)
        {
            Append($"[err] QuerySelected -> {round.ErrorMessage ?? "bridge error"}", error: true);
            return 0UL;
        }
        return ParseSelectedUnitResponse(round.Response);
    }

    private async Task RefreshSelectionSummaryAsync(ulong addr)
    {
        var inspect = await _unitInspector
            .InspectUnitAsync(_settings.ProfileId, (long)addr, CancellationToken.None)
            .ConfigureAwait(true);
        SelectedUnitSummary = inspect.Succeeded
            ? $"0x{addr:X} -> {ExtractShortSummary(inspect.Message)}"
            : $"0x{addr:X} -> inspect failed: {inspect.Message}";
    }

    /// <summary>
    /// Parses the bridge response from <c>SWFOC_GetSelectedUnit()</c>. The
    /// helper returns a Lua number representing a raw pointer, which the
    /// bridge stringifies to either <c>"0"</c> (no selection) or a decimal
    /// integer (e.g. <c>"140283945472"</c>). Returns 0 on any parse
    /// failure rather than throwing so the caller can degrade gracefully.
    /// </summary>
    internal static ulong ParseSelectedUnitResponse(string? response)
    {
        if (string.IsNullOrWhiteSpace(response)) return 0UL;
        var trimmed = response.Trim();
        // Lua 5.0's tostring(number) emits fractional zero for integers
        // ("1.4e+11"). We accept both integer and scientific forms via
        // double parsing, then bounds-check the result.
        if (double.TryParse(
                trimmed,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var d)
            && d >= 0 && d < 1.8e19)  // UInt64.MaxValue sanity bound
        {
            return (ulong)d;
        }
        return 0UL;
    }

    /// <summary>
    /// Extract a short one-line summary from the bridge InspectUnit
    /// response, for the selection label. The full response is multi-field;
    /// the label only needs the most useful bits.
    /// </summary>
    private static string ExtractShortSummary(string? message)
    {
        if (string.IsNullOrWhiteSpace(message)) return "(empty)";
        var hull = TryFindField(message, "hull=");
        var owner = TryFindField(message, "owner=");
        var objId = TryFindField(message, "obj_id=");
        if (hull is null && owner is null && objId is null)
        {
            return message.Length > 80 ? message.Substring(0, 80) + "..." : message;
        }
        return $"hull={hull ?? "?"} owner={owner ?? "?"} obj_id={objId ?? "?"}";
    }

    private static string? TryFindField(string haystack, string key)
    {
        var idx = haystack.IndexOf(key, StringComparison.Ordinal);
        if (idx < 0) return null;
        var start = idx + key.Length;
        var end = start;
        while (end < haystack.Length && haystack[end] != ' ') end++;
        return haystack.Substring(start, end - start);
    }

    // ------------------------------------------------------------------
    // 2026-04-27: the previously-internal Build*LuaCommand methods that
    // lived here moved to V2UnitMutationDispatcher.cs as part of the
    // service-wrapper consolidation. Tests in
    // tests/SwfocTrainer.Tests/Regression/UnitControlLuaFormatRegressionTests.cs
    // were updated in the same commit to call the dispatcher's static
    // methods directly. See that file's header comment for the Lua-5.0
    // hex-literal background.
    // ------------------------------------------------------------------

    private async Task SpawnUnitAsync()
    {
        if (!int.TryParse(_spawnCount, NumberStyles.Any, CultureInfo.InvariantCulture, out var count) || count < 1)
        {
            Append($"[error] Spawn count must be a positive integer, got '{_spawnCount}'.", error: true);
            return;
        }

        var request = new EnhancedSpawnRequest(
            UnitId: _spawnUnitId,
            TargetFaction: _spawnFaction,
            Mode: SpawnMode.Tactical,
            Quantity: count,
            PositionKind: SpawnPositionKind.AtCamera,
            TargetPlanet: null,
            AllowCrossFaction: true,
            StopOnFailure: false);

        var result = await _enhancedSpawn
            .ExecuteSpawnAsync(_settings.ProfileId, request, CancellationToken.None)
            .ConfigureAwait(true);

        Append(
            $"[{(result.Failed == 0 ? "ok" : "err")}] Spawn '{_spawnUnitId}' x{count} -> " +
            $"attempted={result.Attempted} succeeded={result.Succeeded} failed={result.Failed}",
            error: result.Failed != 0);

        if (result.Errors.Count > 0)
        {
            foreach (var err in result.Errors)
            {
                Append($"  · {err}", error: true);
            }
        }
    }

    private bool TryParseObjAddr(out ulong addr)
    {
        addr = 0;
        var trimmed = (_objAddrInput ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            Append("[error] Obj address is empty.", error: true);
            return false;
        }

        var span = trimmed.AsSpan();
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            span = span[2..];
        }

        if (!ulong.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out addr))
        {
            Append($"[error] Obj address '{_objAddrInput}' is not a valid hex value.", error: true);
            return false;
        }

        if (addr > (ulong)long.MaxValue)
        {
            Append($"[error] Obj address '{_objAddrInput}' is beyond long.MaxValue.", error: true);
            return false;
        }

        return true;
    }

    private void AppendResult(string label, ActionExecutionResult result)
    {
        var prefix = result.Succeeded ? "[ok]" : "[err]";
        Append($"{prefix} {label} -> {result.Message}", error: !result.Succeeded);
    }

    private void AppendRoundTrip(string label, BridgeRoundTripResult round)
    {
        if (round.Succeeded)
        {
            Append($"[ok] {label} -> {round.Response}", error: false);
        }
        else
        {
            Append($"[err] {label} -> {round.ErrorMessage}", error: true);
        }
    }

    private void Append(string line, bool error)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _output.Add($"{timestamp} {line}");
        while (_output.Count > 200)
        {
            _output.RemoveAt(0);
        }
        _ = error;
    }
}
