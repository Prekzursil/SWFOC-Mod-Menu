using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;

namespace SwfocTrainer.Runtime.Services;

public sealed class ModMechanicDetectionService : IModMechanicDetectionService
{
    private readonly ITransplantCompatibilityService? _transplantCompatibilityService;

    public ModMechanicDetectionService()
        : this(null)
    {
    }

    public ModMechanicDetectionService(ITransplantCompatibilityService? transplantCompatibilityService)
    {
        _transplantCompatibilityService = transplantCompatibilityService;
    }

    public async Task<ModMechanicReport> DetectAsync(
        TrainerProfile profile,
        AttachSession session,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var disabledActions = ParseCsvSet(session.Process.Metadata, "dependencyDisabledActions");
        var helperReady = string.Equals(
            ReadMetadataValue(session.Process.Metadata, "helperBridgeState"),
            "ready",
            StringComparison.OrdinalIgnoreCase);
        var dependenciesSatisfied = disabledActions.Count == 0;
        var activeWorkshopIds = ParseActiveWorkshopIds(session.Process);
        var rosterEntities = BuildRosterEntities(profile, catalog);

        TransplantValidationReport? transplantReport = null;
        if (_transplantCompatibilityService is not null)
        {
            try
            {
                transplantReport = await _transplantCompatibilityService
                    .ValidateAsync(profile.Id, activeWorkshopIds, rosterEntities, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                transplantReport = null;
            }
        }

        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["dependencyValidation"] = ReadMetadataValue(session.Process.Metadata, "dependencyValidation") ?? string.Empty,
            ["dependencyDisabledActions"] = disabledActions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            ["helperBridgeState"] = ReadMetadataValue(session.Process.Metadata, "helperBridgeState") ?? "unknown",
            ["unitCatalogCount"] = catalog is not null && catalog.TryGetValue("unit_catalog", out var units) ? units.Count : 0,
            ["factionCatalogCount"] = catalog is not null && catalog.TryGetValue("faction_catalog", out var factions) ? factions.Count : 0,
            ["buildingCatalogCount"] = catalog is not null && catalog.TryGetValue("building_catalog", out var buildings) ? buildings.Count : 0,
            ["activeWorkshopIds"] = activeWorkshopIds,
            ["transplantAllResolved"] = transplantReport?.AllResolved ?? true,
            ["transplantBlockingEntityCount"] = transplantReport?.BlockingEntityCount ?? 0,
            ["transplantEnabled"] = _transplantCompatibilityService is not null
        };
        if (transplantReport is not null)
        {
            diagnostics["transplantBlockingEntityIds"] = transplantReport.Entities
                .Where(static entity => !entity.Resolved)
                .Select(static entity => entity.EntityId)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var supports = new List<ModMechanicSupport>(profile.Actions.Count);
        foreach (var (actionId, action) in profile.Actions.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            supports.Add(EvaluateAction(
                actionId,
                action,
                profile,
                session,
                catalog,
                disabledActions,
                helperReady,
                transplantReport));
        }

        var report = new ModMechanicReport(
            ProfileId: profile.Id,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            DependenciesSatisfied: dependenciesSatisfied,
            HelperBridgeReady: helperReady,
            ActionSupport: supports,
            Diagnostics: diagnostics);

        return report;
    }

    private static ModMechanicSupport EvaluateAction(
        string actionId,
        ActionSpec action,
        TrainerProfile profile,
        AttachSession session,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
        IReadOnlySet<string> disabledActions,
        bool helperReady,
        TransplantValidationReport? transplantReport)
    {
        if (disabledActions.Contains(actionId))
        {
            return new ModMechanicSupport(
                ActionId: actionId,
                Supported: false,
                ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                Message: "Action is dependency-gated for the current workshop chain.",
                Confidence: 0.98d);
        }

        if (IsEntityOperationAction(actionId) &&
            transplantReport is not null &&
            !transplantReport.AllResolved)
        {
            var blocking = transplantReport.Entities
                .FirstOrDefault(static entity => !entity.Resolved);
            var blockingEntityId = blocking?.EntityId ?? "unknown_entity";
            var blockingReason = blocking?.ReasonCode.ToString() ?? RuntimeReasonCode.TRANSPLANT_VALIDATION_FAILED.ToString();
            return new ModMechanicSupport(
                ActionId: actionId,
                Supported: false,
                ReasonCode: RuntimeReasonCode.CROSS_MOD_TRANSPLANT_REQUIRED,
                Message: $"Cross-mod transplant validation failed for entity '{blockingEntityId}' (reason={blockingReason}).",
                Confidence: 0.99d);
        }

        if (action.ExecutionKind == ExecutionKind.Helper)
        {
            if (!helperReady)
            {
                return new ModMechanicSupport(
                    ActionId: actionId,
                    Supported: false,
                    ReasonCode: RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE,
                    Message: "Helper bridge is unavailable for this session.",
                    Confidence: 0.98d);
            }

            if (profile.HelperModHooks.Count == 0)
            {
                return new ModMechanicSupport(
                    ActionId: actionId,
                    Supported: false,
                    ReasonCode: RuntimeReasonCode.HELPER_ENTRYPOINT_NOT_FOUND,
                    Message: "Profile has helper actions but no helper hook metadata.",
                    Confidence: 0.98d);
            }

            return new ModMechanicSupport(
                ActionId: actionId,
                Supported: true,
                ReasonCode: RuntimeReasonCode.HELPER_EXECUTION_APPLIED,
                Message: "Helper bridge and hook metadata are available.",
                Confidence: 0.85d);
        }

        if (RequiresSpawnRoster(actionId))
        {
            var unitCount = catalog is not null && catalog.TryGetValue("unit_catalog", out var units)
                ? units.Count
                : 0;
            var factionCount = catalog is not null && catalog.TryGetValue("faction_catalog", out var factions)
                ? factions.Count
                : 0;
            if (unitCount == 0 || factionCount == 0)
            {
                return new ModMechanicSupport(
                    ActionId: actionId,
                    Supported: false,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                    Message: "Spawn roster catalog is unavailable for this profile/chain.",
                    Confidence: 0.95d);
            }
        }

        if (RequiresBuildingRoster(actionId))
        {
            var buildingCount = catalog is not null && catalog.TryGetValue("building_catalog", out var buildings)
                ? buildings.Count
                : 0;
            var factionCount = catalog is not null && catalog.TryGetValue("faction_catalog", out var factions)
                ? factions.Count
                : 0;
            if (buildingCount == 0 || factionCount == 0)
            {
                return new ModMechanicSupport(
                    ActionId: actionId,
                    Supported: false,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                    Message: "Building roster catalog is unavailable for this profile/chain.",
                    Confidence: 0.95d);
            }
        }

        if (IsContextFactionAction(actionId))
        {
            var hasTacticalOwner = TryGetHealthySymbol(session, "selected_owner_faction");
            var hasPlanetOwner = TryGetHealthySymbol(session, "planet_owner");
            if (!hasTacticalOwner && !hasPlanetOwner)
            {
                return new ModMechanicSupport(
                    ActionId: actionId,
                    Supported: false,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                    Message: "Neither selected-unit owner nor planet owner symbols are available.",
                    Confidence: 0.95d);
            }

            return new ModMechanicSupport(
                ActionId: actionId,
                Supported: true,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "Context faction/allegiance routing symbols are available.",
                Confidence: 0.85d);
        }

        if (ActionSymbolRegistry.TryGetSymbol(actionId, out var symbol))
        {
            if (!session.Symbols.TryGetValue(symbol, out var symbolInfo) ||
                symbolInfo is null ||
                symbolInfo.Address == nint.Zero ||
                symbolInfo.HealthStatus == SymbolHealthStatus.Unresolved)
            {
                return new ModMechanicSupport(
                    ActionId: actionId,
                    Supported: false,
                    ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                    Message: $"Symbol '{symbol}' is unresolved for this profile variant.",
                    Confidence: 0.95d);
            }
        }

        return new ModMechanicSupport(
            ActionId: actionId,
            Supported: true,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Message: "Mechanic prerequisites are available.",
            Confidence: 0.80d);
    }

    private static IReadOnlyList<string> ParseActiveWorkshopIds(ProcessMetadata process)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in process.LaunchContext?.SteamModIds ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(id))
            {
                values.Add(id.Trim());
            }
        }

        AddCsvValues(values, ReadMetadataValue(process.Metadata, "forcedWorkshopIds"));
        AddCsvValues(values, ReadMetadataValue(process.Metadata, "steamModIdsDetected"));

        return values.OrderBy(static id => id, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void AddCsvValues(ISet<string> sink, string? csv)
    {
        if (string.IsNullOrWhiteSpace(csv))
        {
            return;
        }

        foreach (var raw in csv.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!string.IsNullOrWhiteSpace(raw))
            {
                sink.Add(raw.Trim());
            }
        }
    }

    private static IReadOnlyList<RosterEntityRecord> BuildRosterEntities(
        TrainerProfile profile,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
    {
        if (catalog is null || !catalog.TryGetValue("entity_catalog", out var entries) || entries.Count == 0)
        {
            return Array.Empty<RosterEntityRecord>();
        }

        var defaultFaction = catalog.TryGetValue("faction_catalog", out var factions) && factions.Count > 0
            ? factions[0]
            : "Empire";
        var records = new List<RosterEntityRecord>(entries.Count);
        foreach (var raw in entries)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            var segments = raw.Split('|', StringSplitOptions.TrimEntries);
            if (segments.Length < 2 || string.IsNullOrWhiteSpace(segments[1]))
            {
                continue;
            }

            var kind = ParseEntityKind(segments[0]);
            var entityId = segments[1];
            var sourceProfileId = segments.Length >= 3 && !string.IsNullOrWhiteSpace(segments[2])
                ? segments[2]
                : profile.Id;
            var sourceWorkshopId = segments.Length >= 4 && !string.IsNullOrWhiteSpace(segments[3])
                ? segments[3]
                : profile.SteamWorkshopId;
            var visualRef = segments.Length >= 5 && !string.IsNullOrWhiteSpace(segments[4])
                ? segments[4]
                : null;
            var dependencies = segments.Length >= 6 && !string.IsNullOrWhiteSpace(segments[5])
                ? segments[5]
                    .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
                : Array.Empty<string>();
            var allowedModes = kind is RosterEntityKind.Building or RosterEntityKind.SpaceStructure
                ? new[] { RuntimeMode.Galactic }
                : new[] { RuntimeMode.AnyTactical, RuntimeMode.Galactic };

            records.Add(new RosterEntityRecord(
                EntityId: entityId,
                DisplayName: entityId,
                SourceProfileId: sourceProfileId,
                SourceWorkshopId: sourceWorkshopId,
                EntityKind: kind,
                DefaultFaction: defaultFaction,
                AllowedModes: allowedModes,
                VisualRef: visualRef,
                DependencyRefs: dependencies));
        }

        return records;
    }

    private static RosterEntityKind ParseEntityKind(string value)
    {
        if (value.Equals("Hero", StringComparison.OrdinalIgnoreCase))
        {
            return RosterEntityKind.Hero;
        }

        if (value.Equals("Building", StringComparison.OrdinalIgnoreCase))
        {
            return RosterEntityKind.Building;
        }

        if (value.Equals("SpaceStructure", StringComparison.OrdinalIgnoreCase))
        {
            return RosterEntityKind.SpaceStructure;
        }

        if (value.Equals("AbilityCarrier", StringComparison.OrdinalIgnoreCase))
        {
            return RosterEntityKind.AbilityCarrier;
        }

        return RosterEntityKind.Unit;
    }

    private static IReadOnlySet<string> ParseCsvSet(IReadOnlyDictionary<string, string>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? ReadMetadataValue(IReadOnlyDictionary<string, string>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Trim();
    }

    private static bool RequiresSpawnRoster(string actionId)
    {
        return actionId.Equals("spawn_unit_helper", StringComparison.OrdinalIgnoreCase) ||
               actionId.Equals("spawn_context_entity", StringComparison.OrdinalIgnoreCase) ||
               actionId.Equals("spawn_tactical_entity", StringComparison.OrdinalIgnoreCase) ||
               actionId.Equals("spawn_galactic_entity", StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresBuildingRoster(string actionId)
    {
        return actionId.Equals("place_planet_building", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsContextFactionAction(string actionId)
    {
        return actionId.Equals("set_context_faction", StringComparison.OrdinalIgnoreCase) ||
               actionId.Equals("set_context_allegiance", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEntityOperationAction(string actionId)
    {
        return RequiresSpawnRoster(actionId) ||
               RequiresBuildingRoster(actionId) ||
               IsContextFactionAction(actionId);
    }

    private static bool TryGetHealthySymbol(AttachSession session, string symbol)
    {
        return session.Symbols.TryGetValue(symbol, out var symbolInfo) &&
               symbolInfo is not null &&
               symbolInfo.Address != nint.Zero &&
               symbolInfo.HealthStatus != SymbolHealthStatus.Unresolved;
    }
}
