using System.Collections.Generic;

namespace SwfocTrainer.Core.Models;

// === V5 Enums ===

/// <summary>
/// Enhanced spawn placement mode for v5 spawn workflows.
/// </summary>
public enum SpawnMode
{
    Tactical = 0,
    Reinforcement = 1,
    GalacticPersistent = 2
}

/// <summary>
/// Determines how spawn position is resolved.
/// </summary>
public enum SpawnPositionKind
{
    AtCamera = 0,
    AtSelectedUnit = 1,
    AtEntryMarker = 2,
    CustomXyz = 3
}

/// <summary>
/// Scope for stat editing operations.
/// </summary>
public enum StatEditScope
{
    SelectedUnit = 0,
    AllOfType = 1,
    AllOfFaction = 2
}

/// <summary>
/// AI control operations.
/// </summary>
public enum AiControlAction
{
    SuspendAll = 0,
    ResumeAll = 1,
    PreventUsage = 2,
    SetDifficulty = 3
}

/// <summary>
/// Scope for cooldown reset operations.
/// </summary>
public enum CooldownResetScope
{
    SelectedUnit = 0,
    AllPlayerUnits = 1
}

/// <summary>
/// Scope for ownership transfer operations.
/// </summary>
public enum OwnershipTransferScope
{
    SelectedUnit = 0,
    AllOfType = 1,
    AllVisible = 2,
    Planet = 3
}

/// <summary>
/// Corruption type for galactic conquest planets.
/// </summary>
public enum CorruptionType
{
    None = 0,
    Racketeering = 1,
    Bribery = 2,
    Piracy = 3,
    Kidnapping = 4,
    Sabotage = 5
}

/// <summary>
/// Diplomacy relation between two factions.
/// </summary>
public enum DiplomacyRelation
{
    Hostile = 0,
    Allied = 1,
    Neutral = 2
}

// === Wave 1: Foundation UI ===

/// <summary>
/// Entry in the v5 roster browser showing a game entity.
/// </summary>
public sealed record RosterBrowserEntry(
    string EntityId,
    string DisplayName,
    string Faction,
    string Category,
    RosterEntityKind Kind);

/// <summary>
/// Point-in-time snapshot of a single faction's dashboard metrics.
/// </summary>
public sealed record FactionDashboardSnapshot(
    string FactionName,
    int Credits,
    int UnitCount,
    int PlanetCount,
    int TechLevel,
    DateTimeOffset CapturedAt);

/// <summary>
/// V5 enhanced spawn request with cross-faction and batch controls.
/// </summary>
public sealed record EnhancedSpawnRequest(
    string UnitId,
    string TargetFaction,
    SpawnMode Mode,
    int Quantity,
    SpawnPositionKind PositionKind,
    string? TargetPlanet,
    bool AllowCrossFaction,
    bool StopOnFailure);

/// <summary>
/// Aggregate result for a v5 enhanced spawn batch execution.
/// </summary>
public sealed record EnhancedSpawnBatchResult(
    int Attempted,
    int Succeeded,
    int Failed,
    IReadOnlyList<string> Errors);

/// <summary>
/// Point-in-time snapshot of the live inspector panel for a selected unit.
/// </summary>
public sealed record InspectorSnapshot(
    string UnitName,
    string UnitType,
    string Faction,
    float Hp,
    float MaxHp,
    float ShieldPercent,
    float Speed,
    bool IsInvulnerable,
    bool IsKillProtected,
    DateTimeOffset CapturedAt);

// === Wave 2: Galactic Map ===

/// <summary>
/// Request to transfer ownership of units or planets.
/// </summary>
public sealed record OwnershipTransferRequest(
    string TargetId,
    string NewOwnerFaction,
    OwnershipTransferScope Scope);

/// <summary>
/// Information about a planet on the galactic map.
/// </summary>
public sealed record PlanetInfo(
    string PlanetId,
    string DisplayName,
    string OwnerFaction,
    int SpaceStationLevel,
    IReadOnlyList<string> Buildings,
    int CorruptionLevel,
    CorruptionType CorruptionKind);

/// <summary>
/// Information about a fleet on the galactic map.
/// </summary>
public sealed record FleetInfo(
    string FleetId,
    string FactionName,
    string Location,
    IReadOnlyList<FleetUnitEntry> Units);

/// <summary>
/// Unit composition entry within a fleet.
/// </summary>
public sealed record FleetUnitEntry(
    string UnitType,
    int Count);

/// <summary>
/// Request to switch the player-controlled faction.
/// </summary>
public sealed record FactionSwitchRequest(
    string TargetFaction);

// === Wave 3: Combat ===

/// <summary>
/// Request to edit a stat field on one or more units.
/// </summary>
public sealed record ExtendedUnitStatEdit(
    string Field,
    string Value,
    StatEditScope Scope);

/// <summary>
/// Request to control AI behavior.
/// </summary>
public sealed record AiControlRequest(
    AiControlAction Action,
    int? SuspendSeconds,
    string? TargetUnitId,
    string? FactionId,
    int? Difficulty);

/// <summary>
/// Request to reset ability cooldowns.
/// </summary>
public sealed record CooldownResetRequest(
    CooldownResetScope Scope,
    string? UnitId);

/// <summary>
/// Current toggle state for orbital bombardment controls per faction.
/// </summary>
public sealed record OrbitalToggleState(
    string FactionId,
    bool BombingRunDisabled,
    bool OrbitalBombardmentDisabled);

// === Wave 4: Content Creation ===

/// <summary>
/// Single keyframe in a camera director path.
/// </summary>
public sealed record CameraKeyframe(
    int Index,
    float X,
    float Y,
    float Z,
    float Zoom,
    float Rotation,
    DateTimeOffset CapturedAt);

/// <summary>
/// Playback plan for camera director interpolation.
/// </summary>
public sealed record CameraPlaybackPlan(
    IReadOnlyList<CameraKeyframe> Keyframes,
    int InterpolationStepMs,
    bool LetterboxEnabled);

/// <summary>
/// Catalogue entry for a triggerable story event.
/// </summary>
public sealed record StoryEventEntry(
    string EventId,
    string DisplayName,
    string Source,
    string Category);

/// <summary>
/// Catalogue entry for a music track or sound event.
/// </summary>
public sealed record MusicTrackEntry(
    string EventName,
    string Category,
    string? Description);

// === Wave 5: Modding ===

/// <summary>
/// Saved Lua script entry for the script runner.
/// </summary>
public sealed record LuaScriptEntry(
    string Name,
    string Code,
    string Category,
    DateTimeOffset CreatedAt);

/// <summary>
/// Detected conflict between two mod sources.
/// </summary>
public sealed record ModConflictEntry(
    string EntityId,
    string ModSource1,
    string ModSource2,
    string ConflictType,
    string Details);

/// <summary>
/// Single entry in the combat damage log.
/// </summary>
public sealed record DamageLogEntry(
    string SourceUnit,
    string TargetUnit,
    float DamageAmount,
    string DamageType,
    DateTimeOffset Timestamp);

/// <summary>
/// Aggregate battle statistics computed from damage log entries.
/// </summary>
public sealed record BattleStatsSummary(
    string MvpUnit,
    IReadOnlyDictionary<string, float> DamagePerFaction,
    IReadOnlyDictionary<string, int> KillsPerFaction,
    TimeSpan BattleDuration);

// === Wave 6: Specialized ===

/// <summary>
/// Diplomacy relation state between two factions.
/// </summary>
public sealed record DiplomacyState(
    string Faction1,
    string Faction2,
    DiplomacyRelation Relation);

/// <summary>
/// Corruption status for a planet.
/// </summary>
public sealed record CorruptionEntry(
    string PlanetId,
    CorruptionType Type,
    int Level);

/// <summary>
/// Veterancy level for a unit.
/// </summary>
public sealed record VeterancyEntry(
    string UnitId,
    int CurrentLevel,
    int MaxLevel);

/// <summary>
/// Named hint location on the game map.
/// </summary>
public sealed record MapHintEntry(
    string HintName,
    float X,
    float Y,
    float Z,
    string Category);
