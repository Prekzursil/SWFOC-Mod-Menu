using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

// === Wave 1 ===

/// <summary>
/// Loads game entity roster data for the v5 roster browser.
/// </summary>
public interface IRosterBrowserService
{
    /// <summary>
    /// Loads the full roster of game entities for a profile.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Roster entries resolved from profile data.</returns>
    Task<IReadOnlyList<RosterBrowserEntry>> LoadRosterAsync(
        string profileId, CancellationToken cancellationToken);

    /// <inheritdoc cref="LoadRosterAsync(string, CancellationToken)"/>
    Task<IReadOnlyList<RosterBrowserEntry>> LoadRosterAsync(string profileId)
    {
        return LoadRosterAsync(profileId, CancellationToken.None);
    }
}

/// <summary>
/// Captures faction dashboard snapshots for the v5 faction overview.
/// </summary>
public interface IFactionDashboardService
{
    /// <summary>
    /// Captures dashboard snapshots for all known factions in the active profile.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Point-in-time snapshots per faction.</returns>
    Task<IReadOnlyList<FactionDashboardSnapshot>> CaptureSnapshotsAsync(
        string profileId, CancellationToken cancellationToken);

    /// <inheritdoc cref="CaptureSnapshotsAsync(string, CancellationToken)"/>
    Task<IReadOnlyList<FactionDashboardSnapshot>> CaptureSnapshotsAsync(string profileId)
    {
        return CaptureSnapshotsAsync(profileId, CancellationToken.None);
    }
}

/// <summary>
/// Executes v5 enhanced spawn requests with cross-faction and batch support.
/// </summary>
public interface IEnhancedSpawnService
{
    /// <summary>
    /// Executes an enhanced spawn batch for the given request.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="request">Enhanced spawn request parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Batch execution result with per-item diagnostics.</returns>
    Task<EnhancedSpawnBatchResult> ExecuteSpawnAsync(
        string profileId, EnhancedSpawnRequest request, CancellationToken cancellationToken);

    /// <inheritdoc cref="ExecuteSpawnAsync(string, EnhancedSpawnRequest, CancellationToken)"/>
    Task<EnhancedSpawnBatchResult> ExecuteSpawnAsync(
        string profileId, EnhancedSpawnRequest request)
    {
        return ExecuteSpawnAsync(profileId, request, CancellationToken.None);
    }
}

/// <summary>
/// Captures live inspector snapshots of the currently selected unit.
/// </summary>
public interface ILiveInspectorService
{
    /// <summary>
    /// Captures a snapshot of the currently selected unit.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Inspector snapshot, or null if no unit is selected.</returns>
    Task<InspectorSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="CaptureSnapshotAsync(CancellationToken)"/>
    Task<InspectorSnapshot?> CaptureSnapshotAsync()
    {
        return CaptureSnapshotAsync(CancellationToken.None);
    }
}

// === Wave 2 ===

/// <summary>
/// Transfers ownership of units and planets between factions.
/// </summary>
public interface IOwnershipTransferService
{
    /// <summary>
    /// Transfers ownership according to the specified request.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="request">Transfer parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    Task<ActionExecutionResult> TransferOwnershipAsync(
        string profileId, OwnershipTransferRequest request, CancellationToken cancellationToken);

    /// <inheritdoc cref="TransferOwnershipAsync(string, OwnershipTransferRequest, CancellationToken)"/>
    Task<ActionExecutionResult> TransferOwnershipAsync(
        string profileId, OwnershipTransferRequest request)
    {
        return TransferOwnershipAsync(profileId, request, CancellationToken.None);
    }
}

/// <summary>
/// Manages galactic map planet data.
/// </summary>
public interface IPlanetManagerService
{
    /// <summary>
    /// Loads planet information for all planets in the active game.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Planet information list.</returns>
    Task<IReadOnlyList<PlanetInfo>> LoadPlanetsAsync(
        string profileId, CancellationToken cancellationToken);

    /// <inheritdoc cref="LoadPlanetsAsync(string, CancellationToken)"/>
    Task<IReadOnlyList<PlanetInfo>> LoadPlanetsAsync(string profileId)
    {
        return LoadPlanetsAsync(profileId, CancellationToken.None);
    }

    /// <summary>
    /// Sets the owner faction for a specific planet.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="planetId">Target planet identifier.</param>
    /// <param name="newOwner">New owner faction name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    Task<ActionExecutionResult> SetPlanetOwnerAsync(
        string profileId, string planetId, string newOwner, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetPlanetOwnerAsync(string, string, string, CancellationToken)"/>
    Task<ActionExecutionResult> SetPlanetOwnerAsync(
        string profileId, string planetId, string newOwner)
    {
        return SetPlanetOwnerAsync(profileId, planetId, newOwner, CancellationToken.None);
    }
}

/// <summary>
/// Reads fleet composition data from the galactic map.
/// </summary>
public interface IFleetManagerService
{
    /// <summary>
    /// Loads all fleets for the active game session.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Fleet information list.</returns>
    Task<IReadOnlyList<FleetInfo>> LoadFleetsAsync(
        string profileId, CancellationToken cancellationToken);

    /// <inheritdoc cref="LoadFleetsAsync(string, CancellationToken)"/>
    Task<IReadOnlyList<FleetInfo>> LoadFleetsAsync(string profileId)
    {
        return LoadFleetsAsync(profileId, CancellationToken.None);
    }
}

/// <summary>
/// Switches the player-controlled faction during galactic conquest.
/// </summary>
public interface IFactionSwitchService
{
    /// <summary>
    /// Switches the active player to the specified faction.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="request">Faction switch parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    Task<ActionExecutionResult> SwitchFactionAsync(
        string profileId, FactionSwitchRequest request, CancellationToken cancellationToken);

    /// <inheritdoc cref="SwitchFactionAsync(string, FactionSwitchRequest, CancellationToken)"/>
    Task<ActionExecutionResult> SwitchFactionAsync(
        string profileId, FactionSwitchRequest request)
    {
        return SwitchFactionAsync(profileId, request, CancellationToken.None);
    }
}

// === Wave 3 ===

/// <summary>
/// Controls AI behavior for factions and units.
/// </summary>
public interface IAiControlService
{
    /// <summary>
    /// Executes an AI control operation.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="request">AI control parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    Task<ActionExecutionResult> ExecuteAiControlAsync(
        string profileId, AiControlRequest request, CancellationToken cancellationToken);

    /// <inheritdoc cref="ExecuteAiControlAsync(string, AiControlRequest, CancellationToken)"/>
    Task<ActionExecutionResult> ExecuteAiControlAsync(
        string profileId, AiControlRequest request)
    {
        return ExecuteAiControlAsync(profileId, request, CancellationToken.None);
    }
}

/// <summary>
/// Resets ability cooldowns on units.
/// </summary>
public interface ICooldownManagerService
{
    /// <summary>
    /// Resets cooldowns according to the specified scope.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="request">Cooldown reset parameters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    Task<ActionExecutionResult> ResetCooldownsAsync(
        string profileId, CooldownResetRequest request, CancellationToken cancellationToken);

    /// <inheritdoc cref="ResetCooldownsAsync(string, CooldownResetRequest, CancellationToken)"/>
    Task<ActionExecutionResult> ResetCooldownsAsync(
        string profileId, CooldownResetRequest request)
    {
        return ResetCooldownsAsync(profileId, request, CancellationToken.None);
    }
}

// === Wave 4 ===

/// <summary>
/// Controls camera path recording and playback for content creation.
/// </summary>
public interface ICameraDirectorService
{
    /// <summary>
    /// Executes a camera director command (e.g., capture keyframe, play, stop).
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="command">Camera command string.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    Task<ActionExecutionResult> ExecuteCameraCommandAsync(
        string profileId, string command, CancellationToken cancellationToken);

    /// <inheritdoc cref="ExecuteCameraCommandAsync(string, string, CancellationToken)"/>
    Task<ActionExecutionResult> ExecuteCameraCommandAsync(
        string profileId, string command)
    {
        return ExecuteCameraCommandAsync(profileId, command, CancellationToken.None);
    }
}

/// <summary>
/// Manages story event catalogue and triggering.
/// </summary>
public interface IStoryEventService
{
    /// <summary>
    /// Loads all available story events for the active profile.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Story event catalogue entries.</returns>
    Task<IReadOnlyList<StoryEventEntry>> LoadEventsAsync(
        string profileId, CancellationToken cancellationToken);

    /// <inheritdoc cref="LoadEventsAsync(string, CancellationToken)"/>
    Task<IReadOnlyList<StoryEventEntry>> LoadEventsAsync(string profileId)
    {
        return LoadEventsAsync(profileId, CancellationToken.None);
    }

    /// <summary>
    /// Fires a story event by identifier.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="eventId">Event identifier to trigger.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    Task<ActionExecutionResult> FireEventAsync(
        string profileId, string eventId, CancellationToken cancellationToken);

    /// <inheritdoc cref="FireEventAsync(string, string, CancellationToken)"/>
    Task<ActionExecutionResult> FireEventAsync(
        string profileId, string eventId)
    {
        return FireEventAsync(profileId, eventId, CancellationToken.None);
    }
}

// === Wave 5 ===

/// <summary>
/// Detects conflicts between loaded mods.
/// </summary>
public interface IModConflictDetectorService
{
    /// <summary>
    /// Scans the provided mod paths for conflicting entity definitions.
    /// </summary>
    /// <param name="modPaths">Paths to mod directories to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Detected conflict entries.</returns>
    Task<IReadOnlyList<ModConflictEntry>> DetectConflictsAsync(
        IReadOnlyList<string> modPaths, CancellationToken cancellationToken);

    /// <inheritdoc cref="DetectConflictsAsync(IReadOnlyList{string}, CancellationToken)"/>
    Task<IReadOnlyList<ModConflictEntry>> DetectConflictsAsync(
        IReadOnlyList<string> modPaths)
    {
        return DetectConflictsAsync(modPaths, CancellationToken.None);
    }
}

/// <summary>
/// Captures and analyzes combat damage log data.
/// </summary>
public interface IDamageLogService
{
    /// <summary>
    /// Polls for new damage log entries since the last poll.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>New damage log entries.</returns>
    Task<IReadOnlyList<DamageLogEntry>> PollEntriesAsync(CancellationToken cancellationToken);

    /// <inheritdoc cref="PollEntriesAsync(CancellationToken)"/>
    Task<IReadOnlyList<DamageLogEntry>> PollEntriesAsync()
    {
        return PollEntriesAsync(CancellationToken.None);
    }

    /// <summary>
    /// Computes aggregate battle statistics from a set of damage log entries.
    /// </summary>
    /// <param name="entries">Damage log entries to summarize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Battle statistics summary.</returns>
    Task<BattleStatsSummary> ComputeSummaryAsync(
        IReadOnlyList<DamageLogEntry> entries, CancellationToken cancellationToken);

    /// <inheritdoc cref="ComputeSummaryAsync(IReadOnlyList{DamageLogEntry}, CancellationToken)"/>
    Task<BattleStatsSummary> ComputeSummaryAsync(
        IReadOnlyList<DamageLogEntry> entries)
    {
        return ComputeSummaryAsync(entries, CancellationToken.None);
    }
}

// === Wave 6 ===

/// <summary>
/// Manages faction diplomacy relations.
/// </summary>
public interface IDiplomacyService
{
    /// <summary>
    /// Loads current diplomacy state for all faction pairs.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current diplomacy states.</returns>
    Task<IReadOnlyList<DiplomacyState>> LoadDiplomacyAsync(
        string profileId, CancellationToken cancellationToken);

    /// <inheritdoc cref="LoadDiplomacyAsync(string, CancellationToken)"/>
    Task<IReadOnlyList<DiplomacyState>> LoadDiplomacyAsync(string profileId)
    {
        return LoadDiplomacyAsync(profileId, CancellationToken.None);
    }

    /// <summary>
    /// Sets the diplomacy relation between two factions.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="state">Desired diplomacy state.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    Task<ActionExecutionResult> SetRelationAsync(
        string profileId, DiplomacyState state, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetRelationAsync(string, DiplomacyState, CancellationToken)"/>
    Task<ActionExecutionResult> SetRelationAsync(
        string profileId, DiplomacyState state)
    {
        return SetRelationAsync(profileId, state, CancellationToken.None);
    }
}

/// <summary>
/// Manages planet corruption state.
/// </summary>
public interface ICorruptionService
{
    /// <summary>
    /// Sets corruption on a planet.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="entry">Corruption state to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    Task<ActionExecutionResult> SetCorruptionAsync(
        string profileId, CorruptionEntry entry, CancellationToken cancellationToken);

    /// <inheritdoc cref="SetCorruptionAsync(string, CorruptionEntry, CancellationToken)"/>
    Task<ActionExecutionResult> SetCorruptionAsync(
        string profileId, CorruptionEntry entry)
    {
        return SetCorruptionAsync(profileId, entry, CancellationToken.None);
    }

    /// <summary>
    /// Removes all corruption from a planet.
    /// </summary>
    /// <param name="profileId">Profile identifier.</param>
    /// <param name="planetId">Planet to clear corruption from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Execution result.</returns>
    Task<ActionExecutionResult> RemoveCorruptionAsync(
        string profileId, string planetId, CancellationToken cancellationToken);

    /// <inheritdoc cref="RemoveCorruptionAsync(string, string, CancellationToken)"/>
    Task<ActionExecutionResult> RemoveCorruptionAsync(
        string profileId, string planetId)
    {
        return RemoveCorruptionAsync(profileId, planetId, CancellationToken.None);
    }
}
