using System.Collections.Generic;
using System.Linq;

namespace SwfocTrainer.Tests.Simulator;

/// <summary>
/// In-memory mock of the SWFOC runtime world. Holds the slots, units,
/// planets, story flags, and global toggles (mode override, ai-enabled,
/// fog-of-war, etc.) that the editor's bridge probes mutate or read.
/// All collections are public-mutable on purpose — tests both DRIVE
/// the state through bridge calls AND directly inspect / seed it.
/// </summary>
/// <remarks>
/// <para>
/// This is intentionally simpler than the real engine. The point isn't to
/// be cycle-accurate; the point is to model enough state that "did the
/// editor command actually do what it claims" becomes testable.
/// </para>
/// <para>
/// Threading: handlers run on the named-pipe listener thread. The simple
/// collection mutations here are safe for the single-client-at-a-time
/// pipe model (max_instances=1 in the real bridge, mirrored here).
/// </para>
/// </remarks>
public sealed class FakeGameState
{
    public List<FakePlayer> Players { get; } = new();
    public List<FakeUnit> Units { get; } = new();
    public List<FakePlanet> Planets { get; } = new();
    public HashSet<string> StoryFlags { get; } = new();
    public HashSet<string> KnownTypeNames { get; } = new();

    /// <summary>
    /// 2026-05-07 (iter 299): currently-loaded mod metadata. Empty
    /// <see cref="ActiveModName"/> means vanilla SWFOC (no mod). Mirrors
    /// the bridge's <c>SWFOC_GetCurrentMod</c> filesystem-probe wire output.
    /// </summary>
    public string ActiveModName { get; set; } = string.Empty;
    public string ActiveModVersion { get; set; } = string.Empty;
    public string ActiveModPath { get; set; } = string.Empty;

    /// <summary>
    /// 2026-05-07 (iter 300; 300th-iter milestone): all mods discoverable
    /// under ./Mods/* in the live game. Simulator handler for SWFOC_ListMods
    /// reads this list to mirror the real bridge's filesystem walk.
    /// Tuple shape: (name, absolutePath).
    /// </summary>
    public List<(string Name, string Path)> AvailableMods { get; } = new();

    /// <summary>
    /// Galactic vs tactical mode. Mirrors the engine's <c>RuntimeMode</c>
    /// enum. The editor's runtime-mode-override pulls this through
    /// MainViewModelV2.
    /// </summary>
    public string RuntimeMode { get; set; } = "Galactic";

    /// <summary>
    /// Engine-wide AI enabled flag. Tactical mode tabs flip this.
    /// </summary>
    public bool AiEnabled { get; set; } = true;

    /// <summary>
    /// Engine-wide fog-of-war flag. World State tab flips this.
    /// </summary>
    public bool FogOfWarEnabled { get; set; } = true;

    /// <summary>
    /// Global game-speed multiplier (1.0 = normal). Mirrors the engine's
    /// <c>g_GameSpeedMultiplier</c>.
    /// </summary>
    public float GameSpeed { get; set; } = 1f;

    /// <summary>
    /// Per-faction speed multipliers (Speed tab). Faction key → multiplier.
    /// Missing entries imply 1.0 (no scaling).
    /// </summary>
    public System.Collections.Generic.Dictionary<string, float> PerFactionSpeed { get; }
        = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Per-faction credit-income multipliers (Economy tab).
    /// </summary>
    public System.Collections.Generic.Dictionary<string, float> PerFactionIncome { get; }
        = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Engine hero-respawn-timer in seconds. -1 disables auto-respawn entirely
    /// (Permadeath toggle); 0 makes heroes respawn instantly.
    /// </summary>
    public int HeroRespawnSeconds { get; set; } = 60;

    /// <summary>
    /// Permadeath master toggle. When true, dead heroes never respawn.
    /// Hero Lab tab toggles this.
    /// </summary>
    public bool Permadeath { get; set; }

    /// <summary>
    /// Diplomacy matrix. Key is "<c>fromSlot:toSlot</c>"; value is one of
    /// <c>Allied</c>, <c>Neutral</c>, <c>Hostile</c>. Missing entries imply
    /// Hostile (the engine default for unset relations).
    /// </summary>
    public System.Collections.Generic.Dictionary<string, string> Diplomacy { get; }
        = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Event stream — engine emits records here when major game events fire.
    /// Editor's Event Stream tab drains this with <c>SWFOC_EventStreamDrain</c>.
    /// </summary>
    public System.Collections.Generic.Queue<string> EventQueue { get; } = new();

    /// <summary>
    /// Free Build mode (no resource cost). Tactical / galactic both honour it.
    /// </summary>
    public bool FreeBuildEnabled { get; set; }

    /// <summary>
    /// Free Cam mode (cinematic camera unrestricted). World State tab toggles.
    /// </summary>
    public bool FreeCamEnabled { get; set; }

    /// <summary>
    /// Credits-frozen mode — operator's credits don't decrease on spend.
    /// Mirrors SWFOC_FreezeCredits.
    /// </summary>
    public bool CreditsFrozen { get; set; }

    /// <summary>
    /// God Mode — every alive unit becomes invulnerable until cleared.
    /// </summary>
    public bool GodModeEnabled { get; set; }

    /// <summary>
    /// Globally enable one-hit-kill attack power. Combat tab toggles.
    /// </summary>
    public bool OneHitKillEnabled { get; set; }

    /// <summary>
    /// Per-faction unit-cap overrides. Missing entries imply engine defaults.
    /// </summary>
    public System.Collections.Generic.Dictionary<string, int> PerFactionUnitCap { get; }
        = new(System.StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Active target-filter bitmask (Combat / Unit Control). 0 = all targets,
    /// non-zero = restrict to ground/space/heroes/etc per the editor's bit
    /// layout in <c>UnitControlTabViewModel</c>.
    /// </summary>
    public int TargetFilterMask { get; set; }

    /// <summary>
    /// Currently inspected unit id (Inspector tab). 0 = none.
    /// </summary>
    public int SelectedUnitId { get; set; }

    /// <summary>
    /// Engine-wide max-credits cap. -1 = uncapped (after SWFOC_UncapCredits).
    /// </summary>
    public int MaxCredits { get; set; } = 999999;

    /// <summary>
    /// Bridge log buffer — every SWFOC_Log call lands here.
    /// </summary>
    public System.Collections.Generic.List<string> LogLines { get; } = new();

    /// <summary>
    /// Engine "tick" counter — bumped by SWFOC_DiagGameTick probes. Useful
    /// for tests that need to demonstrate "the engine is making forward
    /// progress" without modelling actual frame timing.
    /// </summary>
    public long GameTickCount { get; set; }

    /// <summary>
    /// Per-slot damage scalar (Combat tab; <c>SWFOC_SetDamageMultiplier(slot, mult)</c>).
    /// Missing entries imply 1.0.
    /// </summary>
    public System.Collections.Generic.Dictionary<int, float> PerSlotDamageMultiplier { get; }
        = new();

    /// <summary>
    /// 2026-04-28 (iter 97 master loop): global damage scalar applied by the
    /// real bridge at the Take_Damage_Outer chokepoint. Mirror of bridge's
    /// <c>g_dmgMult_global</c>. Default 1.0 = no scaling.
    /// </summary>
    public float GlobalDamageMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// 2026-05-06 (iter 225/226): global fire-rate scalar applied by the real
    /// bridge at the WeaponTick @ 0x387010 chokepoint. Mirror of bridge's
    /// <c>g_fireRateMult_global</c>. Default 1.0 = no scaling. Closes A1.3
    /// after 124-day deferral (iter-101/130/132/221 audits). See
    /// <c>knowledge-base/iter224_setfirerate_global_re_kickoff.md</c> for the
    /// design doc + engine semantic caveats.
    /// </summary>
    public float GlobalFireRateMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// 2026-05-08 (iter 285): Tier 3 HUD counters mirroring the bridge's
    /// <c>g_localPlayerKills</c> / <c>g_localPlayerDeaths</c> atomic
    /// counters + the on-demand <c>SWFOC_GetTotalUnitsAlive</c> walk of
    /// <c>Selection::kObjectListHead</c>. Tests can pre-seed these to
    /// exercise downstream consumer code (overlay HUD, HudSnapshot probes)
    /// without invoking a real <c>Hook_DeathHandler</c>. Default 0/0/0
    /// matches bridge boot state. See <c>iter285_bridge_wires_design_2026-05-08.md</c>.
    /// </summary>
    public int LocalPlayerKills { get; set; } = 0;
    public int LocalPlayerDeaths { get; set; } = 0;
    public int TotalUnitsAlive { get; set; } = 0;

    /// <summary>
    /// 2026-05-06 (iter 231/232): bool freeze applied by the real bridge at
    /// the AddCredits @ 0x27F370 chokepoint. Mirror of bridge's
    /// <c>g_creditsFreeze_global</c>. Default false = no short-circuit. When
    /// true, AddCredits returns the unchanged balance without writing
    /// PlayerClass+0x70 (no event notification, no tracking callback).
    /// Wins-over-mult precedence per iter-230 RE design doc. See
    /// <c>knowledge-base/iter230_freeze_credits_re_kickoff.md</c> for design
    /// + engine semantic caveats.
    /// </summary>
    public bool GlobalCreditsFreeze { get; set; } = false;

    /// <summary>
    /// 2026-05-06 (iter 231/232): scalar multiplier applied by the real
    /// bridge at the AddCredits @ 0x27F370 chokepoint. Mirror of bridge's
    /// <c>g_creditsMult_global</c>. Default 1.0 = no scaling (engine identity).
    /// Sanity clamp [0.0, 100.0] applied bridge-side. mult=2.0 → 2x income/spend,
    /// mult=0.5 → halved both, mult=0.0 → soft freeze.
    /// </summary>
    public float GlobalCreditsMultiplier { get; set; } = 1.0f;

    /// <summary>
    /// 2026-05-07 (iter 451): SWFOC_TriggerVictory pending-state mirror.
    /// Bridge wrapper @ iter-450 stages this when the operator calls
    /// SWFOC_TriggerVictory(victory_type) with one of the 14-of-18 known
    /// VictoryType enum names (per rva_victory_type_enum_init @ 0x341FF0).
    /// iter-450a will detour rva_victory_monitor_counter_inc @ 0x341FE0
    /// and inject an always-pass AwaitingVictoryTest into VictoryMonitor's
    /// vector at instance+0x68 when this flag is set. Tests preset/inspect
    /// to verify simulator round-trip semantics without invoking real DLL.
    /// </summary>
    public bool VictoryTriggerPending { get; set; } = false;

    /// <summary>
    /// 2026-05-07 (iter 451): companion to <see cref="VictoryTriggerPending"/>;
    /// holds the operator-provided victory_type string (validated by the
    /// simulator handler against the 14-name allow-list). Empty string means
    /// no trigger has been staged yet (or the previous one was consumed by
    /// the future iter-450a injection).
    /// </summary>
    public string VictoryTriggerType { get; set; } = "";

    /// <summary>Per-slot fire-rate scalar (<c>SWFOC_SetFireRate(slot, mult)</c>).</summary>
    public System.Collections.Generic.Dictionary<int, float> PerSlotFireRateMultiplier { get; }
        = new();

    /// <summary>Per-slot target-filter bitmask (<c>SWFOC_SetTargetFilter(slot, mask)</c>).</summary>
    public System.Collections.Generic.Dictionary<int, int> PerSlotTargetFilter { get; } = new();

    /// <summary>Per-slot income multiplier (<c>SWFOC_SetIncomeMultiplier(slot, mult)</c>).</summary>
    public System.Collections.Generic.Dictionary<int, float> PerSlotIncomeMultiplier { get; }
        = new();

    /// <summary>Per-slot unit-cap override; -1 = unlimited, -2 = clear override.</summary>
    public System.Collections.Generic.Dictionary<int, int> PerSlotUnitCap { get; } = new();

    /// <summary>Per-slot tech level (<c>SWFOC_SetTechForSlot(slot, level)</c>).</summary>
    public System.Collections.Generic.Dictionary<int, int> PerSlotTechLevel { get; } = new();

    /// <summary>Global area-damage toggle (<c>SWFOC_SetAreaDamage</c>).</summary>
    public bool AreaDamageEnabled { get; set; }

    /// <summary>Camera position (<c>SWFOC_SetCameraPos(x, y, z)</c>).</summary>
    public (float X, float Y, float Z) CameraPos { get; set; }

    /// <summary>
    /// 2026-04-28 (iter 107): last <c>SWFOC_ScrollCameraToTarget</c> Lua
    /// expression captured by the simulator. Mirrors the bridge's LIVE
    /// dispatch through <c>Scroll_Camera_To(&lt;expr&gt;)</c> — tests assert on
    /// the raw expression that would have been spliced at the engine call
    /// site. Empty string when no call has been made.
    /// </summary>
    public string LastScrollCameraToTarget { get; set; } = string.Empty;

    /// <summary>
    /// Last raw target expression dispatched via SWFOC_CameraFollow (iter 143
    /// LIVE wire). Empty string when no call has been made.
    /// </summary>
    public string LastCameraFollowTarget { get; set; } = string.Empty;

    /// <summary>
    /// Last raw target expression dispatched via SWFOC_RotateCameraTo (iter 144
    /// LIVE wire). Empty string when no call has been made.
    /// </summary>
    public string LastRotateCameraToTarget { get; set; } = string.Empty;

    /// <summary>
    /// True while cinematic-camera mode is active (iter 145 — between
    /// SWFOC_StartCinematicCamera and SWFOC_EndCinematicCamera).
    /// </summary>
    public bool CinematicCameraActive { get; set; }

    /// <summary>
    /// Last raw args expression dispatched via SWFOC_SetCinematicCameraKey
    /// (iter 145 LIVE).
    /// </summary>
    public string LastCinematicCameraKeyArgs { get; set; } = string.Empty;

    /// <summary>
    /// Last raw args expression dispatched via
    /// SWFOC_TransitionCinematicCameraKey (iter 145 LIVE).
    /// </summary>
    public string LastCinematicCameraTransitionArgs { get; set; } = string.Empty;

    /// <summary>
    /// True while cinematic letterbox bars are active (iter 150 — between
    /// SWFOC_LetterBoxOn and SWFOC_LetterBoxOff).
    /// </summary>
    public bool LetterBoxActive { get; set; }

    /// <summary>
    /// Iter 151 LIVE — last raw unit expression dispatched via SWFOC_TeleportUnitLua.
    /// </summary>
    public string LastTeleportUnitExpr { get; set; } = string.Empty;

    /// <summary>
    /// Iter 151 LIVE — last raw position expression dispatched via SWFOC_TeleportUnitLua.
    /// </summary>
    public string LastTeleportPositionExpr { get; set; } = string.Empty;

    /// <summary>Iter 152 LIVE — last Galactic_Spawn_Unit player expression.</summary>
    public string LastGalacticSpawnPlayer { get; set; } = string.Empty;
    /// <summary>Iter 152 LIVE — last Galactic_Spawn_Unit type expression.</summary>
    public string LastGalacticSpawnType { get; set; } = string.Empty;
    /// <summary>Iter 152 LIVE — last Galactic_Spawn_Unit planet expression.</summary>
    public string LastGalacticSpawnPlanet { get; set; } = string.Empty;

    /// <summary>
    /// 2026-04-28 (iter 108): last <c>SWFOC_ChangeUnitOwner</c> unit Lua
    /// expression. Mirrors the bridge's LIVE
    /// <c>(&lt;unit&gt;):Change_Owner(&lt;player&gt;)</c> dispatch.
    /// </summary>
    public string LastChangeUnitOwnerUnit { get; set; } = string.Empty;

    /// <summary>2026-04-28 (iter 108): last <c>SWFOC_ChangeUnitOwner</c> player Lua expression.</summary>
    public string LastChangeUnitOwnerPlayer { get; set; } = string.Empty;

    /// <summary>2026-04-28 (iter 109): last <c>SWFOC_SpawnUnitLua</c> player Lua expression.</summary>
    public string LastSpawnUnitLuaPlayer { get; set; } = string.Empty;
    /// <summary>2026-04-28 (iter 109): last <c>SWFOC_SpawnUnitLua</c> type Lua expression.</summary>
    public string LastSpawnUnitLuaType { get; set; } = string.Empty;
    /// <summary>2026-04-28 (iter 109): last <c>SWFOC_SpawnUnitLua</c> position Lua expression.</summary>
    public string LastSpawnUnitLuaPosition { get; set; } = string.Empty;

    /// <summary>2026-04-28 (iter 110): last <c>SWFOC_MakeUnitInvulnLua</c> unit Lua expression.</summary>
    public string LastMakeUnitInvulnLuaUnit { get; set; } = string.Empty;
    /// <summary>2026-04-28 (iter 110): last <c>SWFOC_MakeUnitInvulnLua</c> bool Lua expression ("true" or "false").</summary>
    public string LastMakeUnitInvulnLuaBool { get; set; } = string.Empty;

    /// <summary>
    /// 2026-04-28 (iter 111): per-method capture for the
    /// Hide / Prevent_AI_Usage / Set_Selectable batch. Key is the
    /// methodTag from the simulator handler ("Hide" / "PreventAiUsage" /
    /// "Selectable"); value is (unit_lua_expr, bool_lua_expr) — last
    /// call wins. Lets tests assert each method's last invocation
    /// without sharing fields between unrelated tests.
    /// </summary>
    public System.Collections.Generic.Dictionary<string, (string Unit, string Bool)>
        LastUnitBoolMethodCalls
    { get; }
        = new(System.StringComparer.Ordinal);

    /// <summary>
    /// 2026-04-28 (iter 112): per-method capture for the no-arg
    /// Despawn / Stop / Retreat batch. Same shape as
    /// <see cref="LastUnitBoolMethodCalls"/> minus the bool tuple.
    /// </summary>
    public System.Collections.Generic.Dictionary<string, string>
        LastUnitNoArgMethodCalls
    { get; }
        = new(System.StringComparer.Ordinal);

    /// <summary>
    /// 2026-04-29 (iter 154): per-method capture for the float-arg
    /// Take_Damage / Set_Damage_Modifier / Set_Rate_Of_Fire_Modifier batch.
    /// Same shape as <see cref="LastUnitBoolMethodCalls"/> with the second
    /// element being the float Lua expression (operator may pass numerals
    /// or expressions like "Get_Hull() * 0.5").
    /// </summary>
    public System.Collections.Generic.Dictionary<string, (string Unit, string Float)>
        LastUnitFloatMethodCalls
    { get; }
        = new(System.StringComparer.Ordinal);

    /// <summary>
    /// 2026-04-28 (iter 113): last <c>SWFOC_CallObjMethodLua</c> call
    /// captured by the simulator. Tuple of (obj_lua_expr, method_name,
    /// args_lua_expr) — empty strings until first call.
    /// </summary>
    public (string Obj, string Method, string Args) LastCallObjMethodLua { get; set; }
        = (string.Empty, string.Empty, string.Empty);

    /// <summary>
    /// Build a typical 4-slot tactical-mode skirmish: REBEL human in slot 0,
    /// EMPIRE AI in slot 1, UNDERWORLD AI in slot 2, NEUTRAL in slot 7.
    /// Seeds the type registry with a small canonical list so
    /// <c>SWFOC_BatchTypeExists</c> probes resolve.
    /// </summary>
    public static FakeGameState NewTacticalSkirmish()
    {
        var s = new FakeGameState
        {
            RuntimeMode = "TacticalLand",
        };
        s.Players.Add(FakePlayer.NewLocalHumanSlot(slot: 0, faction: "REBEL"));
        s.Players.Add(FakePlayer.NewAiSlot(slot: 1, faction: "EMPIRE"));
        s.Players.Add(FakePlayer.NewAiSlot(slot: 2, faction: "UNDERWORLD"));
        s.Players.Add(FakePlayer.NewAiSlot(slot: 7, faction: "NEUTRAL"));

        foreach (var t in new[]
        {
            "Rebel_Trooper_Squad",
            "Rebel_Plex_Soldier_Squad",
            "Rebel_Infantry_T2_B",
            "Empire_Stormtrooper_Squad",
            "Empire_AT_ST",
            "Empire_AT_AT",
            "Underworld_Mercenary_Squad",
            "Underworld_Heavy_Mercenary_Squad",
        })
        {
            s.KnownTypeNames.Add(t);
        }

        return s;
    }

    /// <summary>
    /// Build a galactic-mode game with a few planets seeded.
    /// </summary>
    public static FakeGameState NewGalacticCampaign()
    {
        var s = new FakeGameState
        {
            RuntimeMode = "Galactic",
        };
        s.Players.Add(FakePlayer.NewLocalHumanSlot(slot: 0, faction: "REBEL"));
        s.Players.Add(FakePlayer.NewAiSlot(slot: 1, faction: "EMPIRE"));
        s.Players.Add(FakePlayer.NewAiSlot(slot: 2, faction: "UNDERWORLD"));

        foreach (var (name, owner, faction) in new[]
        {
            ("Yavin", 0, "REBEL"),
            ("Hoth", 0, "REBEL"),
            ("Coruscant", 1, "EMPIRE"),
            ("Kuat", 1, "EMPIRE"),
            ("Hypori", 2, "UNDERWORLD"),
        })
        {
            s.Planets.Add(FakePlanet.New(name, owner, revealed: false, ownerFaction: faction));
        }

        return s;
    }

    /// <summary>
    /// Update both representations of a planet's owner in lockstep so
    /// handlers don't have to remember which axis the editor used.
    /// </summary>
    public void SetPlanetOwner(FakePlanet planet, string ownerFaction)
    {
        planet.OwnerFaction = ownerFaction;
        var matchingPlayer = Players.FirstOrDefault(p =>
            string.Equals(p.Faction, ownerFaction, System.StringComparison.OrdinalIgnoreCase));
        planet.OwnerSlot = matchingPlayer?.Slot ?? -1;
    }

    public FakePlayer? GetPlayer(int slot)
        => Players.FirstOrDefault(p => p.Slot == slot);

    public FakePlayer? GetLocalHuman()
        => Players.FirstOrDefault(p => p.IsHuman && p.IsLocal);

    public FakeUnit? GetUnit(int id)
        => Units.FirstOrDefault(u => u.Id == id);
}
