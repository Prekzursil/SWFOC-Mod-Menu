namespace SwfocTrainer.App.Models;

/// <summary>
/// UI model for the v5 roster browser grid.
/// </summary>
public sealed record RosterBrowserViewItem(
    string EntityId,
    string DisplayName,
    string Faction,
    string Category,
    string KindLabel);

/// <summary>
/// UI model for the faction dashboard snapshot display.
/// </summary>
public sealed record FactionSnapshotViewItem(
    string FactionName,
    string Credits,
    string UnitCount,
    string PlanetCount,
    string TechLevel,
    string LastUpdated);

/// <summary>
/// UI model for the live inspector panel.
/// </summary>
public sealed record InspectorViewItem(
    string UnitName,
    string UnitType,
    string Faction,
    string Hp,
    string Shield,
    string Speed,
    string Invulnerable,
    string KillProtected);

/// <summary>
/// UI model for the galactic map planet list.
/// </summary>
public sealed record PlanetViewItem(
    string PlanetId,
    string DisplayName,
    string Owner,
    string StationLevel,
    string Buildings,
    string Corruption);

/// <summary>
/// UI model for the fleet overview list.
/// </summary>
public sealed record FleetViewItem(
    string FleetId,
    string Faction,
    string Location,
    string UnitCount,
    string Composition);

/// <summary>
/// UI model for the damage log table.
/// </summary>
public sealed record DamageLogViewItem(
    string Timestamp,
    string Source,
    string Target,
    string Damage,
    string Type);

/// <summary>
/// UI model for the story event catalogue.
/// </summary>
public sealed record StoryEventViewItem(
    string EventId,
    string DisplayName,
    string Category,
    string Source);

/// <summary>
/// UI model for map hint entries.
/// </summary>
public sealed record MapHintViewItem(
    string Name,
    string Position,
    string Category);
