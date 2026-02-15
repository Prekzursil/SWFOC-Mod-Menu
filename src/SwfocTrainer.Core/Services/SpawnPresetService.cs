using System.Text.Json;
using System.Text.Json.Nodes;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class SpawnPresetService : ISpawnPresetService
{
    private readonly IProfileRepository _profiles;
    private readonly ICatalogService _catalog;
    private readonly TrainerOrchestrator _orchestrator;
    private readonly LiveOpsOptions _options;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public SpawnPresetService(
        IProfileRepository profiles,
        ICatalogService catalog,
        TrainerOrchestrator orchestrator,
        LiveOpsOptions options)
    {
        _profiles = profiles;
        _catalog = catalog;
        _orchestrator = orchestrator;
        _options = options;
    }

    public async Task<IReadOnlyList<SpawnPreset>> LoadPresetsAsync(string profileId, CancellationToken cancellationToken = default)
    {
        var presetPath = BuildPresetPath(profileId);
        if (File.Exists(presetPath))
        {
            var json = await File.ReadAllTextAsync(presetPath, cancellationToken);
            var parsed = JsonSerializer.Deserialize<SpawnPresetDocument>(json, JsonOptions);
            var presets = parsed?.Presets ?? Array.Empty<SpawnPreset>();
            if (presets.Count > 0)
            {
                return presets.Select(NormalizePreset).ToArray();
            }
        }

        var generated = await GenerateDefaultPresetsAsync(profileId, cancellationToken);
        return generated;
    }

    public SpawnBatchPlan BuildBatchPlan(
        string profileId,
        SpawnPreset preset,
        int quantity,
        int delayMs,
        string? factionOverride,
        string? entryMarkerOverride,
        bool stopOnFailure)
    {
        var normalizedQuantity = Math.Clamp(quantity <= 0 ? preset.DefaultQuantity : quantity, 1, 100);
        var normalizedDelay = Math.Clamp(delayMs < 0 ? preset.DefaultDelayMs : delayMs, 0, 5000);
        var faction = string.IsNullOrWhiteSpace(factionOverride) ? preset.Faction : factionOverride.Trim();
        var entry = string.IsNullOrWhiteSpace(entryMarkerOverride) ? preset.EntryMarker : entryMarkerOverride.Trim();

        var items = new List<SpawnBatchItem>(normalizedQuantity);
        for (var i = 0; i < normalizedQuantity; i++)
        {
            items.Add(new SpawnBatchItem(i + 1, preset.UnitId, faction, entry, normalizedDelay));
        }

        return new SpawnBatchPlan(
            profileId,
            preset.Id,
            stopOnFailure,
            items);
    }

    public async Task<SpawnBatchExecutionResult> ExecuteBatchAsync(
        string profileId,
        SpawnBatchPlan plan,
        RuntimeMode runtimeMode,
        CancellationToken cancellationToken = default)
    {
        if (runtimeMode == RuntimeMode.Unknown)
        {
            return new SpawnBatchExecutionResult(
                false,
                "Spawn batch blocked: runtime mode is unknown.",
                0,
                0,
                0,
                true,
                Array.Empty<SpawnBatchItemResult>());
        }

        if (plan.Items.Count == 0)
        {
            return new SpawnBatchExecutionResult(true, "Spawn plan has no items.", 0, 0, 0, false, Array.Empty<SpawnBatchItemResult>());
        }

        var profile = await _profiles.ResolveInheritedProfileAsync(profileId, cancellationToken);
        if (!profile.Actions.ContainsKey("spawn_unit_helper"))
        {
            return new SpawnBatchExecutionResult(
                false,
                $"Profile '{profileId}' does not expose spawn_unit_helper action.",
                0,
                0,
                0,
                false,
                Array.Empty<SpawnBatchItemResult>());
        }

        var helperHookId = profile.HelperModHooks
            .Select(x => x.Id)
            .FirstOrDefault(id => id.Contains("spawn", StringComparison.OrdinalIgnoreCase))
            ?? "spawn_bridge";

        var results = new List<SpawnBatchItemResult>(plan.Items.Count);
        foreach (var item in plan.Items)
        {
            var payload = new JsonObject
            {
                ["helperHookId"] = helperHookId,
                ["unitId"] = item.UnitId,
                ["entryMarker"] = item.EntryMarker,
                ["faction"] = item.Faction,
            };

            var context = new Dictionary<string, object?>
            {
                ["bundleGateResult"] = "bundle_pass",
                ["spawnPresetId"] = plan.PresetId,
                ["spawnSequence"] = item.Sequence
            };

            var result = await _orchestrator.ExecuteAsync(
                profileId,
                "spawn_unit_helper",
                payload,
                runtimeMode,
                context,
                cancellationToken);

            results.Add(new SpawnBatchItemResult(
                item.Sequence,
                item.UnitId,
                result.Succeeded,
                result.Message,
                result.Diagnostics));

            if (!result.Succeeded && plan.StopOnFailure)
            {
                var attempted = results.Count;
                var succeeded = results.Count(x => x.Succeeded);
                var failed = attempted - succeeded;
                return new SpawnBatchExecutionResult(
                    false,
                    $"Spawn batch stopped at item {item.Sequence} after failure.",
                    attempted,
                    succeeded,
                    failed,
                    true,
                    results);
            }

            if (item.DelayMs > 0)
            {
                await Task.Delay(item.DelayMs, cancellationToken);
            }
        }

        var succeededCount = results.Count(x => x.Succeeded);
        var failedCount = results.Count - succeededCount;
        return new SpawnBatchExecutionResult(
            failedCount == 0,
            failedCount == 0
                ? $"Spawn batch succeeded ({succeededCount}/{results.Count})."
                : $"Spawn batch completed with failures ({succeededCount} succeeded, {failedCount} failed).",
            results.Count,
            succeededCount,
            failedCount,
            false,
            results);
    }

    private string BuildPresetPath(string profileId)
    {
        return Path.Combine(_options.PresetRootPath, profileId, "spawn_presets.json");
    }

    private async Task<IReadOnlyList<SpawnPreset>> GenerateDefaultPresetsAsync(string profileId, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, IReadOnlyList<string>> catalog;
        try
        {
            catalog = await _catalog.LoadCatalogAsync(profileId, cancellationToken);
        }
        catch
        {
            return Array.Empty<SpawnPreset>();
        }

        if (!catalog.TryGetValue("unit_catalog", out var units) || units.Count == 0)
        {
            return Array.Empty<SpawnPreset>();
        }

        var factions = catalog.TryGetValue("faction_catalog", out var factionCatalog) && factionCatalog.Count > 0
            ? factionCatalog
            : new[] { "EMPIRE" };

        var faction = factions[0];
        var defaults = units
            .Take(12)
            .Select((unit, index) => new SpawnPreset(
                Id: $"auto_{index + 1}_{unit.ToLowerInvariant()}",
                Name: unit.Replace('_', ' '),
                UnitId: unit,
                Faction: faction,
                EntryMarker: "AUTO",
                DefaultQuantity: 1,
                DefaultDelayMs: 125,
                Description: "Auto-generated from catalog"))
            .ToArray();

        return defaults;
    }

    private static SpawnPreset NormalizePreset(SpawnPreset preset)
    {
        var id = string.IsNullOrWhiteSpace(preset.Id) ? preset.UnitId.ToLowerInvariant() : preset.Id;
        var name = string.IsNullOrWhiteSpace(preset.Name) ? preset.UnitId : preset.Name;
        var faction = string.IsNullOrWhiteSpace(preset.Faction) ? "EMPIRE" : preset.Faction;
        var marker = string.IsNullOrWhiteSpace(preset.EntryMarker) ? "AUTO" : preset.EntryMarker;
        var quantity = Math.Clamp(preset.DefaultQuantity <= 0 ? 1 : preset.DefaultQuantity, 1, 100);
        var delay = Math.Clamp(preset.DefaultDelayMs < 0 ? 125 : preset.DefaultDelayMs, 0, 5000);

        return preset with
        {
            Id = id,
            Name = name,
            Faction = faction,
            EntryMarker = marker,
            DefaultQuantity = quantity,
            DefaultDelayMs = delay
        };
    }

    private sealed record SpawnPresetDocument(string SchemaVersion, IReadOnlyList<SpawnPreset> Presets);
}
