using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Assets;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Combat tab) — INPC wrapper around CombatTabState.
/// Exposes the eight bindable inputs + four toggle commands (god / ohk /
/// ohk-attack / area-damage) + four scalar commands (damage mult / shield /
/// fire rate / target filter).
///
/// All toggles share the FeatureToggleCoordinator instance so the UI's
/// IsXxxEnabled flags update live and CleanupAllAsync runs every disable
/// callback on tab disposal.
/// </summary>
public sealed class CombatTabViewModel : ObservableBase
{
    private readonly CombatTabState _state;
    private readonly RecordingFeedbackSink _sink;
    private readonly FeatureToggleCoordinator _toggles;

    private int _slot = -1;
    private long _selectedObjAddr;
    private float _damageMultiplier = 1.0f;
    private float _shieldValue;
    private float _fireRateMultiplier = 1.0f;
    private int _targetFilterBitmask = 0x7;
    private string _lastStatus = "(idle)";
    // 2026-05-05 (iter 193): per-unit Lua-method buttons. Operator types unit
    // Lua expression once + a float arg, then picks one of 4 buttons.
    private readonly V2UnitMutationDispatcher _unitMutator;
    private string _combatUnitLuaExpr = "Find_First_Object(\"Empire_AT_AT\")";
    private string _combatUnitArg = "1.0";
    // 2026-05-07 (iter 338): Hardpoint Inspector (mirrors iter-190 Diagnostics
    // tab pattern; uses _bridge.SendRawAsync directly via SafeProbeAsync infra).
    // SWFOC_GetHardpoints LIVE @ lua_bridge.cpp:2228 (RequiresLiveSwfoc per
    // catalog). Operator types unit obj_addr → clicks Refresh → ListBox shows
    // (index, child_addr, hp) per hardpoint.
    // 2026-05-07 (iter 343): Approach A optimistic icon-resolution chain.
    // After GetHardpoints, calls SWFOC_GetTypeLua(child_addr) per child to get
    // the type name string, then ResolveWeaponIcon(typeName) to resolve PNG.
    // If tostring(GameObjectType_handle) returns name: icons render LIVE.
    // If tostring returns "userdata: 0x...": IconPath stays null (graceful
    // failure mode); iter-344 pivots to Approach B (new name-extraction wire).
    private readonly V2BridgeAdapter _bridge;
    private UnitIconResolver? _iconResolver;
    private string _hardpointInspectAddrText = "0x12345678";

    public CombatTabViewModel(
        V2BridgeAdapter bridge,
        V2UnitMutationDispatcher unitMutator,
        UnitIconResolver? iconResolver = null)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(unitMutator);
        _bridge = bridge;
        _iconResolver = iconResolver;
        _unitMutator = unitMutator;
        _sink = new RecordingFeedbackSink();
        _toggles = new FeatureToggleCoordinator(_sink);
        var dispatcher = new BridgeCombatDispatcher(bridge);
        _state = new CombatTabState(dispatcher, _sink, _toggles);

        ToggleGodModeCommand = new AsyncRelayCommand(ToggleGodCore, onError: HandleError);
        ToggleOhkCommand = new AsyncRelayCommand(ToggleOhkCore, onError: HandleError);
        ToggleOhkAttackPowerCommand = new AsyncRelayCommand(ToggleOhkAttackCore, () => false, HandleError);
        ToggleAreaDamageCommand = new AsyncRelayCommand(ToggleAreaDamageCore, () => false, HandleError);
        SetDamageMultiplierCommand = new AsyncRelayCommand(SetDamageMultCore, () => false, HandleError);
        // 2026-04-28 (iter 100): GLOBAL damage-multiplier sibling — LIVE
        // via Take_Damage_Outer detour. Different surface from the per-slot
        // SetDamageMultiplier; uses the same DamageMultiplier slider.
        SetDamageMultiplierGlobalCommand = new AsyncRelayCommand(
            SetDamageMultGlobalCore, onError: HandleError);
        // 2026-05-06 (iter 227): GLOBAL fire-rate-multiplier sibling — LIVE
        // via iter-225 WeaponTick MinHook detour @ 0x387010 (closes 124-day
        // A1.3 deferral). Different surface from the per-slot SetFireRate;
        // uses the same FireRateMultiplier slider.
        SetFireRateMultiplierGlobalCommand = new AsyncRelayCommand(
            SetFireRateMultGlobalCore, onError: HandleError);
        GetFireRateMultiplierGlobalCommand = new AsyncRelayCommand(
            GetFireRateMultGlobalCore, onError: HandleError);
        SetUnitShieldCommand = new AsyncRelayCommand(SetUnitShieldCore, onError: HandleError);
        SetFireRateCommand = new AsyncRelayCommand(SetFireRateCore, () => false, HandleError);
        SetTargetFilterCommand = new AsyncRelayCommand(SetTargetFilterCore, () => false, HandleError);

        // 2026-04-27 (iter 56): per-button capability metadata. The
        // group-level "REPLAY MIRROR ONLY" amber banner from the original
        // PHASE 2 PENDING work (iter 1, task #224) lives in XAML. Per-button
        // badges below give the operator a finer-grained signal — at a
        // glance: God-mode + OHK are LIVE today; the four scalar setters
        // (damage/shield/fire-rate/area-damage) plus target-filter are
        // PHASE 2 PENDING (Phase-1-mirror only). OHK-attack-power is a
        // sibling toggle of OHK and shares its PHASE 2 PENDING status.
        // 2026-04-28 (iter 76): difficulty presets for the Combat
        // scalars. Operators get one-click damage + fire-rate combos
        // for streaming/recording use cases. Same pattern as iter-12
        // Speed presets. Each preset sets the bound DamageMultiplier
        // + FireRateMultiplier through the existing setters (which
        // already update the underlying TabState), then fires the
        // existing SetDamageMultiplier + SetFireRate commands so the
        // operator gets the same status feedback as a manual click.
        // Note: the underlying SWFOC_SetDamageMultiplier /
        // SWFOC_SetFireRate are PHASE 2 PENDING — replay-harness
        // verifiable today; live engine effect lands when the IDA
        // pin completes. The MIXED badge surfaces this via the
        // existing iter-56 capability infrastructure.
        ApplyEasyPresetCommand = new AsyncRelayCommand(
            () => ApplyDifficultyPresetAsync(damageMult: 0.5f, fireRateMult: 0.75f),
            onError: HandleError);
        ApplyNormalPresetCommand = new AsyncRelayCommand(
            () => ApplyDifficultyPresetAsync(damageMult: 1.0f, fireRateMult: 1.0f),
            onError: HandleError);
        ApplyHardPresetCommand = new AsyncRelayCommand(
            () => ApplyDifficultyPresetAsync(damageMult: 1.5f, fireRateMult: 1.25f),
            onError: HandleError);
        ApplyHardcorePresetCommand = new AsyncRelayCommand(
            () => ApplyDifficultyPresetAsync(damageMult: 2.5f, fireRateMult: 1.5f),
            onError: HandleError);

        // 2026-05-05 (iter 193): per-unit combat Lua native UX commands +
        // capability metadata. All 4 wires LIVE since iter 154.
        HealUnitLuaCommand = new AsyncRelayCommand(HealUnitLuaCore, onError: HandleError);
        TakeDamageLuaCommand = new AsyncRelayCommand(TakeDamageLuaCore, onError: HandleError);
        SetDamageModifierLuaCommand = new AsyncRelayCommand(SetDamageModifierLuaCore, onError: HandleError);
        SetRateOfFireModifierLuaCommand = new AsyncRelayCommand(SetRateOfFireModifierLuaCore, onError: HandleError);
        HealUnitLua = new CapabilityAwareAction("Heal unit (Lua)", "SWFOC_HealUnitLua");
        TakeDamageLua = new CapabilityAwareAction("Take damage (Lua)", "SWFOC_TakeDamageLua");
        SetDamageModifierLua = new CapabilityAwareAction("Set damage modifier (Lua)", "SWFOC_SetDamageModifierLua");
        SetRateOfFireModifierLua = new CapabilityAwareAction("Set RoF modifier (Lua)", "SWFOC_SetRateOfFireModifierLua");

        // 2026-05-06 (iter 219): Suspend_AI(seconds) cinematic helper —
        // last unsurfaced wire from the iter-216 changelog queue.
        // Pauses AI decision-making for the given duration. Pairs with
        // iter-208 Lock_Controls + iter-145 cinematic camera quad for
        // full battle-pause cinematic recording workflow.
        SuspendAiLuaCommand = new AsyncRelayCommand(SuspendAiLuaCore, onError: HandleError);
        SuspendAiLua = new CapabilityAwareAction("Suspend AI (Lua)", "SWFOC_SuspendAiLua");

        // 2026-05-07 (iter 338): Hardpoint Inspector wire-up (closes iter-336
        // honest-defer for Combat tab weapon work). RefreshHardpoints calls
        // SWFOC_GetHardpoints + parses the bridge reply.
        RefreshHardpointsCommand = new AsyncRelayCommand(RefreshHardpointsCore, onError: HandleError);
        RefreshHardpoints = new CapabilityAwareAction("Refresh hardpoints", "SWFOC_GetHardpoints");

        ToggleGodMode = new CapabilityAwareAction("Toggle god mode", "SWFOC_GodMode");
        ToggleOhk = new CapabilityAwareAction("Toggle one-hit-kill", "SWFOC_OneHitKill");
        ToggleOhkAttackPower = new CapabilityAwareAction("Toggle OHK attack-power", "SWFOC_ToggleOHKAttackPower");
        ToggleAreaDamage = new CapabilityAwareAction("Toggle area damage", "SWFOC_SetAreaDamage");
        SetDamageMultiplier = new CapabilityAwareAction("Set damage multiplier", "SWFOC_SetDamageMultiplier");
        // 2026-04-28 (iter 96 + iter 100): LIVE global sibling — Take_Damage_Outer detour scales damageParams[0].
        SetDamageMultiplierGlobal = new CapabilityAwareAction(
            "Set GLOBAL damage multiplier", "SWFOC_SetDamageMultiplierGlobal");
        // 2026-05-06 (iter 225 + iter 227): LIVE global sibling — WeaponTick
        // MinHook detour @ 0x387010 scales `dt` arg by g_fireRateMult_global.
        // Closes A1.3 SetFireRate global path (124-day deferral).
        SetFireRateMultiplierGlobal = new CapabilityAwareAction(
            "Set GLOBAL fire rate multiplier", "SWFOC_SetFireRateMultiplierGlobal");
        GetFireRateMultiplierGlobal = new CapabilityAwareAction(
            "Read GLOBAL fire rate multiplier", "SWFOC_GetFireRateMultiplierGlobal");
        SetUnitShield = new CapabilityAwareAction("Set unit shield", "SWFOC_SetUnitShield");
        SetFireRate = new CapabilityAwareAction("Set fire rate", "SWFOC_SetFireRate");
        SetTargetFilter = new CapabilityAwareAction("Set target filter", "SWFOC_SetTargetFilter");
    }

    public CapabilityAwareAction ToggleGodMode { get; }
    public CapabilityAwareAction ToggleOhk { get; }
    public CapabilityAwareAction ToggleOhkAttackPower { get; }
    public CapabilityAwareAction ToggleAreaDamage { get; }
    public CapabilityAwareAction SetDamageMultiplier { get; }
    /// <summary>2026-04-28 (iter 100): LIVE global damage multiplier.</summary>
    public CapabilityAwareAction SetDamageMultiplierGlobal { get; }
    /// <summary>2026-05-06 (iter 225 + iter 227): LIVE global fire-rate multiplier (WeaponTick MinHook detour). A1.3 closure.</summary>
    public CapabilityAwareAction SetFireRateMultiplierGlobal { get; }
    /// <summary>2026-05-06 (iter 227): LIVE read-back of global fire-rate multiplier.</summary>
    public CapabilityAwareAction GetFireRateMultiplierGlobal { get; }
    public CapabilityAwareAction SetUnitShield { get; }
    public CapabilityAwareAction SetFireRate { get; }
    public CapabilityAwareAction SetTargetFilter { get; }

    // 2026-05-05 (iter 193) — per-unit combat Lua actions.
    public CapabilityAwareAction HealUnitLua { get; }
    public CapabilityAwareAction TakeDamageLua { get; }
    public CapabilityAwareAction SetDamageModifierLua { get; }
    public CapabilityAwareAction SetRateOfFireModifierLua { get; }

    // 2026-05-06 (iter 219) — Suspend_AI cinematic helper.
    public CapabilityAwareAction SuspendAiLua { get; }
    public ICommand SuspendAiLuaCommand { get; }

    /// <summary>
    /// 2026-05-06 (iter 219): numeric seconds expression for Suspend_AI.
    /// Operator types e.g. <c>"5"</c> for "pause AI for 5 seconds". Default
    /// empty so the operator sees the "(no seconds)" feedback when they
    /// click without typing.
    /// </summary>
    private string _suspendAiSecondsLuaExpr = string.Empty;
    public string SuspendAiSecondsLuaExpr
    {
        get => _suspendAiSecondsLuaExpr;
        set => SetField(ref _suspendAiSecondsLuaExpr, value ?? string.Empty);
    }

    public string CombatUnitLuaExpr
    {
        get => _combatUnitLuaExpr;
        set => SetField(ref _combatUnitLuaExpr, value ?? string.Empty);
    }

    public string CombatUnitArg
    {
        get => _combatUnitArg;
        set => SetField(ref _combatUnitArg, value ?? string.Empty);
    }

    public IReadOnlyList<CapabilityAwareAction> AllActions => new[]
    {
        ToggleGodMode, ToggleOhk, ToggleOhkAttackPower, ToggleAreaDamage,
        SetDamageMultiplier, SetDamageMultiplierGlobal,
        SetUnitShield, SetFireRate, SetTargetFilter,
        // iter 193: per-unit combat Lua actions
        HealUnitLua, TakeDamageLua, SetDamageModifierLua, SetRateOfFireModifierLua,
        // iter 219: Suspend_AI cinematic helper (closes iter-216 queue)
        SuspendAiLua,
        // iter 227: GLOBAL fire-rate multiplier pair (closes A1.3 after 124-day deferral)
        SetFireRateMultiplierGlobal, GetFireRateMultiplierGlobal,
    };

    /// <summary>
    /// True when the tab contains both LIVE and non-LIVE actions. Most of
    /// the scalar actions are PHASE 2 PENDING today, so this is expected
    /// to stay <c>true</c> until the Phase 2 hook detours land.
    /// </summary>
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

    public int Slot
    {
        get => _slot;
        set { if (SetField(ref _slot, value)) _state.Slot = value; }
    }

    public long SelectedObjAddr
    {
        get => _selectedObjAddr;
        set { if (SetField(ref _selectedObjAddr, value)) _state.SelectedObjAddr = value; }
    }

    public float DamageMultiplier
    {
        get => _damageMultiplier;
        set { if (SetField(ref _damageMultiplier, value)) _state.DamageMultiplier = value; }
    }

    public float ShieldValue
    {
        get => _shieldValue;
        set { if (SetField(ref _shieldValue, value)) _state.ShieldValue = value; }
    }

    public float FireRateMultiplier
    {
        get => _fireRateMultiplier;
        set { if (SetField(ref _fireRateMultiplier, value)) _state.FireRateMultiplier = value; }
    }

    public int TargetFilterBitmask
    {
        get => _targetFilterBitmask;
        set
        {
            if (SetField(ref _targetFilterBitmask, value))
            {
                _state.TargetFilterBitmask = value;
                // 2026-04-27: notify the per-bit accessors so the checkbox UI
                // tracks the bitmask state when SetField returns true.
                OnPropertyChanged(nameof(FilterIncludesEnemy));
                OnPropertyChanged(nameof(FilterIncludesFriendly));
                OnPropertyChanged(nameof(FilterIncludesNeutral));
            }
        }
    }

    /// <summary>
    /// 2026-04-27: per-bit checkbox accessors for <see cref="TargetFilterBitmask"/>.
    /// Operators were previously typing a 3-bit hex value into a TextBox
    /// (0x1=ENEMY, 0x2=FRIENDLY, 0x4=NEUTRAL); the audit flagged this as
    /// the kind of opaque input that drives users to the wiki for every
    /// adjustment. The bitmask property still exposes the raw int so the
    /// underlying state machine and tests stay untouched.
    /// </summary>
    public bool FilterIncludesEnemy
    {
        get => (TargetFilterBitmask & 0x1) != 0;
        set => TargetFilterBitmask = value
            ? (TargetFilterBitmask | 0x1)
            : (TargetFilterBitmask & ~0x1);
    }

    public bool FilterIncludesFriendly
    {
        get => (TargetFilterBitmask & 0x2) != 0;
        set => TargetFilterBitmask = value
            ? (TargetFilterBitmask | 0x2)
            : (TargetFilterBitmask & ~0x2);
    }

    public bool FilterIncludesNeutral
    {
        get => (TargetFilterBitmask & 0x4) != 0;
        set => TargetFilterBitmask = value
            ? (TargetFilterBitmask | 0x4)
            : (TargetFilterBitmask & ~0x4);
    }

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge(
        "SWFOC_GodMode", "SWFOC_OneHitKill", "SWFOC_ToggleOHKAttackPower", "SWFOC_SetAreaDamage",
        "SWFOC_SetDamageMultiplier", "SWFOC_SetDamageMultiplierGlobal",
        "SWFOC_SetUnitShield", "SWFOC_SetFireRate", "SWFOC_SetTargetFilter",
        // iter 227 — global fire-rate multiplier (LIVE)
        "SWFOC_SetFireRateMultiplierGlobal", "SWFOC_GetFireRateMultiplierGlobal");

    public bool IsGodModeEnabled => _toggles.IsEnabled("god_mode");
    public bool IsOhkEnabled => _toggles.IsEnabled("one_hit_kill");
    public bool IsOhkAttackPowerEnabled => _toggles.IsEnabled("ohk_attack_power");
    public bool IsAreaDamageEnabled => _toggles.IsEnabled("area_damage");

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand ToggleGodModeCommand { get; }
    public ICommand ToggleOhkCommand { get; }
    public ICommand ToggleOhkAttackPowerCommand { get; }
    public ICommand ToggleAreaDamageCommand { get; }
    public ICommand SetDamageMultiplierCommand { get; }
    /// <summary>
    /// 2026-04-28 (iter 96 + iter 100): set the GLOBAL damage multiplier.
    /// LIVE through Take_Damage_Outer detour at RVA 0x38A350.
    /// </summary>
    public ICommand SetDamageMultiplierGlobalCommand { get; }
    /// <summary>
    /// 2026-05-06 (iter 225 + iter 227): set the GLOBAL fire-rate multiplier.
    /// LIVE through WeaponTick MinHook detour at RVA 0x387010 — closes A1.3
    /// SetFireRate global path (124-day deferral).
    /// </summary>
    public ICommand SetFireRateMultiplierGlobalCommand { get; }
    /// <summary>
    /// 2026-05-06 (iter 227): read-back the global fire-rate multiplier.
    /// </summary>
    public ICommand GetFireRateMultiplierGlobalCommand { get; }
    public ICommand SetUnitShieldCommand { get; }
    public ICommand SetFireRateCommand { get; }
    public ICommand SetTargetFilterCommand { get; }

    // 2026-05-05 (iter 193) — per-unit combat Lua commands.
    public ICommand HealUnitLuaCommand { get; }
    public ICommand TakeDamageLuaCommand { get; }
    public ICommand SetDamageModifierLuaCommand { get; }
    public ICommand SetRateOfFireModifierLuaCommand { get; }

    /// <summary>2026-04-28 (iter 76): one-click "Easy" preset — half damage, slower fire rate.</summary>
    public ICommand ApplyEasyPresetCommand { get; }
    /// <summary>One-click "Normal" preset — multipliers reset to 1.0 / 1.0.</summary>
    public ICommand ApplyNormalPresetCommand { get; }
    /// <summary>One-click "Hard" preset — 1.5× damage, 1.25× fire rate.</summary>
    public ICommand ApplyHardPresetCommand { get; }
    /// <summary>One-click "Hardcore" preset — 2.5× damage, 1.5× fire rate.</summary>
    public ICommand ApplyHardcorePresetCommand { get; }

    private async Task ToggleGodCore()
    {
        var next = !_toggles.IsEnabled("god_mode");
        ApplyFeedback(await _state.ToggleGodModeAsync(next));
        OnPropertyChanged(nameof(IsGodModeEnabled));
    }
    private async Task ToggleOhkCore()
    {
        var next = !_toggles.IsEnabled("one_hit_kill");
        ApplyFeedback(await _state.ToggleOhkAsync(next));
        OnPropertyChanged(nameof(IsOhkEnabled));
    }
    private async Task ToggleOhkAttackCore()
    {
        var next = !_toggles.IsEnabled("ohk_attack_power");
        ApplyFeedback(await _state.ToggleOhkAttackPowerAsync(next));
        OnPropertyChanged(nameof(IsOhkAttackPowerEnabled));
    }
    private async Task ToggleAreaDamageCore()
    {
        var next = !_toggles.IsEnabled("area_damage");
        ApplyFeedback(await _state.ToggleAreaDamageAsync(next));
        OnPropertyChanged(nameof(IsAreaDamageEnabled));
    }
    private async Task SetDamageMultCore() => ApplyFeedback(await _state.SetDamageMultiplierAsync());
    private async Task SetDamageMultGlobalCore() =>
        ApplyFeedback(await _state.SetDamageMultiplierGlobalAsync());
    private async Task SetUnitShieldCore() => ApplyFeedback(await _state.SetUnitShieldAsync());
    private async Task SetFireRateCore() => ApplyFeedback(await _state.SetFireRateAsync());
    private async Task SetTargetFilterCore() => ApplyFeedback(await _state.SetTargetFilterAsync());

    // 2026-05-06 (iter 227) -- global fire rate multiplier handlers. The
    // FireRateMultiplier slider is already bound to _state.FireRateMultiplier
    // via the existing iter-100 setter; the new Set/Get methods on
    // CombatTabState use that same value.
    private async Task SetFireRateMultGlobalCore() =>
        ApplyFeedback(await _state.SetFireRateMultiplierGlobalAsync());
    private async Task GetFireRateMultGlobalCore()
    {
        var v = await _state.GetFireRateMultiplierGlobalAsync().ConfigureAwait(true);
        LastStatus = $"[ok] GetFireRateMultiplierGlobal -> {v.ToString("0.000", CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// 2026-04-28 (iter 76): apply a difficulty preset by setting the
    /// bound multiplier properties (which propagate to TabState) then
    /// firing both GLOBAL Set commands in sequence so the operator gets a
    /// fully live preset path. The legacy per-slot damage/fire-rate helpers
    /// remain disabled until their live hook exists.
    /// </summary>
    internal async Task ApplyDifficultyPresetAsync(float damageMult, float fireRateMult)
    {
        DamageMultiplier = damageMult;
        FireRateMultiplier = fireRateMult;
        // Sequence the two underlying applies so the LastStatus reflects
        // both. The second one wins the LastStatus message; that's fine
        // — the operator sees both fire in the activity log via the
        // iter-45 ring buffer.
        await SetDamageMultGlobalCore().ConfigureAwait(true);
        await SetFireRateMultGlobalCore().ConfigureAwait(true);
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

    // 2026-05-05 (iter 193) — per-unit combat Lua command handlers. Each
    // validates the unit Lua expression is non-empty, then dispatches via
    // V2UnitMutationDispatcher (iter-154 float-arg helper). Result lands in
    // LastStatus.
    private async Task HealUnitLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_combatUnitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        var round = await _unitMutator.HealUnitLuaAsync(_combatUnitLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" HealUnitLua({_combatUnitLuaExpr}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task TakeDamageLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_combatUnitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        if (string.IsNullOrWhiteSpace(_combatUnitArg))
        {
            LastStatus = "(no damage amount — type a number into the Arg field first)";
            return;
        }
        var round = await _unitMutator.TakeDamageLuaAsync(
            _combatUnitLuaExpr, _combatUnitArg, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" TakeDamageLua({_combatUnitLuaExpr}, {_combatUnitArg}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task SetDamageModifierLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_combatUnitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        if (string.IsNullOrWhiteSpace(_combatUnitArg))
        {
            LastStatus = "(no modifier — type a multiplier into the Arg field first)";
            return;
        }
        var round = await _unitMutator.SetDamageModifierLuaAsync(
            _combatUnitLuaExpr, _combatUnitArg, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" SetDamageModifierLua({_combatUnitLuaExpr}, {_combatUnitArg}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    private async Task SetRateOfFireModifierLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_combatUnitLuaExpr))
        {
            LastStatus = "(no unit Lua expression — type one above first)";
            return;
        }
        if (string.IsNullOrWhiteSpace(_combatUnitArg))
        {
            LastStatus = "(no modifier — type a multiplier into the Arg field first)";
            return;
        }
        var round = await _unitMutator.SetRateOfFireModifierLuaAsync(
            _combatUnitLuaExpr, _combatUnitArg, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" SetRateOfFireModifierLua({_combatUnitLuaExpr}, {_combatUnitArg}) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    // 2026-05-06 (iter 219) — Suspend_AI cinematic helper handler. Single
    // numeric seconds arg via SuspendAiSecondsLuaExpr field. No unit
    // expression needed (global wire). Last unsurfaced wire from iter-216
    // changelog queue.
    private async Task SuspendAiLuaCore()
    {
        if (string.IsNullOrWhiteSpace(_suspendAiSecondsLuaExpr))
        {
            LastStatus = "(no seconds — type a duration into the Suspend AI field first)";
            return;
        }
        var round = await _unitMutator.SuspendAiLuaAsync(
            _suspendAiSecondsLuaExpr, CancellationToken.None)
            .ConfigureAwait(true);
        LastStatus = (round.Succeeded ? "[ok]" : "[err]") +
            $" SuspendAiLua({_suspendAiSecondsLuaExpr}s) -> {(round.Succeeded ? round.Response : round.ErrorMessage)}";
    }

    // ── 2026-05-07 (iter 338) Hardpoint Inspector ─────────────────
    // Smaller-scope read-only inspector. Defers icon resolution to iter-339+
    // (would need 2-bridge-call chain GetHardpoints → per-child GetType).

    public string HardpointInspectAddrText
    {
        get => _hardpointInspectAddrText;
        set => SetField(ref _hardpointInspectAddrText, value ?? "0x12345678");
    }

    /// <summary>Bound to the Combat tab Hardpoint Inspector ListBox ItemsSource.</summary>
    public ObservableCollection<HardpointEntry> Hardpoints { get; } = new();

    public ICommand RefreshHardpointsCommand { get; }
    public CapabilityAwareAction RefreshHardpoints { get; }

    private async Task RefreshHardpointsCore()
    {
        var addrText = _hardpointInspectAddrText?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(addrText))
        {
            LastStatus = "[err] RefreshHardpoints: provide a unit obj_addr (e.g. 0x12345678)";
            return;
        }
        // Parse hex/decimal obj_addr per iter-194 UnitControl pattern.
        var trimmed = addrText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? addrText.Substring(2)
            : addrText;
        if (!ulong.TryParse(trimmed,
            addrText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                ? NumberStyles.HexNumber : NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var addr))
        {
            LastStatus = $"[err] RefreshHardpoints: cannot parse addr '{addrText}' as hex or decimal";
            return;
        }
        // Mirror iter-190 Diagnostics tab pattern — direct _bridge.SendRawAsync.
        // BridgeRoundTripResult has Succeeded + Response + ErrorMessage.
        var script = $"return SWFOC_GetHardpoints({addr})";
        var round = await _bridge.SendRawAsync(script, CancellationToken.None)
            .ConfigureAwait(true);
        if (!round.Succeeded)
        {
            LastStatus = $"[err] RefreshHardpoints({addrText}) -> {round.ErrorMessage}";
            return;
        }
        var entries = HardpointEntry.ParseListFromBridgeReply(round.Response);
        // 2026-05-07 (iter 343): Approach A optimistic icon-resolution chain.
        // For each hardpoint child, call SWFOC_GetTypeLua + ResolveWeaponIcon.
        // Per iter-342 design: if tostring(handle) returns name, icons resolve;
        // if it returns userdata, IconPath stays null (graceful failure).
        var enriched = new List<HardpointEntry>(entries.Count);
        foreach (var e in entries)
        {
            var iconPath = await ResolveHardpointIconAsync(e.ChildAddr).ConfigureAwait(true);
            enriched.Add(e with { IconPath = iconPath });
        }
        Hardpoints.Clear();
        foreach (var e in enriched) Hardpoints.Add(e);
        var withIcons = enriched.Count(h => h.IconPath != null);
        LastStatus = $"[ok] RefreshHardpoints({addrText}) -> {entries.Count} hardpoint(s); {withIcons} with icons";
    }

    /// <summary>
    /// 2026-05-07 (iter 343): per-hardpoint icon resolution via Approach A.
    /// Calls SWFOC_GetTypeLua(child_addr) → expects type name string →
    /// ResolveWeaponIcon(typeName) → PNG path. Returns null on any failure
    /// (no resolver wired, bridge error, GetTypeLua returns userdata, or
    /// resolver finds no matching DDS).
    /// </summary>
    private async Task<string?> ResolveHardpointIconAsync(long childAddr)
    {
        if (_iconResolver is null) return null;
        var typeScript = $"return SWFOC_GetTypeLua({childAddr})";
        var typeRound = await _bridge.SendRawAsync(typeScript, CancellationToken.None)
            .ConfigureAwait(true);
        if (!typeRound.Succeeded || string.IsNullOrWhiteSpace(typeRound.Response))
        {
            return null;
        }
        var typeName = typeRound.Response.Trim();
        // Defensive: if tostring(GameObjectType_handle) returns "userdata: 0x..."
        // instead of a clean type name, ResolveWeaponIcon will fail to match
        // any DDS and return null (graceful failure — operator sees text-only
        // hardpoint row, no broken-image icon).
        if (typeName.StartsWith("userdata:", StringComparison.OrdinalIgnoreCase) ||
            typeName.StartsWith("ERR:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return _iconResolver.ResolveWeaponIcon(typeName);
    }

    /// <summary>
    /// 2026-05-07 (iter 343): hot-swap the icon resolver. Composition root
    /// (MainViewModelV2) calls this when operator changes Settings.IconsRoot.
    /// Mirrors iter-312/iter-321 SetIconResolver pattern. Hardpoints already
    /// in the ObservableCollection are NOT auto-refreshed — operator clicks
    /// Refresh to pick up icon changes (avoids surprise UI freezes for large
    /// hardpoint vectors).
    /// </summary>
    public void SetIconResolver(UnitIconResolver? iconResolver)
    {
        _iconResolver = iconResolver;
    }
}
