namespace SwfocTrainer.Core.Diagnostics;

/// <summary>
/// Execution path taxonomy for a ``Build*LuaCommand`` method.
///
/// The editor has grown to 30+ services that each build a Lua-command
/// string for the bridge. Previously, callers had no programmatic way to
/// tell whether a given service emitted a production-ready call or a
/// placeholder TODO string. Task #119 introduces this taxonomy so the
/// Orchestrator / Router / UI layers can gate features based on status
/// and pick the correct execution strategy.
/// </summary>
public enum BuildLuaCommandPath
{
    /// <summary>
    /// Unknown / unclassified. Should never appear in the hand-curated
    /// inventory; callers treating Unknown as disabled is the safe default.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// The service emits <c>return SWFOC_Something(args)</c> calls that
    /// hit the bridge's registered helper table in ``lua_bridge.cpp``.
    /// These calls are fully in our control (the bridge harness pins the
    /// behavior) and can be expected to work wherever the bridge DLL is
    /// loaded.
    /// </summary>
    RealBridge,

    /// <summary>
    /// The service emits engine-native Lua globals (``Find_Player``,
    /// ``FOWManager.Reveal_All``, ``Spawn_Unit``, ``Story_Event``, ...)
    /// which the engine's embedded Lua 5.0.2 VM evaluates directly. These
    /// depend on whatever Lua state the pipe is attached to and will
    /// silently no-op if the global isn't registered in that state
    /// (main-menu vs tactical vs galactic).
    /// </summary>
    RealEngine,

    /// <summary>
    /// The method switches on an input and returns a mix of real Lua for
    /// some branches and ``-- TODO`` comment strings for others. The
    /// caller must check the returned string with <c>StartsWith("--")</c>
    /// before dispatching. Upgrading a PartialStub to <see cref="RealBridge"/> or
    /// <see cref="RealEngine"/> requires wiring the remaining branches.
    /// </summary>
    PartialStub,

    /// <summary>
    /// The method ONLY ever emits ``-- ...`` comment strings. No branch
    /// currently dispatches a real Lua call. These are placeholders that
    /// exist to satisfy the interface / UI binding surface while the
    /// implementation is queued.
    /// </summary>
    Stub,
}

/// <summary>
/// One entry in the <see cref="BuildLuaCommandInventory"/> audit table.
/// </summary>
public sealed record BuildLuaCommandInfo(
    string ServiceTypeName,
    string MethodName,
    BuildLuaCommandPath Path,
    string Summary,
    IReadOnlyList<string> LuaEntryPoints);

/// <summary>
/// Hand-curated audit of every <c>Build*LuaCommand</c> static method in
/// <c>SwfocTrainer.Core.Services</c>. Cross-referenced with the actual
/// source via a reflection-based drift guard test (see
/// <c>BuildLuaCommandInventoryTests</c>).
///
/// Updating this inventory:
///   * Keep one entry per method. When you add / rename / remove a
///     <c>Build*LuaCommand</c> method, update this file in the same PR
///     — the drift-guard test will fail otherwise.
///   * <see cref="BuildLuaCommandPath.RealBridge"/> means the entry
///     dispatches through a registered SWFOC_* helper; list every such
///     helper in <c>LuaEntryPoints</c> so the audit surfaces bridge
///     dependencies (useful when shipping bridge helper changes).
///   * <see cref="BuildLuaCommandPath.RealEngine"/> means the entry
///     dispatches through the engine's native Lua. List the globals it
///     calls (``Find_Player``, ``Spawn_Unit``, ...) so cross-profile
///     compatibility audits can diff against mod-specific overrides.
/// </summary>
public static class BuildLuaCommandInventory
{
    // Sentinel empty list to avoid allocating one per entry that has no
    // Lua entry points (shouldn't happen in practice, but keeps the
    // invariant "every entry has at least one bullet of evidence").
    private static readonly IReadOnlyList<string> _empty = Array.Empty<string>();

    /// <summary>
    /// All <c>Build*LuaCommand</c> methods currently present in
    /// <c>SwfocTrainer.Core.Services</c>, keyed by fully-qualified
    /// method name (<c>ServiceTypeName.MethodName</c>).
    /// </summary>
    public static IReadOnlyDictionary<string, BuildLuaCommandInfo> Entries { get; } =
        new Dictionary<string, BuildLuaCommandInfo>(StringComparer.Ordinal)
        {
            // --- EconomyService (7 methods, all RealBridge) ---
            ["EconomyService.BuildSetCreditsLuaCommand"] = new(
                "EconomyService", "BuildSetCreditsLuaCommand", BuildLuaCommandPath.RealBridge,
                "Write credits, global or per-slot.",
                new[] { "SWFOC_SetCredits", "SWFOC_SetCreditsForSlot" }),
            ["EconomyService.BuildGetCreditsLuaCommand"] = new(
                "EconomyService", "BuildGetCreditsLuaCommand", BuildLuaCommandPath.RealBridge,
                "Read credits, global or per-slot.",
                new[] { "SWFOC_GetCredits", "SWFOC_GetCreditsForSlot" }),
            ["EconomyService.BuildDrainEnemyCreditsLuaCommand"] = new(
                "EconomyService", "BuildDrainEnemyCreditsLuaCommand", BuildLuaCommandPath.RealBridge,
                "Drain every non-local slot's credits to zero.",
                new[] { "SWFOC_DrainEnemyCredits" }),
            ["EconomyService.BuildUncapCreditsLuaCommand"] = new(
                "EconomyService", "BuildUncapCreditsLuaCommand", BuildLuaCommandPath.RealBridge,
                "Disable the engine credit cap.",
                new[] { "SWFOC_UncapCredits" }),
            ["EconomyService.BuildGetMaxCreditsLuaCommand"] = new(
                "EconomyService", "BuildGetMaxCreditsLuaCommand", BuildLuaCommandPath.RealBridge,
                "Read the current credit cap.",
                new[] { "SWFOC_GetMaxCredits" }),
            ["EconomyService.BuildSetTechLuaCommand"] = new(
                "EconomyService", "BuildSetTechLuaCommand", BuildLuaCommandPath.RealBridge,
                "Write tech level, global or per-slot.",
                new[] { "SWFOC_SetTechLevel", "SWFOC_SetTechForSlot" }),
            ["EconomyService.BuildGetTechLuaCommand"] = new(
                "EconomyService", "BuildGetTechLuaCommand", BuildLuaCommandPath.RealBridge,
                "Read tech level, global or per-slot.",
                new[] { "SWFOC_GetTechForSlot" }),

            // --- GodModeService / OneHitKillService / HardpointService (combat bridge helpers) ---
            ["GodModeService.BuildGodModeLuaCommand"] = new(
                "GodModeService", "BuildGodModeLuaCommand", BuildLuaCommandPath.RealBridge,
                "Toggle GodMode (hardpoint-behavior sweep).",
                new[] { "SWFOC_GodMode" }),
            ["OneHitKillService.BuildOneHitKillLuaCommand"] = new(
                "OneHitKillService", "BuildOneHitKillLuaCommand", BuildLuaCommandPath.RealBridge,
                "Toggle OHK (SetHP detour flag).",
                new[] { "SWFOC_OneHitKill" }),
            ["HardpointService.BuildGetHardpointsLuaCommand"] = new(
                "HardpointService", "BuildGetHardpointsLuaCommand", BuildLuaCommandPath.RealBridge,
                "Read hardpoint table for a given obj_addr.",
                new[] { "SWFOC_GetHardpoints" }),

            // --- HeroRespawnService ---
            ["HeroRespawnService.BuildSetCustomRespawnLuaCommand"] = new(
                "HeroRespawnService", "BuildSetCustomRespawnLuaCommand", BuildLuaCommandPath.RealBridge,
                "Set hero respawn timer to a custom ms value.",
                new[] { "SWFOC_SetHeroRespawn" }),
            ["HeroRespawnService.BuildSetInstantRespawnLuaCommand"] = new(
                "HeroRespawnService", "BuildSetInstantRespawnLuaCommand", BuildLuaCommandPath.RealBridge,
                "Set hero respawn timer to 0 (instant).",
                new[] { "SWFOC_HeroInstantRespawn" }),

            // --- UnitInspectorService / DamageLogService / CrashAnalyzerService ---
            ["UnitInspectorService.BuildInspectUnitLuaCommand"] = new(
                "UnitInspectorService", "BuildInspectUnitLuaCommand", BuildLuaCommandPath.RealBridge,
                "Read selected-unit state (hull/shield/etc).",
                new[] { "SWFOC_InspectUnit" }),
            ["DamageLogService.BuildEventControlLuaCommand"] = new(
                "DamageLogService", "BuildEventControlLuaCommand", BuildLuaCommandPath.RealBridge,
                "Start/stop/drain the damage-event ring buffer.",
                new[] { "SWFOC_EventControl" }),
            ["CrashAnalyzerService.BuildCaptureSnapshotLuaCommand"] = new(
                "CrashAnalyzerService", "BuildCaptureSnapshotLuaCommand", BuildLuaCommandPath.RealBridge,
                "Capture a live SWFOCSNAPv2 blob to a file path.",
                new[] { "SWFOC_DumpState" }),

            // --- FactionSwitchService ---
            ["FactionSwitchService.BuildFactionSwitchLuaCommand"] = new(
                "FactionSwitchService", "BuildFactionSwitchLuaCommand", BuildLuaCommandPath.RealBridge,
                "Switch the human-controllable slot (v2 hardpoint path).",
                new[] { "SWFOC_SetHumanPlayer_v2" }),
            ["FactionSwitchService.BuildSetHumanPlayerSlotLuaCommand"] = new(
                "FactionSwitchService", "BuildSetHumanPlayerSlotLuaCommand", BuildLuaCommandPath.RealBridge,
                "Legacy byte-flip set-human-player path (pre-v2).",
                new[] { "SWFOC_SetHumanPlayer" }),

            // --- Maphack / RosterBrowser (RealEngine globals) ---
            ["MaphackService.BuildRevealAllLuaCommand"] = new(
                "MaphackService", "BuildRevealAllLuaCommand", BuildLuaCommandPath.RealEngine,
                "Reveal the map for the local player via FOWManager global.",
                new[] { "Find_Player", "FOWManager.Reveal_All" }),
            ["MaphackService.BuildUndoRevealLuaCommand"] = new(
                "MaphackService", "BuildUndoRevealLuaCommand", BuildLuaCommandPath.RealEngine,
                "Undo a prior reveal for the local player.",
                new[] { "Find_Player", "FOWManager.Undo_Reveal_All" }),
            ["RosterBrowserService.BuildDiscoverTypesLuaCommand"] = new(
                "RosterBrowserService", "BuildDiscoverTypesLuaCommand", BuildLuaCommandPath.RealEngine,
                "Probe the engine's Object_Type table via SWFOC_Log.",
                new[] { "SWFOC_Log" }),
            ["CorruptionService.BuildCorruptionLuaCommand"] = new(
                "CorruptionService", "BuildCorruptionLuaCommand", BuildLuaCommandPath.RealEngine,
                "Apply a corruption event via Story_Event(CORRUPTION_*).",
                new[] { "Story_Event" }),
            ["CorruptionService.BuildRemoveCorruptionLuaCommand"] = new(
                "CorruptionService", "BuildRemoveCorruptionLuaCommand", BuildLuaCommandPath.RealEngine,
                "Remove corruption via Story_Event(REMOVE_CORRUPTION_*).",
                new[] { "Story_Event" }),
            ["FactionDashboardService.BuildFactionQueryLuaCommand"] = new(
                "FactionDashboardService", "BuildFactionQueryLuaCommand", BuildLuaCommandPath.RealEngine,
                "Query a faction's credits via Find_Player:Get_Credits.",
                new[] { "Find_Player", "Get_Credits" }),
            ["FleetManagerService.BuildAssembleFleetLuaCommand"] = new(
                "FleetManagerService", "BuildAssembleFleetLuaCommand", BuildLuaCommandPath.RealEngine,
                "Call engine Assemble_Fleet global with faction + planet.",
                new[] { "Assemble_Fleet", "Find_Player", "FindPlanet" }),
            ["OwnershipTransferService.BuildOwnershipLuaCommand"] = new(
                "OwnershipTransferService", "BuildOwnershipLuaCommand", BuildLuaCommandPath.RealEngine,
                "Change object owner via engine Change_Owner method.",
                new[] { "Find_First_Object", "Change_Owner", "Find_Player" }),
            ["PlanetManagerService.BuildSetPlanetOwnerLuaCommand"] = new(
                "PlanetManagerService", "BuildSetPlanetOwnerLuaCommand", BuildLuaCommandPath.RealEngine,
                "Change planet owner via engine Change_Owner method.",
                new[] { "FindPlanet", "Change_Owner", "Find_Player" }),
            ["StoryEventService.BuildStoryEventLuaCommand"] = new(
                "StoryEventService", "BuildStoryEventLuaCommand", BuildLuaCommandPath.RealEngine,
                "Dispatch an arbitrary Story_Event by id.",
                new[] { "Story_Event" }),
            ["EnhancedSpawnService.BuildSpawnLuaCommand"] = new(
                "EnhancedSpawnService", "BuildSpawnLuaCommand", BuildLuaCommandPath.RealEngine,
                "Spawn a unit via engine Spawn_Unit/Reinforce_Unit globals.",
                new[] { "Spawn_Unit", "Find_Player", "Find_Object_Type", "Create_Position" }),

            // --- AiControlService: 2 real Lua branches + 2 TODO branches (PartialStub) ---
            ["AiControlService.BuildAiLuaCommand"] = new(
                "AiControlService", "BuildAiLuaCommand", BuildLuaCommandPath.PartialStub,
                "Suspend/Resume AI (real) + PreventUsage/SetDifficulty (stubs with leading `-- ` comment).",
                new[] { "Suspend_AI" }),

            // --- CooldownManagerService: SelectedUnit real, AllPlayerUnits stub ---
            ["CooldownManagerService.BuildCooldownResetLuaCommand"] = new(
                "CooldownManagerService", "BuildCooldownResetLuaCommand", BuildLuaCommandPath.PartialStub,
                "SelectedUnit: Find_First_Object:Reset_Ability_Counter; AllPlayerUnits: TODO-stub.",
                new[] { "Find_First_Object", "Reset_Ability_Counter" }),

            // --- CameraDirectorService: engine camera-control globals (RealEngine) ---
            ["CameraDirectorService.BuildCameraLuaCommand"] = new(
                "CameraDirectorService", "BuildCameraLuaCommand", BuildLuaCommandPath.RealEngine,
                "Drive engine camera/pacing globals: Zoom/Rotate/Point/Scroll/LetterBox/Game_Set_Speed.",
                new[] {
                    "Zoom_Camera", "Rotate_Camera_By", "Point_Camera_At", "Scroll_Camera_To",
                    "Letter_Box_On", "Letter_Box_Off", "Game_Set_Speed"
                }),

            // --- DiplomacyService: engine Make_Ally/Make_Enemy via PlayerWrapper (RealEngine) ---
            ["DiplomacyService.BuildDiplomacyLuaCommand"] = new(
                "DiplomacyService", "BuildDiplomacyLuaCommand", BuildLuaCommandPath.RealEngine,
                "Set relation via PlayerWrapper:Make_Ally / :Make_Enemy (Neutral returns null — not supported by game API).",
                new[] { "Find_Player", "Make_Ally", "Make_Enemy" }),
        };

    /// <summary>
    /// Returns only entries whose <see cref="BuildLuaCommandInfo.Path"/> is
    /// <see cref="BuildLuaCommandPath.RealBridge"/> — i.e. those that hit
    /// our registered bridge helper table. Useful for capability probes
    /// that want to check "does the bridge support every Lua function
    /// this editor build claims to use?".
    /// </summary>
    public static IEnumerable<BuildLuaCommandInfo> RealBridgeEntries() =>
        Entries.Values.Where(e => e.Path == BuildLuaCommandPath.RealBridge);

    /// <summary>
    /// Returns only entries that are <see cref="BuildLuaCommandPath.Stub"/>
    /// or <see cref="BuildLuaCommandPath.PartialStub"/>. The Router uses
    /// this list to warn / disable UI buttons backed by incomplete
    /// services.
    /// </summary>
    public static IEnumerable<BuildLuaCommandInfo> StubEntries() =>
        Entries.Values.Where(e =>
            e.Path == BuildLuaCommandPath.Stub ||
            e.Path == BuildLuaCommandPath.PartialStub);
}
