using System.Globalization;
using System.IO;
using System.Linq;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

public abstract class MainViewModelV5FeaturesBase : MainViewModelSaveOpsBase
{
    private protected readonly IRosterBrowserService _rosterBrowser;
    private protected readonly IFactionDashboardService _factionDashboard;
    private protected readonly IEnhancedSpawnService _enhancedSpawn;

    // Wave 2
    private protected readonly IOwnershipTransferService _ownershipTransfer;
    private protected readonly IPlanetManagerService _planetManager;
    private protected readonly IFleetManagerService _fleetManager;
    private protected readonly IFactionSwitchService _factionSwitch;

    // Wave 3
    private protected readonly IAiControlService _aiControl;
    private protected readonly ICooldownManagerService _cooldownManager;

    // Wave 4
    private protected readonly ICameraDirectorService _cameraDirector;
    private protected readonly IStoryEventService _storyEvents;

    // Wave 5
    private protected readonly IModConflictDetectorService _modConflicts;
    private protected readonly IDamageLogService _damageLog;

    // Wave 6
    private protected readonly IDiplomacyService _diplomacy;
    private protected readonly ICorruptionService _corruption;

    protected MainViewModelV5FeaturesBase(MainViewModelDependencies dependencies)
        : base(dependencies)
    {
        _rosterBrowser = dependencies.RosterBrowser;
        _factionDashboard = dependencies.FactionDashboard;
        _enhancedSpawn = dependencies.EnhancedSpawn;

        _ownershipTransfer = dependencies.OwnershipTransfer;
        _planetManager = dependencies.PlanetManager;
        _fleetManager = dependencies.FleetManager;
        _factionSwitch = dependencies.FactionSwitch;
        _aiControl = dependencies.AiControl;
        _cooldownManager = dependencies.CooldownManager;
        _cameraDirector = dependencies.CameraDirector;
        _storyEvents = dependencies.StoryEvents;
        _modConflicts = dependencies.ModConflicts;
        _damageLog = dependencies.DamageLog;
        _diplomacy = dependencies.Diplomacy;
        _corruption = dependencies.Corruption;
    }

    protected async Task LoadRosterAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        try
        {
            var entries = await _rosterBrowser.LoadRosterAsync(SelectedProfileId);
            RosterEntries.Clear();
            foreach (var entry in entries)
            {
                RosterEntries.Add(new RosterBrowserViewItem(
                    entry.EntityId,
                    entry.DisplayName,
                    entry.Faction,
                    entry.Category,
                    entry.Kind.ToString()));
            }

            ApplyRosterFilter();
            Status = $"Loaded {RosterEntries.Count} roster entries.";
        }
        catch (InvalidOperationException ex)
        {
            Status = $"Roster load failed: {ex.Message}";
        }
        catch (IOException ex)
        {
            Status = $"Roster load failed: {ex.Message}";
        }
    }

    protected async Task RefreshDashboardAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        try
        {
            var snapshots = await _factionDashboard.CaptureSnapshotsAsync(SelectedProfileId);
            FactionSnapshots.Clear();
            foreach (var snapshot in snapshots)
            {
                FactionSnapshots.Add(new FactionSnapshotViewItem(
                    snapshot.FactionName,
                    snapshot.Credits.ToString(CultureInfo.InvariantCulture),
                    snapshot.UnitCount.ToString(CultureInfo.InvariantCulture),
                    snapshot.PlanetCount.ToString(CultureInfo.InvariantCulture),
                    snapshot.TechLevel.ToString(CultureInfo.InvariantCulture),
                    snapshot.CapturedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)));
            }

            Status = $"Dashboard refreshed: {FactionSnapshots.Count} faction(s).";
        }
        catch (InvalidOperationException ex)
        {
            Status = $"Dashboard refresh failed: {ex.Message}";
        }
        catch (IOException ex)
        {
            Status = $"Dashboard refresh failed: {ex.Message}";
        }
    }

    protected async Task ExecuteEnhancedSpawnAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(EnhancedSpawnUnitId))
        {
            Status = "Enhanced spawn: Unit ID is required.";
            return;
        }

        if (!int.TryParse(EnhancedSpawnQuantity, CultureInfo.InvariantCulture, out var quantity) || quantity < 1)
        {
            Status = "Enhanced spawn: Quantity must be a positive integer.";
            return;
        }

        var mode = ResolveSpawnMode(EnhancedSpawnMode);

        var request = new EnhancedSpawnRequest(
            UnitId: EnhancedSpawnUnitId,
            TargetFaction: string.IsNullOrWhiteSpace(EnhancedSpawnFaction) ? "EMPIRE" : EnhancedSpawnFaction,
            Mode: mode,
            Quantity: quantity,
            PositionKind: SpawnPositionKind.AtCamera,
            TargetPlanet: null,
            AllowCrossFaction: true,
            StopOnFailure: true);

        try
        {
            var result = await _enhancedSpawn.ExecuteSpawnAsync(SelectedProfileId, request);
            Status = $"Spawn batch: attempted={result.Attempted}, succeeded={result.Succeeded}, failed={result.Failed}.";
        }
        catch (InvalidOperationException ex)
        {
            Status = $"Enhanced spawn failed: {ex.Message}";
        }
        catch (IOException ex)
        {
            Status = $"Enhanced spawn failed: {ex.Message}";
        }
    }

    protected void ApplyRosterFilter()
    {
        // No separate filtered collection for now — the DataGrid is bound directly
        // to RosterEntries. If a search query is set, rebuild with only matching items.
        // This keeps alignment with the existing pattern where filters clear/re-add.
    }

    // === Wave 2 command handlers ===

    protected async Task TransferOwnershipAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(TransferTargetFaction))
        {
            Status = "Ownership transfer: Target faction is required.";
            return;
        }

        try
        {
            Status = "Transferring ownership...";
            var request = new OwnershipTransferRequest(
                TargetId: string.Empty,
                NewOwnerFaction: TransferTargetFaction,
                Scope: OwnershipTransferScope.SelectedUnit);
            var result = await _ownershipTransfer.TransferOwnershipAsync(SelectedProfileId, request);
            Status = result.Succeeded
                ? $"Ownership transferred to {TransferTargetFaction}."
                : $"Ownership transfer failed: {result.Message}";
        }
        catch (InvalidOperationException ex) { Status = $"Ownership transfer failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Ownership transfer failed: {ex.Message}"; }
    }

    protected async Task LoadPlanetsAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        try
        {
            Status = "Loading planets...";
            var planets = await _planetManager.LoadPlanetsAsync(SelectedProfileId, CancellationToken.None);
            PlanetEntries.Clear();
            foreach (var planet in planets)
            {
                PlanetEntries.Add(new PlanetViewItem(
                    planet.PlanetId,
                    planet.DisplayName,
                    planet.OwnerFaction,
                    planet.SpaceStationLevel.ToString(CultureInfo.InvariantCulture),
                    string.Join(", ", planet.Buildings),
                    planet.CorruptionKind.ToString()));
            }

            Status = $"Loaded {PlanetEntries.Count} planets.";
        }
        catch (InvalidOperationException ex) { Status = $"Planet load failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Planet load failed: {ex.Message}"; }
    }

    protected async Task LoadFleetsAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        try
        {
            Status = "Loading fleets...";
            var fleets = await _fleetManager.LoadFleetsAsync(SelectedProfileId, CancellationToken.None);
            FleetEntries.Clear();
            foreach (var fleet in fleets)
            {
                var composition = string.Join(", ", fleet.Units.Select(u => $"{u.UnitType}x{u.Count}"));
                FleetEntries.Add(new FleetViewItem(
                    fleet.FleetId,
                    fleet.FactionName,
                    fleet.Location,
                    fleet.Units.Count.ToString(CultureInfo.InvariantCulture),
                    composition));
            }

            Status = $"Loaded {FleetEntries.Count} fleets.";
        }
        catch (InvalidOperationException ex) { Status = $"Fleet load failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Fleet load failed: {ex.Message}"; }
    }

    protected async Task SwitchFactionAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(SwitchFactionTarget))
        {
            Status = "Faction switch: Target faction is required.";
            return;
        }

        try
        {
            Status = $"Switching faction to {SwitchFactionTarget}...";
            var request = new FactionSwitchRequest(TargetFaction: SwitchFactionTarget);
            var result = await _factionSwitch.SwitchFactionAsync(SelectedProfileId, request);
            Status = result.Succeeded
                ? $"Switched to faction {SwitchFactionTarget}."
                : $"Faction switch failed: {result.Message}";
        }
        catch (InvalidOperationException ex) { Status = $"Faction switch failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Faction switch failed: {ex.Message}"; }
    }

    // === Wave 3 command handlers ===

    protected async Task ExecuteAiControlAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        try
        {
            int? suspendSeconds = null;
            if (int.TryParse(AiSuspendSeconds, CultureInfo.InvariantCulture, out var parsed))
            {
                suspendSeconds = parsed;
            }

            Status = "Executing AI control...";
            var request = new AiControlRequest(
                Action: AiControlAction.SuspendAll,
                SuspendSeconds: suspendSeconds,
                TargetUnitId: null,
                FactionId: null,
                Difficulty: null);
            var result = await _aiControl.ExecuteAiControlAsync(SelectedProfileId, request);
            Status = result.Succeeded
                ? "AI control applied."
                : $"AI control failed: {result.Message}";
        }
        catch (InvalidOperationException ex) { Status = $"AI control failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"AI control failed: {ex.Message}"; }
    }

    protected async Task ResetCooldownsAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        try
        {
            Status = "Resetting cooldowns...";
            var request = new CooldownResetRequest(
                Scope: string.IsNullOrWhiteSpace(CooldownTargetUnitId)
                    ? CooldownResetScope.AllPlayerUnits
                    : CooldownResetScope.SelectedUnit,
                UnitId: string.IsNullOrWhiteSpace(CooldownTargetUnitId) ? null : CooldownTargetUnitId);
            var result = await _cooldownManager.ResetCooldownsAsync(SelectedProfileId, request);
            Status = result.Succeeded
                ? "Cooldowns reset."
                : $"Cooldown reset failed: {result.Message}";
        }
        catch (InvalidOperationException ex) { Status = $"Cooldown reset failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Cooldown reset failed: {ex.Message}"; }
    }

    // === Wave 4 command handlers ===

    protected async Task ExecuteCameraCommandAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CameraCommand))
        {
            Status = "Camera director: Command is required.";
            return;
        }

        try
        {
            Status = $"Executing camera command: {CameraCommand}...";
            var result = await _cameraDirector.ExecuteCameraCommandAsync(SelectedProfileId, CameraCommand);
            Status = result.Succeeded
                ? $"Camera command executed: {CameraCommand}"
                : $"Camera command failed: {result.Message}";
        }
        catch (InvalidOperationException ex) { Status = $"Camera command failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Camera command failed: {ex.Message}"; }
    }

    protected async Task LoadStoryEventsAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        try
        {
            Status = "Loading story events...";
            var events = await _storyEvents.LoadEventsAsync(SelectedProfileId, CancellationToken.None);
            StoryEventEntries.Clear();
            foreach (var entry in events)
            {
                StoryEventEntries.Add(new StoryEventViewItem(
                    entry.EventId,
                    entry.DisplayName,
                    entry.Category,
                    entry.Source));
            }

            Status = $"Loaded {StoryEventEntries.Count} story events.";
        }
        catch (InvalidOperationException ex) { Status = $"Story event load failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Story event load failed: {ex.Message}"; }
    }

    protected async Task FireStoryEventAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        var eventId = !string.IsNullOrWhiteSpace(CustomStoryEvent) ? CustomStoryEvent : SelectedStoryEvent;
        if (string.IsNullOrWhiteSpace(eventId))
        {
            Status = "Story events: Select or enter an event ID.";
            return;
        }

        try
        {
            Status = $"Firing story event: {eventId}...";
            var result = await _storyEvents.FireEventAsync(SelectedProfileId, eventId);
            Status = result.Succeeded
                ? $"Story event fired: {eventId}"
                : $"Story event failed: {result.Message}";
        }
        catch (InvalidOperationException ex) { Status = $"Story event failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Story event failed: {ex.Message}"; }
    }

    // === Wave 5 command handlers ===

    protected async Task DetectModConflictsAsync()
    {
        try
        {
            Status = "Detecting mod conflicts...";
            var modPaths = new List<string>();
            if (!string.IsNullOrWhiteSpace(LaunchModPath))
            {
                modPaths.Add(LaunchModPath);
            }

            var conflicts = await _modConflicts.DetectConflictsAsync(modPaths, CancellationToken.None);
            Status = conflicts.Count == 0
                ? "No mod conflicts detected."
                : $"Detected {conflicts.Count} mod conflict(s).";
        }
        catch (InvalidOperationException ex) { Status = $"Mod conflict detection failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Mod conflict detection failed: {ex.Message}"; }
    }

    protected async Task RefreshDamageLogAsync()
    {
        try
        {
            Status = "Refreshing damage log...";
            var entries = await _damageLog.PollEntriesAsync(CancellationToken.None);
            DamageLogEntries.Clear();
            foreach (var entry in entries)
            {
                DamageLogEntries.Add(new DamageLogViewItem(
                    entry.Timestamp.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture),
                    entry.SourceUnit,
                    entry.TargetUnit,
                    entry.DamageAmount.ToString(DecimalPrecision3, CultureInfo.InvariantCulture),
                    entry.DamageType));
            }

            Status = $"Damage log: {DamageLogEntries.Count} entries.";
        }
        catch (InvalidOperationException ex) { Status = $"Damage log refresh failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Damage log refresh failed: {ex.Message}"; }
    }

    // === Wave 6 command handlers ===

    protected async Task LoadDiplomacyAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        try
        {
            Status = "Loading diplomacy state...";
            var states = await _diplomacy.LoadDiplomacyAsync(SelectedProfileId, CancellationToken.None);
            Status = $"Loaded {states.Count} diplomacy relation(s).";
        }
        catch (InvalidOperationException ex) { Status = $"Diplomacy load failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Diplomacy load failed: {ex.Message}"; }
    }

    protected async Task SetDiplomacyRelationAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        try
        {
            Status = "Setting diplomacy relation...";
            var state = new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Allied);
            var result = await _diplomacy.SetRelationAsync(SelectedProfileId, state);
            Status = result.Succeeded
                ? "Diplomacy relation set."
                : $"Diplomacy set failed: {result.Message}";
        }
        catch (InvalidOperationException ex) { Status = $"Diplomacy set failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Diplomacy set failed: {ex.Message}"; }
    }

    protected async Task SetCorruptionAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CorruptionPlanetId))
        {
            Status = "Corruption: Planet ID is required.";
            return;
        }

        try
        {
            Status = $"Setting corruption on {CorruptionPlanetId}...";
            var corruptionType = Enum.TryParse<SwfocTrainer.Core.Models.CorruptionType>(CorruptionType, ignoreCase: true, out var parsed)
                ? parsed
                : SwfocTrainer.Core.Models.CorruptionType.Racketeering;
            var entry = new CorruptionEntry(CorruptionPlanetId, corruptionType, 1);
            var result = await _corruption.SetCorruptionAsync(SelectedProfileId, entry);
            Status = result.Succeeded
                ? $"Corruption set on {CorruptionPlanetId}."
                : $"Corruption set failed: {result.Message}";
        }
        catch (InvalidOperationException ex) { Status = $"Corruption set failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Corruption set failed: {ex.Message}"; }
    }

    protected async Task RemoveCorruptionAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(CorruptionPlanetId))
        {
            Status = "Corruption: Planet ID is required.";
            return;
        }

        try
        {
            Status = $"Removing corruption from {CorruptionPlanetId}...";
            var result = await _corruption.RemoveCorruptionAsync(SelectedProfileId, CorruptionPlanetId);
            Status = result.Succeeded
                ? $"Corruption removed from {CorruptionPlanetId}."
                : $"Corruption removal failed: {result.Message}";
        }
        catch (InvalidOperationException ex) { Status = $"Corruption removal failed: {ex.Message}"; }
        catch (IOException ex) { Status = $"Corruption removal failed: {ex.Message}"; }
    }

    private static SpawnMode ResolveSpawnMode(string modeText)
    {
        if (string.IsNullOrWhiteSpace(modeText))
        {
            return SpawnMode.Tactical;
        }

        if (modeText.Equals("Reinforcement", StringComparison.OrdinalIgnoreCase))
        {
            return SpawnMode.Reinforcement;
        }

        if (modeText.Equals("GalacticPersistent", StringComparison.OrdinalIgnoreCase)
            || modeText.Equals("Galactic", StringComparison.OrdinalIgnoreCase))
        {
            return SpawnMode.GalacticPersistent;
        }

        return SpawnMode.Tactical;
    }
}
