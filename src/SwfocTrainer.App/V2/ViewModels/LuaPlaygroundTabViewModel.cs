using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Lua Playground tab) — INPC wrapper around
/// LuaPlaygroundTabState. The recipe library is in-memory; persistence
/// to disk is owned by App.V2.Infrastructure.V2RecipeStore (independent
/// concern).
///
/// All Run results land as Warning severity — the playground bypasses every
/// typed validation surface, so the operator should treat even "OK" results
/// as "did what I just type actually do?".
/// </summary>
public sealed class LuaPlaygroundTabViewModel : ObservableBase
{
    private readonly LuaPlaygroundTabState _state;
    private readonly RecordingFeedbackSink _sink;
    private readonly ObservableCollection<string> _recipeNames = new();

    private string _scriptText = string.Empty;
    private string _recipeName = string.Empty;
    private string _selectedRecipeName = string.Empty;
    private string _lastResponse = string.Empty;
    private string _lastStatus = "(idle)";

    public LuaPlaygroundTabViewModel(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _sink = new RecordingFeedbackSink();
        var dispatcher = new BridgeLuaPlaygroundDispatcher(bridge);
        _state = new LuaPlaygroundTabState(dispatcher, _sink);

        RunCommand = new AsyncRelayCommand(RunCore, onError: HandleError);
        SaveRecipeCommand = new RelayCommand(SaveRecipe);
        LoadRecipeCommand = new RelayCommand(LoadRecipe);
        DeleteRecipeCommand = new RelayCommand(DeleteRecipe);
        // 2026-04-27: clipboard ergonomics for the Lua playground.
        // Operators iterate scripts; copying the running script to share
        // / archive is a common need.
        CopyScriptCommand = new RelayCommand(CopyScriptToClipboard);
        CopyResponseCommand = new RelayCommand(CopyResponseToClipboard);

        // 2026-04-27 (iter 59): per-button capability metadata. Run Lua
        // routes through SWFOC_DoString — engine-native escape hatch is
        // catalogued LIVE.
        Run = new CapabilityAwareAction("Run Lua", "SWFOC_DoString");
    }

    public ICommand CopyScriptCommand { get; }
    public ICommand CopyResponseCommand { get; }

    public CapabilityAwareAction Run { get; }
    public IReadOnlyList<CapabilityAwareAction> AllActions => new[] { Run };

    /// <summary>
    /// 2026-04-28 (iter 116): operator quick-paste presets covering every
    /// LIVE wire shipped in iter 100-113. Bound to a ComboBox in the
    /// Playground UI; selecting a preset overwrites <see cref="ScriptText"/>
    /// with the example invocation. Saves the operator from grepping
    /// <c>docs/iter-100-113-quick-reference.md</c> every time.
    /// </summary>
    public sealed record LuaPreset(string Label, string Script);

    public IReadOnlyList<LuaPreset> Iter100to113Presets { get; } = new LuaPreset[]
    {
        new("— pick a LIVE wire —", string.Empty),
        // Speed (iter 100)
        new("[100] Set unit speed (250)",
            "return SWFOC_SetUnitSpeed(0xABCD, 250)"),
        new("[100] Get unit speed",
            "return SWFOC_GetUnitSpeed(0xABCD)"),
        new("[100] Set per-faction speed (slot 0 → 350)",
            "return SWFOC_SetPerFactionSpeedMultiplier(0, 350)"),
        new("[100] Revert per-unit speed override",
            "return SWFOC_ClearUnitSpeedOverride(0xABCD)"),
        // Damage (iter 96/102)
        new("[96]  Set GLOBAL damage multiplier (2.0×)",
            "return SWFOC_SetDamageMultiplierGlobal(2.0)"),
        new("[96]  Get GLOBAL damage multiplier",
            "return SWFOC_GetDamageMultiplierGlobal()"),
        // Camera (iter 107)
        new("[107] Scroll camera to planet (Yavin)",
            "return SWFOC_ScrollCameraToTarget('Find_Planet(\"Yavin\")')"),
        new("[107] Scroll camera to first AT-AT",
            "return SWFOC_ScrollCameraToTarget('Find_First_Object(\"Empire_AT_AT\")')"),
        // Owner (iter 108)
        new("[108] Convert first Empire AT-AT → Rebel",
            "return SWFOC_ChangeUnitOwner('Find_First_Object(\"Empire_AT_AT\")', 'Find_Player(\"REBEL\")')"),
        // Spawn (iter 109)
        new("[109] Spawn Rebel Trooper Squad at origin",
            "return SWFOC_SpawnUnitLua('Find_Player(\"REBEL\")', 'Find_Object_Type(\"Rebel_Trooper_Squad\")', 'Create_Position(0, 0, 0)')"),
        // Per-unit toggles (iter 110, 111)
        new("[110] Make first AT-AT invulnerable",
            "return SWFOC_MakeUnitInvulnLua('Find_First_Object(\"Empire_AT_AT\")', 'true')"),
        new("[111] Hide first AT-AT",
            "return SWFOC_HideUnitLua('Find_First_Object(\"Empire_AT_AT\")', 'true')"),
        new("[111] Lock AT-AT away from AI",
            "return SWFOC_PreventAiUsageLua('Find_First_Object(\"Empire_AT_AT\")', 'true')"),
        new("[111] Make AT-AT non-selectable",
            "return SWFOC_SetUnitSelectableLua('Find_First_Object(\"Empire_AT_AT\")', 'false')"),
        // Per-unit actions (iter 112)
        new("[112] Despawn first AT-AT",
            "return SWFOC_DespawnUnitLua('Find_First_Object(\"Empire_AT_AT\")')"),
        new("[112] Stop first AT-AT",
            "return SWFOC_StopUnitLua('Find_First_Object(\"Empire_AT_AT\")')"),
        new("[112] Retreat first Rebel Trooper Squad",
            "return SWFOC_RetreatUnitLua('Find_First_Object(\"Rebel_Trooper_Squad\")')"),
        // Universal escape hatch (iter 113)
        new("[113] Give Rebels 5000 credits (universal escape hatch)",
            "return SWFOC_CallObjMethodLua('Find_Player(\"REBEL\")', 'Give_Money', '5000')"),
        new("[113] Heal first AT-AT (universal, no-arg)",
            "return SWFOC_CallObjMethodLua('Find_First_Object(\"Empire_AT_AT\")', 'Heal', '')"),
        new("[113] Activate Empire DEATH_STAR superweapon",
            "return SWFOC_CallObjMethodLua('Find_Player(\"EMPIRE\")', 'Activate_Power', '\"DEATH_STAR\"')"),
        // Camera primitive arc — iter 143-145 (closes the iter 106 set)
        new("[143] Camera follow first AT-AT (track as it moves)",
            "return SWFOC_CameraFollow('Find_First_Object(\"Empire_AT_AT\")')"),
        new("[144] Rotate camera to face first Rebel Tank",
            "return SWFOC_RotateCameraTo('Find_First_Object(\"Rebel_T2A_Tank\")')"),
        new("[145] Start cinematic camera mode",
            "return SWFOC_StartCinematicCamera()"),
        new("[145] Set cinematic camera key (1, Yavin, 5.0s)",
            "return SWFOC_SetCinematicCameraKey('1, Find_Planet(\"Yavin\"), 5.0')"),
        new("[145] Transition cinematic camera key (1 → 2 over 2.5s)",
            "return SWFOC_TransitionCinematicCameraKey('1, 2, 2.5')"),
        new("[145] End cinematic camera mode",
            "return SWFOC_EndCinematicCamera()"),
        // 2026-04-29 (iter 150) — letterbox toggles
        new("[150] Letterbox ON (cinematic mode start)",
            "return SWFOC_LetterBoxOn()"),
        new("[150] Letterbox OFF",
            "return SWFOC_LetterBoxOff()"),
        // 2026-04-29 (iter 151-152) — teleport + galactic spawn
        new("[151] Teleport AT-AT to Yavin",
            "return SWFOC_TeleportUnitLua('Find_First_Object(\"Empire_AT_AT\")', 'Find_Planet(\"Yavin\")')"),
        new("[152] Galactic-spawn Rebel Trooper at Hoth",
            "return SWFOC_GalacticSpawnUnit('Find_Player(\"REBEL\")', 'Find_Object_Type(\"Rebel_Trooper_Squad\")', 'Find_Planet(\"Hoth\")')"),
        // 2026-04-29 (iter 153) — invuln/stealth bool toggles
        new("[153] Set Cannot_Be_Killed on AT-AT",
            "return SWFOC_SetCannotBeKilledLua('Find_First_Object(\"Empire_AT_AT\")', 'true')"),
        new("[153] Enable stealth on AT-AT",
            "return SWFOC_EnableStealthLua('Find_First_Object(\"Empire_AT_AT\")', 'true')"),
        // 2026-04-29 (iter 154) — float-arg unit methods
        new("[154] Heal first Rebel Trooper",
            "return SWFOC_HealUnitLua('Find_First_Object(\"Rebel_Trooper_Squad\")')"),
        new("[154] Damage AT-AT for 500",
            "return SWFOC_TakeDamageLua('Find_First_Object(\"Empire_AT_AT\")', '500')"),
        new("[154] Set damage modifier on AT-AT (2.0×)",
            "return SWFOC_SetDamageModifierLua('Find_First_Object(\"Empire_AT_AT\")', '2.0')"),
        new("[154] Set rate-of-fire modifier on AT-AT (1.5×)",
            "return SWFOC_SetRateOfFireModifierLua('Find_First_Object(\"Empire_AT_AT\")', '1.5')"),
        // 2026-04-29 (iter 155) — player-method wires
        new("[155] Give Rebels 100000 credits",
            "return SWFOC_PlayerGiveMoneyLua('Find_Player(\"REBEL\")', '100000')"),
        new("[155] Set Rebel tech level to 5",
            "return SWFOC_PlayerSetTechLevelLua('Find_Player(\"REBEL\")', '5')"),
        new("[155] Unlock specific tech for Rebels",
            "return SWFOC_PlayerUnlockTechLua('Find_Player(\"REBEL\")', 'Find_Object_Type(\"Tech_Hyperspace_Engines\")')"),
        // 2026-04-29 (iter 156) — abilities + capture/spawn flags
        new("[156] Activate AT-AT special ability",
            "return SWFOC_ActivateAbilityLua('Find_First_Object(\"Empire_AT_AT\")', '\"DEPLOY\"')"),
        new("[156] Disable capture on AT-AT",
            "return SWFOC_DisableCaptureLua('Find_First_Object(\"Empire_AT_AT\")', 'true')"),
        new("[156] Cancel hyperspace on Rebel Tank",
            "return SWFOC_CancelHyperspaceLua('Find_First_Object(\"Rebel_T2A_Tank\")')"),
        // 2026-04-29 (iter 157) — limbo / move / fire-special
        new("[157] Set limbo on AT-AT",
            "return SWFOC_SetInLimboLua('Find_First_Object(\"Empire_AT_AT\")', 'true')"),
        new("[157] Move AT-AT to Yavin",
            "return SWFOC_MoveToLua('Find_First_Object(\"Empire_AT_AT\")', 'Find_Planet(\"Yavin\")')"),
        new("[157] Bribe Empire AT-AT to Rebel ownership",
            "return SWFOC_BribeLua('Find_First_Object(\"Empire_AT_AT\")', 'Find_Player(\"REBEL\")')"),
        new("[157] Fire AT-AT special weapon (slot 1)",
            "return SWFOC_FireSpecialWeaponLua('Find_First_Object(\"Empire_AT_AT\")', '1')"),
        // 2026-04-29 (iter 158-159) — global-arg + string-arg globals
        new("[158] Disable bombing run for Rebels (note: REVERSED logic)",
            "return SWFOC_DisableBombingRunLua('false')"),
        new("[159] Fire story event 'BATTLE_OF_YAVIN_BEGIN'",
            "return SWFOC_StoryEventLua('\"BATTLE_OF_YAVIN_BEGIN\"')"),
        new("[159] Add objective to UI",
            "return SWFOC_AddObjectiveLua('\"OBJECTIVE_BLOW_UP_DEATH_STAR\"')"),
        new("[159] Play music event",
            "return SWFOC_PlayMusicLua('\"MUSIC_REBEL_VICTORY\"')"),
        // 2026-04-29 (iter 160-161) — lock controls + diplomacy
        new("[160] Lock player controls (cinematic)",
            "return SWFOC_LockControlsLua('true')"),
        new("[161] Make Empire ally with Underworld",
            "return SWFOC_MakeAllyLua('Find_Player(\"EMPIRE\")', 'Find_Player(\"UNDERWORLD\")')"),
        // 2026-04-29 (iter 162-165) — speed + cinematic + camera
        new("[162] Override AT-AT max speed (500)",
            "return SWFOC_OverrideMaxSpeedLua('Find_First_Object(\"Empire_AT_AT\")', '500')"),
        new("[162] Suspend AI for 30s (cinematic)",
            "return SWFOC_SuspendAiLua('30')"),
        new("[162] Fade screen in (1.5s)",
            "return SWFOC_FadeScreenInLua('1.5')"),
        new("[163] Order AT-AT to attack first Rebel Tank",
            "return SWFOC_AttackTargetLua('Find_First_Object(\"Empire_AT_AT\")', 'Find_First_Object(\"Rebel_T2A_Tank\")')"),
        new("[164] Release tactical credits to Rebels (5000)",
            "return SWFOC_ReleaseCreditsForTacticalLua('Find_Player(\"REBEL\")', '5000')"),
        // 2026-04-29 (iter 166) — music control
        new("[166] Stop all music",
            "return SWFOC_StopAllMusicLua()"),
        new("[166] Resume mode-based music",
            "return SWFOC_ResumeModeBasedMusicLua()"),
        // 2026-04-29 (iter 167-172) — read-side wires (capture engine values)
        // Iter-469: relabelled [167]-[172] -> [read] per iter-388 codified rule
        // (operator-visible labels drop iter-N codenames; semantic category
        // tag instead). Per-object inspection cluster (LIVE read-only) —
        // distinct from [disc] environment discovery cluster (iter-467/468).
        new("[read] AT-AT current hull",
            "return SWFOC_GetHullLua('Find_First_Object(\"Empire_AT_AT\")')"),
        new("[read] Check if AT-AT engines online",
            "return SWFOC_AreEnginesOnlineLua('Find_First_Object(\"Empire_AT_AT\")')"),
        new("[read] Rebel current credits",
            "return SWFOC_GetCreditsLua('Find_Player(\"REBEL\")')"),
        new("[read] AT-AT name",
            "return SWFOC_GetNameLua('Find_First_Object(\"Empire_AT_AT\")')"),
        new("[read] AT-AT current position",
            "return SWFOC_GetPositionLua('Find_First_Object(\"Empire_AT_AT\")')"),
        new("[read] Garrison units inside AT-AT",
            "return SWFOC_GetGarrisonUnitsLua('Find_First_Object(\"Empire_AT_AT\")')"),
        // 2026-05-04 (iter 173-174) — arg-getter wires
        // Iter-469: relabelled [173]-[174] -> [read] (same cluster as 167-172).
        new("[read] Check if AT-AT has 'DEPLOY' ability active",
            "return SWFOC_IsAbilityActiveLua('Find_First_Object(\"Empire_AT_AT\")', '\"DEPLOY\"')"),
        new("[read] Bone position (HEAD bone of AT-AT)",
            "return SWFOC_GetBonePositionLua('Find_First_Object(\"Empire_AT_AT\")', '\"HEAD\"')"),
        // 2026-05-04 (iter 175-176) — TaskForce arc
        new("[175] TaskForce Move_To Yavin",
            "return SWFOC_TaskForceMoveToLua('my_taskforce', 'Find_Planet(\"Yavin\")')"),
        new("[176] TaskForce Land_Units on Hoth",
            "return SWFOC_TaskForceLandUnitsLua('my_taskforce', 'Find_Planet(\"Hoth\")')"),
        // 2026-05-04 (iter 177-178) — discovery + global-getter wires
        // Iter-470: relabelled [178] -> [read] per iter-388 codified rule.
        // The 2 explicit "Read X" globals extend the iter-469 [read] cluster
        // from per-object inspection to ANY explicit Read operation. Absence
        // of an object name in the label IS the global-scope signal.
        new("[177] Find_Object_Type for AT-AT",
            "return SWFOC_FindObjectTypeLua('\"Empire_AT_AT\"')"),
        new("[177] FindPlanet for Coruscant",
            "return SWFOC_FindPlanetLua('\"CORUSCANT\"')"),
        new("[read] Current game mode",
            "return SWFOC_GetGameModeLua()"),
        new("[read] Local player handle",
            "return SWFOC_GetLocalPlayerLua()"),
        // 2026-05-04 (iter 179-180) — pair-completion + namespaced
        new("[179] Check if Rebel is enemy of Empire",
            "return SWFOC_IsEnemyLua('Find_Player(\"REBEL\")', 'Find_Player(\"EMPIRE\")')"),
        new("[179] Find all AT-ATs (returns table handle)",
            "return SWFOC_FindAllObjectsOfTypeLua('Find_Object_Type(\"Empire_AT_AT\")')"),
        new("[180] FOWManager: reveal map for Rebels",
            "return SWFOC_FOWRevealAllLua('Find_Player(\"REBEL\")')"),
        new("[180] Unlock player controls",
            "return SWFOC_UnlockControlsLua()"),
        new("[180] Corrupt AT-AT (Underworld signature ability)",
            "return SWFOC_CorruptLua('Find_First_Object(\"Empire_AT_AT\")', '50')"),
        // 2026-05-05 (iter 181-182) — Thread/SFXManager + 2-arg globals
        // Iter-470: relabelled [181] Read-side -> [read] per iter-388 rule
        // (joins iter-470 [178] globals in the extended [read] cluster).
        // The [181] write-side "Disable unit VO" stays — it's a mutation
        // (SFXManager.Allow_Unit_Reponse_VO = false), not a read.
        new("[read] Current cinematic Thread stage",
            "return SWFOC_ThreadGetCurrentStageLua()"),
        new("[181] Disable unit VO (note: engine typo 'Reponse')",
            "return SWFOC_SFXAllowUnitReponseVoLua('false')"),
        new("[182] Make_Ally global-form (Empire + Underworld)",
            "return SWFOC_GlobalMakeAllyLua('Find_Player(\"EMPIRE\")', 'Find_Player(\"UNDERWORLD\")')"),
        new("[182] Make_Enemy global-form (Rebel + Empire)",
            "return SWFOC_GlobalMakeEnemyLua('Find_Player(\"REBEL\")', 'Find_Player(\"EMPIRE\")')"),
        // 2026-05-06 (iter 223): preset menu refresh covering iter 184-219 wires.
        // Closes the 2nd discoverability path (native UX is path 1; preset menu is path 2).
        // Operators can now find every iter 100-219 LIVE wire via this dropdown.
        new("[160] Disable orbital bombardment for Rebels",
            "return SWFOC_DisableOrbitalBombardmentLua('Find_Player(\"REBEL\")', 'true')"),
        new("[162] Suspend AI for 5 seconds (cinematic helper)",
            "return SWFOC_SuspendAiLua('5')"),
        new("[179] TaskForce: move to target object",
            "return SWFOC_TaskForceMoveToTargetLua('Find_TaskForce(\"MyForce\")', 'Find_First_Object(\"Empire_Star_Destroyer\")')"),
        new("[184] FOWManager: reveal area at position (3-arg)",
            "return SWFOC_FOWRevealLua('Find_Player(\"REBEL\")', 'FindPlanet(\"Yavin\"):Get_Position()', '500')"),
        new("[185] Reinforce unit at position (alternative spawn variant)",
            "return SWFOC_ReinforceUnitLua('Find_Player(\"REBEL\")', 'Find_Object_Type(\"Rebel_Trooper\")', 'FindPlanet(\"Yavin\"):Get_Position()')"),
        new("[185] Spawn from reinforcement pool",
            "return SWFOC_SpawnFromReinforcementPoolLua('Find_Player(\"REBEL\")', 'Find_Object_Type(\"Rebel_Trooper\")', 'FindPlanet(\"Yavin\"):Get_Position()')"),
        new("[185] Create generic object (NOTE: param order is type/position/player, NOT player/type/position)",
            "return SWFOC_CreateGenericObjectLua('Find_Object_Type(\"Crate\")', 'Find_First_Object(\"Rebel_Trooper\"):Get_Position()', 'Find_Player(\"REBEL\")')"),
        new("[186] Find nearest AT-AT to current camera",
            "return SWFOC_FindNearestLua('Get_Camera_Position()', 'Find_Object_Type(\"Empire_AT_AT\")', 'Find_Player(\"REBEL\")')"),

        // ===== Iter 225 — A1.3 SetFireRate global LIVE wire (WeaponTick MinHook detour) =====
        new("[225] Set GLOBAL fire-rate multiplier (2.0x = double rate of fire for ALL weapons)",
            "return SWFOC_SetFireRateMultiplierGlobal(2.0)"),
        new("[225] Reset GLOBAL fire-rate (1.0 = engine default)",
            "return SWFOC_SetFireRateMultiplierGlobal(1.0)"),

        // ===== Iter 231 — A1.x FreezeCredits global 4-wire batch (Hook_AddCredits MinHook detour) =====
        new("[231] FREEZE credits globally (no faction can earn or spend until unfrozen)",
            "return SWFOC_SetCreditsFreezeGlobal(1)"),
        new("[231] UNFREEZE credits globally (restore normal credit flow)",
            "return SWFOC_SetCreditsFreezeGlobal(0)"),
        new("[231] Read GLOBAL credits-freeze state (returns 0 or 1)",
            "return SWFOC_GetCreditsFreezeGlobal()"),
        new("[231] Set GLOBAL credit multiplier (3.0 = 3x credits earned; clamped [0.0, 100.0])",
            "return SWFOC_SetCreditsMultiplierGlobal(3.0)"),
        new("[231] Read GLOBAL credit multiplier",
            "return SWFOC_GetCreditsMultiplierGlobal()"),

        // ===== Iter 237 — A1.x SetCameraPos LIVE pair-flip (direct call to SetTransformMatrix) =====
        new("[237] Set tactical camera position (X, Y, Z; tactical-mode only)",
            "return SWFOC_SetCameraPos(0, 0, 1000)"),
        new("[237] Read current tactical camera position (returns 'X,Y,Z' string)",
            "return SWFOC_GetCameraPos()"),

        // ===== Iter 243 — A1.x SetUnitField extras 2 sub-field LIVE branches (display flag + bit-flip) =====
        // NOTE: these are display-only direct writes; pair with iter-110 SWFOC_MakeInvulnerableLua
        // (engine-state-aware invulnerability via BehaviorMarker + hardpoint propagation) and
        // iter-153 SWFOC_SetCannotBeKilledLua (engine-state-aware cannot-be-killed) for full effect.
        // Iter-482: dropped 'iter-110' / 'iter-153' cross-reference codenames per
        // iter-388 codified rule (project-wide drift sweep — Iter482PresetCodenameLeakSweepTests).
        // Cross-references now use the catalog's [NNN] prefix form, which the operator
        // can scroll back to in the same dropdown.
        new("[243] Set invuln_flag = 1 (DISPLAY-only; pair with [110] MakeInvulnerableLua for engine effect)",
            "return SWFOC_SetUnitField(0x12345678, 'invuln_flag', 1)"),
        new("[243] Set prevent_death = 1 (bit 0x80 of GameObj+0x3A1; operator may prefer [153] SetCannotBeKilledLua)",
            "return SWFOC_SetUnitField(0x12345678, 'prevent_death', 1)"),

        // ===== Iter 258 — A1.x SetUnitField max_* batch 2 sub-field LIVE branches (TYPE-LEVEL writes) =====
        // CRITICAL CAVEAT: these write to the per-unit-TYPE stats struct (walks GameObj+0x298 →
        // UnitType*). Effect applies to EVERY unit instance of the same type for the session, NOT
        // just the targeted obj_addr. Operator buff/nerf is global-by-type, not per-instance.
        // Iter-258 RE walk semantically verified ObjectTypePtr offset at +0x298 via two
        // independent engine-reader callers (rva_get_hull_percentage + rva_set_hp); applied
        // iter-256 memory rule (feedback_aob_drift_across_binary_versions).
        new("[258] Set max_hull = 9999 (TYPE-LEVEL — affects EVERY unit of this type for the session)",
            "return SWFOC_SetUnitField(0x12345678, 'max_hull', 9999)"),
        new("[258] Set max_shield = 750 (TYPE-LEVEL dual-write to UnitType+0xDD0 front + UnitType+0xDD4 rear)",
            "return SWFOC_SetUnitField(0x12345678, 'max_shield', 750)"),

        // ===== Iter 267-268 — A1.x SetUnitField max_speed HONEST DEFER (operator-trust audit trail) =====
        // max_speed is Phase-1 mirror with HONEST DEFER (iter 267-268 RE walk per iter-256 memory rule
        // confirmed NO TYPE-LEVEL max_speed offset in ledger; Override_Max_Speed @ 0x57E590 walks
        // unit+0x60 locomotor NOT unit+0x298 UnitType). This entry is INFORMATIONAL ONLY — clicking it
        // pastes a comment that points to the iter-99/100 LIVE alternatives already present above.
        // Iter-482: dropped 'iter-99 / iter-100' cross-reference codenames per
        // iter-388 codified rule. The [100] preset cluster covers both SWFOC_SetUnitSpeed
        // (per-instance) and SWFOC_SetPerFactionSpeedMultiplier; both are findable above.
        new("[267-268] max_speed HONEST DEFER → see [100] SetUnitSpeed (per-instance) or [100] SetPerFactionSpeedMultiplier",
            "-- iter 267-268: max_speed has NO TYPE-LEVEL offset (semantic verification per iter-256 memory rule).\n" +
            "-- Use iter-99 SWFOC_SetUnitSpeed for per-instance speed override OR iter-100 SWFOC_SetPerFactionSpeedMultiplier for per-faction.\n" +
            "-- Both call SetSpeedOverride @ 0x3A8C90 directly. See catalog rationale for SWFOC_SetUnitField + UnitStatEditor comment for full audit trail.\n" +
            "-- Example LIVE alternative (per-instance):\n" +
            "return SWFOC_SetUnitSpeed(0x12345678, 2.0)"),

        // ===== Iter 269-270 — A1.x SetUnitField attack_power HONEST DEFER (alternative-set pattern) =====
        // attack_power is Phase-1 mirror with HONEST DEFER (iter 269-270 RE walk per iter-256 memory
        // rule EMPIRICALLY REAFFIRMED iter-94's rejection — combat path has NO central per-unit
        // attack_power read site; HardpointFire @ 0x387F50 inspection shows damage is param_4 PASSED IN,
        // computed from per-weapon XML at fire time). The alternative-set pattern (iter-270 NEW, refines
        // iter-251/268 single-alternative pattern) lists ALL THREE existing LIVE alternatives by SCOPE:
        // iter-96 SWFOC_SetDamageMultiplierGlobal (global outgoing), iter-154 SWFOC_SetDamageModifierLua
        // (per-instance), iter-225 SWFOC_SetFireRateMultiplierGlobal (global fire-rate). Adding a 4th
        // attack_power LIVE branch would not add operator capability and would sacrifice iter-258
        // TYPE-LEVEL semantic consistency. This entry is INFORMATIONAL ONLY — clicking it pastes a
        // comment that points to all three alternatives.
        // Iter-482: dropped 'iter-96 / iter-154 / iter-225' cross-reference codenames per
        // iter-388 codified rule. Alternative-set pattern preserved; cross-refs use the
        // catalog's [NNN] prefix form.
        new("[269-270] attack_power HONEST DEFER → alternative-set: [96] (global) / [154] (per-unit) / [225] (fire-rate)",
            "-- iter 269-270: attack_power has NO central per-unit read site (HardpointFire confirms damage is param-passed, computed from per-weapon XML).\n" +
            "-- Alternative-set pattern (iter-270 NEW, refines iter-251/268 single-alternative) — pick by SCOPE:\n" +
            "--   1. GLOBAL outgoing damage scaling   → iter-96  SWFOC_SetDamageMultiplierGlobal (Take_Damage_Outer detour)\n" +
            "--   2. PER-INSTANCE damage scaling      → iter-154 SWFOC_SetDamageModifierLua    (Set_Damage_Modifier engine API)\n" +
            "--   3. GLOBAL fire-rate scaling         → iter-225 SWFOC_SetFireRateMultiplierGlobal (WeaponTick detour)\n" +
            "-- See catalog rationale for SWFOC_SetUnitField + UnitStatEditor comment + iter270_setunitfield_attack_power_honest_defer.md for full audit trail.\n" +
            "-- Example LIVE alternative (per-instance):\n" +
            "return SWFOC_SetDamageModifierLua('Find_First_Object(\"Empire_AT_AT\")', '2.0')"),

        // ===== Iter 282 — A1.x SetFireRate getter pair (closes iter-225 setter; mirror of iter-96/iter-129 pair pattern) =====
        // Iter-482: dropped 'iter-225' cross-reference codename in label per iter-388
        // codified rule. The setter is the [225] preset (findable above).
        new("[282] Read GLOBAL fire-rate multiplier (pair-flip with [225] setter)",
            "return SWFOC_GetFireRateMultiplierGlobal()"),

        // ===== Iter 285 — Tier 3 overlay bridge wires (kills/deaths/units-alive for HUD) =====
        new("[285] Read player kill count (atomic counter from Hook_DeathHandler)",
            "return SWFOC_GetPlayerKills()"),
        new("[285] Read player death count (atomic counter from Hook_DeathHandler)",
            "return SWFOC_GetPlayerDeaths()"),
        new("[285] Read total units alive (poll-on-demand walk of Selection::kObjectListHead)",
            "return SWFOC_GetTotalUnitsAlive()"),

        // ===== Iter 296 — SWFOC_GetPlanets real impl (galactic-mode planet enumeration) =====
        // Iter-468: relabelled [296] -> [disc] per iter-388 codified rule
        // (operator-visible labels drop iter-N codenames; semantic category
        // tag instead). Extends iter-467's [disc] cluster to the standalone
        // read-only discovery wires that predate the iter-450 series.
        new("[disc] Get galactic planets (returns CSV `name;faction;tech` rows)",
            "return SWFOC_GetPlanets()"),

        // ===== Iter 299 — Faction roster + current mod (Audit B enumeration wires) =====
        // Iter-468: relabelled [299] -> [disc] per iter-388 codified rule.
        new("[disc] Get faction roster — list units owned by faction (e.g. 'Rebel')",
            "return SWFOC_GetFactionRoster('Rebel')"),
        new("[disc] Get current mod (filesystem probe of ./Mods/*/Modinfo.xml)",
            "return SWFOC_GetCurrentMod()"),

        // ===== Iter 300 — ListMods (300th-iter milestone — full mod enumeration) =====
        // Iter-468: relabelled [300] -> [disc] per iter-388 codified rule.
        new("[disc] List all installed mods (CSV `name;path` per row)",
            "return SWFOC_ListMods()"),

        // ===== Iter 450/451 — SWFOC_TriggerVictory (PHASE 2 PENDING) =====
        // Bridge wrapper @ iter-450 + simulator handler @ iter-451 ship the
        // 14-name input-validation contract; ACTUAL injection lands iter-450a
        // (capture-on-CTOR hook + AwaitingVictoryTest 48-byte struct layout
        // RE + flip MH_EnableHook on the 0x341FE0 detour). Until iter-450a:
        // calling these presets returns "PHASE2_PENDING: ..." and stages
        // pending state without firing victory. Operators see the pending
        // status in the response panel; no engine state changes yet.
        new("— [450/451] SWFOC_TriggerVictory (PHASE 2 PENDING — staged only) —",
            string.Empty),
        // Galactic_* family (5 entries)
        new("[450] Trigger victory: Galactic_Conquer (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Galactic_Conquer')"),
        new("[450] Trigger victory: Galactic_Control (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Galactic_Control')"),
        new("[450] Trigger victory: Galactic_Cycles (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Galactic_Cycles')"),
        new("[450] Trigger victory: Galactic_Kill_Enemy (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Galactic_Kill_Enemy')"),
        new("[450] Trigger victory: Galactic_Super_Weapon (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Galactic_Super_Weapon')"),
        // Skirmish_* family (4 entries)
        new("[450] Trigger victory: Skirmish_All_Enemies (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Skirmish_All_Enemies')"),
        new("[450] Trigger victory: Skirmish_Control (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Skirmish_Control')"),
        new("[450] Trigger victory: Skirmish_Enemy_Capitulate (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Skirmish_Enemy_Capitulate')"),
        new("[450] Trigger victory: Skirmish_Space_Eradication (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Skirmish_Space_Eradication')"),
        // Sub_Tactical_* family (5 entries)
        new("[450] Trigger victory: Sub_Tactical_All (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Sub_Tactical_All')"),
        new("[450] Trigger victory: Sub_Tactical_Enemy (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Sub_Tactical_Enemy')"),
        new("[450] Trigger victory: Sub_Tactical_Land (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Sub_Tactical_Land')"),
        new("[450] Trigger victory: Sub_Tactical_Space (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Sub_Tactical_Space')"),
        new("[450] Trigger victory: Sub_Tactical_Story (PHASE 2 PENDING)",
            "return SWFOC_TriggerVictory('Sub_Tactical_Story')"),

        // ===== Iter 467 — Read-side discovery + bridge diagnostics =====
        // Closes the preset-menu gap on operator-meaningful read-only LIVE
        // wires that pre-date the iter-450 series but were never surfaced
        // in the dropdown. All five are side-effect-free and safe to fire
        // at any time; engine state is unchanged. Pair with iter-461
        // operator-visible work cadence — extends discoverability path #2
        // (preset menu) for wires already on path #1 (per-tab native UX).
        new("— Discovery + diagnostics (read-only LIVE) —",
            string.Empty),
        new("[disc] List all players (roster CSV)",
            "return SWFOC_GetAllPlayers()"),
        new("[disc] List factions (CSV with faction names)",
            "return SWFOC_ListFactions()"),
        new("[disc] Enumerate Rebel-owned units (CSV)",
            "return SWFOC_EnumerateUnits('Rebel')"),
        new("[disc] Bridge self-test (read-only health probe)",
            "return SWFOC_DiagSelfTest()"),
        new("[disc] Bridge build banner (version + commit)",
            "return SWFOC_GetBuildInfo()"),
    };

    private string _selectedIter100to113Preset = string.Empty;
    public string SelectedIter100to113Preset
    {
        get => _selectedIter100to113Preset;
        set
        {
            if (!SetField(ref _selectedIter100to113Preset, value ?? string.Empty)) return;
            // Auto-paste the preset script into the editor when the user
            // picks one. Skipping when value is null/empty/the placeholder.
            var match = Iter100to113Presets.FirstOrDefault(p => p.Label == value);
            if (match is { Script.Length: > 0 }) ScriptText = match.Script;
        }
    }

    private void CopyScriptToClipboard()
    {
        try
        {
            System.Windows.Clipboard.SetText(_scriptText ?? string.Empty);
            LastStatus = "Script copied to clipboard.";
        }
        catch (Exception ex)
        {
            LastStatus = $"Clipboard copy failed: {ex.Message}";
        }
    }

    private void CopyResponseToClipboard()
    {
        try
        {
            System.Windows.Clipboard.SetText(_lastResponse ?? string.Empty);
            LastStatus = "Last response copied to clipboard.";
        }
        catch (Exception ex)
        {
            LastStatus = $"Clipboard copy failed: {ex.Message}";
        }
    }

    public string ScriptText
    {
        get => _scriptText;
        set { if (SetField(ref _scriptText, value ?? string.Empty)) _state.ScriptText = _scriptText; }
    }

    public string RecipeName
    {
        get => _recipeName;
        set => SetField(ref _recipeName, value ?? string.Empty);
    }

    public string SelectedRecipeName
    {
        get => _selectedRecipeName;
        set => SetField(ref _selectedRecipeName, value ?? string.Empty);
    }

    public string LastResponse
    {
        get => _lastResponse;
        private set => SetField(ref _lastResponse, value);
    }

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge("SWFOC_DoString");

    public ObservableCollection<string> RecipeNames => _recipeNames;

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand RunCommand { get; }
    public ICommand SaveRecipeCommand { get; }
    public ICommand LoadRecipeCommand { get; }
    public ICommand DeleteRecipeCommand { get; }

    private async Task RunCore()
    {
        ApplyFeedback(await _state.RunAsync());
        LastResponse = _state.LastResponse;
    }

    private void SaveRecipe()
    {
        ApplyFeedback(_state.SaveRecipe(_recipeName));
        RefreshRecipeNames();
    }

    private void LoadRecipe()
    {
        var name = string.IsNullOrWhiteSpace(_selectedRecipeName) ? _recipeName : _selectedRecipeName;
        ApplyFeedback(_state.LoadRecipe(name));
        // Mirror loaded text back into the bound TextBox.
        ScriptText = _state.ScriptText;
    }

    private void DeleteRecipe()
    {
        var name = string.IsNullOrWhiteSpace(_selectedRecipeName) ? _recipeName : _selectedRecipeName;
        ApplyFeedback(_state.DeleteRecipe(name));
        RefreshRecipeNames();
    }

    private void RefreshRecipeNames()
    {
        _recipeNames.Clear();
        foreach (var name in _state.Recipes.Keys)
        {
            _recipeNames.Add(name);
        }
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
