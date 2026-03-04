using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;

namespace SwfocTrainer.Runtime.Services;

public sealed class ModMechanicDetectionService : IModMechanicDetectionService
{
    private const string UnitCatalogKey = "unit_catalog";
    private const string FactionCatalogKey = "faction_catalog";
    private const string BuildingCatalogKey = "building_catalog";
    private const string EntityCatalogKey = "entity_catalog";
    private const string ActionSpawnUnitHelper = "spawn_unit_helper";
    private const string ActionSpawnContextEntity = "spawn_context_entity";
    private const string ActionSpawnTacticalEntity = "spawn_tactical_entity";
    private const string ActionSpawnGalacticEntity = "spawn_galactic_entity";
    private const string ActionPlacePlanetBuilding = "place_planet_building";
    private const string ActionSetContextFaction = "set_context_faction";
    private const string ActionSetContextAllegiance = "set_context_allegiance";
    private const string ActionTransferFleetSafe = "transfer_fleet_safe";
    private const string ActionFlipPlanetOwner = "flip_planet_owner";
    private const string ActionSwitchPlayerFaction = "switch_player_faction";
    private const string ActionEditHeroState = "edit_hero_state";
    private const string ActionCreateHeroVariant = "create_hero_variant";

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
        var activeWorkshopIds = ParseActiveWorkshopIds(session.Process);
        var rosterEntities = BuildRosterEntities(profile, catalog);
        var transplantReport = await TryResolveTransplantReportAsync(profile, activeWorkshopIds, rosterEntities, cancellationToken);

        var diagnostics = BuildDiagnostics(
            profile,
            session,
            catalog,
            disabledActions,
            activeWorkshopIds,
            transplantReport);
        var supports = BuildActionSupport(
            profile,
            session,
            catalog,
            disabledActions,
            helperReady,
            transplantReport);

        var report = new ModMechanicReport(
            ProfileId: profile.Id,
            GeneratedAtUtc: DateTimeOffset.UtcNow,
            DependenciesSatisfied: disabledActions.Count == 0,
            HelperBridgeReady: helperReady,
            ActionSupport: supports,
            Diagnostics: diagnostics);

        return report;
    }

    private async Task<TransplantValidationReport?> TryResolveTransplantReportAsync(
        TrainerProfile profile,
        IReadOnlyList<string> activeWorkshopIds,
        IReadOnlyList<RosterEntityRecord> rosterEntities,
        CancellationToken cancellationToken)
    {
        if (_transplantCompatibilityService is null)
        {
            return null;
        }

        try
        {
            return await _transplantCompatibilityService
                .ValidateAsync(profile.Id, activeWorkshopIds, rosterEntities, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private Dictionary<string, object?> BuildDiagnostics(
        TrainerProfile profile,
        AttachSession session,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
        IReadOnlySet<string> disabledActions,
        IReadOnlyList<string> activeWorkshopIds,
        TransplantValidationReport? transplantReport)
    {
        var heroMechanics = ResolveHeroMechanicsProfile(profile, session);

        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["dependencyValidation"] = ReadMetadataValue(session.Process.Metadata, "dependencyValidation") ?? string.Empty,
            ["dependencyDisabledActions"] = disabledActions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToArray(),
            ["helperBridgeState"] = ReadMetadataValue(session.Process.Metadata, "helperBridgeState") ?? "unknown",
            ["unitCatalogCount"] = catalog is not null && catalog.TryGetValue(UnitCatalogKey, out var units) ? units.Count : 0,
            ["factionCatalogCount"] = catalog is not null && catalog.TryGetValue(FactionCatalogKey, out var factions) ? factions.Count : 0,
            ["buildingCatalogCount"] = catalog is not null && catalog.TryGetValue(BuildingCatalogKey, out var buildings) ? buildings.Count : 0,
            ["activeWorkshopIds"] = activeWorkshopIds,
            ["transplantAllResolved"] = transplantReport?.AllResolved ?? true,
            ["transplantBlockingEntityCount"] = transplantReport?.BlockingEntityCount ?? 0,
            ["transplantEnabled"] = _transplantCompatibilityService is not null,
            ["heroMechanicsSummary"] = BuildHeroMechanicsSummary(heroMechanics)
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

        return diagnostics;
    }

    private static IReadOnlyList<ModMechanicSupport> BuildActionSupport(
        TrainerProfile profile,
        AttachSession session,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
        IReadOnlySet<string> disabledActions,
        bool helperReady,
        TransplantValidationReport? transplantReport)
    {
        var supports = new List<ModMechanicSupport>(profile.Actions.Count);
        foreach (var (actionId, action) in profile.Actions.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            supports.Add(EvaluateAction(new ActionEvaluationContext(
                ActionId: actionId,
                Action: action,
                Profile: profile,
                Session: session,
                Catalog: catalog,
                DisabledActions: disabledActions,
                HelperReady: helperReady,
                TransplantReport: transplantReport)));
        }

        return supports;
    }

    private static ModMechanicSupport EvaluateAction(ActionEvaluationContext context)
    {
        if (TryEvaluateDependencyGate(context, out var support) ||
            TryEvaluateHelperGate(context, out support) ||
            TryEvaluateRosterGate(context, out support) ||
            TryEvaluateContextFactionGate(context, out support) ||
            TryEvaluateSymbolGate(context, out support))
        {
            return support;
        }

        return new ModMechanicSupport(
            ActionId: context.ActionId,
            Supported: true,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Message: "Mechanic prerequisites are available.",
            Confidence: 0.80d);
    }

    private static bool TryEvaluateDependencyGate(ActionEvaluationContext context, out ModMechanicSupport support)
    {
        if (context.DisabledActions.Contains(context.ActionId))
        {
            support = new ModMechanicSupport(
                ActionId: context.ActionId,
                Supported: false,
                ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                Message: "Action is dependency-gated for the current workshop chain.",
                Confidence: 0.98d);
            return true;
        }

        if (IsEntityOperationAction(context.ActionId) &&
            context.TransplantReport is not null &&
            !context.TransplantReport.AllResolved)
        {
            var blocking = context.TransplantReport.Entities.FirstOrDefault(static entity => !entity.Resolved);
            var blockingEntityId = blocking?.EntityId ?? "unknown_entity";
            var blockingReason = blocking?.ReasonCode.ToString() ?? RuntimeReasonCode.TRANSPLANT_VALIDATION_FAILED.ToString();
            support = new ModMechanicSupport(
                ActionId: context.ActionId,
                Supported: false,
                ReasonCode: RuntimeReasonCode.CROSS_MOD_TRANSPLANT_REQUIRED,
                Message: $"Cross-mod transplant validation failed for entity '{blockingEntityId}' (reason={blockingReason}).",
                Confidence: 0.99d);
            return true;
        }

        support = default!;
        return false;
    }

    private static bool TryEvaluateHelperGate(ActionEvaluationContext context, out ModMechanicSupport support)
    {
        if (context.Action.ExecutionKind != ExecutionKind.Helper)
        {
            support = default!;
            return false;
        }

        if (!context.HelperReady)
        {
            support = new ModMechanicSupport(
                ActionId: context.ActionId,
                Supported: false,
                ReasonCode: RuntimeReasonCode.HELPER_BRIDGE_UNAVAILABLE,
                Message: "Helper bridge is unavailable for this session.",
                Confidence: 0.98d);
            return true;
        }

        if (context.Profile.HelperModHooks.Count == 0)
        {
            support = new ModMechanicSupport(
                ActionId: context.ActionId,
                Supported: false,
                ReasonCode: RuntimeReasonCode.HELPER_ENTRYPOINT_NOT_FOUND,
                Message: "Profile has helper actions but no helper hook metadata.",
                Confidence: 0.98d);
            return true;
        }

        support = new ModMechanicSupport(
            ActionId: context.ActionId,
            Supported: true,
            ReasonCode: RuntimeReasonCode.HELPER_EXECUTION_APPLIED,
            Message: "Helper bridge and hook metadata are available.",
            Confidence: 0.85d);
        return true;
    }

    private static bool TryEvaluateRosterGate(ActionEvaluationContext context, out ModMechanicSupport support)
    {
        if (RequiresSpawnRoster(context.ActionId) &&
            (!HasCatalogEntries(context.Catalog, UnitCatalogKey) || !HasCatalogEntries(context.Catalog, FactionCatalogKey)))
        {
            support = new ModMechanicSupport(
                ActionId: context.ActionId,
                Supported: false,
                ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                Message: "Spawn roster catalog is unavailable for this profile/chain.",
                Confidence: 0.95d);
            return true;
        }

        if (RequiresBuildingRoster(context.ActionId) &&
            (!HasCatalogEntries(context.Catalog, BuildingCatalogKey) || !HasCatalogEntries(context.Catalog, FactionCatalogKey)))
        {
            support = new ModMechanicSupport(
                ActionId: context.ActionId,
                Supported: false,
                ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                Message: "Building roster catalog is unavailable for this profile/chain.",
                Confidence: 0.95d);
            return true;
        }

        support = default!;
        return false;
    }

    private static bool TryEvaluateContextFactionGate(ActionEvaluationContext context, out ModMechanicSupport support)
    {
        if (!IsContextFactionAction(context.ActionId))
        {
            support = default!;
            return false;
        }

        if (context.ActionId.Equals(ActionSwitchPlayerFaction, StringComparison.OrdinalIgnoreCase))
        {
            support = new ModMechanicSupport(
                ActionId: context.ActionId,
                Supported: true,
                ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
                Message: "Switch-player-faction flow is helper-routed for this chain.",
                Confidence: 0.80d);
            return true;
        }

        var hasTacticalOwner = TryGetHealthySymbol(context.Session, "selected_owner_faction");
        var hasPlanetOwner = TryGetHealthySymbol(context.Session, "planet_owner");
        if (!hasTacticalOwner && !hasPlanetOwner)
        {
            support = new ModMechanicSupport(
                ActionId: context.ActionId,
                Supported: false,
                ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
                Message: "Neither selected-unit owner nor planet owner symbols are available.",
                Confidence: 0.95d);
            return true;
        }

        support = new ModMechanicSupport(
            ActionId: context.ActionId,
            Supported: true,
            ReasonCode: RuntimeReasonCode.CAPABILITY_PROBE_PASS,
            Message: "Context faction/allegiance routing symbols are available.",
            Confidence: 0.85d);
        return true;
    }

    private static bool TryEvaluateSymbolGate(ActionEvaluationContext context, out ModMechanicSupport support)
    {
        if (!ActionSymbolRegistry.TryGetSymbol(context.ActionId, out var symbol))
        {
            support = default!;
            return false;
        }

        if (context.Session.Symbols.TryGetValue(symbol, out var symbolInfo) &&
            symbolInfo is not null &&
            symbolInfo.Address != nint.Zero &&
            symbolInfo.HealthStatus != SymbolHealthStatus.Unresolved)
        {
            support = default!;
            return false;
        }

        support = new ModMechanicSupport(
            ActionId: context.ActionId,
            Supported: false,
            ReasonCode: RuntimeReasonCode.CAPABILITY_REQUIRED_MISSING,
            Message: $"Symbol '{symbol}' is unresolved for this profile variant.",
            Confidence: 0.95d);
        return true;
    }

    private static bool HasCatalogEntries(IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog, string key)
    {
        return catalog is not null && catalog.TryGetValue(key, out var values) && values.Count > 0;
    }

    private static HeroMechanicsProfile ResolveHeroMechanicsProfile(TrainerProfile profile, AttachSession session)
    {
        var supportsRespawn = SupportsHeroRespawn(profile);
        var supportsRescue = SupportsHeroRescue(profile);
        var supportsPermadeath = SupportsHeroPermadeath(profile);
        var defaultRespawnTime = ResolveDefaultRespawnTime(profile, session, supportsRespawn);
        var respawnExceptionSources = ResolveRespawnExceptionSources(profile);
        var duplicateHeroPolicy = ResolveDuplicateHeroPolicy(profile, supportsPermadeath, supportsRescue);

        return new HeroMechanicsProfile(
            SupportsRespawn: supportsRespawn,
            SupportsPermadeath: supportsPermadeath,
            SupportsRescue: supportsRescue,
            DefaultRespawnTime: defaultRespawnTime,
            RespawnExceptionSources: respawnExceptionSources,
            DuplicateHeroPolicy: duplicateHeroPolicy,
            Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["profileId"] = profile.Id,
                ["runtimeMode"] = session.Process.Mode.ToString()
            });
    }

    private static bool SupportsHeroRespawn(TrainerProfile profile)
    {
        return profile.Actions.ContainsKey("set_hero_respawn_timer") ||
               profile.Actions.ContainsKey("set_hero_state_helper") ||
               profile.Actions.ContainsKey("toggle_roe_respawn_helper") ||
               profile.Actions.ContainsKey("edit_hero_state");
    }

    private static bool SupportsHeroRescue(TrainerProfile profile)
    {
        return ReadBoolMetadata(profile.Metadata, "supports_hero_rescue") ||
               profile.Id.Contains("aotr", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SupportsHeroPermadeath(TrainerProfile profile)
    {
        return ReadBoolMetadata(profile.Metadata, "supports_hero_permadeath") ||
               profile.Id.Contains("roe", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ResolveDefaultRespawnTime(TrainerProfile profile, AttachSession session, bool supportsRespawn)
    {
        var defaultRespawnTime = ParseOptionalInt(
            ReadMetadataValue(profile.Metadata, "defaultHeroRespawnTime") ??
            ReadMetadataValue(profile.Metadata, "default_hero_respawn_time"));

        if (supportsRespawn && TryGetHealthySymbol(session, "hero_respawn_timer") && defaultRespawnTime is null)
        {
            return 1;
        }

        return defaultRespawnTime;
    }

    private static IReadOnlyList<string> ResolveRespawnExceptionSources(TrainerProfile profile)
    {
        return ParseListMetadata(
            ReadMetadataValue(profile.Metadata, "respawnExceptionSources") ??
            ReadMetadataValue(profile.Metadata, "respawn_exception_sources"));
    }

    private static string ResolveDuplicateHeroPolicy(TrainerProfile profile, bool supportsPermadeath, bool supportsRescue)
    {
        return ReadMetadataValue(profile.Metadata, "duplicateHeroPolicy") ??
               ReadMetadataValue(profile.Metadata, "duplicate_hero_policy") ??
               InferDuplicateHeroPolicy(profile.Id, supportsPermadeath, supportsRescue);
    }
    private static IReadOnlyDictionary<string, object?> BuildHeroMechanicsSummary(HeroMechanicsProfile profile)
    {
        return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["supportsRespawn"] = profile.SupportsRespawn,
            ["supportsPermadeath"] = profile.SupportsPermadeath,
            ["supportsRescue"] = profile.SupportsRescue,
            ["defaultRespawnTime"] = profile.DefaultRespawnTime,
            ["respawnExceptionSources"] = profile.RespawnExceptionSources,
            ["duplicateHeroPolicy"] = profile.DuplicateHeroPolicy
        };
    }

    private static string InferDuplicateHeroPolicy(string profileId, bool supportsPermadeath, bool supportsRescue)
    {
        if (supportsRescue)
        {
            return "rescue_or_respawn";
        }

        if (supportsPermadeath)
        {
            return "mod_defined_permadeath";
        }

        if (profileId.Contains("base", StringComparison.OrdinalIgnoreCase))
        {
            return "canonical_singleton";
        }

        return "mod_defined";
    }

    private static bool ReadBoolMetadata(IReadOnlyDictionary<string, string>? metadata, string key)
    {
        if (!TryReadMetadataValue(metadata, key, out var value))
        {
            return false;
        }

        if (bool.TryParse(value, out var parsed))
        {
            return parsed;
        }

        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private static int? ParseOptionalInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return int.TryParse(value, out var parsed) ? parsed : null;
    }

    private static IReadOnlyList<string> ParseListMetadata(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<string>();
        }

        return raw
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<string> ParseActiveWorkshopIds(ProcessMetadata process)
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in (process.LaunchContext?.SteamModIds ?? Array.Empty<string>())
                     .Where(static id => !string.IsNullOrWhiteSpace(id)))
        {
            values.Add(id.Trim());
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

        foreach (var value in csv
                     .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                     .Where(static raw => !string.IsNullOrWhiteSpace(raw)))
        {
            sink.Add(value.Trim());
        }
    }

    private static IReadOnlyList<RosterEntityRecord> BuildRosterEntities(
        TrainerProfile profile,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
    {
        if (catalog is null || !catalog.TryGetValue(EntityCatalogKey, out var entries) || entries.Count == 0)
        {
            return Array.Empty<RosterEntityRecord>();
        }

        var defaultFaction = catalog.TryGetValue(FactionCatalogKey, out var factions) && factions.Count > 0
            ? factions[0]
            : "Empire";
        var records = new List<RosterEntityRecord>(entries.Count);
        foreach (var raw in entries)
        {
            if (!TryParseEntityCatalogEntry(raw, profile, defaultFaction, out var record))
            {
                continue;
            }

            records.Add(record);
        }

        return records;
    }

    private static bool TryParseEntityCatalogEntry(
        string raw,
        TrainerProfile profile,
        string defaultFaction,
        out RosterEntityRecord record)
    {
        record = default!;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var segments = raw.Split('|', StringSplitOptions.TrimEntries);
        if (segments.Length < 2 || string.IsNullOrWhiteSpace(segments[1]))
        {
            return false;
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

        record = new RosterEntityRecord(
            EntityId: entityId,
            DisplayName: entityId,
            SourceProfileId: sourceProfileId,
            SourceWorkshopId: sourceWorkshopId,
            EntityKind: kind,
            DefaultFaction: defaultFaction,
            AllowedModes: ResolveAllowedModes(kind),
            VisualRef: visualRef,
            DependencyRefs: dependencies);
        return true;
    }

    private static IReadOnlyList<RuntimeMode> ResolveAllowedModes(RosterEntityKind kind)
    {
        return kind is RosterEntityKind.Building or RosterEntityKind.SpaceStructure
            ? new[] { RuntimeMode.Galactic }
            : new[] { RuntimeMode.AnyTactical, RuntimeMode.Galactic };
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
        return TryReadMetadataValue(metadata, key, out var value)
            ? value
            : null;
    }

    private static bool TryReadMetadataValue(IReadOnlyDictionary<string, string>? metadata, string key, out string value)
    {
        value = string.Empty;
        if (metadata is null || !metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        value = raw.Trim();
        return true;
    }

    private static bool RequiresSpawnRoster(string actionId)
    {
        return actionId.Equals(ActionSpawnUnitHelper, StringComparison.OrdinalIgnoreCase) ||
               actionId.Equals(ActionSpawnContextEntity, StringComparison.OrdinalIgnoreCase) ||
               actionId.Equals(ActionSpawnTacticalEntity, StringComparison.OrdinalIgnoreCase) ||
               actionId.Equals(ActionSpawnGalacticEntity, StringComparison.OrdinalIgnoreCase) ||
               actionId.Equals(ActionTransferFleetSafe, StringComparison.OrdinalIgnoreCase) ||
               actionId.Equals(ActionEditHeroState, StringComparison.OrdinalIgnoreCase) ||
               actionId.Equals(ActionCreateHeroVariant, StringComparison.OrdinalIgnoreCase);
    }

    private static bool RequiresBuildingRoster(string actionId)
    {
        return actionId.Equals(ActionPlacePlanetBuilding, StringComparison.OrdinalIgnoreCase) ||
               actionId.Equals(ActionFlipPlanetOwner, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsContextFactionAction(string actionId)
    {
        return actionId.Equals(ActionSetContextFaction, StringComparison.OrdinalIgnoreCase) ||
               actionId.Equals(ActionSetContextAllegiance, StringComparison.OrdinalIgnoreCase) ||
               actionId.Equals(ActionSwitchPlayerFaction, StringComparison.OrdinalIgnoreCase);
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

    private sealed record ActionEvaluationContext(
        string ActionId,
        ActionSpec Action,
        TrainerProfile Profile,
        AttachSession Session,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? Catalog,
        IReadOnlySet<string> DisabledActions,
        bool HelperReady,
        TransplantValidationReport? TransplantReport);
}
