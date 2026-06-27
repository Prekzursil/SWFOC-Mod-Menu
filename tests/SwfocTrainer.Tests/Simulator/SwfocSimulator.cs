using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace SwfocTrainer.Tests.Simulator;

/// <summary>
/// In-memory simulator that pairs a <see cref="FakeBridgePipeServer"/>
/// (transport) with a <see cref="FakeGameState"/> (semantics) and wires
/// up handlers for the core SWFOC_* bridge functions the editor calls.
/// </summary>
/// <remarks>
/// <para>
/// <b>Phase A coverage</b> — diagnostics, player state, spawning, basic
/// unit control. The handlers parse Lua call sites with simple regex
/// (the bridge protocol is line-oriented ASCII; we don't need a full
/// Lua parser) and return the same wire format the real bridge emits so
/// the editor's response-parsing code paths run unmodified.
/// </para>
/// <para>
/// <b>Wire format reminders</b> — these came from cross-checking the real
/// powrprof.dll source in <c>swfoc_lua_bridge/lua_bridge.cpp</c> against
/// editor parse code:
/// </para>
/// <list type="bullet">
///   <item><c>SWFOC_GetAllPlayers</c> → <c>"slot;faction;credits;is_human;is_ai;is_local;unit_count|..."</c> (rows pipe-separated, fields semicolon-separated).</item>
///   <item><c>SWFOC_BatchTypeExists("a|b|c")</c> → <c>"1|0|1"</c> (one flag per input name).</item>
///   <item><c>SWFOC_ListTacticalUnits</c> → <c>"id|type|hull/maxhull|owner|alive|..."</c>.</item>
///   <item><c>SWFOC_SpawnUnit("type", slot, qty)</c> → <c>"ok:N"</c> (N = spawned).</item>
/// </list>
/// </remarks>
public sealed class SwfocSimulator : IDisposable
{
    private readonly FakeBridgePipeServer _server;

    public FakeGameState GameState { get; }
    public FakeBridgePipeServer Bridge => _server;
    public string PipeName => _server.PipeName;

    public SwfocSimulator(FakeGameState gameState, string? pipeName = null)
    {
        GameState = gameState ?? throw new ArgumentNullException(nameof(gameState));
        _server = new FakeBridgePipeServer(
            pipeName ?? "swfoc_sim_" + Guid.NewGuid().ToString("N"));
        RegisterHandlers();
    }

    public void Start() => _server.Start();
    public void Dispose() => _server.Dispose();

    // ====================================================================
    // Handler registration
    // ====================================================================

    private void RegisterHandlers()
    {
        // Diagnostics
        Reg("return SWFOC_GetVersion", _ => "swfoc_sim v0.1 (in-memory simulator)");
        Reg("return SWFOC_GetBuildInfo",
            _ => "build=sim commit=test branch=test ts=2026-04-27T00:00:00Z");
        Reg("return SWFOC_DiagListRegisteredFunctions", _ => string.Join(",",
            "SWFOC_GetVersion",
            "SWFOC_GetBuildInfo",
            "SWFOC_DiagListRegisteredFunctions",
            "SWFOC_DiagSelfTest",
            "SWFOC_GetAllPlayers",
            "SWFOC_GetLocalPlayer",
            "SWFOC_SetHumanPlayer_v3",
            "SWFOC_GetCredits",
            "SWFOC_SetCredits",
            "SWFOC_BatchTypeExists",
            "SWFOC_ListUnitTypes",
            "SWFOC_SpawnUnit",
            "SWFOC_ListTacticalUnits",
            "SWFOC_KillUnit",
            "SWFOC_SetUnitInvuln",
            "SWFOC_SetUnitHull",
            "SWFOC_PreventUnitDeath",
            "SWFOC_RevealAll",
            "SWFOC_GetPlanets",
            "SWFOC_FireStoryEvent"));
        Reg("return SWFOC_DiagSelfTest", _ => "OK self_test=pass simulator=true");

        // Player state
        Reg("return SWFOC_GetAllPlayers", _ => HandleGetAllPlayers());
        Reg("return SWFOC_GetLocalPlayer", _ => HandleGetLocalPlayer());
        Reg("return SWFOC_SetHumanPlayer_v3", HandleSetHumanPlayerV3);
        Reg("return SWFOC_GetCredits", HandleGetCreditsForSlot);
        Reg("return SWFOC_SetCredits", HandleSetCreditsForSlot);

        // Spawning
        Reg("return SWFOC_BatchTypeExists", HandleBatchTypeExists);
        Reg("return SWFOC_ListUnitTypes", _ => string.Join("|", GameState.KnownTypeNames.OrderBy(n => n)));
        Reg("return SWFOC_SpawnUnit", HandleSpawnUnit);

        // Tactical units
        Reg("return SWFOC_ListTacticalUnits", _ => HandleListTacticalUnits());
        Reg("return SWFOC_KillUnit", HandleKillUnit);
        Reg("return SWFOC_SetUnitInvuln", HandleSetUnitInvuln);
        Reg("return SWFOC_SetUnitHull", HandleSetUnitHull);
        Reg("return SWFOC_PreventUnitDeath", HandlePreventUnitDeath);

        // Galactic
        Reg("return SWFOC_RevealAll", HandleRevealAll);
        Reg("return SWFOC_GetPlanets", _ => HandleGetPlanets());
        // 2026-05-07 (iter 299): faction roster + current-mod probes.
        Reg("return SWFOC_GetFactionRoster", HandleGetFactionRoster);
        Reg("return SWFOC_GetCurrentMod", _ => HandleGetCurrentMod());
        // 2026-05-07 (iter 300; 300th-iter milestone): mod enumeration.
        Reg("return SWFOC_ListMods", _ => HandleListMods());
        Reg("return SWFOC_ChangePlanetOwner", HandleChangePlanetOwner);
        Reg("return SWFOC_ChangePlanetOwnerWithMode", HandleChangePlanetOwnerWithMode);
        Reg("return SWFOC_SpawnAsStoryArrival", HandleSpawnAsStoryArrival);
        Reg("return SWFOC_GetTechForSlot", HandleGetTechForSlot);
        Reg("return SWFOC_SetTechForSlot", HandleSetTechForSlot);
        Reg("return SWFOC_InstantBuild", HandleInstantBuild);

        // Combat scalars (Phase B — iter 23 / Phase E — iter 25)
        Reg("return SWFOC_SetDamageMultiplier", HandleSetDamageMultiplier);
        Reg("return SWFOC_SetFireRate", HandleSetFireRate);
        // 2026-04-28 (iter 97 master loop): the global-only LIVE-badged
        // sibling. Real bridge uses Take_Damage_Outer detour to scale
        // damageParams[0]; the simulator just stores into a global field.
        Reg("return SWFOC_SetDamageMultiplierGlobal", HandleSetDamageMultiplierGlobal);
        Reg("return SWFOC_GetDamageMultiplierGlobal", HandleGetDamageMultiplierGlobal);
        // 2026-05-06 (iter 226): SetFireRate global LIVE wire shipped iter 225
        // via WeaponTick MinHook detour. Simulator mirrors the bridge's clamp
        // [0.0, 100.0] and stores into FakeGameState.GlobalFireRateMultiplier.
        Reg("return SWFOC_SetFireRateMultiplierGlobal", HandleSetFireRateMultiplierGlobal);
        Reg("return SWFOC_GetFireRateMultiplierGlobal", HandleGetFireRateMultiplierGlobal);
        // 2026-05-08 (iter 285): Tier 3 HUD counter read-side handlers.
        Reg("return SWFOC_GetPlayerKills", HandleGetPlayerKills);
        Reg("return SWFOC_GetPlayerDeaths", HandleGetPlayerDeaths);
        Reg("return SWFOC_GetTotalUnitsAlive", HandleGetTotalUnitsAlive);
        // 2026-05-06 (iter 232): FreezeCredits global LIVE wires shipped iter 231
        // via AddCredits MinHook detour. 4 wires (bool freeze pair + mult pair).
        // Simulator mirrors the bridge's clamp [0.0, 100.0] for mult + bool semantics
        // for freeze. Stores into FakeGameState.GlobalCreditsFreeze + .GlobalCreditsMultiplier.
        Reg("return SWFOC_SetCreditsFreezeGlobal", HandleSetCreditsFreezeGlobal);
        Reg("return SWFOC_GetCreditsFreezeGlobal", HandleGetCreditsFreezeGlobal);
        Reg("return SWFOC_SetCreditsMultiplierGlobal", HandleSetCreditsMultiplierGlobal);
        Reg("return SWFOC_GetCreditsMultiplierGlobal", HandleGetCreditsMultiplierGlobal);
        // 2026-05-07 (iter 451): SWFOC_TriggerVictory wrapper. Bridge wrapper
        // @ iter-450 validates input + stages pending state. Simulator mirrors
        // the same shape so editor unit tests can verify wrapper input handling
        // without a live game. Real injection lands iter-450a (MinHook detour
        // at rva_victory_monitor_counter_inc @ 0x341FE0).
        Reg("return SWFOC_TriggerVictory", HandleTriggerVictory);
        // 2026-04-29 (iter 140): read-side handlers for the iter 96/100/107/131 LIVE
        // wires so simulator-driven E2E tests can verify round-trip read-after-write
        // semantics. Pre-iter-140 these fell through to the catch-all "(sim:
        // unhandled probe)" sentinel, masking real round-trip behavior.
        Reg("return SWFOC_GetDamageMultiplier", HandleGetDamageMultiplier);
        Reg("return SWFOC_GetUnitShield", HandleGetUnitShield);
        Reg("return SWFOC_GetUnitSpeed", HandleGetUnitSpeed);
        Reg("return SWFOC_GetCameraPos", HandleGetCameraPos);
        Reg("return SWFOC_SetUnitShield", HandleSetUnitShield);
        Reg("return SWFOC_OneHitKill", HandleOneHitKill);
        Reg("return SWFOC_SetAreaDamage", HandleSetAreaDamage);
        Reg("return SWFOC_SetCameraPos", HandleSetCameraPos);
        // 2026-04-28 (iter 107): LIVE camera target via Lua Scroll_Camera_To.
        Reg("return SWFOC_ScrollCameraToTarget", HandleScrollCameraToTarget);
        // 2026-04-29 (iter 143): LIVE camera follow via Camera_To_Follow Lua API.
        Reg("return SWFOC_CameraFollow", HandleCameraFollow);
        // 2026-04-29 (iter 144): LIVE camera rotation via Rotate_Camera_To Lua API.
        Reg("return SWFOC_RotateCameraTo", HandleRotateCameraTo);
        // 2026-04-29 (iter 145): cinematic camera quad LIVE wires.
        Reg("return SWFOC_StartCinematicCamera", _ => HandleStartCinematicCamera());
        Reg("return SWFOC_EndCinematicCamera", _ => HandleEndCinematicCamera());
        Reg("return SWFOC_SetCinematicCameraKey", HandleSetCinematicCameraKey);
        Reg("return SWFOC_TransitionCinematicCameraKey", HandleTransitionCinematicCameraKey);
        // 2026-04-29 (iter 151): tactical teleport via Teleport unit method.
        Reg("return SWFOC_TeleportUnitLua", HandleTeleportUnitLua);
        // 2026-04-29 (iter 152): galactic-mode spawn via Galactic_Spawn_Unit.
        Reg("return SWFOC_GalacticSpawnUnit", HandleGalacticSpawnUnit);
        // 2026-04-29 (iter 153): bool-arg unit-method LIVE batch — reuses
        // the existing iter-111 HandleUnitBoolMethod helper.
        Reg("return SWFOC_SetCannotBeKilledLua",
            c => HandleUnitBoolMethod(c, "Set_Cannot_Be_Killed", "SetCannotBeKilled"));
        Reg("return SWFOC_EnableStealthLua",
            c => HandleUnitBoolMethod(c, "Enable_Stealth", "EnableStealth"));
        // 2026-04-29 (iter 154): float-arg unit-method LIVE batch + Heal no-arg.
        Reg("return SWFOC_HealUnitLua",
            c => HandleUnitNoArgMethod(c, "Heal", "Heal"));
        Reg("return SWFOC_TakeDamageLua",
            c => HandleUnitFloatMethod(c, "Take_Damage", "TakeDamage"));
        Reg("return SWFOC_SetDamageModifierLua",
            c => HandleUnitFloatMethod(c, "Set_Damage_Modifier", "SetDamageModifier"));
        Reg("return SWFOC_SetRateOfFireModifierLua",
            c => HandleUnitFloatMethod(c, "Set_Rate_Of_Fire_Modifier", "SetRateOfFireModifier"));
        // 2026-04-29 (iter 155): player-method LIVE batch (reuses iter-154 helper).
        Reg("return SWFOC_PlayerGiveMoneyLua",
            c => HandleUnitFloatMethod(c, "Give_Money", "PlayerGiveMoney"));
        Reg("return SWFOC_PlayerSetTechLevelLua",
            c => HandleUnitFloatMethod(c, "Set_Tech_Level", "PlayerSetTechLevel"));
        Reg("return SWFOC_PlayerUnlockTechLua",
            c => HandleUnitFloatMethod(c, "Unlock_Tech", "PlayerUnlockTech"));
        // 2026-04-29 (iter 156): unit-method batch via existing helpers.
        Reg("return SWFOC_ActivateAbilityLua",
            c => HandleUnitFloatMethod(c, "Activate_Ability", "ActivateAbility"));
        Reg("return SWFOC_DisableCaptureLua",
            c => HandleUnitBoolMethod(c, "Disable_Capture", "DisableCapture"));
        Reg("return SWFOC_SetGarrisonSpawnLua",
            c => HandleUnitBoolMethod(c, "Set_Garrison_Spawn", "SetGarrisonSpawn"));
        Reg("return SWFOC_CancelHyperspaceLua",
            c => HandleUnitNoArgMethod(c, "Cancel_Hyperspace", "CancelHyperspace"));
        // 2026-04-29 (iter 157): 6-wire unit-method mega-batch via existing helpers.
        Reg("return SWFOC_SetInLimboLua",
            c => HandleUnitBoolMethod(c, "Set_In_Limbo", "SetInLimbo"));
        Reg("return SWFOC_SetCheckContestedSpaceLua",
            c => HandleUnitBoolMethod(c, "Set_Check_Contested_Space", "SetCheckContestedSpace"));
        Reg("return SWFOC_SellUnitLua",
            c => HandleUnitNoArgMethod(c, "Sell", "Sell"));
        Reg("return SWFOC_BribeLua",
            c => HandleUnitFloatMethod(c, "Bribe", "Bribe"));
        Reg("return SWFOC_MoveToLua",
            c => HandleUnitFloatMethod(c, "Move_To", "MoveTo"));
        Reg("return SWFOC_FireSpecialWeaponLua",
            c => HandleUnitFloatMethod(c, "Fire_Special_Weapon", "FireSpecialWeapon"));
        // 2026-04-29 (iter 158): global-method batch via global-arg dispatcher.
        // Simulator: just acknowledge OK — no state to track.
        Reg("return SWFOC_DisableBombingRunLua",
            _ => "OK: Disable_Bombing_Run dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_FlashGuiObjectLua",
            _ => "OK: Flash_GUI_Object dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_HideGuiObjectLua",
            _ => "OK: Hide_GUI_Object dispatched (LIVE — engine Lua API)");
        // 2026-04-29 (iter 159): string-arg global batch via same dispatcher.
        // Helper is shape-agnostic — bool/string args are both raw Lua.
        Reg("return SWFOC_StoryEventLua",
            _ => "OK: Story_Event dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_AddObjectiveLua",
            _ => "OK: Add_Objective dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_PlayMusicLua",
            _ => "OK: Play_Music dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_PlaySfxEventLua",
            _ => "OK: Play_SFX_Event dispatched (LIVE — engine Lua API)");
        // 2026-04-29 (iter 160): mixed-helper batch.
        Reg("return SWFOC_LockControlsLua",
            _ => "OK: Lock_Controls dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_DisableOrbitalBombardmentLua",
            _ => "OK: Disable_Orbital_Bombardment dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_StoryEventTriggerLua",
            _ => "OK: Story_Event_Trigger dispatched (LIVE — engine Lua API)");
        // 2026-04-29 (iter 161): player-method batch via generic 2-arg helper.
        Reg("return SWFOC_LockTechLua",
            _ => "OK: Lock_Tech dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_MakeAllyLua",
            _ => "OK: Make_Ally dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_MakeEnemyLua",
            _ => "OK: Make_Enemy dispatched (LIVE — engine Lua API)");
        // 2026-04-29 (iter 162): 4-wire batch (1 unit method + 3 globals).
        Reg("return SWFOC_OverrideMaxSpeedLua",
            _ => "OK: Override_Max_Speed dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_SuspendAiLua", HandleSuspendAiLua);
        Reg("return SWFOC_FadeScreenInLua",
            _ => "OK: Fade_Screen_In dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_ZoomCameraLua",
            _ => "OK: Zoom_Camera dispatched (LIVE — engine Lua API)");
        // 2026-04-29 (iter 163): combat-order batch.
        Reg("return SWFOC_AttackTargetLua",
            _ => "OK: Attack_Target dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_GuardTargetLua",
            _ => "OK: Guard_Target dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_DivertLua",
            _ => "OK: Divert dispatched (LIVE — engine Lua API)");
        // 2026-04-29 (iter 164): player-method extension batch.
        Reg("return SWFOC_EnableAsActorLua",
            _ => "OK: Enable_As_Actor dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_ReleaseCreditsForTacticalLua",
            _ => "OK: Release_Credits_For_Tactical dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_SelectObjectLua",
            _ => "OK: Select_Object dispatched (LIVE — engine Lua API)");
        // 2026-04-29 (iter 165): camera/cinematic complement batch.
        Reg("return SWFOC_FadeScreenOutLua",
            _ => "OK: Fade_Screen_Out dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_RotateCameraByLua",
            _ => "OK: Rotate_Camera_By dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_PointCameraAtLua",
            _ => "OK: Point_Camera_At dispatched (LIVE — engine Lua API)");
        // 2026-04-29 (iter 166): new global-no-arg helper + 3 wires.
        Reg("return SWFOC_StopAllMusicLua",
            _ => "OK: Stop_All_Music dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_ResumeModeBasedMusicLua",
            _ => "OK: Resume_Mode_Based_Music dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_ShowGuiObjectLua",
            _ => "OK: Show_GUI_Object dispatched (LIVE — engine Lua API)");
        // 2026-04-29 (iter 167): NEW unit-getter helper + 3 read-side wires.
        // Simulator returns synthetic values that match the LIVE response format.
        Reg("return SWFOC_GetHullLua",
            _ => "OK: Get_Hull = 1500 (LIVE — engine Lua API)");
        Reg("return SWFOC_GetHealthLua",
            _ => "OK: Get_Health = 0.85 (LIVE — engine Lua API)");
        Reg("return SWFOC_GetShieldLua",
            _ => "OK: Get_Shield = 1.0 (LIVE — engine Lua API)");
        // 2026-04-29 (iter 168): read-side getter expansion.
        Reg("return SWFOC_HasAttackTargetLua",
            _ => "OK: Has_Attack_Target = false (LIVE — engine Lua API)");
        Reg("return SWFOC_AreEnginesOnlineLua",
            _ => "OK: Are_Engines_Online = true (LIVE — engine Lua API)");
        Reg("return SWFOC_GetOwnerLua",
            _ => "OK: Get_Owner = table: 0x7fff0001 (LIVE — engine Lua API)");
        // 2026-04-29 (iter 169): read-side getter expansion #2.
        Reg("return SWFOC_GetTypeLua",
            _ => "OK: Get_Type = table: 0x7fff0002 (LIVE — engine Lua API)");
        Reg("return SWFOC_GetCreditsLua",
            _ => "OK: Get_Credits = 50000 (LIVE — engine Lua API)");
        Reg("return SWFOC_GetFactionLua",
            _ => "OK: Get_Faction = table: 0x7fff0003 (LIVE — engine Lua API)");
        Reg("return SWFOC_GetTechLevelLua",
            _ => "OK: Get_Tech_Level = 5 (LIVE — engine Lua API)");
        // 2026-04-29 (iter 170): read-side state-query batch.
        Reg("return SWFOC_GetNameLua",
            _ => "OK: Get_Name = Player1 (LIVE — engine Lua API)");
        Reg("return SWFOC_IsStealthedLua",
            _ => "OK: Is_Stealthed = false (LIVE — engine Lua API)");
        Reg("return SWFOC_IsInLimboLua",
            _ => "OK: Is_In_Limbo = false (LIVE — engine Lua API)");
        Reg("return SWFOC_IsCapturableLua",
            _ => "OK: Is_Capturable = true (LIVE — engine Lua API)");
        // 2026-04-30 (iter 171): read-side query batch.
        Reg("return SWFOC_GetPositionLua",
            _ => "OK: Get_Position = table: 0x7fff0010 (LIVE — engine Lua API)");
        Reg("return SWFOC_GetParentObjectLua",
            _ => "OK: Get_Parent_Object = nil (LIVE — engine Lua API)");
        Reg("return SWFOC_GetAttackTargetLua",
            _ => "OK: Get_Attack_Target = nil (LIVE — engine Lua API)");
        Reg("return SWFOC_GetDamageModifierLua",
            _ => "OK: Get_Damage_Modifier = 1.0 (LIVE — engine Lua API)");
        // 2026-04-30 (iter 172): garrison/behavior read-side batch — 100 LIVE milestone.
        Reg("return SWFOC_GetGarrisonUnitsLua",
            _ => "OK: Get_Garrison_Units = table: 0x7fff0011 (LIVE — engine Lua API)");
        Reg("return SWFOC_GetContainedObjectCountLua",
            _ => "OK: Get_Contained_Object_Count = 4 (LIVE — engine Lua API)");
        Reg("return SWFOC_GetBehaviorIdLua",
            _ => "OK: Get_Behavior_ID = 7 (LIVE — engine Lua API)");
        Reg("return SWFOC_GetRateOfFireModifierLua",
            _ => "OK: Get_Rate_Of_Fire_Modifier = 1.0 (LIVE — engine Lua API)");
        // 2026-05-04 (iter 173): arg-getter batch via NEW iter-173 helper.
        Reg("return SWFOC_IsAbilityActiveLua",
            _ => "OK: Is_Ability_Active(\"Tractor_Beam\") = false (LIVE — engine Lua API)");
        Reg("return SWFOC_HasPropertyLua",
            _ => "OK: Has_Property(\"Hero\") = true (LIVE — engine Lua API)");
        Reg("return SWFOC_IsCategoryLua",
            _ => "OK: Is_Category(\"Infantry\") = false (LIVE — engine Lua API)");
        Reg("return SWFOC_GetDistanceLua",
            _ => "OK: Get_Distance(target) = 250.5 (LIVE — engine Lua API)");
        // 2026-05-04 (iter 174): cross-receiver arg-getter batch.
        Reg("return SWFOC_GetBonePositionLua",
            _ => "OK: Get_Bone_Position(\"head\") = table: 0x7fff0020 (LIVE — engine Lua API)");
        Reg("return SWFOC_ContainsObjectTypeLua",
            _ => "OK: Contains_Object_Type(\"AT_AT\") = false (LIVE — engine Lua API)");
        Reg("return SWFOC_GetSpaceStationLevelLua",
            _ => "OK: Get_Space_Station_Level(planet) = 3 (LIVE — engine Lua API)");
        Reg("return SWFOC_GetTypeOfUnitLua",
            _ => "OK: Get_Type_Of_Unit(0) = table: 0x7fff0021 (LIVE — engine Lua API)");
        // 2026-05-04 (iter 175): TaskForce write-side batch.
        Reg("return SWFOC_TaskForceMoveToLua",
            _ => "OK: Move_To dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_TaskForceReinforceLua",
            _ => "OK: Reinforce dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_TaskForceReleaseReinforcementsLua",
            _ => "OK: Release_Reinforcements dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_TaskForceLaunchUnitsLua",
            _ => "OK: Launch_Units dispatched (LIVE — engine Lua API)");
        // 2026-05-04 (iter 176): TaskForce coverage extension.
        Reg("return SWFOC_TaskForceAttackTargetLua",
            _ => "OK: Attack_Target dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_TaskForceGuardTargetLua",
            _ => "OK: Guard_Target dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_TaskForceLandUnitsLua",
            _ => "OK: Land_Units dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_TaskForceSetAsGoalSystemRemovableLua",
            _ => "OK: Set_As_Goal_System_Removable dispatched (LIVE — engine Lua API)");
        // 2026-05-04 (iter 177): global-getter discovery batch via NEW iter-177 helper.
        Reg("return SWFOC_FindObjectTypeLua",
            _ => "OK: Find_Object_Type(\"AT_AT\") = table: 0x7fff0030 (LIVE — engine Lua API)");
        Reg("return SWFOC_FindPlanetLua",
            _ => "OK: FindPlanet(\"CORUSCANT\") = table: 0x7fff0031 (LIVE — engine Lua API)");
        Reg("return SWFOC_FindFirstObjectLua",
            _ => "OK: Find_First_Object(\"AT_AT\") = table: 0x7fff0032 (LIVE — engine Lua API)");
        // 2026-05-04 (iter 178): global-no-arg-getter batch via NEW iter-178 helper.
        // 9th in dispatcher set — closes receiver × arg × read/write matrix.
        Reg("return SWFOC_GetGameModeLua",
            _ => "OK: Get_Game_Mode() = Land (LIVE — engine Lua API)");
        Reg("return SWFOC_GetLocalPlayerLua",
            _ => "OK: Get_Local_Player() = table: 0x7fff0040 (LIVE — engine Lua API)");
        Reg("return SWFOC_GetSecondsPerGameMinuteLua",
            _ => "OK: Get_Seconds_Per_Game_Minute() = 6.0 (LIVE — engine Lua API)");
        // 2026-05-04 (iter 179): first marginal-cost batch post matrix-complete.
        Reg("return SWFOC_IsEnemyLua",
            _ => "OK: Is_Enemy(player_b) = false (LIVE — engine Lua API)");
        Reg("return SWFOC_IsAllyLua",
            _ => "OK: Is_Ally(player_b) = true (LIVE — engine Lua API)");
        Reg("return SWFOC_FindAllObjectsOfTypeLua",
            _ => "OK: Find_All_Objects_Of_Type(\"AT_AT\") = table: 0x7fff0050 (LIVE — engine Lua API)");
        Reg("return SWFOC_TaskForceMoveToTargetLua",
            _ => "OK: Move_To_Target dispatched (LIVE — engine Lua API)");
        // 2026-05-04 (iter 180): namespaced + pair-completion batch.
        Reg("return SWFOC_FOWRevealAllLua",
            _ => "OK: FOWManager.Reveal_All dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_FOWUndoRevealAllLua",
            _ => "OK: FOWManager.Undo_Reveal_All dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_UnlockControlsLua",
            _ => "OK: Unlock_Controls dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_CorruptLua",
            _ => "OK: Corrupt dispatched (LIVE — engine Lua API)");
        // 2026-05-05 (iter 181): namespace expansion (Thread + SFXManager).
        Reg("return SWFOC_ThreadGetCurrentStageLua",
            _ => "OK: Thread.Get_Current_Stage() = 0 (LIVE — engine Lua API)");
        Reg("return SWFOC_SFXAllowUnitReponseVoLua",
            _ => "OK: SFXManager.Allow_Unit_Reponse_VO dispatched (LIVE — engine Lua API)");
        // 2026-05-05 (iter 182): first multi-arg expansion (10th helper).
        Reg("return SWFOC_GlobalMakeAllyLua",
            _ => "OK: Make_Ally dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_GlobalMakeEnemyLua",
            _ => "OK: Make_Enemy dispatched (LIVE — engine Lua API)");
        // 2026-05-05 (iter 184): second multi-arg expansion (3-arg globals).
        Reg("return SWFOC_FOWRevealLua",
            _ => "OK: FOWManager.Reveal dispatched (LIVE — engine Lua API)");
        // 2026-05-05 (iter 185): spawn-variant batch via iter-184 3-arg helper.
        Reg("return SWFOC_ReinforceUnitLua",
            _ => "OK: Reinforce_Unit dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_SpawnFromReinforcementPoolLua",
            _ => "OK: Spawn_From_Reinforcement_Pool dispatched (LIVE — engine Lua API)");
        Reg("return SWFOC_CreateGenericObjectLua",
            _ => "OK: Create_Generic_Object dispatched (LIVE — engine Lua API)");
        // 2026-05-05 (iter 186): NEW 3-arg-getter helper (12th in dispatcher set).
        Reg("return SWFOC_FindNearestLua",
            _ => "OK: Find_Nearest = table: 0x7fff0090 (LIVE — engine Lua API)");
        // 2026-04-29 (iter 150): cinematic-mode letterbox toggles.
        Reg("return SWFOC_LetterBoxOn", _ =>
        {
            GameState.LetterBoxActive = true;
            return "OK: Letter_Box_On dispatched (LIVE — engine Lua API)";
        });
        Reg("return SWFOC_LetterBoxOff", _ =>
        {
            GameState.LetterBoxActive = false;
            return "OK: Letter_Box_Off dispatched (LIVE — engine Lua API)";
        });
        // 2026-04-28 (iter 108): LIVE per-unit owner change via Change_Owner.
        Reg("return SWFOC_ChangeUnitOwner", HandleChangeUnitOwner);
        // 2026-04-28 (iter 109): LIVE unit spawn via Spawn_Unit Lua API.
        Reg("return SWFOC_SpawnUnitLua", HandleSpawnUnitLua);
        // 2026-04-28 (iter 110): LIVE per-unit invuln via Make_Invulnerable.
        Reg("return SWFOC_MakeUnitInvulnLua", HandleMakeUnitInvulnLua);
        // 2026-04-28 (iter 111): LIVE per-unit Lua-method-bool batch.
        Reg("return SWFOC_HideUnitLua", c => HandleUnitBoolMethod(c, "Hide", "Hide"));
        Reg("return SWFOC_PreventAiUsageLua",
            c => HandleUnitBoolMethod(c, "Prevent_AI_Usage", "PreventAiUsage"));
        Reg("return SWFOC_SetUnitSelectableLua",
            c => HandleUnitBoolMethod(c, "Set_Selectable", "Selectable"));
        // 2026-04-28 (iter 112): LIVE per-unit Lua-method-noarg batch.
        Reg("return SWFOC_DespawnUnitLua", c => HandleUnitNoArgMethod(c, "Despawn", "Despawn"));
        Reg("return SWFOC_StopUnitLua", c => HandleUnitNoArgMethod(c, "Stop", "Stop"));
        Reg("return SWFOC_RetreatUnitLua", c => HandleUnitNoArgMethod(c, "Retreat", "Retreat"));
        // 2026-04-28 (iter 113): UNIVERSAL Lua method dispatcher.
        Reg("return SWFOC_CallObjMethodLua", HandleCallObjMethodLua);
        Reg("return SWFOC_SetTechLevel", HandleSetTechLevel);

        // Speed (Phase B)
        Reg("return SWFOC_SetGameSpeed", HandleSetGameSpeed);
        Reg("return SWFOC_SetPerFactionSpeedMultiplier", HandleSetPerFactionSpeed);
        Reg("return SWFOC_SetUnitSpeed", HandleSetUnitSpeed);
        Reg("return SWFOC_ClearUnitSpeedOverride", HandleClearUnitSpeedOverride);

        // Hero Lab (Phase B)
        Reg("return SWFOC_ListHeroes", _ => HandleListHeroes());
        Reg("return SWFOC_HeroInstantRespawn", HandleHeroInstantRespawn);
        Reg("return SWFOC_HeroStatEdit", HandleHeroStatEdit);
        Reg("return SWFOC_SetHeroRespawnTimer", HandleSetHeroRespawnTimer);
        Reg("return SWFOC_SetHeroRespawn", HandleSetHeroRespawn);
        Reg("return SWFOC_SetPermadeath", HandleSetPermadeath);
        Reg("return SWFOC_ReviveUnit", HandleReviveUnit);

        // AI brain (Phase B)
        Reg("return SWFOC_GetAiBrain", HandleGetAiBrain);
        Reg("return SWFOC_NullAiBrain", HandleNullAiBrain);
        Reg("return SWFOC_AttachAiBrain", HandleAttachAiBrain);
        Reg("return SWFOC_FreezeAI", HandleFreezeAi);

        // Diplomacy (Phase B)
        Reg("return SWFOC_SetDiplomacy", HandleSetDiplomacy);

        // Economy (Phase B)
        Reg("return SWFOC_SetIncomeMultiplier", HandleSetIncomeMultiplier);
        Reg("return SWFOC_DrainEnemyCredits", HandleDrainEnemyCredits);

        // Event stream (Phase B)
        Reg("return SWFOC_EventStreamDrain", _ => HandleEventStreamDrain());
        Reg("return SWFOC_EventControl", HandleEventControl);

        // Story
        Reg("return SWFOC_FireStoryEvent", HandleFireStoryEvent);

        // Phase C — global toggles, inspector, hardpoints, log/tick
        Reg("return SWFOC_GodMode", HandleGodMode);
        Reg("return SWFOC_HealAllLocal", _ => HandleHealAllLocal());
        Reg("return SWFOC_FreeBuild", HandleFreeBuild);
        Reg("return SWFOC_FreeCam", HandleFreeCam);
        Reg("return SWFOC_FreezeCredits", HandleFreezeCredits);
        Reg("return SWFOC_ToggleOHKAttackPower", HandleToggleOhk);
        Reg("return SWFOC_CombinedGodOHK", HandleCombinedGodOhk);
        Reg("return SWFOC_UncapCredits", _ =>
        {
            GameState.MaxCredits = -1;
            return "ok";
        });
        Reg("return SWFOC_GetMaxCredits", _ =>
            GameState.MaxCredits.ToString(CultureInfo.InvariantCulture));
        Reg("return SWFOC_GetCreditsForSlot", HandleGetCreditsForSlot);
        Reg("return SWFOC_SetCreditsForSlot", HandleSetCreditsForSlot);
        Reg("return SWFOC_GetSelectedUnit", _ =>
            GameState.SelectedUnitId.ToString(CultureInfo.InvariantCulture));
        Reg("return SWFOC_InspectUnit", HandleInspectUnit);
        Reg("return SWFOC_GetHardpoints", HandleGetHardpoints);
        Reg("return SWFOC_SetTargetFilter", HandleSetTargetFilter);
        Reg("return SWFOC_SetUnitCapOverride", HandleSetUnitCapOverride);
        Reg("return SWFOC_SetUnitField", HandleSetUnitField);
        Reg("return SWFOC_GetPlayerWrapper", HandleGetPlayerWrapper);
        Reg("return SWFOC_DiagGameTick", _ =>
        {
            GameState.GameTickCount++;
            return GameState.GameTickCount.ToString(CultureInfo.InvariantCulture);
        });
        Reg("return SWFOC_DumpState", _ => HandleDumpState());
        Reg("return SWFOC_Log", HandleLog);
        Reg("return SWFOC_DoString", _ => "ok"); // Generic Lua eval — ack only.
        Reg("return SWFOC_EnumerateUnits", _ => HandleEnumerateUnits());

        // Native-engine Lua patterns (no SWFOC_ prefix). These are emitted
        // by services that talk directly to the engine's exposed Lua API
        // rather than going through the powrprof.dll wrapper.

        // DiplomacyService: "local p1 = Find_Player(\"FACTION_A\"); local p2 = Find_Player(\"FACTION_B\"); if p1 and p2 then p1:Make_Ally(p2) end"
        Reg("local p1 = Find_Player", HandleNativeDiplomacy);

        // StoryEventService: 'Story_Event("EVENT_ID")'
        Reg("Story_Event", HandleStoryEventNative);

        // Catch-all "return ..." stub so unknown probes don't blow up tests.
        Reg("return ", _ => "(sim: unhandled probe — handler not registered)");
    }

    private string HandleNativeDiplomacy(string command)
    {
        // Pattern: local p1 = Find_Player("A"); local p2 = Find_Player("B"); if p1 and p2 then p1:Make_Ally(p2) end
        // Or :Make_Enemy(p2). We don't have a Neutral form because DiplomacyService
        // returns null for Neutral and never sends to the bridge.
        var strs = ExtractAllStringArgs(command);
        if (strs.Count < 2) return "ERR: bad faction args";
        var rel = command.Contains(":Make_Ally", StringComparison.Ordinal)
            ? "Allied"
            : command.Contains(":Make_Enemy", StringComparison.Ordinal)
                ? "Hostile"
                : null;
        if (rel is null) return "ERR: unknown diplomacy method";
        var key = strs[0] + ":" + strs[1];
        GameState.Diplomacy[key] = rel;
        return "ok";
    }

    private string HandleStoryEventNative(string command)
    {
        // Pattern: Story_Event("EVENT_ID")
        var ev = ExtractFirstStringArg(command);
        if (ev is null) return "ERR: bad arg";
        GameState.StoryFlags.Add(ev);
        GameState.EventQueue.Enqueue("STORY_FIRED:" + ev);
        return "ok:" + ev;
    }

    private void Reg(string prefix, Func<string, string> handler)
        => _server.Register(prefix, handler);

    // ====================================================================
    // Player-state handlers
    // ====================================================================

    private string HandleGetAllPlayers()
    {
        // Format mirrors the real bridge — verified in
        // PlayerStateTabViewModel.RefreshSlotMapAsync (semicolon fields,
        // pipe rows). Field order: slot;faction;credits;is_human;is_ai;is_local;unit_count
        return string.Join("|", GameState.Players.OrderBy(p => p.Slot).Select(p =>
        {
            var unitCount = GameState.Units.Count(u => u.OwnerSlot == p.Slot && u.Alive);
            return string.Format(CultureInfo.InvariantCulture,
                "{0};{1};{2};{3};{4};{5};{6}",
                p.Slot,
                p.Faction,
                p.Credits,
                p.IsHuman ? 1 : 0,
                p.HasAiBrain ? 1 : 0,
                p.IsLocal ? 1 : 0,
                unitCount);
        }));
    }

    private string HandleGetLocalPlayer()
    {
        var p = GameState.GetLocalHuman();
        return p is null
            ? "ERR: no local human"
            : string.Format(CultureInfo.InvariantCulture, "{0};{1}", p.Slot, p.Faction);
    }

    private string HandleSetHumanPlayerV3(string command)
    {
        // return SWFOC_SetHumanPlayer_v3(<slot>)
        var arg = ExtractFirstIntArg(command);
        if (arg is null) return "ERR: bad arg";
        var newSlot = arg.Value;
        var newPlayer = GameState.GetPlayer(newSlot);
        if (newPlayer is null) return "ERR: no such slot";

        var prevHuman = GameState.GetLocalHuman();
        if (prevHuman is not null)
        {
            prevHuman.IsHuman = false;
            prevHuman.IsLocal = false;
            prevHuman.HasAiBrain = true;
        }
        newPlayer.IsHuman = true;
        newPlayer.IsLocal = true;
        newPlayer.HasAiBrain = false; // v3 contribution: AI brain swap
        return string.Format(CultureInfo.InvariantCulture, "ok:{0};{1}", newPlayer.Slot, newPlayer.Faction);
    }

    private string HandleGetCreditsForSlot(string command)
    {
        var slot = ExtractFirstIntArg(command);
        if (slot is null) return "ERR: bad arg";
        var p = GameState.GetPlayer(slot.Value);
        return p is null ? "ERR: no such slot"
            : p.Credits.ToString(CultureInfo.InvariantCulture);
    }

    private string HandleSetCreditsForSlot(string command)
    {
        var args = ExtractIntArgs(command);
        if (args.Count < 2) return "ERR: bad args";
        var p = GameState.GetPlayer(args[0]);
        if (p is null) return "ERR: no such slot";
        p.Credits = args[1];
        return "ok";
    }

    // ====================================================================
    // Spawning handlers
    // ====================================================================

    private string HandleBatchTypeExists(string command)
    {
        // return SWFOC_BatchTypeExists("name1|name2|name3")
        var raw = ExtractFirstStringArg(command);
        if (raw is null) return "ERR: bad arg";
        var names = raw.Split('|', StringSplitOptions.None);
        var flags = names.Select(n => GameState.KnownTypeNames.Contains(n) ? "1" : "0");
        return string.Join("|", flags);
    }

    private string HandleSpawnUnit(string command)
    {
        // Two arities are in use by editor call sites:
        //   3-arg form: SWFOC_SpawnUnit("type", slot, qty)         — early tests
        //   6-arg form: SWFOC_SpawnUnit('type', slot, x, y, z, qty) — BridgeSpawningDispatcher
        // The 6-arg form is what the editor actually emits in production; the
        // 3-arg form is kept as a convenience for direct-bridge tests.
        var typeName = ExtractFirstStringArg(command);
        if (typeName is null)
        {
            return "ERR: bad type arg";
        }
        var ints = ExtractIntArgs(command);
        if (ints.Count < 1)
        {
            return "ERR: bad slot arg";
        }
        var slot = ints[0];
        // Quantity is the LAST integer in the arg list — true for both arities
        // because the dispatcher's count is the trailing arg and the 3-arg
        // form's qty is also the trailing arg.
        var qty = ints.Count >= 2 ? ints[^1] : 1;
        if (!GameState.KnownTypeNames.Contains(typeName))
        {
            return "ERR: unknown type: " + typeName;
        }
        if (GameState.GetPlayer(slot) is null)
        {
            return "ERR: no such slot";
        }
        for (var i = 0; i < qty; i++)
        {
            GameState.Units.Add(new FakeUnit
            {
                TypeName = typeName,
                OwnerSlot = slot,
                MaxHull = 100,
                CurrentHull = 100,
                IsGround = true,
            });
        }
        return string.Format(CultureInfo.InvariantCulture, "ok:{0}", qty);
    }

    // ====================================================================
    // Unit-control handlers
    // ====================================================================

    private string HandleListTacticalUnits()
    {
        return string.Join("|", GameState.Units.Where(u => u.Alive).Select(u =>
            string.Format(CultureInfo.InvariantCulture,
                "{0};{1};{2};{3};{4};{5};{6};{7}",
                u.Id,
                u.TypeName,
                u.CurrentHull.ToString(CultureInfo.InvariantCulture),
                u.MaxHull.ToString(CultureInfo.InvariantCulture),
                u.OwnerSlot,
                u.Invulnerable ? 1 : 0,
                u.DeathPrevented ? 1 : 0,
                u.IsHero ? 1 : 0)));
    }

    private string HandleKillUnit(string command)
    {
        var id = ExtractFirstIntArg(command);
        if (id is null) return "ERR: bad arg";
        var u = GameState.GetUnit(id.Value);
        if (u is null) return "ERR: no such unit";
        u.Alive = false;
        u.CurrentHull = 0;
        return "ok";
    }

    private string HandleSetUnitInvuln(string command)
    {
        var args = ExtractIntArgs(command);
        if (args.Count < 2) return "ERR: bad args";
        var u = GameState.GetUnit(args[0]);
        if (u is null) return "ERR: no such unit";
        u.Invulnerable = args[1] != 0;
        return "ok";
    }

    private string HandleSetUnitHull(string command)
    {
        // SWFOC_SetUnitHull(id, value) — value is float
        var ints = ExtractIntArgs(command);
        if (ints.Count < 1) return "ERR: bad id";
        var u = GameState.GetUnit(ints[0]);
        if (u is null) return "ERR: no such unit";
        var floats = ExtractFloatArgs(command);
        // floats[0] is the id, floats[1] is the value (regex catches both since both are numeric)
        if (floats.Count < 2) return "ERR: bad value";
        u.CurrentHull = floats[1];
        if (u.CurrentHull > 0) u.Alive = true;
        return "ok";
    }

    private string HandlePreventUnitDeath(string command)
    {
        var args = ExtractIntArgs(command);
        if (args.Count < 2) return "ERR: bad args";
        var u = GameState.GetUnit(args[0]);
        if (u is null) return "ERR: no such unit";
        u.DeathPrevented = args[1] != 0;
        return "ok";
    }

    // ====================================================================
    // Galactic + story
    // ====================================================================

    private string HandleGetPlanets()
    {
        // BridgeGalacticDispatcher.GetPlanetsAsync parses NEWLINE-separated
        // rows with format "id;owner;tech" — owner is a faction NAME,
        // not a slot index, and there are exactly 3 fields.
        if (GameState.Planets.Count == 0) return "(no_planets)";
        return string.Join("\n", GameState.Planets.Select(p =>
            string.Format(CultureInfo.InvariantCulture,
                "{0};{1};{2}",
                p.Name,
                string.IsNullOrEmpty(p.OwnerFaction) ? "NONE" : p.OwnerFaction,
                p.TechLevel)));
    }

    /// <summary>
    /// 2026-05-07 (iter 299): faction roster simulator handler. Mirrors the
    /// real bridge's wire format from <c>lua_bridge.cpp::Lua_GetFactionRoster</c>:
    /// NEWLINE-separated rows of <c>"unit_type;category"</c>. The faction
    /// name arrives wrapped in single quotes per the standard SWFOC_*
    /// dispatcher convention. Returns <c>"(empty)"</c> when the requested
    /// faction has no units. Faction is derived from the unit's OwnerSlot
    /// to Players[slot].Faction (the real engine path); category is a
    /// heuristic from IsHero/IsGround flags.
    /// </summary>
    private string HandleGetFactionRoster(string command)
    {
        var faction = ExtractFirstStringArg(command) ?? string.Empty;
        if (string.IsNullOrEmpty(faction))
            return "ERR: faction name required (arg #1 missing or empty)";
        var rows = new List<string>();
        foreach (var u in GameState.Units)
        {
            if (!u.Alive) continue;
            var owner = GameState.Players.FirstOrDefault(p => p.Slot == u.OwnerSlot);
            var unitFaction = owner?.Faction ?? string.Empty;
            if (!string.Equals(unitFaction, faction, StringComparison.Ordinal)) continue;
            var category = u.IsHero ? "Hero"
                         : u.IsGround ? "GroundCompany"
                         : "SpaceUnit";
            rows.Add(string.Format(CultureInfo.InvariantCulture,
                "{0};{1}",
                string.IsNullOrEmpty(u.TypeName) ? "?" : u.TypeName,
                category));
        }
        return rows.Count == 0 ? "(empty)" : string.Join("\n", rows);
    }

    /// <summary>
    /// 2026-05-07 (iter 299): current-mod simulator handler. Mirrors the
    /// real bridge's wire format from <c>lua_bridge.cpp::Lua_GetCurrentMod</c>:
    /// <c>"mod_name;version\nabsolute_path"</c> or <c>"vanilla"</c>. The
    /// fake state stores ActiveModName/ActiveModPath/ActiveModVersion
    /// fields populated by test scenarios.
    /// </summary>
    private string HandleGetCurrentMod()
    {
        if (string.IsNullOrEmpty(GameState.ActiveModName))
            return "vanilla";
        var version = string.IsNullOrEmpty(GameState.ActiveModVersion)
            ? "unknown"
            : GameState.ActiveModVersion;
        var path = string.IsNullOrEmpty(GameState.ActiveModPath)
            ? @"C:\fake\Mods\" + GameState.ActiveModName
            : GameState.ActiveModPath;
        return string.Format(CultureInfo.InvariantCulture,
            "{0};{1}\n{2}", GameState.ActiveModName, version, path);
    }

    /// <summary>
    /// 2026-05-07 (iter 300; 300th-iter milestone): mod enumeration handler.
    /// Mirrors the real bridge's wire format from <c>lua_bridge.cpp::Lua_ListMods</c>:
    /// NEWLINE-separated rows of <c>"mod_name;absolute_path"</c>. Returns
    /// <c>"(no_mods)"</c> when the fake state's <c>AvailableMods</c> list
    /// is empty (matches the real bridge's sentinel for "no Mods/ folder
    /// or no Modinfo.xml").
    /// </summary>
    private string HandleListMods()
    {
        if (GameState.AvailableMods.Count == 0)
            return "(no_mods)";
        return string.Join("\n", GameState.AvailableMods.Select(m =>
            string.Format(CultureInfo.InvariantCulture, "{0};{1}", m.Name, m.Path)));
    }

    private string HandleRevealAll(string command)
    {
        // Two arities seen:
        //   SWFOC_RevealAll()        — Phase A legacy form
        //   SWFOC_RevealAll(enable)  — BridgeGalacticDispatcher (1=on, 0=off)
        var v = ExtractFirstIntArg(command);
        var on = v is null ? true : v.Value != 0;
        foreach (var p in GameState.Planets) p.IsRevealed = on;
        return "ok";
    }

    private string HandleFireStoryEvent(string command)
    {
        var ev = ExtractFirstStringArg(command);
        if (ev is null) return "ERR: bad arg";
        GameState.StoryFlags.Add(ev);
        GameState.EventQueue.Enqueue("STORY_FIRED:" + ev);
        return "ok:" + ev;
    }

    // ====================================================================
    // Galactic — Phase B
    // ====================================================================

    private string HandleChangePlanetOwner(string command)
    {
        // Two arities seen:
        //   SWFOC_ChangePlanetOwner('name', slot)        — Phase B legacy
        //   SWFOC_ChangePlanetOwner('name', 'faction')   — BridgeGalacticDispatcher
        var allStrings = ExtractAllStringArgs(command);
        if (allStrings.Count == 0) return "ERR: bad planet arg";
        var planetName = allStrings[0];
        var planet = GameState.Planets.FirstOrDefault(p =>
            string.Equals(p.Name, planetName, StringComparison.OrdinalIgnoreCase));
        if (planet is null) return "ERR: no such planet";

        if (allStrings.Count >= 2)
        {
            // Faction-string form (the production dispatcher).
            GameState.SetPlanetOwner(planet, allStrings[1]);
            GameState.EventQueue.Enqueue($"PLANET_OWNED:{planetName}:{allStrings[1]}");
            return "ok";
        }
        var ints = ExtractIntArgs(command);
        if (ints.Count < 1) return "ERR: bad slot arg";
        planet.OwnerSlot = ints[0];
        var faction = GameState.GetPlayer(ints[0])?.Faction ?? string.Empty;
        planet.OwnerFaction = faction;
        GameState.EventQueue.Enqueue($"PLANET_OWNED:{planetName}:{ints[0]}");
        return "ok";
    }

    private string HandleGetTechForSlot(string command)
    {
        var slot = ExtractFirstIntArg(command);
        if (slot is null) return "ERR: bad arg";
        var p = GameState.GetPlayer(slot.Value);
        if (p is null) return "ERR: no such slot";
        // We don't model tech-level on the player yet — this returns 1 by
        // default. Phase B+ can extend FakePlayer with tech tiers.
        return "1";
    }

    private string HandleSetTechForSlot(string command)
    {
        var args = ExtractIntArgs(command);
        if (args.Count < 2) return "ERR: bad args";
        if (GameState.GetPlayer(args[0]) is null) return "ERR: no such slot";
        // Tech is acked but not modelled in FakePlayer yet.
        GameState.EventQueue.Enqueue($"TECH_SET:{args[0]}:{args[1]}");
        return "ok";
    }

    private string HandleInstantBuild(string command)
    {
        var planetName = ExtractFirstStringArg(command);
        if (planetName is null) return "ERR: bad arg";
        var planet = GameState.Planets.FirstOrDefault(p =>
            string.Equals(p.Name, planetName, StringComparison.OrdinalIgnoreCase));
        if (planet is null) return "ERR: no such planet";
        planet.Structures = Math.Max(planet.Structures + 1, 1);
        return "ok";
    }

    /// <summary>
    /// 2026-04-27 (iter 32) — Overlay Feature 2 backing handler.
    /// <c>SWFOC_SpawnAsStoryArrival('type', 'planet', 'faction')</c> — spawn
    /// a unit on a galactic-mode planet AS IF a story event delivered it.
    /// In the live engine this routes through the campaign's fleet-arrival
    /// machinery so the unit integrates correctly into galactic state
    /// (planet defenders / attackers). The simulator just adds a unit
    /// anchored to the planet with the right owner.
    /// </summary>
    /// <remarks>
    /// The dispatcher contract is locked here so the overlay's right-click
    /// radial menu has a target when it ships. Live engine integration
    /// requires an IDA pin on the campaign's fleet-arrival entry point —
    /// that's tracked in the overlay design doc as Phase 4 RE work.
    /// </remarks>
    private string HandleSpawnAsStoryArrival(string command)
    {
        var strs = ExtractAllStringArgs(command);
        if (strs.Count < 3) return "ERR: bad args (type, planet, faction required)";
        var typeName = strs[0];
        var planetName = strs[1];
        var faction = strs[2];

        if (!GameState.KnownTypeNames.Contains(typeName))
        {
            return "ERR: unknown type: " + typeName;
        }
        var planet = GameState.Planets.FirstOrDefault(p =>
            string.Equals(p.Name, planetName, StringComparison.OrdinalIgnoreCase));
        if (planet is null) return "ERR: no such planet";
        var slot = GameState.Players.FirstOrDefault(p =>
            string.Equals(p.Faction, faction, StringComparison.OrdinalIgnoreCase))?.Slot ?? -1;
        if (slot == -1) return "ERR: no such faction in current scenario: " + faction;

        var unit = new FakeUnit
        {
            TypeName = typeName,
            OwnerSlot = slot,
            OnPlanet = planetName,
            MaxHull = 100,
            CurrentHull = 100,
            IsGround = true,
        };
        GameState.Units.Add(unit);
        // Story-arrival spawns also fire the engine's event queue so other
        // listeners (mission scripts, AI subsystems) can react.
        GameState.EventQueue.Enqueue(
            $"STORY_ARRIVAL:{typeName}@{planetName}#{faction}");
        return string.Format(CultureInfo.InvariantCulture, "ok:{0}", unit.Id);
    }

    /// <summary>
    /// 2026-04-27 (iter 32) — Overlay Feature 3 backing handler.
    /// <c>SWFOC_ChangePlanetOwnerWithMode('planet', 'newFaction', 'mode')</c>
    /// where mode is one of:
    /// <list type="bullet">
    ///   <item><c>default</c> — engine kick-out queue: foreign units leave
    ///     the planet via OnPlanet="" but stay alive in <c>FakeGameState.Units</c>.
    ///     Mirrors the live engine's post-conquest cleanup.</item>
    ///   <item><c>convert</c> — foreign units re-team to the new owner via
    ///     per-unit Switch_Sides; OnPlanet stays put. The operator gets to
    ///     keep the AT-AT garrison they captured.</item>
    ///   <item><c>pure_kick</c> — foreign units removed from the world
    ///     entirely. Use sparingly; it's the harshest option.</item>
    /// </list>
    /// Returns "ok:N" with N the count of units affected.
    /// </summary>
    private string HandleChangePlanetOwnerWithMode(string command)
    {
        var strs = ExtractAllStringArgs(command);
        if (strs.Count < 3) return "ERR: bad args (planet, faction, mode required)";
        var planetName = strs[0];
        var newFaction = strs[1];
        var mode = strs[2].ToLowerInvariant();

        var planet = GameState.Planets.FirstOrDefault(p =>
            string.Equals(p.Name, planetName, StringComparison.OrdinalIgnoreCase));
        if (planet is null) return "ERR: no such planet";

        // Capture pre-flip owner faction for filtering "foreign" units.
        var prevFaction = planet.OwnerFaction;
        var newOwnerSlot = GameState.Players.FirstOrDefault(p =>
            string.Equals(p.Faction, newFaction, StringComparison.OrdinalIgnoreCase))?.Slot ?? -1;

        // Flip the planet record itself.
        GameState.SetPlanetOwner(planet, newFaction);

        // Find foreign units stationed here (units whose OnPlanet matches
        // this planet AND whose owner slot doesn't match the new owner).
        var foreign = GameState.Units
            .Where(u => string.Equals(u.OnPlanet, planetName, StringComparison.OrdinalIgnoreCase)
                        && u.OwnerSlot != newOwnerSlot)
            .ToList();

        var affected = 0;
        switch (mode)
        {
            case "default":
            case "kick":
                // Engine cleanup: unit leaves the planet but stays alive in
                // the world (returned to its owner's nearest base — modelled
                // by simply clearing OnPlanet).
                foreach (var u in foreign)
                {
                    u.OnPlanet = string.Empty;
                    affected++;
                }
                GameState.EventQueue.Enqueue(
                    $"PLANET_FLIP_KICK:{planetName}:{prevFaction}->{newFaction}:{affected}");
                break;
            case "convert":
                // Re-team: per-unit Switch_Sides. OwnerSlot flips, OnPlanet
                // stays. Operator keeps the captured garrison.
                foreach (var u in foreign)
                {
                    u.OwnerSlot = newOwnerSlot;
                    affected++;
                }
                GameState.EventQueue.Enqueue(
                    $"PLANET_FLIP_CONVERT:{planetName}:{prevFaction}->{newFaction}:{affected}");
                break;
            case "pure_kick":
            case "purekick":
                // Hard deletion: unit removed from the world entirely.
                foreach (var u in foreign)
                {
                    GameState.Units.Remove(u);
                    affected++;
                }
                GameState.EventQueue.Enqueue(
                    $"PLANET_FLIP_PUREKICK:{planetName}:{prevFaction}->{newFaction}:{affected}");
                break;
            default:
                // Roll back the owner change so the operator's mistake
                // doesn't silently mutate state.
                GameState.SetPlanetOwner(planet, prevFaction);
                return "ERR: unknown mode (use default/convert/pure_kick): " + strs[2];
        }
        return string.Format(CultureInfo.InvariantCulture, "ok:{0}", affected);
    }

    // ====================================================================
    // Combat scalars — Phase B
    // ====================================================================

    private string HandleSetDamageMultiplier(string command)
    {
        // BridgeCombatDispatcher emits: SWFOC_SetDamageMultiplier(slot, mult).
        // Per-slot scaling — we store the multiplier under PerSlotDamageMultiplier
        // AND also apply it to every alive unit owned by that slot so tests can
        // observe the effect via FakeUnit.DamageScalar.
        var ints = ExtractIntArgs(command);
        var floats = ExtractFloatArgs(command);
        if (ints.Count < 1 || floats.Count < 2) return "ERR: bad args";
        var slot = ints[0];
        if (GameState.GetPlayer(slot) is null) return "ERR: no such slot";
        var mult = floats[1];
        GameState.PerSlotDamageMultiplier[slot] = mult;
        foreach (var u in GameState.Units.Where(u => u.OwnerSlot == slot && u.Alive))
        {
            u.DamageScalar = mult;
        }
        return "ok";
    }

    // 2026-04-28 (iter 97 master loop): SWFOC_SetDamageMultiplierGlobal(mult).
    // Mirror of the bridge-side LIVE handler. Stores into
    // FakeGameState.GlobalDamageMultiplier; the operator's intent is that
    // ALL damage is scaled by this value (which matches the real bridge's
    // Take_Damage_Outer detour scaling damageParams[0]).
    private string HandleSetDamageMultiplierGlobal(string command)
    {
        var floats = ExtractFloatArgs(command);
        if (floats.Count < 1) return "ERR: bad args";
        var mult = floats[0];
        if (mult < 0.0f) return "ERR: multiplier must be >= 0";
        GameState.GlobalDamageMultiplier = mult;
        // Symmetric with the per-slot handler: apply to every alive unit so
        // simulator-driven tests can inspect FakeUnit.DamageScalar even when
        // only the global multiplier is set.
        foreach (var u in GameState.Units.Where(u => u.Alive))
        {
            u.DamageScalar = mult;
        }
        return "ok";
    }

    private string HandleGetDamageMultiplierGlobal(string command)
    {
        return GameState.GlobalDamageMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // 2026-05-06 (iter 226): SetFireRate global iter 225 LIVE wire — bridge
    // installs MinHook detour at WeaponTick @ 0x387010 that scales `dt` arg
    // passed to sub_140387400 by g_fireRateMult_global. Simulator mirror
    // applies the same [0.0, 100.0] clamp the bridge uses. Engine semantic
    // caveat: mult=2.0 → 2x fire rate, mult=0.5 → halved, mult=0.0 → effective
    // freeze. Closes A1.3 after 124-day deferral (iter-101/130/132/221 audits).
    private string HandleSetFireRateMultiplierGlobal(string command)
    {
        var floats = ExtractFloatArgs(command);
        if (floats.Count < 1) return "ERR: bad args";
        var mult = floats[0];
        if (mult < 0.0f) mult = 0.0f;       // bridge clamp lower bound
        if (mult > 100.0f) mult = 100.0f;   // bridge clamp upper bound (int overflow guard)
        GameState.GlobalFireRateMultiplier = mult;
        return "ok";
    }

    private string HandleGetFireRateMultiplierGlobal(string command)
    {
        return GameState.GlobalFireRateMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // 2026-05-08 (iter 285): Tier 3 HUD counter handlers. Simulator mirrors the
    // bridge's atomic-counter read-only semantics. Tests can pre-seed
    // GameState.LocalPlayerKills / LocalPlayerDeaths / TotalUnitsAlive to
    // exercise downstream consumer code without invoking a real DeathHandler.
    private string HandleGetPlayerKills(string command)
    {
        return GameState.LocalPlayerKills.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private string HandleGetPlayerDeaths(string command)
    {
        return GameState.LocalPlayerDeaths.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private string HandleGetTotalUnitsAlive(string command)
    {
        return GameState.TotalUnitsAlive.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // 2026-05-06 (iter 232): FreezeCredits global iter 231 LIVE wires — bridge
    // installs MinHook detour at AddCredits @ 0x27F370 (universal credit-adjust
    // function, 47 callers). Bool freeze precedence (short-circuits AddCredits
    // entirely); mult scales delta arg before forwarding (mult=1.0 fast-path).
    // Simulator mirrors bool semantics for freeze + [0.0, 100.0] clamp for mult.
    // Pattern parallels iter-226 SetFireRate handler exactly. Closes A1.x
    // FreezeCredits arc at sim level.
    private string HandleSetCreditsFreezeGlobal(string command)
    {
        var ints = ExtractIntArgs(command);
        if (ints.Count < 1) return "ERR: bad args";
        GameState.GlobalCreditsFreeze = (ints[0] != 0);
        return "ok";
    }

    // 2026-05-07 (iter 451): SWFOC_TriggerVictory simulator handler. Mirrors
    // the bridge wrapper's input validation against 14-of-18 known VictoryType
    // enum names (per rva_victory_type_enum_init @ 0x341FF0). Stages state in
    // FakeGameState.VictoryTriggerPending + .VictoryTriggerType so editor
    // unit tests can verify wrapper input handling without a live game.
    // Bridge wrapper currently emits PHASE2_PENDING (iter-450 scaffolding);
    // simulator mirrors the same shape. iter-450a will replace the
    // PHASE2_PENDING return string with "ok" once the MinHook injection ships.
    private static readonly string[] s_knownVictoryTypes =
    {
        "Galactic_Conquer",
        "Galactic_Control",
        "Galactic_Cycles",
        "Galactic_Kill_Enemy",
        "Galactic_Super_Weapon",
        "Skirmish_All_Enemies",
        "Skirmish_Control",
        "Skirmish_Enemy_Capitulate",
        "Skirmish_Space_Eradication",
        "Sub_Tactical_All",
        "Sub_Tactical_Enemy",
        "Sub_Tactical_Land",
        "Sub_Tactical_Space",
        "Sub_Tactical_Story",
    };

    private string HandleTriggerVictory(string command)
    {
        // Wire format: `return SWFOC_TriggerVictory("Galactic_Conquer")`
        var open = command.IndexOf('(');
        var close = command.LastIndexOf(')');
        if (open < 0 || close < 0 || close <= open + 1)
        {
            return "ERR_NO_ARG: SWFOC_TriggerVictory(victory_type) requires 1 string arg";
        }
        var inner = command.Substring(open + 1, close - open - 1).Trim().Trim('"', '\'').Trim();
        if (string.IsNullOrEmpty(inner))
        {
            return "ERR_BAD_ARG: victory_type must be non-empty string";
        }
        if (System.Array.IndexOf(s_knownVictoryTypes, inner) < 0)
        {
            return "ERR_UNKNOWN_TYPE: not in VictoryType enum (per rva_victory_type_enum_init @ 0x341FF0)";
        }
        GameState.VictoryTriggerType = inner;
        GameState.VictoryTriggerPending = true;
        return "PHASE2_PENDING: victory_type validated and staged; iter-450 ships "
             + "the wrapper + DORMANT detour. iter-450a will enable the MinHook at "
             + "0x341FE0 once AwaitingVictoryTest 48-byte struct layout is RE'd "
             + "and capture-on-CTOR hook at 0x341850 is added (resolves the "
             + "rcx-discriminator problem).";
    }

    private string HandleGetCreditsFreezeGlobal(string command)
    {
        return GameState.GlobalCreditsFreeze ? "1" : "0";
    }

    private string HandleSetCreditsMultiplierGlobal(string command)
    {
        var floats = ExtractFloatArgs(command);
        if (floats.Count < 1) return "ERR: bad args";
        var mult = floats[0];
        if (mult < 0.0f) mult = 0.0f;       // bridge clamp lower bound
        if (mult > 100.0f) mult = 100.0f;   // bridge clamp upper bound (overflow guard)
        GameState.GlobalCreditsMultiplier = mult;
        return "ok";
    }

    private string HandleGetCreditsMultiplierGlobal(string command)
    {
        return GameState.GlobalCreditsMultiplier.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // 2026-04-29 (iter 140): read companion to HandleSetDamageMultiplier (per-slot).
    // Returns 1.0 (engine identity scalar) when the slot has no override recorded —
    // matches the real bridge's behavior where unset slots fall through to the
    // engine default.
    private string HandleGetDamageMultiplier(string command)
    {
        var slot = ExtractFirstIntArg(command);
        if (slot is null) return "ERR: bad args";
        var mult = GameState.PerSlotDamageMultiplier.TryGetValue(slot.Value, out var v) ? v : 1.0f;
        return mult.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // 2026-04-29 (iter 140): read companion to HandleSetUnitShield (iter 129 LIVE)
    // and the iter 131 LIVE pair-flip. Real bridge calls FrontShield_Read @ 0x3963C0;
    // simulator returns FakeUnit.CurrentShield directly. Returns -1.0 sentinel for
    // unknown unit ids matching the bridge's pre-iter-131 cache-miss behavior.
    private string HandleGetUnitShield(string command)
    {
        var unitId = ExtractFirstIntArg(command);
        if (unitId is null) return "ERR: bad args";
        var u = GameState.GetUnit(unitId.Value);
        if (u is null) return "-1";
        return u.CurrentShield.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // 2026-04-29 (iter 140): read companion to HandleSetUnitSpeed (iter 100 LIVE).
    // Real bridge reads engine locomotor +0x2A0 when override active; simulator
    // returns FakeUnit.Speed. Returns -1 for unknown unit ids.
    private string HandleGetUnitSpeed(string command)
    {
        var unitId = ExtractFirstIntArg(command);
        if (unitId is null) return "ERR: bad args";
        var u = GameState.GetUnit(unitId.Value);
        if (u is null) return "-1";
        return u.Speed.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    // 2026-04-29 (iter 140): read companion to HandleSetCameraPos / iter 107
    // ScrollCameraToTarget. Returns the last-set camera position as "x,y,z" (or
    // "0,0,0" if never set). Real bridge reads engine camera transform matrix.
    private string HandleGetCameraPos(string command)
    {
        var (x, y, z) = GameState.CameraPos;
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "{0},{1},{2}", x, y, z);
    }

    private string HandleSetFireRate(string command)
    {
        // SWFOC_SetFireRate(slot, mult) — symmetric to damage multiplier.
        var ints = ExtractIntArgs(command);
        var floats = ExtractFloatArgs(command);
        if (ints.Count < 1 || floats.Count < 2) return "ERR: bad args";
        var slot = ints[0];
        if (GameState.GetPlayer(slot) is null) return "ERR: no such slot";
        var mult = floats[1];
        GameState.PerSlotFireRateMultiplier[slot] = mult;
        foreach (var u in GameState.Units.Where(u => u.OwnerSlot == slot && u.Alive))
        {
            u.FireRateScalar = mult;
        }
        return "ok";
    }

    private string HandleSetUnitShield(string command)
    {
        // SWFOC_SetUnitShield(unitId, value)
        var floats = ExtractFloatArgs(command);
        var ints = ExtractIntArgs(command);
        if (ints.Count < 1 || floats.Count < 2) return "ERR: bad args";
        var u = GameState.GetUnit(ints[0]);
        if (u is null) return "ERR: no such unit";
        u.CurrentShield = floats[1];
        if (u.MaxShield < u.CurrentShield) u.MaxShield = u.CurrentShield;
        return "ok";
    }

    private string HandleOneHitKill(string command)
    {
        // Two arities seen:
        //   SWFOC_OneHitKill(unitId)   — Phase B legacy direct-kill
        //   SWFOC_OneHitKill(enable)   — BridgeCombatDispatcher TOGGLE
        // We disambiguate by looking up the int as a unit id; if that fails,
        // treat it as a toggle.
        var v = ExtractFirstIntArg(command);
        if (v is null) return "ERR: bad arg";
        var u = GameState.GetUnit(v.Value);
        if (u is not null)
        {
            u.Alive = false;
            u.CurrentHull = 0;
            u.CurrentShield = 0;
            return "ok";
        }
        // No matching unit id → treat as toggle.
        GameState.OneHitKillEnabled = v.Value != 0;
        return "ok";
    }

    private string HandleSetAreaDamage(string command)
    {
        var v = ExtractFirstIntArg(command);
        if (v is null) return "ERR: bad arg";
        GameState.AreaDamageEnabled = v.Value != 0;
        return "ok";
    }

    private string HandleSetCameraPos(string command)
    {
        // SWFOC_SetCameraPos(x, y, z) — store the triple verbatim.
        var floats = ExtractFloatArgs(command);
        if (floats.Count < 3) return "ERR: bad args";
        GameState.CameraPos = (floats[0], floats[1], floats[2]);
        return "ok";
    }

    // 2026-04-28 (iter 113): UNIVERSAL Lua-method dispatcher handler.
    // Captures (obj_expr, method_name, args_expr) from the bridge call
    // and stores into FakeGameState.LastCallObjMethodLua. Tests assert
    // on what would have been spliced into `(obj):method(args)` at
    // the engine call site. Real bridge dispatches via DoString and
    // returns "OK: <method> dispatched (LIVE — engine Lua API)".
    private string HandleCallObjMethodLua(string command)
    {
        var args = ExtractAllStringArgs(command);
        if (args.Count < 2)
            return "ERR: SWFOC_CallObjMethodLua: expected (obj_lua_expr, method_name, args_lua_expr)";
        var argsExpr = args.Count >= 3 ? args[2] : string.Empty;
        GameState.LastCallObjMethodLua = (args[0], args[1], argsExpr);
        return $"OK: {args[1]} dispatched (LIVE — engine Lua API)";
    }

    // 2026-04-28 (iter 112): generic handler for zero-arg unit method
    // wires (Despawn / Stop / Retreat). Real bridge composes
    // `(<unit>):<method>()` via DoString. Simulator captures the unit
    // expression keyed by methodTag.
    private string HandleUnitNoArgMethod(string command, string methodName, string methodTag)
    {
        var args = ExtractAllStringArgs(command);
        if (args.Count < 1)
            return $"ERR: SWFOC_{methodTag}UnitLua: expected (unit_lua_expr)";
        GameState.LastUnitNoArgMethodCalls[methodTag] = args[0];
        return $"OK: {methodName} dispatched (LIVE — engine Lua API)";
    }

    // 2026-04-28 (iter 111): generic handler for the per-unit Lua-bool
    // method wires (Hide / Prevent_AI_Usage / Set_Selectable). Real
    // bridge composes `(<unit>):<method>(<bool>)` via DoString and
    // returns "OK: <method> dispatched (LIVE — engine Lua API)".
    // Simulator captures (unit, bool) into a per-method last-call slot
    // keyed by the methodTag so tests can assert each independently.
    private string HandleUnitBoolMethod(string command, string methodName, string methodTag)
    {
        var args = ExtractAllStringArgs(command);
        if (args.Count < 2)
            return $"ERR: SWFOC_{methodTag}UnitLua: expected (unit_lua_expr, bool_lua_expr)";
        GameState.LastUnitBoolMethodCalls[methodTag] = (args[0], args[1]);
        return $"OK: {methodName} dispatched (LIVE — engine Lua API)";
    }

    // 2026-04-29 (iter 154): generic handler for the per-unit Lua-float
    // method wires (Take_Damage / Set_Damage_Modifier / Set_Rate_Of_Fire_Modifier).
    // Real bridge composes `(<unit>):<method>(<float>)` via DoString.
    // Simulator captures (unit, float) into a per-method last-call slot
    // keyed by methodTag.
    private string HandleUnitFloatMethod(string command, string methodName, string methodTag)
    {
        var args = ExtractAllStringArgs(command);
        if (args.Count < 2)
            return $"ERR: SWFOC_{methodTag}Lua: expected (unit_lua_expr, float_lua_expr)";
        GameState.LastUnitFloatMethodCalls[methodTag] = (args[0], args[1]);
        return $"OK: {methodName} dispatched (LIVE — engine Lua API)";
    }

    // 2026-04-28 (iter 110): LIVE per-unit invuln via engine's
    // Make_Invulnerable Lua method. The bridge composes
    // `(<unit>):Make_Invulnerable(<bool>)` and dispatches via DoString.
    // Engine wrapper at RVA 0x57D550 propagates via BehaviorAttach to
    // every hardpoint (verified). Simulator captures both expressions.
    private string HandleMakeUnitInvulnLua(string command)
    {
        var args = ExtractAllStringArgs(command);
        if (args.Count < 2)
            return "ERR: SWFOC_MakeUnitInvulnLua: expected (unit_lua_expr, bool_lua_expr)";
        GameState.LastMakeUnitInvulnLuaUnit = args[0];
        GameState.LastMakeUnitInvulnLuaBool = args[1];
        return "OK: Make_Invulnerable dispatched (LIVE — engine Lua API)";
    }

    // 2026-04-28 (iter 109): LIVE unit spawn via engine's Spawn_Unit Lua
    // API. The bridge composes `Spawn_Unit(<player>, <type>, <position>)`
    // and dispatches via DoString. Simulator mirror: capture all three
    // raw expressions so tests can assert the full call shape.
    private string HandleSpawnUnitLua(string command)
    {
        var args = ExtractAllStringArgs(command);
        if (args.Count < 3)
            return "ERR: SWFOC_SpawnUnitLua: expected (player_expr, type_expr, position_expr)";
        GameState.LastSpawnUnitLuaPlayer = args[0];
        GameState.LastSpawnUnitLuaType = args[1];
        GameState.LastSpawnUnitLuaPosition = args[2];

        var typeName = ExtractSingleStringCallArg(args[1], "Find_Object_Type");
        var ownerSlot = ResolveFindPlayerSlot(args[0]);
        if (typeName is not null)
        {
            if (!GameState.KnownTypeNames.Contains(typeName))
            {
                return "ERR: unknown type: " + typeName;
            }

            if (ownerSlot is int slot && GameState.GetPlayer(slot) is not null)
            {
                GameState.Units.Add(new FakeUnit
                {
                    TypeName = typeName,
                    OwnerSlot = slot,
                    MaxHull = 100,
                    CurrentHull = 100,
                    IsGround = true,
                });
            }
        }

        return "OK: Spawn_Unit dispatched (LIVE — engine Lua API)";
    }

    private int? ResolveFindPlayerSlot(string luaExpr)
    {
        var faction = ExtractSingleStringCallArg(luaExpr, "Find_Player");
        if (string.IsNullOrWhiteSpace(faction))
        {
            return null;
        }

        if (string.Equals(faction, "local", StringComparison.OrdinalIgnoreCase))
        {
            return GameState.GetLocalHuman()?.Slot;
        }

        return GameState.Players.FirstOrDefault(p =>
            string.Equals(p.Faction, faction, StringComparison.OrdinalIgnoreCase))?.Slot;
    }

    private static string? ExtractSingleStringCallArg(string luaExpr, string functionName)
    {
        var pattern = @"^\s*" + Regex.Escape(functionName) + @"\s*\(\s*""([^""]+)""\s*\)\s*$";
        var match = Regex.Match(luaExpr, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    // 2026-04-28 (iter 108): LIVE per-unit owner change via engine's
    // Change_Owner Lua method. The bridge composes
    // `(<unit_expr>):Change_Owner(<player_expr>)` and dispatches via
    // DoString. Simulator mirror: capture both raw expressions so tests
    // can assert what was sent. Real bridge returns "OK: Change_Owner
    // dispatched (LIVE — engine Lua API)" on success.
    private string HandleChangeUnitOwner(string command)
    {
        var args = ExtractAllStringArgs(command);
        if (args.Count < 2)
            return "ERR: SWFOC_ChangeUnitOwner: expected (unit_lua_expr, player_lua_expr)";
        GameState.LastChangeUnitOwnerUnit = args[0];
        GameState.LastChangeUnitOwnerPlayer = args[1];
        return "OK: Change_Owner dispatched (LIVE — engine Lua API)";
    }

    // 2026-04-28 (iter 107): LIVE camera target via engine's Scroll_Camera_To
    // Lua API. The bridge wraps an arbitrary Lua expression in
    // `Scroll_Camera_To(<expr>)` and dispatches via DoString. Simulator
    // mirror: capture the raw expression so tests can assert what was sent.
    // Real bridge returns "OK: Scroll_Camera_To dispatched (LIVE — engine
    // Lua API)" on success, "ERR: ..." on engine error. We mirror the
    // success shape because the simulator has no engine to fail.
    private string HandleScrollCameraToTarget(string command)
    {
        // Extract the first string arg — that's the target expression.
        var target = ExtractFirstStringArg(command);
        if (target is null || target.Length == 0)
            return "ERR: SWFOC_ScrollCameraToTarget: target expression required";
        GameState.LastScrollCameraToTarget = target;
        return "OK: Scroll_Camera_To dispatched (LIVE — engine Lua API)";
    }

    // 2026-04-29 (iter 143) LIVE — bridge composes Camera_To_Follow(EXPR)
    // and dispatches via DoString. Same shape as ScrollCameraToTarget; the
    // simulator captures the raw expression so tests can assert what was sent.
    private string HandleCameraFollow(string command)
    {
        var target = ExtractFirstStringArg(command);
        if (target is null || target.Length == 0)
            return "ERR: SWFOC_CameraFollow: target expression required";
        GameState.LastCameraFollowTarget = target;
        return "OK: Camera_To_Follow dispatched (LIVE — engine Lua API)";
    }

    // 2026-04-29 (iter 144) LIVE — bridge composes Rotate_Camera_To(EXPR)
    // and dispatches via DoString. Sibling to iter 143 CameraFollow; the
    // simulator captures the raw expression for round-trip assertions.
    private string HandleRotateCameraTo(string command)
    {
        var target = ExtractFirstStringArg(command);
        if (target is null || target.Length == 0)
            return "ERR: SWFOC_RotateCameraTo: target expression required";
        GameState.LastRotateCameraToTarget = target;
        return "OK: Rotate_Camera_To dispatched (LIVE — engine Lua API)";
    }

    // 2026-04-29 (iter 145) LIVE — cinematic camera quad. Each handler
    // tracks state on FakeGameState so tests can verify the state machine
    // transitions: Start → SetKey×N → TransitionKey → End.
    // 2026-04-29 (iter 151) LIVE — bridge composes (unit):Teleport(pos) and
    // dispatches via DoString. Simulator captures both raw expressions.
    private string HandleTeleportUnitLua(string command)
    {
        var args = ExtractAllStringArgs(command);
        if (args.Count < 2 ||
            string.IsNullOrWhiteSpace(args[0]) ||
            string.IsNullOrWhiteSpace(args[1]))
            return "ERR: SWFOC_TeleportUnitLua: expected (unit_expr, position_expr)";
        GameState.LastTeleportUnitExpr = args[0];
        GameState.LastTeleportPositionExpr = args[1];
        return "OK: Teleport dispatched (LIVE — engine Lua API)";
    }

    // 2026-04-29 (iter 152) LIVE — bridge composes
    // Galactic_Spawn_Unit(player, type, planet) and dispatches via DoString.
    // Simulator captures all 3 raw expressions for round-trip verification.
    private string HandleGalacticSpawnUnit(string command)
    {
        var args = ExtractAllStringArgs(command);
        if (args.Count < 3) return "ERR: SWFOC_GalacticSpawnUnit: expected (player_expr, type_expr, planet_expr)";
        GameState.LastGalacticSpawnPlayer = args[0];
        GameState.LastGalacticSpawnType = args[1];
        GameState.LastGalacticSpawnPlanet = args[2];
        return "OK: Galactic_Spawn_Unit dispatched (LIVE — engine Lua API)";
    }

    private string HandleStartCinematicCamera()
    {
        GameState.CinematicCameraActive = true;
        return "OK: Start_Cinematic_Camera dispatched (LIVE — engine Lua API)";
    }

    private string HandleEndCinematicCamera()
    {
        GameState.CinematicCameraActive = false;
        return "OK: End_Cinematic_Camera dispatched (LIVE — engine Lua API)";
    }

    private string HandleSetCinematicCameraKey(string command)
    {
        var args = ExtractFirstStringArg(command);
        if (args is null || args.Length == 0)
            return "ERR: SWFOC_SetCinematicCameraKey: args expression required";
        GameState.LastCinematicCameraKeyArgs = args;
        return "OK: Set_Cinematic_Camera_Key dispatched (LIVE — engine Lua API)";
    }

    private string HandleTransitionCinematicCameraKey(string command)
    {
        var args = ExtractFirstStringArg(command);
        if (args is null || args.Length == 0)
            return "ERR: SWFOC_TransitionCinematicCameraKey: args expression required";
        GameState.LastCinematicCameraTransitionArgs = args;
        return "OK: Transition_Cinematic_Camera_Key dispatched (LIVE — engine Lua API)";
    }

    private string HandleSetTechLevel(string command)
    {
        // Single-arg form: SWFOC_SetTechLevel(level) — applies to local human's slot.
        var level = ExtractFirstIntArg(command);
        if (level is null) return "ERR: bad arg";
        var local = GameState.GetLocalHuman();
        if (local is null) return "ERR: no local human";
        GameState.PerSlotTechLevel[local.Slot] = level.Value;
        return "ok";
    }

    // ====================================================================
    // Speed — Phase B
    // ====================================================================

    private string HandleSetGameSpeed(string command)
    {
        var floats = ExtractFloatArgs(command);
        if (floats.Count < 1) return "ERR: bad arg";
        GameState.GameSpeed = floats[0];
        return "ok";
    }

    private string HandleSetPerFactionSpeed(string command)
    {
        // 2026-04-28 (iter 100, master ralph loop): wire-format fix.
        // The bridge emits `SWFOC_SetPerFactionSpeedMultiplier(slot, mult)`
        // where `slot` is an integer (BridgeSpeedDispatcher.cs line 34).
        // The earlier simulator handler parsed it as a faction string —
        // a wire mismatch that would have masked editor regressions.
        // Now: parse as int, mirror the bridge's per-unit application:
        //   - record the value in PerFactionSpeed (key = slot.ToString())
        //   - set Speed on every alive unit owned by the slot
        //   - bump MaxSpeed when Speed exceeds it (simulator semantics)
        var ints = ExtractIntArgs(command);
        var floats = ExtractFloatArgs(command);
        if (ints.Count < 1 || floats.Count < 2) return "ERR: bad args";
        var slot = ints[0];
        // floats[0] is the int slot (regex captures it as a number too).
        // The mult/speed value is floats[1] when there are two distinct
        // numerical args, or floats[0] when the int didn't reach the float
        // regex. Pick the LAST float — robust to either parsing path.
        var absSpeed = floats[^1];

        GameState.PerFactionSpeed[slot.ToString(CultureInfo.InvariantCulture)] = absSpeed;
        foreach (var u in GameState.Units.Where(u => u.Alive && u.OwnerSlot == slot))
        {
            u.Speed = absSpeed;
            if (u.MaxSpeed < u.Speed) u.MaxSpeed = u.Speed;
        }
        return "ok";
    }

    private string HandleSetUnitSpeed(string command)
    {
        // 2026-04-28 (iter 100): bridge calls SetSpeedOverride engine fn
        // which writes locomotor +0x2A0 (override speed) and sets the
        // active flag at +0x29C. Simulator mirrors by writing FakeUnit.Speed.
        var ints = ExtractIntArgs(command);
        var floats = ExtractFloatArgs(command);
        if (ints.Count < 1 || floats.Count < 2) return "ERR: bad args";
        var u = GameState.GetUnit(ints[0]);
        if (u is null) return "ERR: no such unit";
        u.Speed = floats[1];
        if (u.MaxSpeed < u.Speed) u.MaxSpeed = u.Speed;
        return "ok";
    }

    private string HandleClearUnitSpeedOverride(string command)
    {
        // 2026-04-28 (iter 100): bridge calls ClearSpeedOverride engine fn
        // which clears the active flag at locomotor +0x29C. The unit's
        // base/natural max speed re-applies. Simulator mirrors by reverting
        // Speed to MaxSpeed (the closest analog to "engine default").
        var ints = ExtractIntArgs(command);
        if (ints.Count < 1) return "ERR: bad args";
        var u = GameState.GetUnit(ints[0]);
        if (u is null) return "ERR: no such unit";
        u.Speed = u.MaxSpeed;
        return "ok";
    }

    // ====================================================================
    // Hero Lab — Phase B
    // ====================================================================

    private string HandleListHeroes()
    {
        // BridgeHeroLabDispatcher.ListHeroesAsync parses NEWLINE-separated rows
        // with format: addr;type;owner;alive;respawn_ms;respawn_enabled
        // (see HeroLabTabState parsing in the editor).
        var heroes = GameState.Units.Where(u => u.IsHero).ToList();
        if (heroes.Count == 0) return "(no_heroes)";
        return string.Join("\n", heroes.Select(h =>
            string.Format(CultureInfo.InvariantCulture,
                "{0};{1};{2};{3};{4};{5}",
                h.Id,                            // addr (the editor parses as long)
                h.TypeName,
                h.OwnerSlot,
                h.Alive ? 1 : 0,
                GameState.HeroRespawnSeconds * 1000,  // respawn_ms
                GameState.Permadeath ? 0 : 1)));      // respawn_enabled
    }

    private string HandleHeroInstantRespawn(string command)
    {
        var id = ExtractFirstIntArg(command);
        if (id is null) return "ERR: bad arg";
        var u = GameState.GetUnit(id.Value);
        if (u is null) return "ERR: no such unit";
        if (!u.IsHero) return "ERR: not a hero";
        u.Revive();
        return "ok";
    }

    private string HandleHeroStatEdit(string command)
    {
        // SWFOC_HeroStatEdit(unitId, "MaxHull", 9999)
        var ints = ExtractIntArgs(command);
        var statName = ExtractFirstStringArg(command);
        var floats = ExtractFloatArgs(command);
        if (ints.Count < 1 || statName is null || floats.Count < 2) return "ERR: bad args";
        var u = GameState.GetUnit(ints[0]);
        if (u is null) return "ERR: no such unit";
        if (!u.IsHero) return "ERR: not a hero";
        var value = floats[1];
        switch (statName)
        {
            case "MaxHull":
                u.MaxHull = value;
                u.CurrentHull = Math.Min(u.CurrentHull, u.MaxHull);
                break;
            case "MaxShield":
                u.MaxShield = value;
                u.CurrentShield = Math.Min(u.CurrentShield, u.MaxShield);
                break;
            case "Speed":
                u.Speed = value;
                if (u.MaxSpeed < value) u.MaxSpeed = value;
                break;
            case "DamageScalar":
                u.DamageScalar = value;
                break;
            case "FireRateScalar":
                u.FireRateScalar = value;
                break;
            default:
                return "ERR: unknown stat: " + statName;
        }
        return "ok";
    }

    private string HandleSetHeroRespawn(string command)
    {
        // Phase B legacy form: SWFOC_SetHeroRespawn(seconds) — single arg.
        var seconds = ExtractFirstIntArg(command);
        if (seconds is null) return "ERR: bad arg";
        GameState.HeroRespawnSeconds = seconds.Value;
        return "ok";
    }

    private string HandleSetHeroRespawnTimer(string command)
    {
        // BridgeHeroLabDispatcher form: SWFOC_SetHeroRespawnTimer(addr, ms).
        // Per-hero override; we still update the global default for tests
        // that don't model individual heroes.
        var args = ExtractIntArgs(command);
        if (args.Count < 2) return "ERR: bad args";
        var heroId = args[0];
        var ms = args[1];
        var hero = GameState.GetUnit(heroId);
        if (hero is not null && hero.IsHero)
        {
            // No per-hero respawn field today; mirror to global so tests
            // can verify the call landed.
            GameState.HeroRespawnSeconds = ms / 1000;
            return "ok";
        }
        // Fall through: still update global so the call is observable.
        GameState.HeroRespawnSeconds = ms / 1000;
        return "ok";
    }

    private string HandleSetPermadeath(string command)
    {
        // Two arities seen:
        //   SetPermadeath(enable)            — Phase B legacy
        //   SetPermadeath(addr, enable)      — BridgeHeroLabDispatcher per-hero
        var args = ExtractIntArgs(command);
        if (args.Count == 1)
        {
            GameState.Permadeath = args[0] != 0;
            return "ok";
        }
        if (args.Count >= 2)
        {
            // Per-hero form: still mirror the engine behaviour by flipping
            // the global toggle (the simulator doesn't model per-hero
            // permadeath separately).
            GameState.Permadeath = args[1] != 0;
            return "ok";
        }
        return "ERR: bad arg";
    }

    private string HandleReviveUnit(string command)
    {
        var id = ExtractFirstIntArg(command);
        if (id is null) return "ERR: bad arg";
        var u = GameState.GetUnit(id.Value);
        if (u is null) return "ERR: no such unit";
        u.Revive();
        return "ok";
    }

    // ====================================================================
    // AI brain — Phase B
    // ====================================================================

    private string HandleGetAiBrain(string command)
    {
        var slot = ExtractFirstIntArg(command);
        if (slot is null) return "ERR: bad arg";
        var p = GameState.GetPlayer(slot.Value);
        if (p is null) return "ERR: no such slot";
        return p.HasAiBrain ? "1" : "0";
    }

    private string HandleNullAiBrain(string command)
    {
        var slot = ExtractFirstIntArg(command);
        if (slot is null) return "ERR: bad arg";
        var p = GameState.GetPlayer(slot.Value);
        if (p is null) return "ERR: no such slot";
        p.HasAiBrain = false;
        return "ok";
    }

    private string HandleAttachAiBrain(string command)
    {
        var slot = ExtractFirstIntArg(command);
        if (slot is null) return "ERR: bad arg";
        var p = GameState.GetPlayer(slot.Value);
        if (p is null) return "ERR: no such slot";
        p.HasAiBrain = true;
        return "ok";
    }

    private string HandleFreezeAi(string command)
    {
        var v = ExtractFirstIntArg(command);
        if (v is null) return "ERR: bad arg";
        GameState.AiEnabled = v.Value == 0;
        return "ok";
    }

    private string HandleSuspendAiLua(string command)
    {
        var floats = ExtractFloatArgs(command);
        if (floats.Count < 1) return "ERR: bad arg";
        GameState.AiEnabled = floats[0] <= 0;
        return "OK: Suspend_AI dispatched (LIVE — engine Lua API)";
    }

    // ====================================================================
    // Diplomacy + Economy — Phase B
    // ====================================================================

    private string HandleSetDiplomacy(string command)
    {
        // Three arities seen across editor call sites:
        //   SWFOC_SetDiplomacy(slotA, slotB, "Allied"|"Neutral"|"Hostile")     — Phase B legacy
        //   SWFOC_SetDiplomacy(slotA, slotB, relCode)                           — int-relation form
        //   SWFOC_SetDiplomacy('factionA', 'factionB', relCode)                 — BridgeGalacticDispatcher
        // BridgeGalacticDispatcher uses int rel codes: 0=Neutral, 1=Allied, 2=Hostile
        var stringArgs = ExtractAllStringArgs(command);
        var ints = ExtractIntArgs(command);

        if (stringArgs.Count >= 2)
        {
            // Faction-string form. Last int (if any) is the relation code.
            var relStr = ints.Count >= 1
                ? RelCodeToString(ints[^1])
                : ExtractFirstStringArg(command) ?? "";
            // The string-arg extractor sometimes catches the relation as the
            // 3rd string arg if the editor encodes it that way; prefer the
            // int code which we know the dispatcher emits.
            if (ints.Count >= 1) relStr = RelCodeToString(ints[^1]);
            if (string.IsNullOrEmpty(relStr)) return "ERR: bad relation";
            var key = stringArgs[0] + ":" + stringArgs[1];
            GameState.Diplomacy[key] = relStr;
            return "ok";
        }

        // Slot-int form.
        if (ints.Count < 2) return "ERR: bad args";
        if (GameState.GetPlayer(ints[0]) is null || GameState.GetPlayer(ints[1]) is null)
        {
            return "ERR: no such slot";
        }
        string rel;
        if (ints.Count >= 3)
        {
            rel = RelCodeToString(ints[2]);
        }
        else
        {
            var asString = ExtractFirstStringArg(command);
            if (asString is null) return "ERR: bad relation";
            rel = asString;
        }
        if (!new[] { "Allied", "Neutral", "Hostile" }.Contains(rel, StringComparer.OrdinalIgnoreCase))
        {
            return "ERR: bad relation: " + rel;
        }
        var slotKey = ints[0] + ":" + ints[1];
        GameState.Diplomacy[slotKey] = rel;
        return "ok";
    }

    private static string RelCodeToString(int code) => code switch
    {
        0 => "Neutral",
        1 => "Allied",
        2 => "Hostile",
        _ => "",
    };

    private string HandleSetIncomeMultiplier(string command)
    {
        // BridgeEconomyDispatcher emits per-SLOT: SWFOC_SetIncomeMultiplier(slot, mult).
        // Falls back to faction-keyed form for legacy tests that pass a string arg.
        var faction = ExtractFirstStringArg(command);
        var ints = ExtractIntArgs(command);
        var floats = ExtractFloatArgs(command);
        if (faction is not null && floats.Count >= 1)
        {
            GameState.PerFactionIncome[faction] = floats[0];
            return "ok";
        }
        if (ints.Count >= 1 && floats.Count >= 2)
        {
            var slot = ints[0];
            if (GameState.GetPlayer(slot) is null) return "ERR: no such slot";
            GameState.PerSlotIncomeMultiplier[slot] = floats[1];
            return "ok";
        }
        return "ERR: bad args";
    }

    private string HandleDrainEnemyCredits(string command)
    {
        // BridgeEconomyDispatcher emits a no-arg form: SWFOC_DrainEnemyCredits().
        // The engine looks up the local human and drains every other slot.
        // We honour an explicit slot arg too (legacy-friendly).
        var mySlot = ExtractFirstIntArg(command);
        if (mySlot is null)
        {
            var local = GameState.GetLocalHuman();
            if (local is null) return "ERR: no local human";
            mySlot = local.Slot;
        }
        var drained = 0;
        foreach (var p in GameState.Players.Where(p => p.Slot != mySlot.Value))
        {
            if (p.Credits > 0)
            {
                p.Credits = 0;
                drained++;
            }
        }
        return "ok:" + drained;
    }

    // ====================================================================
    // Event stream — Phase B
    // ====================================================================

    private string HandleEventStreamDrain()
    {
        if (GameState.EventQueue.Count == 0) return "(no_events)";
        var batch = new System.Text.StringBuilder();
        while (GameState.EventQueue.Count > 0)
        {
            if (batch.Length > 0) batch.Append('|');
            batch.Append(GameState.EventQueue.Dequeue());
        }
        return batch.ToString();
    }

    private string HandleEventControl(string command)
    {
        // SWFOC_EventControl("clear"|"pause"|"resume")
        var arg = ExtractFirstStringArg(command);
        if (arg is null) return "ERR: bad arg";
        switch (arg.ToLowerInvariant())
        {
            case "clear":
                GameState.EventQueue.Clear();
                return "ok";
            case "pause":
            case "resume":
                // The simulator doesn't model pause semantics yet — ack
                // so the editor can toggle without errors.
                return "ok:" + arg;
            default:
                return "ERR: unknown control verb: " + arg;
        }
    }

    // ====================================================================
    // Phase C — global toggles, inspector, hardpoints, log/tick
    // ====================================================================

    private string HandleGodMode(string command)
    {
        var v = ExtractFirstIntArg(command);
        if (v is null) return "ERR: bad arg";
        var on = v.Value != 0;
        GameState.GodModeEnabled = on;
        // Also flip Invulnerable on every alive unit so ApplyDamage actually
        // demonstrates the effect — what the engine's god-mode hook does.
        foreach (var u in GameState.Units.Where(u => u.Alive))
        {
            u.Invulnerable = on;
        }
        return "ok";
    }

    private string HandleHealAllLocal()
    {
        var local = GameState.GetLocalHuman();
        if (local is null) return "ERR: no local human";
        foreach (var u in GameState.Units.Where(u => u.OwnerSlot == local.Slot))
        {
            u.CurrentHull = u.MaxHull;
            u.CurrentShield = u.MaxShield;
            if (!u.Alive)
            {
                u.Alive = true;
            }
        }
        return "ok";
    }

    private string HandleFreeBuild(string command)
    {
        var v = ExtractFirstIntArg(command);
        if (v is null) return "ERR: bad arg";
        GameState.FreeBuildEnabled = v.Value != 0;
        return "ok";
    }

    private string HandleFreeCam(string command)
    {
        var v = ExtractFirstIntArg(command);
        if (v is null) return "ERR: bad arg";
        GameState.FreeCamEnabled = v.Value != 0;
        return "ok";
    }

    private string HandleFreezeCredits(string command)
    {
        var v = ExtractFirstIntArg(command);
        if (v is null) return "ERR: bad arg";
        GameState.CreditsFrozen = v.Value != 0;
        return "ok";
    }

    private string HandleToggleOhk(string command)
    {
        var v = ExtractFirstIntArg(command);
        if (v is null) return "ERR: bad arg";
        GameState.OneHitKillEnabled = v.Value != 0;
        return "ok";
    }

    private string HandleCombinedGodOhk(string command)
    {
        // Composite: toggle GodMode + OHK in one shot.
        var v = ExtractFirstIntArg(command);
        if (v is null) return "ERR: bad arg";
        var on = v.Value != 0;
        GameState.GodModeEnabled = on;
        GameState.OneHitKillEnabled = on;
        foreach (var u in GameState.Units.Where(u => u.Alive))
        {
            u.Invulnerable = on;
        }
        return "ok";
    }

    private string HandleInspectUnit(string command)
    {
        var id = ExtractFirstIntArg(command);
        if (id is null) return "ERR: bad arg";
        var u = GameState.GetUnit(id.Value);
        if (u is null) return "ERR: no such unit";
        GameState.SelectedUnitId = u.Id;
        // BridgeInspectorDispatcher parses SPACE-DELIMITED key=value tokens.
        // Keys it reads: hull, owner, invuln_flag, prevent_death (with
        // unused: obj_id, parent_idx, status_flags, hardpoint_flag,
        // components_ptr — emit them so the wire payload looks complete).
        return string.Format(CultureInfo.InvariantCulture,
            "obj_id={0} hull={1} owner={2} parent_idx=0 status_flags=0 prevent_death={3} invuln_flag={4} hardpoint_flag=0 components_ptr=0",
            u.Id,
            u.CurrentHull.ToString(CultureInfo.InvariantCulture),
            u.OwnerSlot,
            u.DeathPrevented ? 1 : 0,
            u.Invulnerable ? 1 : 0);
    }

    private string HandleGetHardpoints(string command)
    {
        // SWFOC_GetHardpoints(unitId) → "name1;status1|name2;status2|..."
        var id = ExtractFirstIntArg(command);
        if (id is null) return "ERR: bad arg";
        var u = GameState.GetUnit(id.Value);
        if (u is null) return "ERR: no such unit";
        // Synthetic hardpoints — every unit has a basic loadout. Real engine
        // exposes per-XML hardpoint definitions; for the simulator a
        // canonical 3-piece list is enough to test the iteration.
        var status = u.Invulnerable ? "INVULNERABLE" : "OK";
        return string.Join("|", new[]
        {
            "MAIN_GUN;" + status,
            "SECONDARY_GUN;" + status,
            "ENGINE;" + status,
        });
    }

    private string HandleSetTargetFilter(string command)
    {
        // Two arities seen:
        //   1-arg: SWFOC_SetTargetFilter(mask)  — early Phase C tests
        //   2-arg: SWFOC_SetTargetFilter(slot, mask) — BridgeCombatDispatcher
        var ints = ExtractIntArgs(command);
        if (ints.Count == 1)
        {
            GameState.TargetFilterMask = ints[0];
            return "ok";
        }
        if (ints.Count >= 2)
        {
            var slot = ints[0];
            if (GameState.GetPlayer(slot) is null) return "ERR: no such slot";
            GameState.PerSlotTargetFilter[slot] = ints[1];
            GameState.TargetFilterMask = ints[1]; // also surface globally for legacy tests
            return "ok";
        }
        return "ERR: bad args";
    }

    private string HandleSetUnitCapOverride(string command)
    {
        var faction = ExtractFirstStringArg(command);
        var ints = ExtractIntArgs(command);
        if (faction is null || ints.Count < 1) return "ERR: bad args";
        GameState.PerFactionUnitCap[faction] = ints[0];
        return "ok";
    }

    private string HandleSetUnitField(string command)
    {
        // SWFOC_SetUnitField(unitId, "field_name", value)
        //
        // Two field-name conventions supported in parallel:
        //   1. Canonical snake_case (matches BridgeUnitStatEditDispatcher
        //      wire format + lua_bridge.cpp's Lua_SetUnitField branches).
        //      LIVE branches: hull/shield/speed (iter 136) + invuln_flag/
        //      prevent_death (iter 243). Phase-1 mirror branches:
        //      max_hull/max_shield/max_speed/attack_power/is_hero/
        //      respawn_enabled/respawn_ms/owner_slot.
        //   2. Legacy PascalCase (predates iter 136 — used by
        //      PhaseCSimulatorTests "MaxHull" assertions). Kept for
        //      backwards-compat; new tests should prefer snake_case.
        //
        // Iter 244 alignment: simulator now matches the bridge's 13-field
        // taxonomy so SimulatorRoundTrip tests can use the same wire
        // format the live editor produces.
        var ints = ExtractIntArgs(command);
        var fieldName = ExtractFirstStringArg(command);
        var floats = ExtractFloatArgs(command);
        if (ints.Count < 1 || fieldName is null || floats.Count < 2) return "ERR: bad args";
        var u = GameState.GetUnit(ints[0]);
        if (u is null) return "ERR: no such unit";
        var value = floats[1];
        switch (fieldName)
        {
            // Canonical snake_case (bridge wire format).
            // Iter 136 LIVE branches:
            case "hull": u.CurrentHull = value; break;
            case "shield": u.CurrentShield = value; break;
            case "speed": u.Speed = value; break;
            // Iter 243 LIVE branches (display-only direct writes; pair
            // with iter-110 SWFOC_MakeInvulnerableLua + iter-153
            // SWFOC_SetCannotBeKilledLua for engine-state-aware paths):
            case "invuln_flag": u.Invulnerable = value != 0f; break;
            case "prevent_death": u.DeathPrevented = value != 0f; break;
            // Iter 258 LIVE branches — TYPE-LEVEL writes mirror the bridge's
            // iter-258 walk (unit + 0x298 → UnitType*; write at +0xDCC for
            // max_hull or dual-write +0xDD0/+0xDD4 for max_shield). Effect
            // applies to EVERY simulator unit sharing the same TypeName, NOT
            // just the indexed unit. Mirrors the operator-trust scope of the
            // bridge: type-shared, not per-instance.
            case "max_hull":
                foreach (var sibling in GameState.Units)
                    if (sibling.TypeName == u.TypeName) sibling.MaxHull = value;
                break;
            case "max_shield":
                // Single MaxShield field on FakeUnit covers both front+rear
                // semantics from the operator's perspective; the bridge's
                // dual-write to UnitType+0xDD0 / +0xDD4 collapses to one
                // value at the FakeUnit abstraction level.
                foreach (var sibling in GameState.Units)
                    if (sibling.TypeName == u.TypeName) sibling.MaxShield = value;
                break;
            // Phase-1 mirror only (no engine effect; bridge queues to
            // g_pendingUnitFieldWrites pending future RTTI offset table):
            case "max_speed": u.MaxSpeed = value; break;
            case "attack_power": u.DamageScalar = value; break;
            case "is_hero": u.IsHero = value != 0f; break;
            case "respawn_enabled": /* no FakeUnit field; no-op mirror */ break;
            case "respawn_ms": /* no FakeUnit field; no-op mirror */ break;
            // owner_slot direct write would bypass engine state machinery
            // (selection-list, AI brain, UI roster). Operator should use
            // iter-108 SWFOC_ChangeUnitOwnerLua for engine-aware change.
            // Simulator mirrors the staged value into FakeUnit.OwnerSlot
            // for display/test round-trip but does NOT cascade to
            // FakePlayer's selection-list (matches Phase-1 mirror semantics).
            case "owner_slot": u.OwnerSlot = (int)value; break;

            // Legacy PascalCase (pre-iter-136). Kept for backwards-compat
            // with PhaseCSimulatorTests + early simulator-only tests.
            case "MaxHull": u.MaxHull = value; break;
            case "CurrentHull": u.CurrentHull = value; break;
            case "MaxShield": u.MaxShield = value; break;
            case "CurrentShield": u.CurrentShield = value; break;
            case "Speed": u.Speed = value; break;
            case "MaxSpeed": u.MaxSpeed = value; break;
            case "DamageScalar": u.DamageScalar = value; break;
            case "FireRateScalar": u.FireRateScalar = value; break;
            default: return "ERR: unknown field: " + fieldName;
        }
        return "ok";
    }

    private string HandleGetPlayerWrapper(string command)
    {
        var slot = ExtractFirstIntArg(command);
        if (slot is null) return "ERR: bad arg";
        var p = GameState.GetPlayer(slot.Value);
        if (p is null) return "ERR: no such slot";
        // Pseudo-pointer: the editor only checks for non-zero.
        return ((p.Slot + 1) * 0x1000).ToString("X", CultureInfo.InvariantCulture);
    }

    private string HandleDumpState()
    {
        return string.Format(CultureInfo.InvariantCulture,
            "mode={0};ai={1};fog={2};speed={3};planets={4};units={5};alive={6};god={7};free_build={8};permadeath={9}",
            GameState.RuntimeMode,
            GameState.AiEnabled ? 1 : 0,
            GameState.FogOfWarEnabled ? 1 : 0,
            GameState.GameSpeed,
            GameState.Planets.Count,
            GameState.Units.Count,
            GameState.Units.Count(u => u.Alive),
            GameState.GodModeEnabled ? 1 : 0,
            GameState.FreeBuildEnabled ? 1 : 0,
            GameState.Permadeath ? 1 : 0);
    }

    private string HandleLog(string command)
    {
        var msg = ExtractFirstStringArg(command);
        if (msg is null) return "ERR: bad arg";
        GameState.LogLines.Add(msg);
        return "ok";
    }

    private string HandleEnumerateUnits()
    {
        // Same shape as ListTacticalUnits but includes dead units. Used by
        // diagnostic dumps; ListTacticalUnits filters to alive only.
        if (GameState.Units.Count == 0) return "(none)";
        return string.Join("|", GameState.Units.Select(u =>
            string.Format(CultureInfo.InvariantCulture,
                "{0};{1};{2};{3};{4}",
                u.Id, u.TypeName, u.OwnerSlot,
                u.CurrentHull.ToString(CultureInfo.InvariantCulture),
                u.Alive ? 1 : 0)));
    }

    // ====================================================================
    // Lua-arg parsing helpers
    // ====================================================================
    //
    // The function name itself can contain digits (SWFOC_SetHumanPlayer_v3).
    // A naive `\d+` scan over the whole command would consume the version
    // suffix as the first int arg — wrong. So we slice the parenthesised
    // arg list first, then scan numbers/strings inside that slice only.

    // Matches integers anywhere in the arg-list slice.
    private static readonly Regex s_intRx = new(@"-?\d+", RegexOptions.Compiled);
    // Matches floats with optional decimal.
    private static readonly Regex s_floatRx = new(@"-?\d+(?:\.\d+)?", RegexOptions.Compiled);
    // Matches Lua quoted strings (either single OR double). Editor call
    // sites are inconsistent — BridgeSpawningDispatcher uses single quotes,
    // PlayerStateTabViewModel uses double quotes — so the simulator must
    // accept both. Doesn't handle escapes; that's fine for the emission
    // patterns the editor actually produces (no embedded quotes).
    // 2026-04-28: iter 107 attempted to extend this regex to handle
    // escaped quotes (`\"Yavin\"` inside `"..."` arguments) but doing so
    // broke 10+ unrelated simulator tests whose handlers depend on the
    // exact match shape. Revert to the original — it captures normal
    // Lua-style argument strings reliably for every existing handler.
    // For iter 107's nested-quote case, callers should use single-quoted
    // Lua syntax (`'Find_Planet("Yavin")'`) so the outer single quotes
    // wrap the inner double quotes without needing escapes. The bridge
    // accepts either form because `SWFOC_DoString` parses Lua source
    // directly.
    private static readonly Regex s_strRx = new(
        "(?:\"([^\"]*)\"|'([^']*)')",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns the substring inside the OUTERMOST parentheses, e.g.
    /// <c>"return SWFOC_Foo_v3(1, 2)"</c> → <c>"1, 2"</c>. Returns the
    /// whole command when no parens are present (safe fallback).
    /// </summary>
    internal static string ArgSlice(string command)
    {
        var open = command.IndexOf('(');
        if (open < 0) return command;
        var close = command.LastIndexOf(')');
        if (close <= open) return command[(open + 1)..];
        return command.Substring(open + 1, close - open - 1);
    }

    private static int? ExtractFirstIntArg(string command)
    {
        var m = s_intRx.Match(ArgSlice(command));
        if (!m.Success) return null;
        return int.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v : null;
    }

    private static System.Collections.Generic.List<int> ExtractIntArgs(string command)
    {
        var result = new System.Collections.Generic.List<int>();
        foreach (Match m in s_intRx.Matches(ArgSlice(command)))
        {
            if (int.TryParse(m.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                result.Add(v);
            }
        }
        return result;
    }

    private static System.Collections.Generic.List<float> ExtractFloatArgs(string command)
    {
        var result = new System.Collections.Generic.List<float>();
        foreach (Match m in s_floatRx.Matches(ArgSlice(command)))
        {
            if (float.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
            {
                result.Add(v);
            }
        }
        return result;
    }

    private static string? ExtractFirstStringArg(string command)
    {
        var m = s_strRx.Match(ArgSlice(command));
        if (!m.Success) return null;
        // Group 1 is the double-quoted alternative, group 2 the single-quoted.
        // For alternation, the non-matching group has Success=false even
        // when the overall match succeeded — that's how we tell them apart.
        return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
    }

    /// <summary>
    /// Returns every quoted-string argument from the command in left-to-right
    /// order. Needed by handlers that take multiple string args (e.g.
    /// <c>SWFOC_ChangePlanetOwner('planet', 'faction')</c> or
    /// <c>SWFOC_SetDiplomacy('a', 'b', code)</c>).
    /// </summary>
    private static System.Collections.Generic.List<string> ExtractAllStringArgs(string command)
    {
        var result = new System.Collections.Generic.List<string>();
        foreach (Match m in s_strRx.Matches(ArgSlice(command)))
        {
            result.Add(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value);
        }
        return result;
    }
}
