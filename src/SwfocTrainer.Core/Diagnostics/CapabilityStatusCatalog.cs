using System.Collections.Generic;

namespace SwfocTrainer.Core.Diagnostics;

/// <summary>
/// 2026-04-26: per-helper readiness badge taxonomy. The V2 UI binds a
/// status pill beside every bridge-helper button so the operator can
/// distinguish "Live engine effect" from "Phase 2 hook pending — clicks
/// will succeed but the game won't change".
///
/// Single source of truth for the badge data; the UI must NOT hard-code
/// per-helper strings. This shape mirrors the
/// <c>knowledge-base/phase2_hook_backlog_2026-04-26.md</c> table.
/// </summary>
public enum CapabilityStatus
{
    /// <summary>Direct engine call, observable mutation.</summary>
    Live,
    /// <summary>Replay mirror green, live behaviour unverified.</summary>
    ReplayVerified,
    /// <summary>Phase 1 mirror works, Phase 2 detour BLOCKED-NO-RVA.</summary>
    Phase2HookPending,
    /// <summary>Needs live game; offline harness can't exercise.</summary>
    RequiresLiveSwfoc,
    /// <summary>Registered but out-of-scope this release.</summary>
    Unavailable,
}

public sealed record CapabilityStatusEntry(
    string HelperName,
    CapabilityStatus Status,
    string? Note = null);

public static class CapabilityStatusCatalog
{
    /// <summary>
    /// 2026-04-26 snapshot. Sourced from the same Phase 2 backlog document
    /// the bridge ships with. When a Phase 2 hook lands, update both the
    /// markdown catalogue + this dictionary in the same commit so the UI
    /// stays in sync with the bridge state.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, CapabilityStatusEntry> Entries =
        new Dictionary<string, CapabilityStatusEntry>(StringComparer.OrdinalIgnoreCase)
        {
            // Confirmed live (engine effect verified)
            // 2026-04-27 (iter 40): added 9 entries the editor was already
            // calling but that hadn't been catalogued. Operator was seeing
            // "UNAVAILABLE" for these even though most are read-only
            // diagnostics that work fine.
            ["SWFOC_BatchTypeExists"] = new("SWFOC_BatchTypeExists", CapabilityStatus.Live,
                "Read-only GameObjectTypeManager probe — vanilla/mod isolation for Spawning tab"),
            ["SWFOC_DiagListRegisteredFunctions"] = new("SWFOC_DiagListRegisteredFunctions", CapabilityStatus.Live,
                "Read-only bridge introspection"),
            ["SWFOC_DiagSelfTest"] = new("SWFOC_DiagSelfTest", CapabilityStatus.Live,
                "Read-only bridge self-test"),
            ["SWFOC_GetBuildInfo"] = new("SWFOC_GetBuildInfo", CapabilityStatus.Live,
                "Read-only bridge build banner"),
            ["SWFOC_Log"] = new("SWFOC_Log", CapabilityStatus.Live,
                "Append to bridge log buffer — write-only diagnostic"),
            ["SWFOC_GodMode"] = new("SWFOC_GodMode", CapabilityStatus.Live,
                "Hardpoint-behavior sweep + SetHP detour"),
            ["SWFOC_OneHitKill"] = new("SWFOC_OneHitKill", CapabilityStatus.Live,
                "SetHP detour"),
            ["SWFOC_HealAllLocal"] = new("SWFOC_HealAllLocal", CapabilityStatus.Live,
                "Engine call sweep over local units"),
            ["SWFOC_RevealAll"] = new("SWFOC_RevealAll", CapabilityStatus.Live,
                "Engine call via SWFOC_DoString shim"),
            ["SWFOC_SetHumanPlayer_v3"] = new("SWFOC_SetHumanPlayer_v3", CapabilityStatus.Live,
                "Manual sweep + +0x360 swap + subsystem refresh"),
            ["SWFOC_NullAiBrain"] = new("SWFOC_NullAiBrain", CapabilityStatus.Live,
                "Direct +0x360 write"),
            ["SWFOC_GetAiBrain"] = new("SWFOC_GetAiBrain", CapabilityStatus.Live,
                "Read-only +0x360 probe"),
            ["SWFOC_AttachAiBrain"] = new("SWFOC_AttachAiBrain", CapabilityStatus.Live,
                "LIVE 2026-04-26 — calls AIPlayerClass simple factory at RVA 0x4AFF50 (allocates 0x60 + ctor at 0x4AF810). Multi-tool VERIFIED ctor (IDA + Ghidra); factory VERIFIED-EXISTS via IDA decompile."),

            // Phase 1 mirror only — Phase 2 hook pending
            // 2026-04-27 (iter 40): 4 more orphans added with Phase2HookPending.
            ["SWFOC_ChangePlanetOwnerWithMode"] = new("SWFOC_ChangePlanetOwnerWithMode", CapabilityStatus.Phase2HookPending,
                "Phase-1 mirror added iter 137 (was vestigial pre-iter-137: editor's "
              + "BridgeGalacticDispatcher.ChangePlanetOwnerWithModeAsync called this helper "
              + "but the bridge had no implementation, so the operator's Convert/PureKick "
              + "buttons errored at runtime with 'attempt to call nil'). Phase-2 engine "
              + "wire-through blocked per iter 134 audit — engine writers "
              + "PlanetFactionChange_FullTransfer @ 0x3FB040 (3989 bytes, 4 args) and "
              + "PlanetFactionChange_InitialSet @ 0x3FA160 (271 bytes, 2 args) too complex "
              + "for single-iter Resolve<>() pattern; no Planet:Change_Owner Lua wrapper. "
              + "Operator's actual button surface uses overlay Feature 3 (iter 33-34) — a "
              + "separate non-SWFOC_ dispatch path in the C++ overlay DLL. This SWFOC_ "
              + "helper is a doc-only fallback so the bridge contract isn't broken."),
            ["SWFOC_SpawnAsStoryArrival"] = new("SWFOC_SpawnAsStoryArrival", CapabilityStatus.Phase2HookPending,
                "Phase-1 mirror added iter 137 (was vestigial pre-iter-137: same situation "
              + "as ChangePlanetOwnerWithMode — editor called the helper but bridge had no "
              + "implementation, operator button errored at runtime). Phase-2 engine "
              + "wire-through blocked per iter 134 audit — StoryEvent_Factory_Create requires "
              + "multi-arg state setup (event trigger, faction, planet binding) not "
              + "single-iter achievable. Operator's actual button surface uses overlay "
              + "Feature 2 (iter 34) via the C++ overlay DLL's separate dispatch path. "
              + "Iter 433: Event-driven subsystem (StoryEvent system; per iter-426 "
              + "feedback_event_driven_defer_pattern.md rule). Multi-iter A1.x offset RE "
              + "required, not 3-iter mini-arc."),
            ["SWFOC_EventControl"] = new("SWFOC_EventControl", CapabilityStatus.Phase2HookPending,
                "Pause/resume of engine event queue — BLOCKED-NO-RVA. "
              + "Iter 433: Event-driven subsystem (engine event queue is the canonical "
              + "Observer-pattern infrastructure; per iter-426 feedback_event_driven_defer_pattern.md "
              + "rule). Multi-iter A1.x offset RE required to hook into queue tick."),
            // iter-450 scaffolding: SWFOC_TriggerVictory wraps the 18 VictoryType
            // enum entries pinned at rva_victory_type_enum_init @ 0x341FF0 (iter-414).
            // Wrapper validates the requested victory_type string + stages pending
            // state. The actual injection requires a MinHook detour at the Option C
            // hook target (rva_victory_monitor_counter_inc @ 0x341FE0; iter-449
            // disambiguation pinned this as the safest hook surface). iter-450 calls
            // MH_CreateHook for the dormant trampoline but DEFERS MH_EnableHook to
            // iter-450a, which still needs (a) AwaitingVictoryTest 48-byte struct
            // layout RE, (b) capture-on-CTOR hook at rva_victory_monitor_ctor @
            // 0x341850 to identify which `rcx` is the VictoryMonitor instance.
            // See knowledge-base/iter449_breakthrough_disambiguation_parent_tick_inlines.md.
            ["SWFOC_TriggerVictory"] = new("SWFOC_TriggerVictory", CapabilityStatus.Phase2HookPending,
                "Programmatic victory trigger from the 18 VictoryType enum entries "
              + "(Galactic_Conquer / Galactic_Control / Skirmish_Control / Sub_Tactical_All / "
              + "Sub_Tactical_Story / etc; per rva_victory_type_enum_init @ 0x341FF0). "
              + "iter-450 scaffolding: bridge wrapper validates the victory_type string "
              + "against 14 known names (Galactic_*, Skirmish_*, Sub_Tactical_*) and stages "
              + "pending state in g_victoryTriggerPending + g_victoryTriggerType. "
              + "Iter 450: Event-driven subsystem (VictoryMonitorClass holds DynamicVector<AwaitingVictoryTestType> "
              + "at instance+0x68; iterated each frame by parent tick @ 0x456970 which calls "
              + "counter_inc @ 0x341FE0 once per tick; per iter-426 feedback_event_driven_defer_pattern.md "
              + "rule -- MUST defer to multi-iter A1.x or commit fully). "
              + "Hook strategy SELECTED at iter-449: Option C MinHook detour at counter_inc "
              + "(LOW RISK; 16-byte trampoline; per-tick timing aligned with VictoryMonitor work) -- "
              + "iter-450 installs the dormant trampoline (MH_CreateHook only); iter-450a needs "
              + "(a) AwaitingVictoryTest 48-byte struct layout RE for safe injection, "
              + "(b) capture-on-CTOR hook at 0x341850 to resolve the `rcx`-discriminator problem "
              + "(0x341FE0 fires for many engine subsystems; we need to distinguish VictoryMonitor "
              + "instances). No operator-LIVE alternative -- VictoryMonitor is the engine's only "
              + "path to programmatic victory triggering."),
            // 2026-04-29 (iter 130): catalog drift caught. Bridge
            // `Lua_SetHeroRespawn` has been writing to
            // `g_base + RVA::DefaultHeroRespawnTime` (RVA 0xB169F0,
            // confirmed by `fact_global_default_hero_respawn_time` in
            // verified_facts.json) since the bridge was first written —
            // but the catalog entry still said PHASE 2 PENDING /
            // BLOCKED-NO-RVA. Iter 128 introduced the
            // re-audit-via-callgraph-CLI pattern; iter 130 caught this
            // drift while re-auditing A1.4 Hero respawn. The wire is
            // GLOBAL-only (sets the default for new respawn timers; does
            // NOT reset already-queued respawns) — see Note. Per-hero
            // `SetHeroRespawnTimer` remains PHASE 2 PENDING.
            ["SWFOC_SetHeroRespawn"] = new("SWFOC_SetHeroRespawn", CapabilityStatus.Live,
                "Global default-respawn-time override — writes float at RVA 0xB169F0 (LIVE). " +
                "Affects timers created AFTER the call; doesn't reset already-queued respawns. " +
                "Range clamped to [0, 600] seconds."),
            ["SWFOC_SetIncomeMultiplier"] = new("SWFOC_SetIncomeMultiplier", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA"),
            ["SWFOC_SetGameSpeed"] = new("SWFOC_SetGameSpeed", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA"),
            ["SWFOC_FreezeCredits"] = new("SWFOC_FreezeCredits", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA — superseded by iter-231 SWFOC_SetCreditsFreezeGlobal "
              + "(Hook_AddCredits MinHook detour at 0x27F370 with bool-precedence; +4 LIVE flips iter 231 — "
              + "operator should use the LIVE alternative). This entry stays PHASE 2 PENDING as a Phase-1 "
              + "mirror legacy wire shape; iter-250 audit caught the operator-trust drift (rationale didn't "
              + "cite the LIVE alternative)."),
            ["SWFOC_SetBuildSpeed"] = new("SWFOC_SetBuildSpeed", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA"),
            // 2026-04-28 (iter 100): LIVE via SetSpeedOverride engine call.
            // Walks every tactical object, filters by OwnerPlayerID==slot,
            // applies absolute speed override per unit. Note: the `mult`
            // param is named for backward compat but is interpreted as an
            // ABSOLUTE speed (engine doesn't expose a base-speed read at
            // this layer). Editor presets already map "Slow/Normal/Fast"
            // to absolute values, so the UX matches the engine semantic.
            ["SWFOC_SetPerFactionSpeedMultiplier"] = new("SWFOC_SetPerFactionSpeedMultiplier", CapabilityStatus.Live,
                "Per-faction speed override — calls SetSpeedOverride per unit owned by slot (LIVE)"),
            ["SWFOC_SetDamageMultiplier"] = new("SWFOC_SetDamageMultiplier", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA — per-slot ATTACKER damage multiplier; ~58 caller-site detours needed. "
              + "iter-94 string-anchor analysis: Damage_Multiplier anchors point to per-ability validators "
              + "(LeechShieldsAbilityClass + 4 others), not a global damage path. iter-95 architectural "
              + "finding: Take_Damage @ 0x3A9E30 chokepoint identified, but Src param 5 is a name string "
              + "for debug logging, NOT the attacker GameObjectClass* — sampled callers all pass "
              + "Src=nullptr; attacker identity is implicit in the call stack, cannot be reliably "
              + "extracted via a single Take_Damage detour. iter-96 split decision: global form "
              + "shipped LIVE as iter-96 SWFOC_SetDamageMultiplierGlobal (single Take_Damage_Outer @ "
              + "0x38A350 detour scaling damageParams[0] by g_dmgMult_global). Per-slot form stays "
              + "Phase2HookPending — needs detours at the ~58 caller sites that have attacker context "
              + "(weapon-fire paths). Operator alternatives: iter-96 SWFOC_SetDamageMultiplierGlobal "
              + "for global outgoing damage scaling; iter-154 SWFOC_SetDamageModifierLua for per-unit "
              + "damage RECEIVED scaling via Set_Damage_Modifier engine API. iter-328 audit confirmed "
              + "ZERO C# consumers (orphan bridge wire); see lua_bridge.cpp:6933-7007 for full RE history."),
            // 2026-04-28 (iter 96): global-only sibling. Take_Damage_Outer detour at RVA 0x38A350
            // scales damageParams[0] by g_dmgMult_global. 3-tool consensus pinned via
            // verified_facts.json::rva_take_damage_function (0x3A9E30 inner / 0x38A350 outer).
            ["SWFOC_SetDamageMultiplierGlobal"] = new("SWFOC_SetDamageMultiplierGlobal", CapabilityStatus.Live,
                "Global damage multiplier — Take_Damage_Outer detour scales damageParams[0]"),
            ["SWFOC_GetDamageMultiplierGlobal"] = new("SWFOC_GetDamageMultiplierGlobal", CapabilityStatus.Live,
                "Read-only mirror of g_dmgMult_global"),
            ["SWFOC_SetFireRate"] = new("SWFOC_SetFireRate", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA — global form: superseded by iter-225 SWFOC_SetFireRateMultiplierGlobal "
              + "(WeaponTick MinHook detour). Per-unit form: superseded by iter-154 "
              + "SWFOC_SetRateOfFireModifierLua (engine Lua API). This entry stays PHASE 2 PENDING "
              + "as a Phase-1 mirror — operators should use the LIVE alternatives."),
            ["SWFOC_SetFireRateMultiplierGlobal"] = new("SWFOC_SetFireRateMultiplierGlobal", CapabilityStatus.Live,
                "Iter 225 LIVE — closes A1.3 after 124-day deferral. WeaponTick @ 0x387010 MinHook "
              + "detour scales dt arg passed to sub_140387400 (cooldown dispatcher) by "
              + "g_fireRateMult_global. Pattern parallels iter-96 Take_Damage_Outer detour. "
              + "Sanity clamp [0.0, 100.0] prevents int overflow in dt math. "
              + "Caveats: mult=2.0 → 2x fire rate, mult=0.5 → halved, mult=0.0 → effective freeze "
              + "(use Suspend_AI for proper pause). "
              + "RE design doc: knowledge-base/iter224_setfirerate_global_re_kickoff.md"),
            ["SWFOC_GetFireRateMultiplierGlobal"] = new("SWFOC_GetFireRateMultiplierGlobal", CapabilityStatus.Live,
                "Iter 225 LIVE — read-only mirror of g_fireRateMult_global. Pair-flip with "
              + "iter-225 SWFOC_SetFireRateMultiplierGlobal."),
            // 2026-05-08 (iter 285): Tier 3 HUD counters via DeathHandler detour extension +
            // poll-on-demand object-list walk. Closes iter-284 honest-defer for the overlay's
            // Tier 3 row group (kills / deaths / units-alive). Pattern: iter-96/225 atomics +
            // Hook_DeathHandler (already hooked at lua_bridge.cpp:8568) extension reads
            // killer/victim OwnerPlayerID at +0x58 and compares against FindLocalPlayerSlot().
            // Units-alive walks Selection::kObjectListHead chain (≤ 2048 cap, ≤ 1 ms).
            // Design doc: knowledge-base/iter285_bridge_wires_design_2026-05-08.md
            ["SWFOC_GetPlayerKills"] = new("SWFOC_GetPlayerKills", CapabilityStatus.Live,
                "Iter 285 LIVE — atomic counter incremented inside Hook_DeathHandler when "
              + "killer.OwnerPlayerID == local-player slot. Reset on bridge DLL load. Returns "
              + "session-cumulative kill count for the local player."),
            ["SWFOC_GetPlayerDeaths"] = new("SWFOC_GetPlayerDeaths", CapabilityStatus.Live,
                "Iter 285 LIVE — atomic counter incremented inside Hook_DeathHandler when "
              + "victim.OwnerPlayerID == local-player slot. Reset on bridge DLL load. Returns "
              + "session-cumulative death count of local-player units."),
            ["SWFOC_GetTotalUnitsAlive"] = new("SWFOC_GetTotalUnitsAlive", CapabilityStatus.Live,
                "Iter 285 LIVE — poll-on-demand walk of Selection::kObjectListHead chain. O(n) "
              + "where n ≤ 2048 (engine cap). No detour, no spawn-event RVA pin needed. "
              + "Returns currently-alive object count across all factions in the active scene."),
            // 2026-05-06 (iter 230-231): A1.x FreezeCredits multi-iter arc.
            // AddCredits @ 0x27F370 MinHook detour — universal engine credit-adjust
            // function (47 callers, gains AND spends). Pattern parallels iter-96
            // Take_Damage_Outer + iter-225 WeaponTick. +4 LIVE flips.
            ["SWFOC_SetCreditsFreezeGlobal"] = new("SWFOC_SetCreditsFreezeGlobal", CapabilityStatus.Live,
                "Iter 231 LIVE — bool freeze on AddCredits @ 0x27F370. Short-circuits "
              + "the function entirely (no write to PlayerClass+0x70, no event notification, "
              + "no tracking callback). Returns unchanged balance to preserve prototype "
              + "contract. Wins-over-mult precedence (freeze=true ignores mult). Caveats "
              + "(per iter-230 RE design doc knowledge-base/iter230_freeze_credits_re_kickoff.md): "
              + "blocks AI subsidies equally (all factions affected); cap at PlayerClass+0x74 "
              + "still applies on unfreeze; analytics events suppressed during freeze. "
              + "Pattern parallels iter-96 Take_Damage_Outer + iter-225 WeaponTick exactly."),
            ["SWFOC_GetCreditsFreezeGlobal"] = new("SWFOC_GetCreditsFreezeGlobal", CapabilityStatus.Live,
                "Iter 231 LIVE — read-only mirror of g_creditsFreeze_global. Pair-flip with "
              + "iter-231 SWFOC_SetCreditsFreezeGlobal."),
            ["SWFOC_SetCreditsMultiplierGlobal"] = new("SWFOC_SetCreditsMultiplierGlobal", CapabilityStatus.Live,
                "Iter 231 LIVE — scalar multiplier on AddCredits @ 0x27F370. Scales the "
              + "delta arg before forwarding (mult=1.0 fast-path). Sanity clamp [0.0, 100.0]. "
              + "mult=2.0 -> 2x income/spending, mult=0.5 -> halved both, mult=0.0 -> soft "
              + "freeze (still calls real_AddCredits with 0 delta — distinguishable from "
              + "hard freeze if operator listens for AddCredits events). Pattern parallels "
              + "iter-96 + iter-225. iter-230 RE design doc: knowledge-base/iter230_freeze_credits_re_kickoff.md."),
            ["SWFOC_GetCreditsMultiplierGlobal"] = new("SWFOC_GetCreditsMultiplierGlobal", CapabilityStatus.Live,
                "Iter 231 LIVE — read-only mirror of g_creditsMult_global. Pair-flip with "
              + "iter-231 SWFOC_SetCreditsMultiplierGlobal."),
            ["SWFOC_SetAreaDamage"] = new("SWFOC_SetAreaDamage", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA. "
              + "Iter 436: Event-driven subsystem (BarrageAreaBehaviorClass + AsteroidFieldDamageBehaviorClass "
              + "ticked per-frame to apply area damage; per iter-426 feedback_event_driven_defer_pattern.md "
              + "rule). Multi-iter A1.x offset RE required to hook tick-time damage application. "
              + "Alternative for global damage scaling: iter-96 SWFOC_SetDamageMultiplierGlobal LIVE."),
            ["SWFOC_SetTargetFilter"] = new("SWFOC_SetTargetFilter", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA. "
              + "Iter 436: Event-driven subsystem (UnitAIBehaviorClass target-selection runs per-tick during "
              + "combat behavior evaluation; per iter-426 feedback_event_driven_defer_pattern.md rule). "
              + "Multi-iter A1.x offset RE required."),
            ["SWFOC_ToggleOHKAttackPower"] = new("SWFOC_ToggleOHKAttackPower", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA. "
              + "Iter 436: Event-driven subsystem (CombatantBehaviorClass + DamageTrackingBehaviorClass "
              + "evaluate damage application per-tick; per iter-426 feedback_event_driven_defer_pattern.md "
              + "rule). Multi-iter A1.x offset RE required to hook damage-multiplier override at the combat "
              + "tick. Alternative for global one-hit-kill: iter-96 SetDamageMultiplierGlobal at large value."),
            ["SWFOC_FreezeAI"] = new("SWFOC_FreezeAI", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA — AI scheduler. "
              + "Iter 433: Event-driven subsystem (UnitAIBehaviorClass attached to GameObjects "
              + "via QueryInterface; ticked per-frame by engine; per iter-426 "
              + "feedback_event_driven_defer_pattern.md rule). Multi-iter A1.x offset RE "
              + "required (alternative: iter-162 SWFOC_SuspendAiLua LIVE wire suspends "
              + "PER-UNIT AI via Suspend_AI Lua API — operator should use that LIVE alternative)."),
            // 2026-04-28 (iter 106-107): camera engine APIs are exposed via
            // Lua, not C++ setters. SWFOC_FreeCam still Phase 2 — there's no
            // engine `Free_Cam(enable)` Lua API; engine implements free-cam
            // as Lua-side scripted behaviour we'd need to mimic. SetCameraPos
            // stays Phase 2 because the engine API takes a Lua object/position
            // userdata, not raw floats — userdata constructor not yet pinned.
            // ScrollCameraToTarget (NEW) is LIVE — calls engine's Lua
            // `Scroll_Camera_To` via DoString with caller-supplied target
            // expression (planet handle, unit handle, or position userdata).
            ["SWFOC_FreeCam"] = new("SWFOC_FreeCam", CapabilityStatus.Phase2HookPending,
                "Phase 2 — engine has no Free_Cam Lua API; would need scripted-behaviour mimic"),
            ["SWFOC_SetCameraPos"] = new("SWFOC_SetCameraPos", CapabilityStatus.Live,
                "Iter 237 LIVE — direct call to CameraClass::SetTransformMatrix @ 0x261BD0. "
              + "Bridge looks up active tactical camera via GameModeRoot+0x90 (mode==2 only), "
              + "reads inline 4x3 matrix at CameraClass+0x10, modifies translation column at "
              + "indices [3]/[7]/[11], calls SetTransformMatrix to write back + propagate. "
              + "Pattern parallels iter-100 SetSpeedOverride (direct C++ call, NOT MinHook detour). "
              + "Caveats: tactical-only (galactic returns ERR); animation pipeline overwrites "
              + "within 1 frame unless paired with iter-145 cinematic camera or iter-208 "
              + "Lock_Controls; no clamp on coords (operator can teleport below terrain). "
              + "RE design doc: knowledge-base/iter236_setcamerapos_per_coord_re_kickoff.md."),
            ["SWFOC_GetCameraPos"] = new("SWFOC_GetCameraPos", CapabilityStatus.Live,
                "Iter 237 LIVE — direct call to CameraClass::GetPosition @ 0x261A40. "
              + "Returns 'X,Y,Z' string from per-frame matrix-pointer at CameraClass+0x40. "
              + "Pair-flip with iter-237 SWFOC_SetCameraPos. Falls back to '0.000,0.000,0.000' "
              + "when no active tactical camera (parseable result; downstream Lua handles)."),
            ["SWFOC_ScrollCameraToTarget"] = new("SWFOC_ScrollCameraToTarget", CapabilityStatus.Live,
                "Iter 107 LIVE — calls engine Lua API Scroll_Camera_To via DoString"),
            // 2026-04-28 (iter 108): Change_Owner is the per-unit Lua method
            // exposed on GameObjectWrapper. Internally calls sub_140574D0E
            // (RVA 0x574D0E, "Phase 2 RE"-pinned per docs/rvas.md). Updates
            // ownership, fires UI events, plays audio, processes corruption,
            // updates AI budgets — the full "swap sides" engine behaviour.
            ["SWFOC_ChangeUnitOwner"] = new("SWFOC_ChangeUnitOwner", CapabilityStatus.Live,
                "Iter 108 LIVE — calls engine Lua method Change_Owner via DoString"),
            // 2026-04-28 (iter 109): Spawn_Unit is the engine Lua API for
            // creating units. Bridge wraps it as
            // `Spawn_Unit(<player>, <type>, <position>)` over DoString.
            // Operator surface for spawning at runtime, no MinHook detour.
            ["SWFOC_SpawnUnitLua"] = new("SWFOC_SpawnUnitLua", CapabilityStatus.Live,
                "Iter 109 LIVE — calls engine Lua API Spawn_Unit via DoString"),
            // 2026-04-28 (iter 110): Make_Invulnerable is a per-unit Lua
            // method on GameObjectWrapper. The engine wrapper at RVA
            // 0x57D550 propagates to all hardpoints via BehaviorAttach
            // (verified ledger fact `fact_make_invulnerable_hardpoint_propagation`).
            // LIVE per-unit invuln toggle alongside the slot-wide GodMode.
            ["SWFOC_MakeUnitInvulnLua"] = new("SWFOC_MakeUnitInvulnLua", CapabilityStatus.Live,
                "Iter 110 LIVE — calls engine Lua method Make_Invulnerable via DoString"),
            // 2026-04-28 (iter 111): batch of three per-unit Lua-method
            // wires sharing the Lua_DispatchUnitBoolMethod helper.
            ["SWFOC_HideUnitLua"] = new("SWFOC_HideUnitLua", CapabilityStatus.Live,
                "Iter 111 LIVE — calls engine Lua method Hide via DoString"),
            ["SWFOC_PreventAiUsageLua"] = new("SWFOC_PreventAiUsageLua", CapabilityStatus.Live,
                "Iter 111 LIVE — calls engine Lua method Prevent_AI_Usage via DoString"),
            ["SWFOC_SetUnitSelectableLua"] = new("SWFOC_SetUnitSelectableLua", CapabilityStatus.Live,
                "Iter 111 LIVE — calls engine Lua method Set_Selectable via DoString"),
            // 2026-04-28 (iter 112): zero-arg unit-method batch.
            ["SWFOC_DespawnUnitLua"] = new("SWFOC_DespawnUnitLua", CapabilityStatus.Live,
                "Iter 112 LIVE — calls engine Lua method Despawn via DoString"),
            ["SWFOC_StopUnitLua"] = new("SWFOC_StopUnitLua", CapabilityStatus.Live,
                "Iter 112 LIVE — calls engine Lua method Stop via DoString"),
            ["SWFOC_RetreatUnitLua"] = new("SWFOC_RetreatUnitLua", CapabilityStatus.Live,
                "Iter 112 LIVE — calls engine Lua method Retreat via DoString"),
            // 2026-04-28 (iter 113): UNIVERSAL Lua-method dispatcher.
            // Operator escape hatch — calls ANY method on a Lua object
            // handle with caller-supplied args. Bridge does no type
            // validation; engine's Lua VM reports syntax/runtime errors
            // back through the normal ERR: channel.
            ["SWFOC_CallObjMethodLua"] = new("SWFOC_CallObjMethodLua", CapabilityStatus.Live,
                "Iter 113 LIVE — universal method dispatcher via DoString"),
            ["SWFOC_SpawnUnit"] = new("SWFOC_SpawnUnit", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA — superseded by iter-109 SWFOC_SpawnUnitLua "
              + "(engine Spawn_Unit Lua API via DoString; 3-arg form (player, type, position)). "
              + "This entry stays PHASE 2 PENDING as a Phase-1 mirror legacy wire shape; "
              + "iter-266 audit caught the operator-trust drift (rationale didn't cite the LIVE alternative). "
              + "Operator should use the iter-109 SWFOC_SpawnUnitLua LIVE wire."),
            ["SWFOC_SetBuildCost"] = new("SWFOC_SetBuildCost", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA"),
            ["SWFOC_SetUnitCapOverride"] = new("SWFOC_SetUnitCapOverride", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA — iter-248 RE design hypothesized a MinHook detour at the Apocalypticx "
              + "CE community ledger entry `rva_apocalypticx_unit_cap_gc @ 0x28DF6F`, but iter-249 RE walk "
              + "discovered the AOB had drifted to a string-deallocation cleanup block (NOT a unit-cap "
              + "calculation). Ledger entry DEPRECATED; arc closed as 2-iter honest-defer cycle. The "
              + "iter-249 finding seeded the iter-256 `feedback_aob_drift_across_binary_versions` memory "
              + "rule (community CE table AOBs lose accuracy across binary versions; semantic verification "
              + "required). Future RE arc needs live-game CheatEngine tracing or IDA MCP xref walk; would "
              + "be 7th multi-iter A1.x arc."),
            ["SWFOC_SetUnitField"] = new("SWFOC_SetUnitField", CapabilityStatus.Live,
                "Generic per-field unit-edit dispatcher (7/13 sub-fields LIVE iter 136+243+258): "
              + "hull → direct write to GameObj::HP (LIVE iter 136); "
              + "shield → SetFrontShield @ 0x3A8630 + SetRearShield @ 0x3A91E0 (LIVE iter 136); "
              + "speed → SetSpeedOverride @ 0x3A8C90 (LIVE iter 136); "
              + "invuln_flag → direct byte write to GameObj+0x3A7 (LIVE iter 243 — display flag "
              + "only; pair with SWFOC_MakeInvulnerableLua iter-110 for engine-effective gameplay "
              + "invulnerability via BehaviorMarker + per-hardpoint INVULNERABLE attachments); "
              + "prevent_death → direct bit-write of bit 0x80 of GameObj+0x3A1 (LIVE iter 243 — "
              + "operator may prefer SWFOC_SetCannotBeKilledLua iter-153 for the engine-state-"
              + "aware path via Set_Cannot_Be_Killed Lua API); "
              + "max_hull → walks GameObj+0x298 → UnitType*, writes float at UnitType+0xDCC "
              + "(LIVE iter 258 — TYPE-LEVEL: affects EVERY unit of this type for the session, "
              + "operator should be aware that buff/nerf is global-by-type, not per-instance); "
              + "max_shield → same +0x298 walk, dual-write to UnitType+0xDD0 (front) and "
              + "UnitType+0xDD4 (rear) mirroring iter-129's per-instance dual-write (LIVE iter "
              + "258 — same TYPE-LEVEL caveat as max_hull); "
              + "max_speed → Phase-1 mirror with HONEST DEFER (iter 267-268 RE walk per iter-256 "
              + "memory rule confirmed NO TYPE-LEVEL max_speed offset in ledger; Override_Max_Speed "
              + "@ 0x57E590 walks unit+0x60 locomotor not unit+0x298 UnitType. Operator should use "
              + "iter-99 SWFOC_SetUnitSpeed for per-instance speed override OR iter-100 "
              + "SWFOC_SetPerFactionSpeedMultiplier for per-faction; both call SetSpeedOverride @ "
              + "0x3A8C90 directly, providing LIVE coverage that max_speed sub-field cannot match "
              + "without sacrificing iter-258 TYPE-LEVEL semantic consistency); "
              + "attack_power → Phase-1 mirror with HONEST DEFER (iter 269-270 RE walk per iter-256 "
              + "memory rule confirmed iter-94 rejection EMPIRICALLY REAFFIRMED — combat path has "
              + "NO central per-unit attack_power read site; HardpointFire @ 0x387F50 inspection "
              + "shows param_1+0x28 is the hardpoint HP CONSUMER and param_4 damage is PASSED IN, "
              + "computed dynamically from per-weapon XML attributes at fire time. Operator has 3 "
              + "LIVE alternatives covering distinct damage scopes (alternative-set pattern): "
              + "iter-96 SWFOC_SetDamageMultiplierGlobal for global outgoing damage scaling via "
              + "Take_Damage_Outer @ 0x38A350 MinHook detour; iter-154 SWFOC_SetDamageModifierLua "
              + "for per-unit damage scaling via Set_Damage_Modifier engine API; iter-225 "
              + "SWFOC_SetFireRateMultiplierGlobal for global fire-rate scaling via WeaponTick @ "
              + "0x387010 MinHook detour. Together these triple-cover damage tuning; adding a 4th "
              + "attack_power LIVE branch would not add operator capability and would sacrifice "
              + "iter-258 TYPE-LEVEL semantic consistency); "
              + "respawn_ms / is_hero / respawn_enabled → Phase-1 mirror only "
              + "(g_pendingUnitFieldWrites) pending per-field RE walk; "
              + "owner_slot → Phase-1 mirror with explicit defer (writing GameObj+0x58 directly "
              + "bypasses Change_Owner @ 0x574D0E + selection-list update + AI brain reassignment "
              + "+ UI roster refresh; operator MUST use SWFOC_ChangeUnitOwnerLua iter-108 for "
              + "engine-aware ownership change). Iter 136 mirrored HeroStatEdit's iter 100/129 "
              + "LIVE branches into the dispatcher; iter 243 extended to invuln_flag + "
              + "prevent_death; iter 258 extended to max_hull + max_shield via the GameObj+0x298 "
              + "→ UnitType chain (semantic verification per iter-256 memory rule via two "
              + "engine-reader callers of GetMaxHealth @ 0x3727A0)."),
            ["SWFOC_InstantBuild"] = new("SWFOC_InstantBuild", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA"),
            ["SWFOC_FreeBuild"] = new("SWFOC_FreeBuild", CapabilityStatus.Phase2HookPending,
                "BLOCKED-NO-RVA"),

            // Live but read-only (no mutation)
            ["SWFOC_GetLocalPlayer"] = new("SWFOC_GetLocalPlayer", CapabilityStatus.Live,
                "Reads PlayerArray + +0x62"),
            ["SWFOC_GetAllPlayers"] = new("SWFOC_GetAllPlayers", CapabilityStatus.Live,
                "Roster CSV"),
            ["SWFOC_ListTacticalUnits"] = new("SWFOC_ListTacticalUnits", CapabilityStatus.Live,
                "Tactical unit walker"),
            ["SWFOC_DiagGameTick"] = new("SWFOC_DiagGameTick", CapabilityStatus.Live,
                "Game-mode + tick state"),
            ["SWFOC_DumpState"] = new("SWFOC_DumpState", CapabilityStatus.Live,
                "Snapshot v2 emitter"),
            ["SWFOC_GetVersion"] = new("SWFOC_GetVersion", CapabilityStatus.Live,
                "Bridge version string"),
            ["SWFOC_DoString"] = new("SWFOC_DoString", CapabilityStatus.Live,
                "Free-form Lua escape hatch — bypasses typed validation"),
            ["SWFOC_EventStreamDrain"] = new("SWFOC_EventStreamDrain", CapabilityStatus.Live,
                "SetHP detour ring buffer drain"),
            // 2026-04-29 (iter 131): LIVE via FrontShield_Read engine call.
            // Same iter-128 catalog-drift pattern as iter 129's writer
            // flip: bridge `Lua_GetUnitShield` was reading from a stale
            // cache map even though `rva_front_shield_read` @ 0x3963C0
            // (`double __fastcall(__int64)`) was already pinned in the
            // verified ledger. Engine value wins over cache; cache
            // survives only as a Resolve<>()-fallback for replay/dev
            // builds without the loaded module.
            ["SWFOC_GetUnitShield"] = new("SWFOC_GetUnitShield", CapabilityStatus.Live,
                "Per-unit shield read — calls FrontShield_Read @ 0x3963C0 (LIVE). " +
                "Returns engine current value; mirrors into cache for read-after-write consistency."),
            // 2026-04-28 (iter 100): GetUnitSpeed reads locomotor +0x2A0
            // when override-active flag (+0x29C) is set; falls back to the
            // per-process cache otherwise. LIVE because the source-of-truth
            // path is the engine's locomotor field.
            ["SWFOC_GetUnitSpeed"] = new("SWFOC_GetUnitSpeed", CapabilityStatus.Live,
                "Read locomotor override at +0x2A0 (LIVE) with cache fallback"),

            // Galactic / planet helpers
            // 2026-05-07 (iter 296): UPGRADED from Phase2HookPending → Live. Bridge
            // implementation now uses DoString to invoke engine's
            // Find_All_Objects_Of_Type("Planet") (with fallback to "GalacticPlanet"
            // / "Planetary"), iterates the returned table, extracts each planet's
            // Get_Type() name + Get_Owner():Get_Faction_Name() faction via pcall,
            // and emits CSV: "count=N|<idx>;<type_name>;<faction>|...". Galactic
            // tab's auto-refresh (iter 295) now produces a live planet roster
            // instead of empty grid. Mod-compat: works for any mod whose planets
            // register under any of the 3 fallback categories.
            ["SWFOC_GetPlanets"] = new("SWFOC_GetPlanets", CapabilityStatus.Live,
                "Iter 296 LIVE — DoString-driven enumeration via Find_All_Objects_Of_Type "
              + "with category fallbacks (Planet/GalacticPlanet/Planetary). Returns CSV: "
              + "count=N|<idx>;<type>;<faction>|... Mod-agnostic via pcall-wrapped Get_Type/Get_Owner."),
            // 2026-05-07 (iter 299): Faction-roster + current-mod enumeration wires.
            // Both ship LIVE on first introduction (no Phase2HookPending intermediate)
            // because they use existing engine-side primitives (DoString-via-engine-Lua-API
            // for GetFactionRoster + filesystem walk for GetCurrentMod) so there's no
            // hook to pend. Mod-compat: filtering by Get_Faction_Name string works for
            // any mod that registers factions; filesystem walk works for any mod folder
            // structure under SWFOC's standard ./Mods/<name>/Modinfo.xml convention.
            // Engine-already-does-this 5th instance (iter-100/107/179/296/299).
            ["SWFOC_GetFactionRoster"] = new("SWFOC_GetFactionRoster", CapabilityStatus.Live,
                "Iter 299 LIVE — DoString-driven roster filter via Find_All_Objects_Of_Type "
              + "across {GroundCompany, Hero, SpaceUnit, Infantry, Vehicle} categories, "
              + "filtered by Get_Owner():Get_Faction_Name() == requested faction. Returns "
              + "newline-CSV: <unit_type>;<category>\\n... or '(empty)'. Per-unit pcall "
              + "guards tolerate one bad entry without breaking enumeration."),
            ["SWFOC_GetCurrentMod"] = new("SWFOC_GetCurrentMod", CapabilityStatus.Live,
                "Iter 299 LIVE — filesystem probe of ./Mods/*/Modinfo.xml; returns the "
              + "most-recently-accessed mod folder (game touches it on launch). Format: "
              + "'<mod_name>;<version>\\n<absolute_path>' or 'vanilla' or 'ERR: ...'. "
              + "Version field is 'unknown' until iter-300+ adds Modinfo.xml parsing."),
            // 2026-05-07 (iter 300; 300th-iter milestone): mod enumeration —
            // mirrors GetCurrentMod's filesystem walk but emits ALL candidates.
            // Settings tab consumes for operator-facing mod-picker UI. Closes
            // 4th of 6 missing enumeration wires from iter-294 Audit B.
            ["SWFOC_ListMods"] = new("SWFOC_ListMods", CapabilityStatus.Live,
                "Iter 300 LIVE — filesystem enumeration of ./Mods/*/Modinfo.xml; emits "
              + "newline-CSV '<mod_name>;<absolute_path>' for every candidate folder. "
              + "Sentinel '(no_mods)' when no Mods/ folder or no Modinfo.xml found. "
              + "Sibling to iter-299 GetCurrentMod (which picks the active one)."),
            ["SWFOC_ChangePlanetOwner"] = new("SWFOC_ChangePlanetOwner", CapabilityStatus.Phase2HookPending,
                "Phase 1 mirror — pending galactic state API"),
            // 2026-04-29 (iter 133): LIVE via MakeAllyEnemy engine writer
            // at 0x288800. Iter 132 audit caught the catalog drift: ledger
            // already had `rva_make_ally_make_enemy_engine` pinned with
            // `__int64 __fastcall(PlayerClass*, int target_slot, int state)`
            // shape; bridge `Lua_SetDiplomacy` was Phase-1 mirror. Iter 133
            // shipped: bridge walks PlayerArray for slot_a → PlayerClass*,
            // calls engine writer with (player_a, slot_b, state_code) where
            // 0=ally, 1=enemy, 2=neutral. One-way per the engine's own Lua
            // wrapper at 0x6046A0 — operator must call twice for
            // symmetric relations.
            ["SWFOC_SetDiplomacy"] = new("SWFOC_SetDiplomacy", CapabilityStatus.Live,
                "Per-pair diplomacy write — calls MakeAllyEnemy @ 0x288800 (LIVE). " +
                "One-way A->B; call twice with swapped slots for symmetric. " +
                "States: 'ally'=0, 'enemy'=1, 'neutral'=2 (last assumed)."),
            ["SWFOC_GetPlanetTechAndBuildings"] = new("SWFOC_GetPlanetTechAndBuildings",
                CapabilityStatus.Phase2HookPending,
                "DEPRECATED ORPHAN — bridge wire registered at lua_bridge.cpp:6046 but ZERO C# "
              + "consumers as of iter-326 audit. Operator value SUBSUMED by iter-296 SWFOC_GetPlanets "
              + "which returns galactic-mode planets in `name;faction;tech` row format with per-planet "
              + "tech embedded. Buildings enumeration genuinely deferred — no engine-side parent-planet "
              + "filter for Find_All_Objects_Of_Type(\"Building\"); would require O(N×M) walk of all "
              + "buildings + Get_Parent_Object filtering. Stays Phase2HookPending solely because the "
              + "bridge wire still exists; consider removing the orphan in iter-330+ alongside any "
              + "other orphans surfaced by the iter-272 reverse-orphan audit pattern. Operator should "
              + "use iter-296 SWFOC_GetPlanets for per-planet tech."),

            // Direct memory-writer helpers (live engine effect via per-slot maps)
            ["SWFOC_SetCredits"] = new("SWFOC_SetCredits", CapabilityStatus.Live,
                "Per-slot credits writer (local player)"),
            ["SWFOC_SetCreditsForSlot"] = new("SWFOC_SetCreditsForSlot", CapabilityStatus.Live,
                "Per-slot credits writer (any slot)"),
            ["SWFOC_GetCredits"] = new("SWFOC_GetCredits", CapabilityStatus.Live,
                "Read-only credits probe"),
            ["SWFOC_GetCreditsForSlot"] = new("SWFOC_GetCreditsForSlot", CapabilityStatus.Live,
                "Read-only per-slot credits probe"),
            ["SWFOC_SetTechLevel"] = new("SWFOC_SetTechLevel", CapabilityStatus.Live,
                "Per-slot tech level writer (local player)"),
            ["SWFOC_SetTechForSlot"] = new("SWFOC_SetTechForSlot", CapabilityStatus.Live,
                "Per-slot tech level writer (any slot)"),
            ["SWFOC_GetTechForSlot"] = new("SWFOC_GetTechForSlot", CapabilityStatus.Live,
                "Read-only per-slot tech level probe"),
            ["SWFOC_DrainEnemyCredits"] = new("SWFOC_DrainEnemyCredits", CapabilityStatus.Live,
                "Sweep over non-local players setting credits=0"),
            ["SWFOC_UncapCredits"] = new("SWFOC_UncapCredits", CapabilityStatus.Live,
                "Sets max-credits cap to int max"),
            ["SWFOC_GetMaxCredits"] = new("SWFOC_GetMaxCredits", CapabilityStatus.Live,
                "Read-only max credits probe"),
            // 2026-04-29 (iter 129): LIVE via SetFrontShield + SetRearShield
            // engine helpers. Iter 105 wrongly deferred this as
            // "XML-attribute-only"; iter 128 callgraph re-audit found
            // rva_set_front_shield @ 0x3A8630 + rva_set_rear_shield @
            // 0x3A91E0, both `void __fastcall(__int64 unit, float val)`,
            // already pinned in the verified ledger. Same engine-helper
            // shape as iter 100's SetSpeedOverride — no DoString, no
            // MinHook, no XML hack. Bridge writes both front and rear
            // to the same value so operators get the expected "shield
            // = 5000" behavior on both rings.
            ["SWFOC_SetUnitShield"] = new("SWFOC_SetUnitShield", CapabilityStatus.Live,
                "Per-unit shield override — calls SetFrontShield @ 0x3A8630 + SetRearShield @ 0x3A91E0 (LIVE)"),
            // 2026-04-28 (iter 100): LIVE via SetSpeedOverride engine call.
            // Calls sub_1403A8C90 directly with __fastcall (obj_addr, speed).
            // Engine writes locomotor +0x2A0 (override speed) and sets the
            // active flag at +0x29C. Verified by 2-tool consensus
            // (rva_set_speed_override).
            ["SWFOC_SetUnitSpeed"] = new("SWFOC_SetUnitSpeed", CapabilityStatus.Live,
                "Per-unit speed override — calls SetSpeedOverride @ RVA 0x3A8C90 (LIVE)"),
            // 2026-04-28 (iter 100): companion to SetUnitSpeed. Calls
            // ClearSpeedOverride @ RVA 0x38F8B0 (sub_14038F8B0) which
            // clears the override-active flag at locomotor +0x29C.
            ["SWFOC_ClearUnitSpeedOverride"] = new("SWFOC_ClearUnitSpeedOverride", CapabilityStatus.Live,
                "Revert per-unit speed to engine default — calls ClearSpeedOverride (LIVE)"),
            ["SWFOC_CombinedGodOHK"] = new("SWFOC_CombinedGodOHK", CapabilityStatus.Live,
                "Atomic god + OHK toggle"),
            ["SWFOC_EnumerateUnits"] = new("SWFOC_EnumerateUnits", CapabilityStatus.Live,
                "Faction-filtered tactical unit enumerate"),
            ["SWFOC_ListFactions"] = new("SWFOC_ListFactions", CapabilityStatus.Live,
                "Roster CSV with faction names"),
            ["SWFOC_GetSelectedUnit"] = new("SWFOC_GetSelectedUnit", CapabilityStatus.Live,
                "Engine-selection probe (single)"),
            ["SWFOC_GetSelectedUnits"] = new("SWFOC_GetSelectedUnits", CapabilityStatus.Live,
                "Engine-selection probe (multi)"),

            // Hero helpers
            ["SWFOC_ListHeroes"] = new("SWFOC_ListHeroes", CapabilityStatus.Phase2HookPending,
                "Phase 1 mirror — needs hero detection table IDA-pin. iter-323 audit REVIEW-flagged as "
              + "drift candidate via iter-179 Find_All_Objects_Of_Type(\"Hero\") composition; iter-325 "
              + "investigation confirmed DEFER — 3 gaps prevent LIVE flip via composition: "
              + "(1) C# parser at BridgeHeroLabDispatcher.ListHeroesAsync requires 6-field row format "
              + "`addr;typeName;owner;alive;respawnMs;respawnEnabled` — heroes are referenced by engine "
              + "pointer for subsequent SWFOC_KillUnit/SWFOC_ReviveUnit/SWFOC_SetHeroRespawnTimer calls; "
              + "(2) Lua handle → engine addr extraction is not exposed by the engine bindings — "
              + "tostring(handle) format is undocumented and non-canonical, would be brittle; "
              + "(3) iter-130 confirmed defer on per-hero respawn-timer table RVA (table location not "
              + "callgraph-discoverable; same gap that keeps SWFOC_SetHeroRespawnTimer Phase2HookPending). "
              + "Stays Phase2HookPending until either: (a) hero detection table RVA pinned via "
              + "callgraph, or (b) Lua handle → engine addr extraction surface added to engine bindings."),
            ["SWFOC_SetHeroRespawnTimer"] = new("SWFOC_SetHeroRespawnTimer",
                CapabilityStatus.Phase2HookPending,
                "Phase 1 mirror — pending hero respawn timer field pin "
              + "(per-hero respawn-timer table RVA not in ledger; iter-104 + iter-130 "
              + "audits both confirmed defer — table location not callgraph-discoverable). "
              + "Operator should use iter-130 SWFOC_SetHeroRespawn for global default-"
              + "respawn-time override (writes float at RVA 0xB169F0; affects timers "
              + "created AFTER the call but doesn't reset already-queued respawns; "
              + "range clamped to [0, 600] seconds). The global form covers ~80% of "
              + "operator use cases (\"all heroes respawn faster/slower\"); the per-hero "
              + "form would only be needed for \"this specific hero respawns differently "
              + "from the others\" workflows that aren't currently surfaced."),
            ["SWFOC_SetPermadeath"] = new("SWFOC_SetPermadeath", CapabilityStatus.Phase2HookPending,
                "Phase 1 mirror — pending hero permadeath flag pin. "
              + "Iter 433: Event-driven subsystem (DeathBehaviorClass attached to GameObjects "
              + "via QueryInterface; emits death events when health hits 0; per iter-426 "
              + "feedback_event_driven_defer_pattern.md rule). Multi-iter A1.x offset RE "
              + "required to hook DeathBehaviorClass tick or intercept death event emit."),
            ["SWFOC_CameraFollow"] = new("SWFOC_CameraFollow", CapabilityStatus.Live,
                "Iter 143 LIVE — calls Camera_To_Follow @ engine LuaUserVar registry slot "
              + "0x140898d70 via DoString. Mirror of iter 107 ScrollCameraToTarget pattern. "
              + "Camera_To_Follow attaches the camera to a target object so it tracks the "
              + "object as it moves; complement to Scroll_Camera_To's one-shot pan."),
            ["SWFOC_RotateCameraTo"] = new("SWFOC_RotateCameraTo", CapabilityStatus.Live,
                "Iter 144 LIVE — calls Rotate_Camera_To @ engine LuaUserVar registry slot "
              + "0x140898db0 via DoString. Third member of the camera primitive trio "
              + "(iter 107 Scroll_Camera_To pans, iter 143 Camera_To_Follow tracks, iter 144 "
              + "Rotate_Camera_To rotates the camera to face the target). Same iter-107 "
              + "skeleton verbatim."),
            ["SWFOC_StartCinematicCamera"] = new("SWFOC_StartCinematicCamera", CapabilityStatus.Live,
                "Iter 145 LIVE — calls Start_Cinematic_Camera() @ engine LuaUserVar slot "
              + "0x140898ec0 via DoString. Enters cinematic camera mode; pair with SetKey + "
              + "TransitionKey for keyframe playback then End_Cinematic_Camera to exit. "
              + "Zero-arg call."),
            ["SWFOC_EndCinematicCamera"] = new("SWFOC_EndCinematicCamera", CapabilityStatus.Live,
                "Iter 145 LIVE — calls End_Cinematic_Camera() @ engine LuaUserVar slot "
              + "0x140898ed8 via DoString. Exits cinematic camera mode. Zero-arg call."),
            ["SWFOC_SetCinematicCameraKey"] = new("SWFOC_SetCinematicCameraKey", CapabilityStatus.Live,
                "Iter 145 LIVE — calls Set_Cinematic_Camera_Key(args) @ engine LuaUserVar slot "
              + "0x140898f30 via DoString. Sets a cinematic-camera keyframe; operator supplies "
              + "the args expression (engine accepts varying argument shapes — most common: "
              + "key index + position + look-at + duration)."),
            ["SWFOC_TransitionCinematicCameraKey"] = new("SWFOC_TransitionCinematicCameraKey", CapabilityStatus.Live,
                "Iter 145 LIVE — calls Transition_Cinematic_Camera_Key(args) @ engine "
              + "LuaUserVar slot 0x140898f50 via DoString. Triggers transition between "
              + "cinematic-camera keyframes. Operator supplies the args expression."),
            ["SWFOC_LetterBoxOn"] = new("SWFOC_LetterBoxOn", CapabilityStatus.Live,
                "Iter 150 LIVE — calls Letter_Box_On() engine global via DoString. "
              + "Cinematic-mode complement to iter 145 cinematic camera quad — adds "
              + "the black bars above/below the gameplay area for filming work. "
              + "Per docs/lua-api.md the canonical cinematic recipe is "
              + "Point_Camera_At(unit) + Letter_Box_On()."),
            ["SWFOC_LetterBoxOff"] = new("SWFOC_LetterBoxOff", CapabilityStatus.Live,
                "Iter 150 LIVE — calls Letter_Box_Off() engine global via DoString. "
              + "Removes the cinematic black bars. Pair with SWFOC_LetterBoxOn for "
              + "filming workflows."),
            ["SWFOC_TeleportUnitLua"] = new("SWFOC_TeleportUnitLua", CapabilityStatus.Live,
                "Iter 151 LIVE — composes (unit):Teleport(position) and dispatches via "
              + "DoString. Engine method per docs/lua-api.md GameObjectWrapper Movement "
              + "section. Two args: unit_lua_expr (Find_First_Object/Find_All_Objects/etc) "
              + "+ position_lua_expr (Create_Position(x, y, z) or Get_Position(other_obj)). "
              + "Single most-requested operator helper not previously wired natively."),
            ["SWFOC_GalacticSpawnUnit"] = new("SWFOC_GalacticSpawnUnit", CapabilityStatus.Live,
                "Iter 152 LIVE — composes Galactic_Spawn_Unit(player, type, planet) and "
              + "dispatches via DoString. Galactic-mode complement to iter 109 "
              + "SWFOC_SpawnUnitLua (tactical Spawn_Unit). 3-arg shape mirrors iter 109; "
              + "the third arg is a PlanetWrapper (FindPlanet) instead of a position "
              + "userdata. Per docs/lua-api.md global functions section."),
            ["SWFOC_SetCannotBeKilledLua"] = new("SWFOC_SetCannotBeKilledLua", CapabilityStatus.Live,
                "Iter 153 LIVE — composes (unit):Set_Cannot_Be_Killed(bool) and dispatches "
              + "via DoString. Engine method per docs/lua-api.md: 'Prevents death (HP stays "
              + "at 1)'. Different semantic from iter 110 Make_Invulnerable: damage is still "
              + "applied, but unit can't die. Same iter-111 bool-arg dispatch pattern. "
              + "Iter 213 native UX: UnitControl tab 'Cannot be killed: on/off' bool-pair "
              + "(iter-204 hardcoded-bool pattern), anchors on SelectedUnitLuaExpr."),
            ["SWFOC_EnableStealthLua"] = new("SWFOC_EnableStealthLua", CapabilityStatus.Live,
                "Iter 153 LIVE — composes (unit):Enable_Stealth(bool) and dispatches via "
              + "DoString. Engine method per docs/lua-api.md GameObjectWrapper section. "
              + "Cloaks the unit until disabled or attacked. Same iter-111 bool-arg pattern. "
              + "Iter 213 native UX: UnitControl tab 'Enable stealth: on/off' bool-pair "
              + "(iter-204 hardcoded-bool pattern), anchors on SelectedUnitLuaExpr."),
            ["SWFOC_HealUnitLua"] = new("SWFOC_HealUnitLua", CapabilityStatus.Live,
                "Iter 154 LIVE — composes (unit):Heal() no-arg call via DoString. "
              + "Mirrors iter 112 no-arg dispatcher pattern (Despawn/Stop/Retreat). "
              + "Iter 193 surfaced as native UX in Combat tab 'Per-unit combat actions' GroupBox."),
            ["SWFOC_TakeDamageLua"] = new("SWFOC_TakeDamageLua", CapabilityStatus.Live,
                "Iter 154 LIVE — composes (unit):Take_Damage(amount) via DoString. New "
              + "iter-154 Lua_DispatchUnitFloatMethod helper (mirrors iter-111 bool-arg "
              + "pattern). Operator passes raw damage amount; engine resolves through the "
              + "iter 96 Take_Damage_Outer chokepoint (so iter 96 GLOBAL multiplier applies). "
              + "Iter 193 surfaced as native UX in Combat tab."),
            ["SWFOC_SetDamageModifierLua"] = new("SWFOC_SetDamageModifierLua", CapabilityStatus.Live,
                "Iter 154 LIVE — composes (unit):Set_Damage_Modifier(float) via DoString. "
              + "Per-unit outgoing-damage multiplier (different from iter 96 global). Same "
              + "iter-154 float-arg dispatcher. Iter 193 surfaced as native UX in Combat tab — "
              + "operators get per-unit + per-slot + GLOBAL damage controls in one place."),
            ["SWFOC_SetRateOfFireModifierLua"] = new("SWFOC_SetRateOfFireModifierLua", CapabilityStatus.Live,
                "Iter 154 LIVE — composes (unit):Set_Rate_Of_Fire_Modifier(float) via "
              + "DoString. Per-unit rate-of-fire multiplier. Closes the SetFireRate "
              + "operator gap that iter 101 left open at the global level (no global engine "
              + "setter exists, but per-unit method does). Iter 193 surfaced as native UX in Combat tab."),
            ["SWFOC_PlayerGiveMoneyLua"] = new("SWFOC_PlayerGiveMoneyLua", CapabilityStatus.Live,
                "Iter 155 LIVE — composes (player):Give_Money(amount) via DoString. "
              + "Reuses iter-154 generic 2-arg dispatcher. Player-method counterpart to "
              + "iter 113 universal CallObjMethodLua but with a typed catalog surface."),
            ["SWFOC_PlayerSetTechLevelLua"] = new("SWFOC_PlayerSetTechLevelLua", CapabilityStatus.Live,
                "Iter 155 LIVE — composes (player):Set_Tech_Level(level) via DoString. "
              + "Per-docs/lua-api.md PlayerWrapper section. Operator passes player handle "
              + "+ tech level int."),
            ["SWFOC_PlayerUnlockTechLua"] = new("SWFOC_PlayerUnlockTechLua", CapabilityStatus.Live,
                "Iter 155 LIVE — composes (player):Unlock_Tech(tech_type) via DoString. "
              + "Per docs/lua-api.md PlayerWrapper. Common usage: "
              + "player:Unlock_Tech(Find_Object_Type('DEATH_STAR')) for tech-tree unlocks."),
            ["SWFOC_ActivateAbilityLua"] = new("SWFOC_ActivateAbilityLua", CapabilityStatus.Live,
                "Iter 156 LIVE — composes (unit):Activate_Ability(name) via DoString. "
              + "Activates a named ability on a unit (Tractor_Beam, Sensor_Jamming, etc.). "
              + "Iter 211 native UX: UnitControl tab Selected Unit Lua Actions GroupBox "
              + "'Activate ability' button — uses dedicated AbilityNameLuaExpr field for "
              + "the ability-name string arg, anchors on SelectedUnitLuaExpr."),
            ["SWFOC_DisableCaptureLua"] = new("SWFOC_DisableCaptureLua", CapabilityStatus.Live,
                "Iter 156 LIVE — composes (unit):Disable_Capture(bool) via DoString. "
              + "Per docs/lua-api.md: prevents the unit from being captured when reduced "
              + "to low HP. Bool-arg via iter-111 helper. Iter 211 native UX: UnitControl "
              + "tab 'Disable capture: on/off' button pair (iter-204 hardcoded-bool pattern), "
              + "anchors on SelectedUnitLuaExpr."),
            ["SWFOC_SetGarrisonSpawnLua"] = new("SWFOC_SetGarrisonSpawnLua", CapabilityStatus.Live,
                "Iter 156 LIVE — composes (unit):Set_Garrison_Spawn(bool) via DoString. "
              + "Per docs/lua-api.md: toggles whether the unit produces garrison units. "
              + "Bool-arg via iter-111 helper. Iter 211 native UX: UnitControl tab "
              + "'Garrison spawn: on/off' button pair (iter-204 hardcoded-bool pattern), "
              + "anchors on SelectedUnitLuaExpr."),
            ["SWFOC_CancelHyperspaceLua"] = new("SWFOC_CancelHyperspaceLua", CapabilityStatus.Live,
                "Iter 156 LIVE — composes (unit):Cancel_Hyperspace() no-arg via DoString. "
              + "Per docs/lua-api.md: cancels an in-progress hyperspace jump. No-arg via "
              + "iter-112 helper. Iter 211 native UX: UnitControl tab 'Cancel hyperspace' "
              + "no-arg button — anchors on SelectedUnitLuaExpr."),
            ["SWFOC_SetInLimboLua"] = new("SWFOC_SetInLimboLua", CapabilityStatus.Live,
                "Iter 157 LIVE — composes (unit):Set_In_Limbo(bool) via DoString. "
              + "Per docs/lua-api.md: removes/restores unit from active gameplay. "
              + "Iter 212 native UX: UnitControl tab 'Set in limbo: on/off' bool-pair "
              + "(iter-204 hardcoded-bool pattern), anchors on SelectedUnitLuaExpr."),
            ["SWFOC_SetCheckContestedSpaceLua"] = new("SWFOC_SetCheckContestedSpaceLua", CapabilityStatus.Live,
                "Iter 157 LIVE — composes (unit):Set_Check_Contested_Space(bool) via DoString. "
              + "Iter 212 native UX: UnitControl tab 'Check contested: on/off' bool-pair "
              + "(iter-204 hardcoded-bool pattern), anchors on SelectedUnitLuaExpr."),
            ["SWFOC_SellUnitLua"] = new("SWFOC_SellUnitLua", CapabilityStatus.Live,
                "Iter 157 LIVE — composes (unit):Sell() no-arg via DoString. "
              + "Per docs/lua-api.md: sells the unit for credits. Iter 212 native UX: "
              + "UnitControl tab 'Sell unit' no-arg button — anchors on SelectedUnitLuaExpr."),
            ["SWFOC_BribeLua"] = new("SWFOC_BribeLua", CapabilityStatus.Live,
                "Iter 157 LIVE — composes (unit):Bribe(player) via DoString. "
              + "Per docs/lua-api.md: corrupts a unit to switch sides for credits. "
              + "Iter 212 native UX: UnitControl tab 'Bribe unit' button — reuses "
              + "iter-118 TargetPlayerLuaExpr field for the destination player handle "
              + "(same field as ChangeUnitOwner, semantically interchangeable)."),
            ["SWFOC_MoveToLua"] = new("SWFOC_MoveToLua", CapabilityStatus.Live,
                "Iter 157 LIVE — composes (unit):Move_To(position) via DoString. "
              + "Per docs/lua-api.md: orders unit to move to a position. Iter 212 native UX: "
              + "UnitControl tab 'Move to position' button — reuses iter-194 "
              + "TargetForCombatOrderLuaExpr field (same 'where to go' semantic as "
              + "iter-163 Divert; operator types position once, can move OR divert)."),
            ["SWFOC_FireSpecialWeaponLua"] = new("SWFOC_FireSpecialWeaponLua", CapabilityStatus.Live,
                "Iter 157 LIVE — composes (unit):Fire_Special_Weapon(slot) via DoString. "
              + "Per docs/lua-api.md: fires the unit's special weapon at the given slot. "
              + "Iter 212 native UX: UnitControl tab 'Fire special weapon' button — uses "
              + "dedicated SpecialWeaponSlotLuaExpr field for the slot index arg."),
            ["SWFOC_DisableBombingRunLua"] = new("SWFOC_DisableBombingRunLua", CapabilityStatus.Live,
                "Iter 158 LIVE — composes Disable_Bombing_Run(bool) via DoString. "
              + "NOTE: docs/lua-api.md flags reversed parameter logic — pass false to disable. "
              + "First wire via iter-158 Lua_DispatchGlobalArgMethod helper (no-receiver shape)."),
            ["SWFOC_FlashGuiObjectLua"] = new("SWFOC_FlashGuiObjectLua", CapabilityStatus.Live,
                "Iter 158 LIVE — composes Flash_GUI_Object(name) via DoString. Highlights a "
              + "named GUI element (operator-test feedback). Per docs/lua-api.md global functions."),
            ["SWFOC_HideGuiObjectLua"] = new("SWFOC_HideGuiObjectLua", CapabilityStatus.Live,
                "Iter 158 LIVE — composes Hide_GUI_Object(name) via DoString. Hides a named "
              + "GUI element. Pair with iter-150 Letter_Box_On for filming workflows. "
              + "Iter 207 native UX: Diagnostics tab GUI toggle row 'Hide GUI' button — "
              + "shares the GuiObjectElementName field with the iter-166 Show button "
              + "(operator types name once, clicks either)."),
            // 2026-04-29 (iter 159) — string-arg global LIVE batch via iter-158 helper.
            ["SWFOC_StoryEventLua"] = new("SWFOC_StoryEventLua", CapabilityStatus.Live,
                "Iter 159 LIVE — composes Story_Event(name) via DoString. Fires a named story "
              + "event (mods/scripts can listen for it). Operator passes pre-quoted Lua string "
              + "literal as arg, e.g. SWFOC_StoryEventLua('\"DEATH_STAR_DESTROYED\"'). "
              + "Iter 201 native UX: WorldState tab 'Story & Audio (engine Lua, LIVE)' GroupBox "
              + "— operator types name in shared StoryAudioNameLuaExpr field and clicks 'Story_Event'. "
              + "Distinct from the upper 'Fire story event' button which routes through "
              + "IStoryEventService (catalog/profile-mediated); this button hits the engine "
              + "Lua API directly via SWFOC_StoryEventLua wire."),
            ["SWFOC_AddObjectiveLua"] = new("SWFOC_AddObjectiveLua", CapabilityStatus.Live,
                "Iter 159 LIVE — composes Add_Objective(id) via DoString. Adds an objective "
              + "to the player's UI tracker. Per docs/lua-api.md global functions section. "
              + "Iter 201 native UX: WorldState tab Story & Audio GroupBox — pairs with the "
              + "Story_Event button in the same row."),
            ["SWFOC_PlayMusicLua"] = new("SWFOC_PlayMusicLua", CapabilityStatus.Live,
                "Iter 159 LIVE — composes Play_Music(event) via DoString. Plays a named music "
              + "event. Pair with SWFOC_StopAllMusic (deferred — needs separate no-arg helper). "
              + "Iter 201 native UX: WorldState tab Story & Audio GroupBox button. Cinematic "
              + "workflow: combine with iter-145 cinematic camera primitives (Set_Cinematic_Camera_Key "
              + "in Camera tab) for full sound+camera filming control."),
            ["SWFOC_PlaySfxEventLua"] = new("SWFOC_PlaySfxEventLua", CapabilityStatus.Live,
                "Iter 159 LIVE — composes Play_SFX_Event(event) via DoString. Plays a named "
              + "sound effect. Same dispatcher shape as Play_Music — both are 1-string-arg globals. "
              + "Iter 201 native UX: WorldState tab Story & Audio GroupBox button."),
            // 2026-04-29 (iter 160) — mixed-helper LIVE batch.
            ["SWFOC_LockControlsLua"] = new("SWFOC_LockControlsLua", CapabilityStatus.Live,
                "Iter 160 LIVE — composes Lock_Controls(bool) via DoString. Toggles operator "
              + "input lockout for cinematic sequences. Reuses iter-158 global-arg dispatcher. "
              + "Iter 208 native UX: WorldState tab Story+Audio GroupBox 'Lock on'/'Lock off' "
              + "buttons (hardcoded bool args, iter-204 pattern) — pairs with iter-180 "
              + "SWFOC_UnlockControlsLua to bracket the cinematic-recording workflow "
              + "(lock player input → record cutscene → unlock)."),
            ["SWFOC_DisableOrbitalBombardmentLua"] = new("SWFOC_DisableOrbitalBombardmentLua",
                CapabilityStatus.Live,
                "Iter 160 LIVE — composes (player):Disable_Orbital_Bombardment(bool) via DoString. "
              + "PlayerWrapper method per docs/lua-api.md. Reuses iter-111 obj-bool dispatcher — "
              + "the helper is shape-agnostic (works for any obj receiver, not just units). "
              + "Iter 217 native UX: PlayerState tab final-extension GroupBox row 6 'Disable orbital "
              + "bombardment: on/off' hardcoded-bool pair (iter-204 on/off lineage now 7 iters deep: "
              + "204→208→211→212→213→215→217). Uses PlayerLuaExpr field shared with iter-189/199/209/210."),
            ["SWFOC_StoryEventTriggerLua"] = new("SWFOC_StoryEventTriggerLua", CapabilityStatus.Live,
                "Iter 160 LIVE — composes Story_Event_Trigger(name) via DoString. Alternative to "
              + "Story_Event (iter 159) per docs/lua-api.md. Some mods use the trigger variant. "
              + "Iter 202 native UX: WorldState tab Story & Audio GroupBox row 3 — sibling to "
              + "iter-201 Story_Event button. Operators can compare engine semantics of the two "
              + "story-fire variants when debugging mod listener behavior."),
            // 2026-04-29 (iter 161) — player-method LIVE batch.
            ["SWFOC_LockTechLua"] = new("SWFOC_LockTechLua", CapabilityStatus.Live,
                "Iter 161 LIVE — composes (player):Lock_Tech(type) via DoString. Locks a tech "
              + "branch that would otherwise be unlocked. Complement to iter-155 Unlock_Tech. "
              + "Reuses iter-154 generic 2-arg helper. Iter 209 native UX: PlayerState tab "
              + "diplomacy GroupBox 'Lock tech' button — uses TechTypeLuaExpr field for the "
              + "tech-name arg, shares PlayerLuaExpr with iter-189/199 read-side buttons."),
            ["SWFOC_MakeAllyLua"] = new("SWFOC_MakeAllyLua", CapabilityStatus.Live,
                "Iter 161 LIVE — composes (player):Make_Ally(other_player) via DoString. "
              + "PlayerWrapper diplomacy primitive per docs/lua-api.md. WARNING: docs flag that "
              + "Make_Ally state RESETS on every game-mode change (Galactic↔Tactical) — caller "
              + "must re-apply after each transition. Iter 209 native UX: PlayerState tab "
              + "diplomacy GroupBox 'Make ally' button — reuses iter-199 OtherPlayerLuaExpr "
              + "field so operator can ask 'Is_Ally?' (read) and click 'Make ally' (write) "
              + "interchangeably. Click handler appends the mode-reset warning to the output."),
            ["SWFOC_MakeEnemyLua"] = new("SWFOC_MakeEnemyLua", CapabilityStatus.Live,
                "Iter 161 LIVE — composes (player):Make_Enemy(other_player) via DoString. "
              + "PlayerWrapper diplomacy primitive. WARNING: same reset-on-game-mode-change "
              + "caveat as Make_Ally. Both are pinned in docs/lua-api.md behavioral warnings table. "
              + "Iter 209 native UX: PlayerState tab diplomacy GroupBox 'Make enemy' button — "
              + "symmetric pair with the Make_Ally button (also iter-209), both reuse the "
              + "iter-199 OtherPlayerLuaExpr shared field. Click handler appends the mode-reset "
              + "warning to the output."),
            // 2026-04-29 (iter 162) — 4-wire LIVE batch (binary-confirmed).
            ["SWFOC_OverrideMaxSpeedLua"] = new("SWFOC_OverrideMaxSpeedLua", CapabilityStatus.Live,
                "Iter 162 LIVE — composes (unit):Override_Max_Speed(speed) via DoString. "
              + "Sets a per-unit max-speed override. Pairs with iter-100 SetUnitSpeed (which is "
              + "the engine-helper-based form). Reuses iter-154 generic 2-arg helper. "
              + "Iter 213 native UX: UnitControl tab 'Override max speed' button — uses "
              + "dedicated MaxSpeedOverrideLuaExpr field for the numeric speed arg. "
              + "Complements iter-100 SetPerFactionSpeedMultiplier global at the per-unit scope."),
            ["SWFOC_SuspendAiLua"] = new("SWFOC_SuspendAiLua", CapabilityStatus.Live,
                "Iter 162 LIVE — composes Suspend_AI(seconds) via DoString. Pauses AI "
              + "decision-making for the given duration (cinematic helper). Per docs/lua-api.md "
              + "Camera & Cinematics section. Reuses iter-158 global-arg helper. "
              + "Iter 219 native UX: Combat tab Cinematic Helpers row 'Suspend AI' button — "
              + "uses NEW SuspendAiSecondsLuaExpr field for the numeric seconds arg. Pairs "
              + "with iter-208 Lock_Controls + iter-145 cinematic camera quad for full "
              + "battle-pause cinematic recording workflow. **CLOSES the iter-216 changelog "
              + "'What's NOT yet surfaced' queue** — last unsurfaced wire from the iter-216 "
              + "list now has a native button."),
            ["SWFOC_FadeScreenInLua"] = new("SWFOC_FadeScreenInLua", CapabilityStatus.Live,
                "Iter 162 LIVE — composes Fade_Screen_In(duration) via DoString. Fades the "
              + "screen in over the given duration. Cinematic primitive. Reuses iter-158 "
              + "global-arg helper."),
            ["SWFOC_ZoomCameraLua"] = new("SWFOC_ZoomCameraLua", CapabilityStatus.Live,
                "Iter 162 LIVE — composes Zoom_Camera(level) via DoString. Sets the camera "
              + "zoom level. Complements the iter-107/143-145 camera primitive arc. Reuses "
              + "iter-158 global-arg helper. Iter 192 surfaced as native UX in the Camera & "
              + "Debug tab 'Camera primitive arc — extras' GroupBox."),
            // 2026-04-29 (iter 163) — combat-order LIVE batch.
            ["SWFOC_AttackTargetLua"] = new("SWFOC_AttackTargetLua", CapabilityStatus.Live,
                "Iter 163 LIVE — composes (unit):Attack_Target(target) via DoString. Orders "
              + "the unit to attack a specific target. Per docs/lua-api.md GameObjectWrapper "
              + "Commands section. Reuses iter-154 generic 2-arg helper. Iter 194 surfaced as "
              + "native UX in UnitControl tab combat-order extension (alongside iter-117/118 buttons)."),
            ["SWFOC_GuardTargetLua"] = new("SWFOC_GuardTargetLua", CapabilityStatus.Live,
                "Iter 163 LIVE — composes (unit):Guard_Target(target) via DoString. Orders "
              + "the unit to guard a target (defensive escort). Reuses iter-154 generic 2-arg helper. "
              + "Iter 194 surfaced as native UX in UnitControl tab combat-order extension."),
            ["SWFOC_DivertLua"] = new("SWFOC_DivertLua", CapabilityStatus.Live,
                "Iter 163 LIVE — composes (unit):Divert(position) via DoString. Diverts "
              + "the unit's current path to the given position. Reuses iter-154 generic "
              + "2-arg helper. Iter 194 surfaced as native UX in UnitControl tab — operator "
              + "reuses SelectedUnitLuaExpr from iter-117 + types a position handle."),
            // 2026-04-29 (iter 164) — player-method extension LIVE batch.
            ["SWFOC_EnableAsActorLua"] = new("SWFOC_EnableAsActorLua", CapabilityStatus.Live,
                "Iter 164 LIVE — composes (player):Enable_As_Actor() via DoString. "
              + "PlayerWrapper Other section per docs/lua-api.md — enables AI actor mode for "
              + "the player. No-arg method via iter-112 helper (shape-agnostic for any obj receiver). "
              + "Iter 210 native UX: PlayerState tab PlayerWrapper extension GroupBox 'Enable as actor' "
              + "button — no-arg, just needs PlayerLuaExpr (shared with iter-189/199/209 buttons)."),
            ["SWFOC_ReleaseCreditsForTacticalLua"] = new("SWFOC_ReleaseCreditsForTacticalLua",
                CapabilityStatus.Live,
                "Iter 164 LIVE — composes (player):Release_Credits_For_Tactical(amount) via "
              + "DoString. Releases credits earmarked for tactical battle (galactic→tactical "
              + "transition helper). Reuses iter-154 generic 2-arg helper. Iter 210 native UX: "
              + "PlayerState tab PlayerWrapper extension GroupBox 'Release credits' button — "
              + "uses dedicated ReleaseCreditsAmount field for the numeric amount arg."),
            ["SWFOC_SelectObjectLua"] = new("SWFOC_SelectObjectLua", CapabilityStatus.Live,
                "Iter 164 LIVE — composes (player):Select_Object(unit) via DoString. Programmatically "
              + "selects a unit in the player's UI. Reuses iter-154 generic 2-arg helper. Iter 210 "
              + "native UX: PlayerState tab PlayerWrapper extension GroupBox 'Select object' button "
              + "— uses dedicated SelectObjectLuaExpr field for the object-handle arg."),
            // 2026-04-29 (iter 165) — camera/cinematic complement LIVE batch.
            ["SWFOC_FadeScreenOutLua"] = new("SWFOC_FadeScreenOutLua", CapabilityStatus.Live,
                "Iter 165 LIVE — composes Fade_Screen_Out(duration) via DoString. Cinematic "
              + "fade-out primitive; complement to iter-162 Fade_Screen_In. Reuses iter-158 "
              + "global-arg helper. Iter 192 surfaced as native UX in Camera & Debug tab."),
            ["SWFOC_RotateCameraByLua"] = new("SWFOC_RotateCameraByLua", CapabilityStatus.Live,
                "Iter 165 LIVE — composes Rotate_Camera_By(degrees) via DoString. Rotates the "
              + "camera by relative degrees; complement to iter-144 Rotate_Camera_To (absolute). "
              + "Extends camera primitive arc. Iter 192 surfaced as native UX in Camera & Debug tab."),
            ["SWFOC_PointCameraAtLua"] = new("SWFOC_PointCameraAtLua", CapabilityStatus.Live,
                "Iter 165 LIVE — composes Point_Camera_At(object) via DoString. Points the "
              + "camera at a unit/object. Per docs/lua-api.md Camera & Cinematics section. "
              + "Reuses iter-158 global-arg helper. Iter 192 surfaced as native UX in Camera & "
              + "Debug tab — 8th camera primitive in the arc."),
            // 2026-04-29 (iter 166) — NEW global-no-arg helper + 3 wires.
            // Dispatcher set extends to 5 helpers covering full 2x2 matrix:
            // (receiver: obj/global) x (args: 0/1).
            ["SWFOC_StopAllMusicLua"] = new("SWFOC_StopAllMusicLua", CapabilityStatus.Live,
                "Iter 166 LIVE — composes Stop_All_Music() via DoString. Stops all currently "
              + "playing music tracks. Per docs/lua-api.md Audio section. First wire shipped "
              + "via NEW iter-166 Lua_DispatchGlobalNoArgMethod helper (5th in dispatcher set; "
              + "completes 2x2 matrix of receiver/arg shapes). Iter 202 native UX: WorldState "
              + "tab Story & Audio GroupBox row 3. Pairs with iter-201 Play_Music for "
              + "cinematic soundtrack swap workflow (Stop → Play → Resume_Mode_Based after)."),
            ["SWFOC_ResumeModeBasedMusicLua"] = new("SWFOC_ResumeModeBasedMusicLua",
                CapabilityStatus.Live,
                "Iter 166 LIVE — composes Resume_Mode_Based_Music() via DoString. Resumes music "
              + "based on current game mode. Pair with SWFOC_StopAllMusicLua for cinematic-mode "
              + "audio control. Reuses iter-166 global-no-arg helper. Iter 202 native UX: "
              + "WorldState tab Story & Audio GroupBox row 3 — restores default game-mode music "
              + "after a cinematic swap."),
            ["SWFOC_ShowGuiObjectLua"] = new("SWFOC_ShowGuiObjectLua", CapabilityStatus.Live,
                "Iter 166 LIVE — composes Show_GUI_Object(name) via DoString. Counterpart to "
              + "iter-158 Hide_GUI_Object — reveals a previously hidden GUI element. Reuses "
              + "iter-158 global-arg helper. Iter 207 native UX: Diagnostics tab GUI toggle "
              + "row 'Show GUI' button — shares the GuiObjectElementName field with the "
              + "iter-158 Hide button (symmetric pair, single input)."),
            // 2026-04-29 (iter 167) — NEW unit-getter helper + 3 read-side wires.
            // Dispatcher set extends to 6 helpers — first one to capture engine
            // return values (previous 5 helpers all discard returns).
            ["SWFOC_GetHullLua"] = new("SWFOC_GetHullLua", CapabilityStatus.Live,
                "Iter 167 LIVE — composes (unit):Get_Hull() via DoString and CAPTURES engine "
              + "return value. Returns current HP as a string. First wire shipped via NEW "
              + "iter-167 Lua_DispatchUnitGetterNoArg helper (6th in dispatcher set, first to "
              + "support read-side semantics)."),
            ["SWFOC_GetHealthLua"] = new("SWFOC_GetHealthLua", CapabilityStatus.Live,
                "Iter 167 LIVE — composes (unit):Get_Health() via DoString and captures the "
              + "health-percentage return value. Reuses iter-167 unit-getter helper."),
            ["SWFOC_GetShieldLua"] = new("SWFOC_GetShieldLua", CapabilityStatus.Live,
                "Iter 167 LIVE — composes (unit):Get_Shield() via DoString and captures the "
              + "shield-percentage return value. Operator-friendly read-side complement to "
              + "iter-129 SetUnitShield writer. Reuses iter-167 unit-getter helper."),
            // 2026-04-29 (iter 168) — read-side getter expansion via iter-167 helper.
            ["SWFOC_HasAttackTargetLua"] = new("SWFOC_HasAttackTargetLua", CapabilityStatus.Live,
                "Iter 168 LIVE — composes (unit):Has_Attack_Target() via DoString, captures "
              + "boolean return as 'true'/'false' string. Reuses iter-167 unit-getter helper. "
              + "Operator can verify if a unit is currently engaged in combat. Iter 191 "
              + "surfaced as native UX in the Inspector tab."),
            ["SWFOC_AreEnginesOnlineLua"] = new("SWFOC_AreEnginesOnlineLua", CapabilityStatus.Live,
                "Iter 168 LIVE — composes (unit):Are_Engines_Online() via DoString, captures "
              + "boolean return. Confirms unit's engines are operational (relevant for ships "
              + "in tactical battles). Reuses iter-167 unit-getter helper. Iter 191 surfaced "
              + "as native UX in the Inspector tab."),
            ["SWFOC_GetOwnerLua"] = new("SWFOC_GetOwnerLua", CapabilityStatus.Live,
                "Iter 168 LIVE — composes (unit):Get_Owner() via DoString. Returns a "
              + "PlayerWrapper handle (stringifies as 'table: 0x...'). Confirms call landed; "
              + "operator can chain (Get_Owner()):Get_Faction() once that helper ships. "
              + "Reuses iter-167 unit-getter helper. Iter 191 surfaced as native UX in the "
              + "Inspector tab 'Selected Unit Lua Read-side' GroupBox."),
            // 2026-04-29 (iter 169) — read-side getter expansion #2.
            ["SWFOC_GetTypeLua"] = new("SWFOC_GetTypeLua", CapabilityStatus.Live,
                "Iter 169 LIVE — composes (unit):Get_Type() via DoString. Returns a "
              + "GameObjectType handle. Reuses iter-167 unit-getter helper (shape-agnostic for "
              + "any obj receiver). Iter 191 surfaced as native UX in the Inspector tab — "
              + "operators no longer need to paste preset Lua to identify a unit's type."),
            ["SWFOC_GetCreditsLua"] = new("SWFOC_GetCreditsLua", CapabilityStatus.Live,
                "Iter 169 LIVE — composes (player):Get_Credits() via DoString. Returns current "
              + "credit balance as numeric string. Pairs with iter-155 PlayerGiveMoney for "
              + "read-after-write verification. Reuses iter-167 helper (works for player too)."),
            ["SWFOC_GetFactionLua"] = new("SWFOC_GetFactionLua", CapabilityStatus.Live,
                "Iter 169 LIVE — composes (player):Get_Faction() via DoString. Returns faction "
              + "handle. Operator can use this with iter-168 Get_Owner to identify the faction "
              + "of any unit. Reuses iter-167 helper."),
            ["SWFOC_GetTechLevelLua"] = new("SWFOC_GetTechLevelLua", CapabilityStatus.Live,
                "Iter 169 LIVE — composes (player):Get_Tech_Level() via DoString. Returns "
              + "current tech level (1-5). Pairs with iter-155 PlayerSetTechLevel for read-after-write. "
              + "Reuses iter-167 helper."),
            // 2026-04-29 (iter 170) — read-side state-query batch.
            // Each wire forms read-after-write pair with earlier writer.
            ["SWFOC_GetNameLua"] = new("SWFOC_GetNameLua", CapabilityStatus.Live,
                "Iter 170 LIVE — composes (player):Get_Name() via DoString. Returns the "
              + "player's display name as string. Reuses iter-167 helper. Iter 199 surfaced as "
              + "native UX in PlayerState tab read-side extension — alongside iter-189 read-side "
              + "(credits/tech/faction)."),
            ["SWFOC_IsStealthedLua"] = new("SWFOC_IsStealthedLua", CapabilityStatus.Live,
                "Iter 170 LIVE — composes (unit):Is_Stealthed() via DoString. Boolean read-side "
              + "complement to iter-153 EnableStealth writer. Reuses iter-167 helper."),
            ["SWFOC_IsInLimboLua"] = new("SWFOC_IsInLimboLua", CapabilityStatus.Live,
                "Iter 170 LIVE — composes (unit):Is_In_Limbo() via DoString. Boolean read-side "
              + "complement to iter-157 SetInLimbo writer. Reuses iter-167 helper."),
            ["SWFOC_IsCapturableLua"] = new("SWFOC_IsCapturableLua", CapabilityStatus.Live,
                "Iter 170 LIVE — composes (unit):Is_Capturable() via DoString. Boolean read-side "
              + "complement to iter-156 DisableCapture writer. Reuses iter-167 helper."),
            // 2026-04-30 (iter 171) — read-side query batch via iter-167 helper.
            ["SWFOC_GetPositionLua"] = new("SWFOC_GetPositionLua", CapabilityStatus.Live,
                "Iter 171 LIVE — composes (unit):Get_Position() via DoString. Returns position "
              + "handle (stringifies as 'table: 0x...'). Operator can use this for chained "
              + "spatial queries. Reuses iter-167 helper."),
            ["SWFOC_GetParentObjectLua"] = new("SWFOC_GetParentObjectLua", CapabilityStatus.Live,
                "Iter 171 LIVE — composes (unit):Get_Parent_Object() via DoString. Returns "
              + "parent handle for nested objects (e.g. garrisoned units). Reuses iter-167 helper. "
              + "Iter 197 surfaced as native UX in Inspector tab read-side extension."),
            ["SWFOC_GetAttackTargetLua"] = new("SWFOC_GetAttackTargetLua", CapabilityStatus.Live,
                "Iter 171 LIVE — composes (unit):Get_Attack_Target() via DoString. Returns "
              + "current attack target handle. Pair with iter-168 Has_Attack_Target predicate. "
              + "Reuses iter-167 helper. Iter 197 surfaced as native UX in Inspector tab — "
              + "alongside iter-191 Has attack target? predicate button for full read coverage."),
            ["SWFOC_GetDamageModifierLua"] = new("SWFOC_GetDamageModifierLua", CapabilityStatus.Live,
                "Iter 171 LIVE — composes (unit):Get_Damage_Modifier() via DoString. Returns "
              + "float damage scalar. Read-after-write pair with iter-154 SetDamageModifier. "
              + "Reuses iter-167 helper. Iter 197 surfaced as native UX in Inspector tab — "
              + "operator can verify per-unit damage scalar set on Combat tab (iter 193)."),
            // 2026-04-30 (iter 172) — read-side garrison/behavior batch.
            // **100 LIVE wire milestone** — iter 100-172 ships 99 LIVE wires
            // before this iter's 4-wire batch crosses the threshold.
            ["SWFOC_GetGarrisonUnitsLua"] = new("SWFOC_GetGarrisonUnitsLua", CapabilityStatus.Live,
                "Iter 172 LIVE — composes (unit):Get_Garrison_Units() via DoString. Returns "
              + "table handle for garrison units (e.g. troopers inside a transport). Reuses "
              + "iter-167 helper."),
            ["SWFOC_GetContainedObjectCountLua"] = new("SWFOC_GetContainedObjectCountLua", CapabilityStatus.Live,
                "Iter 172 LIVE — composes (unit):Get_Contained_Object_Count() via DoString. "
              + "Returns numeric count of contained objects. Pair with iter-172 Get_Garrison_Units "
              + "for garrison inspection workflows. Reuses iter-167 helper. Iter 197 surfaced "
              + "as native UX in Inspector tab — count complement to iter-188 Read garrison button on UnitControl tab."),
            ["SWFOC_GetBehaviorIdLua"] = new("SWFOC_GetBehaviorIdLua", CapabilityStatus.Live,
                "Iter 172 LIVE — composes (unit):Get_Behavior_ID() via DoString. Returns the "
              + "current behavior identifier. Reuses iter-167 helper. Iter 197 surfaced as "
              + "native UX in Inspector tab — useful for AI debugging workflows."),
            ["SWFOC_GetRateOfFireModifierLua"] = new("SWFOC_GetRateOfFireModifierLua", CapabilityStatus.Live,
                "Iter 172 LIVE — composes (unit):Get_Rate_Of_Fire_Modifier() via DoString. "
              + "Returns float fire-rate scalar. Read-after-write pair with iter-154 "
              + "SetRateOfFireModifier writer. Reuses iter-167 helper. **Crosses 100 LIVE wire "
              + "milestone in master loop iter 100-172.** Iter 197 surfaced as native UX in "
              + "Inspector tab — operator can verify per-unit fire-rate scalar set on Combat tab (iter 193)."),
            // 2026-05-04 (iter 173) — NEW unit-getter-with-arg helper + 4 wires.
            // Dispatcher set extends to 7 helpers — iter-173 mirrors iter-167's
            // return-value-capture pattern but for `(obj):method(arg)` shape.
            ["SWFOC_IsAbilityActiveLua"] = new("SWFOC_IsAbilityActiveLua", CapabilityStatus.Live,
                "Iter 173 LIVE — composes (unit):Is_Ability_Active(name) via DoString and "
              + "captures boolean return. Read-after-write pair with iter-156 ActivateAbility "
              + "writer — operator can verify ability activation took effect. First wire shipped "
              + "via NEW iter-173 Lua_DispatchUnitGetterArg helper (7th in dispatcher set, "
              + "first arg-getter to capture engine return values). Iter 198 surfaced as native "
              + "UX in Inspector tab arg-getter extension."),
            ["SWFOC_HasPropertyLua"] = new("SWFOC_HasPropertyLua", CapabilityStatus.Live,
                "Iter 173 LIVE — composes (unit):Has_Property(prop) via DoString and captures "
              + "boolean return. Per docs/lua-api.md GameObjectWrapper Queries section. "
              + "Reuses iter-173 unit-getter-with-arg helper. Iter 198 surfaced as native UX "
              + "in Inspector tab — operators can probe arbitrary property predicates."),
            ["SWFOC_IsCategoryLua"] = new("SWFOC_IsCategoryLua", CapabilityStatus.Live,
                "Iter 173 LIVE — composes (unit):Is_Category(cat) via DoString and captures "
              + "boolean return. Operator can categorize units (Hero/Infantry/Vehicle/etc.) "
              + "for filtering workflows. Reuses iter-173 helper. Iter 198 surfaced as native UX "
              + "in Inspector tab."),
            ["SWFOC_GetDistanceLua"] = new("SWFOC_GetDistanceLua", CapabilityStatus.Live,
                "Iter 173 LIVE — composes (unit):Get_Distance(target) via DoString and captures "
              + "float distance. Useful for AI/scripting workflows (range-check before attack). "
              + "Reuses iter-173 helper. Iter 198 surfaced as native UX in Inspector tab — "
              + "second arg field accepts target unit Lua expression."),
            // 2026-05-04 (iter 174) — cross-receiver arg-getter batch via iter-173 helper.
            // Demonstrates iter-173 helper is shape-agnostic across unit/player/TaskForce.
            ["SWFOC_GetBonePositionLua"] = new("SWFOC_GetBonePositionLua", CapabilityStatus.Live,
                "Iter 174 LIVE — composes (unit):Get_Bone_Position(bone) via DoString and "
              + "captures position handle. Operator can read model-bone positions for cinematics. "
              + "Binary-confirmed in docs/lua-api.md GameObjectWrapper Movement & Position section. "
              + "Reuses iter-173 helper. Iter 214 native UX: Inspector tab cross-receiver "
              + "arg-getter row 'Get bone position' button — reuses iter-198 UnitLuaExpr + "
              + "UnitArgExpr (operator types unit handle + bone-name)."),
            ["SWFOC_ContainsObjectTypeLua"] = new("SWFOC_ContainsObjectTypeLua", CapabilityStatus.Live,
                "Iter 174 LIVE — composes (unit):Contains_Object_Type(type) via DoString and "
              + "captures boolean. Useful for garrison-content queries (e.g. is this transport "
              + "carrying any AT-ATs?). Per docs/lua-api.md community-doc additions. "
              + "Reuses iter-173 helper. Iter 214 native UX: Inspector tab 'Contains object "
              + "type?' button — reuses iter-198 UnitLuaExpr + UnitArgExpr (operator types "
              + "unit handle + child-type name)."),
            ["SWFOC_GetSpaceStationLevelLua"] = new("SWFOC_GetSpaceStationLevelLua", CapabilityStatus.Live,
                "Iter 174 LIVE — composes (player):Get_Space_Station_Level(planet) via DoString "
              + "and captures numeric level (0-5). PlayerWrapper galactic query. Useful for AI "
              + "tech-progression checks. Reuses iter-173 helper (proven shape-agnostic for "
              + "player receivers). Iter 214 native UX: Inspector tab 'Get space station level' "
              + "button — receiver is PLAYER (operator types Find_Player(...) into UnitLuaExpr) + "
              + "planet handle into UnitArgExpr. Field naming reflects the iter-198 history but "
              + "the helper accepts any receiver type."),
            ["SWFOC_GetTypeOfUnitLua"] = new("SWFOC_GetTypeOfUnitLua", CapabilityStatus.Live,
                "Iter 174 LIVE — composes (taskforce):Get_Type_Of_Unit(idx) via DoString and "
              + "captures GameObjectType handle. Binary-confirmed TaskForce method per docs. "
              + "Demonstrates iter-173 helper is fully receiver-agnostic — first TaskForce "
              + "wire shipped via that helper. Iter 214 native UX: Inspector tab 'Get type "
              + "of unit at index' button — receiver is TASKFORCE (operator types a TaskForce "
              + "handle into UnitLuaExpr) + index into UnitArgExpr."),
            // 2026-05-04 (iter 175) — TaskForce write-side batch via existing helpers.
            // Names are TaskForce-prefixed to disambiguate from unit-method versions
            // (e.g. iter-157 SWFOC_MoveToLua handles unit Move_To; iter-175
            // SWFOC_TaskForceMoveToLua handles taskforce Move_To). Both bridge dispatch
            // entries are valid because the operator's chosen SWFOC_* call selects the
            // receiver semantics.
            ["SWFOC_TaskForceMoveToLua"] = new("SWFOC_TaskForceMoveToLua", CapabilityStatus.Live,
                "Iter 175 LIVE — composes (taskforce):Move_To(target) via DoString. Orders "
              + "an entire task force to move. Distinct from iter-157 SWFOC_MoveToLua which "
              + "targets unit-receivers. Reuses iter-154 generic 2-arg helper. "
              + "Iter 215 native UX: Galactic tab TaskForce write-side row 'Move to' — "
              + "anchors on TaskForceLuaExpr + TaskForceTargetLuaExpr fields."),
            ["SWFOC_TaskForceReinforceLua"] = new("SWFOC_TaskForceReinforceLua", CapabilityStatus.Live,
                "Iter 175 LIVE — composes (taskforce):Reinforce(type) via DoString. Adds a "
              + "unit type to the task force reinforcement queue. Per docs/lua-api.md TaskForce "
              + "section. Reuses iter-154 helper. Iter 215 native UX: Galactic tab "
              + "'Reinforce' button — operator types unit-type into TaskForceTargetLuaExpr."),
            ["SWFOC_TaskForceReleaseReinforcementsLua"] = new("SWFOC_TaskForceReleaseReinforcementsLua",
                CapabilityStatus.Live,
                "Iter 175 LIVE — composes (taskforce):Release_Reinforcements() via DoString. "
              + "Releases held reinforcements for a SpaceTaskForce. No-arg method — reuses "
              + "iter-112 helper (proven shape-agnostic for TaskForce receivers). "
              + "Iter 215 native UX: Galactic tab 'Release reinforcements' no-arg button."),
            ["SWFOC_TaskForceLaunchUnitsLua"] = new("SWFOC_TaskForceLaunchUnitsLua", CapabilityStatus.Live,
                "Iter 175 LIVE — composes (taskforce):Launch_Units(planet) via DoString. "
              + "Launches a galactic task force toward a target planet. Per docs/lua-api.md "
              + "GalacticTaskForce section. Reuses iter-154 helper. Iter 215 native UX: "
              + "Galactic tab 'Launch units' button — operator types planet handle into "
              + "TaskForceTargetLuaExpr."),
            // 2026-05-04 (iter 176) — TaskForce coverage extension via existing helpers.
            ["SWFOC_TaskForceAttackTargetLua"] = new("SWFOC_TaskForceAttackTargetLua", CapabilityStatus.Live,
                "Iter 176 LIVE — composes (taskforce):Attack_Target(target) via DoString. "
              + "TaskForce-receiver variant of iter-163 SWFOC_AttackTargetLua (which targets "
              + "unit receivers). Per docs/lua-api.md SpaceTaskForce/LandTaskForce section. "
              + "Reuses iter-154 helper. Iter 215 native UX: Galactic tab 'Attack target' "
              + "button — operator types target into TaskForceTargetLuaExpr."),
            ["SWFOC_TaskForceGuardTargetLua"] = new("SWFOC_TaskForceGuardTargetLua", CapabilityStatus.Live,
                "Iter 176 LIVE — composes (taskforce):Guard_Target(target) via DoString. "
              + "TaskForce-receiver variant of iter-163 SWFOC_GuardTargetLua. SpaceTaskForce-only "
              + "per docs/lua-api.md. Reuses iter-154 helper. Iter 215 native UX: Galactic tab "
              + "'Guard target' button — operator types target into TaskForceTargetLuaExpr."),
            ["SWFOC_TaskForceLandUnitsLua"] = new("SWFOC_TaskForceLandUnitsLua", CapabilityStatus.Live,
                "Iter 176 LIVE — composes (taskforce):Land_Units(planet) via DoString. "
              + "GalacticTaskForce complement to iter-175 SWFOC_TaskForceLaunchUnitsLua. "
              + "Reuses iter-154 helper. Iter 215 native UX: Galactic tab 'Land units' button "
              + "— pairs visually with Launch_Units in the same surfacing row."),
            ["SWFOC_TaskForceSetAsGoalSystemRemovableLua"] = new(
                "SWFOC_TaskForceSetAsGoalSystemRemovableLua", CapabilityStatus.Live,
                "Iter 176 LIVE — composes (taskforce):Set_As_Goal_System_Removable(b) via "
              + "DoString. AI goal-cleanup flag, TaskForceClass-specific. Reuses iter-111 "
              + "obj-bool helper. Iter 215 native UX: Galactic tab 'Goal-system-removable: "
              + "on/off' bool-pair (iter-204 hardcoded-bool pattern, now 6 iters deep: "
              + "204→208→211→212→213→215)."),
            // 2026-05-04 (iter 177) — NEW global-getter-with-arg helper + 3 wires.
            // Dispatcher set extends to 8 helpers — iter-177 mirrors iter-173's
            // arg-getter pattern but for no-receiver globals. Discovery operations
            // return engine handles for further composition.
            ["SWFOC_FindObjectTypeLua"] = new("SWFOC_FindObjectTypeLua", CapabilityStatus.Live,
                "Iter 177 LIVE — composes Find_Object_Type(name) via DoString and captures "
              + "GameObjectType handle for further composition. First wire shipped via NEW "
              + "iter-177 Lua_DispatchGlobalGetterArg helper (8th in dispatcher set, first "
              + "global-arg-getter to capture engine return values). Operator-friendly for "
              + "discovery workflows like `Spawn_Unit(player, Find_Object_Type('AT_AT'), pos)`. "
              + "Iter 203 native UX: Spawning tab 'Discovery helpers' GroupBox — operator "
              + "types a type-name in the shared FindTypeNameLuaExpr field and clicks; "
              + "captured handle lands in LastStatus for paste into spawn fields."),
            ["SWFOC_FindPlanetLua"] = new("SWFOC_FindPlanetLua", CapabilityStatus.Live,
                "Iter 177 LIVE — composes FindPlanet(name) via DoString and captures "
              + "PlanetWrapper handle. Useful for galactic workflows that need to target "
              + "specific planets by name (e.g. `(taskforce):Launch_Units(FindPlanet('CORUSCANT'))`). "
              + "Reuses iter-177 helper. Iter 203 native UX: Spawning tab Discovery helpers "
              + "GroupBox — pairs with iter-200 FOWReveal partial-reveal workflow (find a "
              + "planet, then reveal FOW around its position)."),
            ["SWFOC_FindFirstObjectLua"] = new("SWFOC_FindFirstObjectLua", CapabilityStatus.Live,
                "Iter 177 LIVE — composes Find_First_Object(type_name) via DoString and "
              + "captures GameObjectWrapper handle for first instance of given type. Useful "
              + "for scripted workflows that need a specific unit reference. Reuses iter-177 helper. "
              + "Iter 203 native UX: Spawning tab Discovery helpers GroupBox — pairs with "
              + "iter-200 FOWReveal partial-reveal workflow."),
            // 2026-05-04 (iter 178) — NEW global-no-arg-getter helper + 3 wires.
            // Closes the dispatcher matrix: 9 helpers covering full receiver × arg ×
            // read/write × 2-cell shape space. After iter 178, future wires using
            // any known shape ship at ~3 LoC marginal bridge cost.
            ["SWFOC_GetGameModeLua"] = new("SWFOC_GetGameModeLua", CapabilityStatus.Live,
                "Iter 178 LIVE — composes Get_Game_Mode() via DoString and captures the "
              + "engine's mode string ('Galactic' / 'Land' / 'Space'). First wire shipped via "
              + "NEW iter-178 Lua_DispatchGlobalGetterNoArg helper (9th in dispatcher set, "
              + "closes the receiver × arg × read/write matrix). Operator-friendly for gating "
              + "tactical-only commands: e.g. check Get_Game_Mode() == 'Land' before "
              + "calling iter-157 SWFOC_MoveToLua, since unit movement is tactical-only."),
            ["SWFOC_GetLocalPlayerLua"] = new("SWFOC_GetLocalPlayerLua", CapabilityStatus.Live,
                "Iter 178 LIVE — composes Get_Local_Player() via DoString and captures the "
              + "PlayerWrapper handle for the operator's own player. Composes with iter-155 "
              + "PlayerGiveMoney for 'give MY player credits' workflows: "
              + "`(Get_Local_Player()):Give_Money(100000)`. Reuses iter-178 helper."),
            ["SWFOC_GetSecondsPerGameMinuteLua"] = new("SWFOC_GetSecondsPerGameMinuteLua", CapabilityStatus.Live,
                "Iter 178 LIVE — composes Get_Seconds_Per_Game_Minute() via DoString and "
              + "captures the time-scale float (real-seconds-per-game-minute). Useful for "
              + "diagnostic workflows that need to report engine time-scale, complementing "
              + "the (deferred) iter-131 SWFOC_SetGameSpeed Phase-1 mirror. Reuses iter-178 helper."),
            // 2026-05-04 (iter 179) — first batch post matrix-complete; ~3 LoC bridge per wire.
            // PlayerWrapper diplomacy queries (Is_Enemy/Is_Ally) + global discovery extension
            // (Find_All_Objects_Of_Type) + TaskForce write-side completion (Move_To_Target).
            ["SWFOC_IsEnemyLua"] = new("SWFOC_IsEnemyLua", CapabilityStatus.Live,
                "Iter 179 LIVE — composes (player_a):Is_Enemy(player_b) via iter-173 unit-getter-arg "
              + "helper (helper is shape-agnostic; works for player receivers, not just units). "
              + "Returns 'true'/'false' string. Pairs with iter-178 GetLocalPlayer for "
              + "'is THIS player my enemy?' workflow: `(Get_Local_Player()):Is_Enemy(other)`. "
              + "Read-side complement to iter-161 SWFOC_PlayerMakeEnemy writer. Iter 199 surfaced "
              + "as native UX in PlayerState tab — second OtherPlayerLuaExpr field for the predicate arg."),
            ["SWFOC_IsAllyLua"] = new("SWFOC_IsAllyLua", CapabilityStatus.Live,
                "Iter 179 LIVE — composes (player_a):Is_Ally(player_b) via iter-173 helper. "
              + "Diplomacy complement to Is_Enemy. Pairs with iter-178 GetLocalPlayer + iter-161 "
              + "PlayerMakeAlly. Note: Make_Ally/Make_Enemy state RESETS on game-mode change "
              + "(Galactic↔Tactical), so Is_Ally readings are mode-bound — re-read after each "
              + "transition. Reuses iter-179 batch helper pattern. Iter 199 surfaced as native UX "
              + "in PlayerState tab — sibling button to Is_Enemy."),
            ["SWFOC_FindAllObjectsOfTypeLua"] = new("SWFOC_FindAllObjectsOfTypeLua", CapabilityStatus.Live,
                "Iter 179 LIVE — composes Find_All_Objects_Of_Type(type_name) via iter-177 "
              + "global-getter-arg helper. Returns a Lua table; helper tostring()s to "
              + "'table: 0xADDR' which operators can pass through Lua Playground for iteration: "
              + "`for i,obj in pairs(Find_All_Objects_Of_Type(t)) do ... end`. Discovery "
              + "complement to iter-177 Find_First_Object (single instance vs. all instances). "
              + "Iter 206 native UX: Spawning tab Discovery helpers GroupBox 5th button "
              + "'Find all of type' — completes the 'first / nearest / all' trio alongside "
              + "iter-203 FindFirstObject + iter-186 FindNearest. Reuses shared "
              + "FindTypeNameLuaExpr field."),
            ["SWFOC_TaskForceMoveToTargetLua"] = new("SWFOC_TaskForceMoveToTargetLua", CapabilityStatus.Live,
                "Iter 179 LIVE — composes (taskforce):Move_To_Target(target) via iter-154 "
              + "1-arg helper. TaskForceClass-only method (per docs/lua-api.md line 213). "
              + "Distinct from iter-175 SWFOC_TaskForceMoveToLua which takes a position. "
              + "Naming convention parallels iter-175 (TaskForce-prefixed disambiguation). "
              + "Iter 218 native UX: Galactic tab TaskForce row extension button "
              + "'TaskForce: move to target' — reuses iter-215 TaskForceLuaExpr + "
              + "TaskForceTargetLuaExpr fields (zero new fields). Operator can A/B test "
              + "Move_To (position-targeted, iter-215) vs Move_To_Target (object-targeted, "
              + "iter-218) without re-typing the TaskForce handle."),
            // 2026-05-04 (iter 180) — namespaced + pair-completion batch.
            ["SWFOC_FOWRevealAllLua"] = new("SWFOC_FOWRevealAllLua", CapabilityStatus.Live,
                "Iter 180 LIVE — composes FOWManager.Reveal_All(player) via iter-158 "
              + "global-arg helper. Demonstrates NAMESPACED method-name dispatch through "
              + "the existing helper — Lua's `.` lookup makes `FOWManager.Reveal_All` "
              + "equivalent to a function call from the helper's perspective. No new "
              + "helper required; proves iter-158 is namespace-agnostic. "
              + "docs/lua-api.md section 5.4. Useful for cinematic/debug workflows. "
              + "Iter 200 native UX: Galactic tab 'Fog of War' GroupBox — operators "
              + "type a player Lua expression into FOWPlayerLuaExpr (default "
              + "'Find_Player(\"REBEL\")') and click 'Reveal map'."),
            ["SWFOC_FOWUndoRevealAllLua"] = new("SWFOC_FOWUndoRevealAllLua", CapabilityStatus.Live,
                "Iter 180 LIVE — composes FOWManager.Undo_Reveal_All(player) via iter-158. "
              + "Pairs with FOWRevealAll for cinematic-mode FOW toggle. Reuses iter-180 "
              + "namespaced-dispatch pattern. Iter 200 native UX: Galactic tab 'Fog of "
              + "War' GroupBox 'Restore fog' button — sibling to 'Reveal map' so operators "
              + "can toggle without dropping into Lua Playground."),
            ["SWFOC_UnlockControlsLua"] = new("SWFOC_UnlockControlsLua", CapabilityStatus.Live,
                "Iter 180 LIVE — composes Unlock_Controls() via iter-166 global-no-arg "
              + "helper. Pair-completion with iter-160 SWFOC_LockControlsLua (operator "
              + "workflow: LockControls(true) → cinematic action → Unlock_Controls()). "
              + "docs/lua-api.md section 5.2. Iter 208 native UX: WorldState tab Story+Audio "
              + "GroupBox 'Unlock_Controls' button — symmetric pair with the Lock-on/Lock-off "
              + "buttons (also iter-208) so operators can bracket cinematic recording without "
              + "dropping into Lua Playground."),
            ["SWFOC_CorruptLua"] = new("SWFOC_CorruptLua", CapabilityStatus.Live,
                "Iter 180 LIVE — composes (unit):Corrupt(amount) via iter-154 1-arg helper. "
              + "Underworld faction signature ability — degrades unit hostility/loyalty. "
              + "Pairs with iter-157 SWFOC_BribeLua (both are Underworld special abilities; "
              + "Bribe takes ownership, Corrupt degrades). docs/lua-api.md section 5.1. "
              + "Iter 218 native UX: UnitControl tab unit-method row extension 'Corrupt unit' "
              + "button — anchors on iter-117 SelectedUnitLuaExpr; needs NEW iter-218 "
              + "CorruptAmountLuaExpr field for the numeric amount arg. Pairs semantically "
              + "with iter-212 Bribe button — both are Underworld signature abilities, "
              + "operator can A/B test Bribe (take ownership) vs Corrupt (degrade only)."),
            // 2026-05-05 (iter 181) — namespace expansion proving iter-180 finding extends.
            ["SWFOC_ThreadGetCurrentStageLua"] = new("SWFOC_ThreadGetCurrentStageLua", CapabilityStatus.Live,
                "Iter 181 LIVE — composes Thread.Get_Current_Stage() via iter-178 "
              + "global-no-arg-getter helper. Extends iter-180's namespace-agnostic finding "
              + "to iter-178: the helper's codegen `return tostring(<name>())` works for "
              + "namespaced names because Lua's `.` lookup is part of the parser, not the "
              + "helper. Returns current cinematic-thread stage int (per docs/lua-api.md "
              + "section 5.2). Iter 205 native UX: Diagnostics tab 'Read engine state' "
              + "row 4th button 'Thread stage' — sibling to iter-190 Game mode / Local "
              + "player / Time scale. Validates iter-181 namespace-agnostic finding holds "
              + "at the UX layer."),
            ["SWFOC_SFXAllowUnitReponseVoLua"] = new("SWFOC_SFXAllowUnitReponseVoLua", CapabilityStatus.Live,
                "Iter 181 LIVE — composes SFXManager.Allow_Unit_Reponse_VO(bool) via iter-158 "
              + "global-arg helper. **NOTE: engine has a typo** — actual function name is "
              + "'Reponse' (not 'Response'). docs/lua-api.md section 6 (Behavioral Warnings) "
              + "flags this; the SWFOC_* name preserves the typo verbatim so future readers "
              + "know it's not a copy-paste error. Toggles whether units play VO responses "
              + "to player commands. Iter 204 native UX: WorldState tab Story & Audio "
              + "GroupBox SFX VO toggle row — TWO BUTTONS 'VO on' / 'VO off' (hardcoded "
              + "bool-string args 1/0). Engine TYPO 'Reponse' preserved verbatim through "
              + "dispatcher method (SfxAllowUnitReponseVoLuaAsync), VM properties + commands "
              + "(SfxAllowUnitReponseVoOn/Off), CapabilityAwareAction names, and XAML button "
              + "tooltips. iter 204 pin tests assert the typo survives at every layer."),
            // 2026-05-05 (iter 182) — first multi-arg expansion beyond the matrix.
            // 10th dispatcher helper (Lua_DispatchGlobalArg2Method) shipped this iter.
            ["SWFOC_GlobalMakeAllyLua"] = new("SWFOC_GlobalMakeAllyLua", CapabilityStatus.Live,
                "Iter 182 LIVE — composes Make_Ally(player1, player2) via NEW iter-182 "
              + "Lua_DispatchGlobalArg2Method (10th helper in dispatcher set; first "
              + "multi-arg expansion beyond the matrix). Global-form alternative to "
              + "iter-161 SWFOC_PlayerMakeAllyLua which uses the obj-receiver form "
              + "(player1):Make_Ally(player2). Both forms work; operator preference. "
              + "**CAVEAT**: state RESETS on every game-mode change (Galactic↔Tactical) "
              + "— caller must re-apply after each transition. Per docs/lua-api.md "
              + "section 6. Iter 217 native UX: PlayerState tab final-extension GroupBox row 6 "
              + "'Make ally (GLOBAL)' button — alternative to iter-209's obj-receiver Make_Ally "
              + "button. Reuses PlayerLuaExpr (player1) + OtherPlayerLuaExpr (player2) — operator "
              + "can A/B test the two forms without re-typing args."),
            ["SWFOC_GlobalMakeEnemyLua"] = new("SWFOC_GlobalMakeEnemyLua", CapabilityStatus.Live,
                "Iter 182 LIVE — composes Make_Enemy(player1, player2) via iter-182 helper. "
              + "Diplomacy complement to GlobalMakeAlly. Same mode-change-reset caveat "
              + "applies. Pairs with iter-161 PlayerMakeEnemyLua (obj-receiver form) and "
              + "iter-179 IsEnemy/IsAlly (read-side queries). Iter 217 native UX: PlayerState "
              + "tab final-extension GroupBox row 6 'Make enemy (GLOBAL)' button — alternative "
              + "to iter-209's obj-receiver Make_Enemy button. Field reuse identical to "
              + "iter-217 GlobalMakeAlly."),
            // 2026-05-05 (iter 184) — second multi-arg expansion (3-arg globals).
            // 11th dispatcher helper (Lua_DispatchGlobalArg3Method) shipped this iter.
            ["SWFOC_FOWRevealLua"] = new("SWFOC_FOWRevealLua", CapabilityStatus.Live,
                "Iter 184 LIVE — composes FOWManager.Reveal(player, position, radius) via "
              + "NEW iter-184 Lua_DispatchGlobalArg3Method (11th helper in dispatcher set; "
              + "second multi-arg expansion after iter-182's 2-arg helper). Partial-reveal "
              + "complement to iter-180 SWFOC_FOWRevealAllLua — operators can reveal a "
              + "specific area instead of the whole map. Inherits namespace-agnosticism "
              + "from iter-180/181 finding. docs/lua-api.md section 5.4. Architectural "
              + "opening: 3-arg helper unlocks future wires for engine APIs like "
              + "Set_Cinematic_Camera_Key(pos, target, duration), Find_Nearest(type, pos, "
              + "player), and similar. Iter 200 native UX: Galactic tab 'Fog of War' "
              + "GroupBox 'Reveal at position' button — operators type a position Lua "
              + "expression (default 'FindPlanet(\"Yavin\"):Get_Position()') and a radius "
              + "(default '500'); the player expression is shared with the FOWRevealAll "
              + "buttons."),
            // 2026-05-05 (iter 185) — first marginal-cost batch using iter-184 3-arg helper.
            // 3 wires from docs/lua-api.md section 2 (Spawning) — alternative spawn pathways.
            ["SWFOC_ReinforceUnitLua"] = new("SWFOC_ReinforceUnitLua", CapabilityStatus.Live,
                "Iter 185 LIVE — composes Reinforce_Unit(player, type, position) via "
              + "iter-184 3-arg helper. Spawn pathway via the reinforcement pool — "
              + "alternative to iter-109 SWFOC_SpawnUnitLua (direct spawn). Useful when "
              + "operators want to use the engine's reinforcement-budget mechanic instead "
              + "of bypassing it. Same arg order as Spawn_Unit (player, type, position). "
              + "First wire shipped via iter-184's helper at ~3 LoC marginal cost. "
              + "Iter 195 surfaced as native UX in Spawning tab — alongside iter-119 button."),
            ["SWFOC_SpawnFromReinforcementPoolLua"] = new("SWFOC_SpawnFromReinforcementPoolLua", CapabilityStatus.Live,
                "Iter 185 LIVE — composes Spawn_From_Reinforcement_Pool(player, type, position) "
              + "via iter-184 3-arg helper. Per docs/lua-api.md line 64 this is an "
              + "'alternative reinforcement spawn' to Reinforce_Unit (line 63) — both go "
              + "through the same pool but the engine exposes both as distinct entrypoints. "
              + "Operators may need either name depending on the script style. Reuses "
              + "iter-185 batch pattern. Iter 195 surfaced as native UX in Spawning tab."),
            ["SWFOC_CreateGenericObjectLua"] = new("SWFOC_CreateGenericObjectLua", CapabilityStatus.Live,
                "Iter 185 LIVE — composes Create_Generic_Object(type, position, player) via "
              + "iter-184 3-arg helper. **GOTCHA: param order differs from Spawn_Unit** — "
              + "Create_Generic_Object takes (type, position, player) while iter-109 "
              + "Spawn_Unit takes (player, type, position). Per docs/lua-api.md line 65 "
              + "explicit note. Catalog rationale flags this so future readers don't "
              + "assume parity. Useful for spawning non-unit objects (props, particle "
              + "emitters, etc.) that aren't valid for Spawn_Unit. Iter 195 surfaced as "
              + "native UX in Spawning tab — dispatcher reorders the player-first input "
              + "fields to match the engine's (type, position, player) order, so operators "
              + "see the GOTCHA in the button label but don't have to re-type."),
            // 2026-05-05 (iter 186) — symmetric to iter-184 (multi-arg getter, mirror of setter).
            // 12th dispatcher helper (Lua_DispatchGlobalGetter3Arg) shipped this iter.
            ["SWFOC_FindNearestLua"] = new("SWFOC_FindNearestLua", CapabilityStatus.Live,
                "Iter 186 LIVE — composes Find_Nearest(type, position, player) via NEW "
              + "iter-186 Lua_DispatchGlobalGetter3Arg (12th helper in dispatcher set; "
              + "symmetric counterpart to iter-184 3-arg setter — mirror with engine "
              + "return-value capture, like iter-177 mirrors iter-158). Returns "
              + "GameObjectWrapper handle for the closest instance of given type owned "
              + "by given player at given position. Composes with iter-177 Find_Object_Type "
              + "+ iter-178 Get_Local_Player for 'find my closest AT-AT to here' workflows: "
              + "`Find_Nearest(Find_Object_Type('AT_AT'), pos, Get_Local_Player())`. "
              + "docs/lua-api.md line 75. Iter 203 native UX: Spawning tab Discovery "
              + "helpers GroupBox — first wire to use the NEW iter-203 generic "
              + "BuildSwfocLua3ArgCall builder (parameterised SWFOC name; iter-200's "
              + "FOWReveal builder hardcoded the name)."),
            ["SWFOC_HeroStatEdit"] = new("SWFOC_HeroStatEdit", CapabilityStatus.Live,
                "Per-field hero edit dispatcher (3/4 sub-fields LIVE): "
              + "hull → direct write to GameObj::HP (LIVE); "
              + "shield → SetFrontShield @ 0x3A8630 + SetRearShield @ 0x3A91E0 (LIVE iter 129); "
              + "speed → SetSpeedOverride @ 0x3A8C90 (LIVE iter 100); "
              + "respawn_ms → Phase-1 mirror only (g_pendingRespawnWrites) pending per-hero respawn-timer table RVA. "
              + "Iter 135 catalog drift fix — bridge always had these LIVE branches; "
              + "the catalog under-reported them as a single Phase2 entry."),
            ["SWFOC_HeroInstantRespawn"] = new("SWFOC_HeroInstantRespawn", CapabilityStatus.Live,
                "Sets respawn-remaining to 0"),

            // Generic kill / revive helpers
            ["SWFOC_KillUnit"] = new("SWFOC_KillUnit", CapabilityStatus.Live,
                "SetHP=0 detour"),
            ["SWFOC_ReviveUnit"] = new("SWFOC_ReviveUnit", CapabilityStatus.Live,
                "Local-only revive via SetHP=max"),
            ["SWFOC_SetUnitInvuln"] = new("SWFOC_SetUnitInvuln", CapabilityStatus.Live,
                "Direct invuln_flag write"),
            ["SWFOC_SetUnitHull"] = new("SWFOC_SetUnitHull", CapabilityStatus.Live,
                "SetHP detour"),
            ["SWFOC_PreventUnitDeath"] = new("SWFOC_PreventUnitDeath", CapabilityStatus.Live,
                "Direct prevent_death flag write"),

            // Live game required (offline harness skip)
            ["SWFOC_InspectUnit"] = new("SWFOC_InspectUnit", CapabilityStatus.RequiresLiveSwfoc,
                "Needs live unit object"),
            ["SWFOC_GetHardpoints"] = new("SWFOC_GetHardpoints", CapabilityStatus.RequiresLiveSwfoc,
                "Needs live hardpoint vector"),
        };

    /// <summary>
    /// Look up a helper's status. Returns <see cref="CapabilityStatus.Unavailable"/>
    /// when the name isn't catalogued — caller should treat that as a
    /// "show generic disabled state" signal so the UI never lies about
    /// what an unknown helper does.
    /// </summary>
    public static CapabilityStatusEntry Lookup(string helperName)
    {
        if (string.IsNullOrEmpty(helperName)) return new("?", CapabilityStatus.Unavailable);
        return Entries.TryGetValue(helperName, out var entry)
            ? entry
            : new CapabilityStatusEntry(helperName, CapabilityStatus.Unavailable,
                "Not in catalogue — update CapabilityStatusCatalog.cs");
    }

    /// <summary>Human-readable single-token form used by the UI badge.</summary>
    public static string ShortLabel(CapabilityStatus status) => status switch
    {
        CapabilityStatus.Live => "LIVE",
        CapabilityStatus.ReplayVerified => "REPLAY",
        CapabilityStatus.Phase2HookPending => "PHASE 2 PENDING",
        CapabilityStatus.RequiresLiveSwfoc => "LIVE ONLY",
        CapabilityStatus.Unavailable => "UNAVAILABLE",
        _ => status.ToString().ToUpperInvariant(),
    };

    /// <summary>
    /// Compose a UI badge string for a tab whose backing helpers may have
    /// different statuses. Returns a single token for uniform-status tabs
    /// (e.g. "LIVE") and a "MIXED (m/n LIVE)" form when a tab combines live
    /// + Phase-2-pending helpers.
    /// </summary>
    public static string ComposeBadge(params string[] helperNames)
    {
        if (helperNames is null || helperNames.Length == 0) return "—";
        var entries = helperNames.Select(Lookup).ToList();
        var distinctStatuses = entries.Select(e => e.Status).Distinct().ToList();
        if (distinctStatuses.Count == 1)
        {
            return ShortLabel(distinctStatuses[0]);
        }
        var liveCount = entries.Count(e => e.Status == CapabilityStatus.Live);
        return string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "MIXED ({0}/{1} LIVE)", liveCount, entries.Count);
    }

    /// <summary>
    /// 2026-04-27 (iter 39): generate a markdown report of every helper's
    /// status, suitable for checking into <c>knowledge-base/</c>. The
    /// matching audit test (<c>CapabilityCatalogReportTests</c>) regenerates
    /// this and asserts the on-disk file matches — so the report can never
    /// drift from the catalog without a test failure.
    /// </summary>
    /// <remarks>
    /// Output is deterministic: sections sorted by status, helpers sorted
    /// alphabetically inside each section. Use <c>\n</c> line endings so
    /// the test comparison isn't whitespace-flaky on Windows checkouts.
    /// </remarks>
    public static string GenerateMarkdownReport()
    {
        var byStatus = Entries.Values
            .GroupBy(e => e.Status)
            .OrderBy(g => (int)g.Key)
            .ToList();
        var totals = Entries.Count;
        var sb = new System.Text.StringBuilder(8 * 1024);
        sb.Append("# SWFOC Capability Status Matrix\n\n");
        sb.Append("**Auto-generated from `CapabilityStatusCatalog.cs` ");
        sb.Append("by `CapabilityStatusCatalog.GenerateMarkdownReport()`. ");
        sb.Append("Do not edit by hand — update the catalog and regenerate.**\n\n");
        sb.Append("Total helpers catalogued: ").Append(totals).Append("\n\n");

        foreach (var group in byStatus)
        {
            sb.Append("## ").Append(ShortLabel(group.Key)).Append(" (")
              .Append(group.Count()).Append(")\n\n");
            sb.Append("| Helper | Note |\n|---|---|\n");
            foreach (var entry in group.OrderBy(e => e.HelperName, StringComparer.Ordinal))
            {
                sb.Append("| `").Append(entry.HelperName).Append("` | ");
                sb.Append(entry.Note?.Replace("|", "\\|") ?? "(none)");
                sb.Append(" |\n");
            }
            sb.Append('\n');
        }

        sb.Append("## Status legend\n\n");
        sb.Append("- **LIVE** — direct engine call, observable mutation.\n");
        sb.Append("- **REPLAY** — replay-mirror green, live unverified.\n");
        sb.Append("- **PHASE 2 PENDING** — Phase 1 mirror works, Phase 2 detour BLOCKED-NO-RVA.\n");
        sb.Append("- **LIVE ONLY** — needs running game; offline harness can't exercise.\n");
        sb.Append("- **UNAVAILABLE** — registered but out-of-scope for current release.\n");
        return sb.ToString();
    }
}
