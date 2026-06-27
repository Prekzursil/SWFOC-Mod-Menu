using System.Text.Json;
using System.Text.Json.Nodes;

namespace SwfocTrainer.Core.Ux;

/// <summary>
/// Bonus follow-up to Task #155 — JSON persistence for the
/// <see cref="FeatureToggleCoordinator"/>. Saves and reloads the
/// per-feature enabled/disabled flags + last-changed timestamp + last
/// reason so an editor restart can resume the operator's previous
/// session state instead of dropping every toggle to "off".
///
/// Persistence is opt-in: callers explicitly invoke
/// <see cref="SaveTo"/> and <see cref="LoadInto"/>. The format is a
/// flat JSON object keyed by feature id; unknown extra keys at load
/// time are silently ignored so future schema additions don't break
/// older saved files.
///
/// IMPORTANT: persistence does NOT carry over the disable callback
/// itself. Loading a "freeze_credits = enabled" entry resumes the
/// flag-state but NOT the registered cleanup-on-disable callback.
/// The caller must re-register callbacks via fresh
/// <c>ToggleAsync(.., disableAction)</c> calls in the same session
/// before <c>CleanupAllAsync</c> can dispose them. The saved flag is
/// a hint, not a binding contract.
/// </summary>
public static class FeatureTogglePersistence
{
    private static readonly JsonSerializerOptions _options = new()
    {
        WriteIndented = true,
    };

    /// <summary>
    /// Serialise the coordinator's current state to a JSON string.
    /// </summary>
    public static string ToJson(FeatureToggleCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        var root = new JsonObject();
        var states = new JsonObject();
        foreach (var (id, state) in coordinator.States)
        {
            states[id] = new JsonObject
            {
                ["enabled"] = state.Enabled,
                ["lastChangedUtc"] = state.LastChanged.ToString("O"),
                ["lastReason"] = state.LastReason,
            };
        }
        root["schemaVersion"] = 1;
        root["states"] = states;
        return root.ToJsonString(_options);
    }

    /// <summary>
    /// Save the coordinator state to a file. Creates the file's parent
    /// directory if needed. Atomic-ish — writes to a temp file then
    /// renames over the target so a crash mid-write doesn't corrupt
    /// the previous saved state.
    /// </summary>
    public static void SaveTo(FeatureToggleCoordinator coordinator, string path)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(path);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        var tempPath = path + ".tmp";
        File.WriteAllText(tempPath, ToJson(coordinator));
        // Replace target — `File.Move` with overwrite (.NET Core+).
        File.Move(tempPath, path, overwrite: true);
    }

    /// <summary>
    /// Snapshot of one persisted feature flag — read-only view, used
    /// by callers that need to inspect saved state without dispatching
    /// real toggle actions (e.g. a "Resume previous session?" prompt).
    /// </summary>
    public sealed record PersistedToggleState(
        string FeatureId,
        bool Enabled,
        DateTimeOffset LastChanged,
        string? LastReason);

    /// <summary>
    /// Parse the persisted JSON into a flat list of state snapshots.
    /// Throws <see cref="JsonException"/> on malformed JSON; silently
    /// skips entries whose enabled bit can't be parsed (defensive
    /// against future schema additions).
    /// </summary>
    public static IReadOnlyList<PersistedToggleState> ReadFromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        var root = JsonNode.Parse(json) as JsonObject
            ?? throw new JsonException("persistence root must be a JSON object");
        var statesNode = root["states"] as JsonObject;
        if (statesNode is null)
        {
            return Array.Empty<PersistedToggleState>();
        }
        var results = new List<PersistedToggleState>();
        foreach (var (id, node) in statesNode)
        {
            if (node is not JsonObject entry) continue;
            var enabledNode = entry["enabled"];
            if (enabledNode is null) continue;

            bool enabled;
            try { enabled = enabledNode.GetValue<bool>(); }
            catch (InvalidOperationException) { continue; }

            DateTimeOffset lastChanged = DateTimeOffset.MinValue;
            if (entry["lastChangedUtc"] is JsonValue tsNode)
            {
                _ = DateTimeOffset.TryParse(
                    tsNode.GetValue<string>(), out lastChanged);
            }
            var reason = entry["lastReason"]?.GetValue<string>();
            results.Add(new PersistedToggleState(id, enabled, lastChanged, reason));
        }
        return results;
    }

    /// <summary>
    /// Load persisted state from a file path. Returns an empty list if
    /// the file doesn't exist (first-run scenario).
    /// </summary>
    public static IReadOnlyList<PersistedToggleState> ReadFromPath(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            return Array.Empty<PersistedToggleState>();
        }
        return ReadFromJson(File.ReadAllText(path));
    }

    /// <summary>
    /// Convenience: load + replay the saved enables on a coordinator.
    /// Each saved enabled feature gets a synthetic
    /// <see cref="UxFeedback"/> Info emission via the coordinator's sink
    /// so the operator sees "Resumed god_mode (was on at 12:05)" in
    /// the status pane on session restart.
    /// </summary>
    public static async Task<int> LoadInto(
        FeatureToggleCoordinator coordinator,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(path);
        var saved = ReadFromPath(path);
        var resumedCount = 0;
        foreach (var entry in saved)
        {
            if (!entry.Enabled) continue;
            // Replay the enable as a synthetic toggle. The action
            // returns an Info feedback because no real bridge call
            // happened; the caller is expected to re-issue real
            // ToggleAsync calls afterward to actually re-enable
            // through the bridge.
            await coordinator.ToggleAsync(entry.FeatureId, enable: true,
                action: _ => Task.FromResult(UxFeedback.Info(
                    title: $"resumed {entry.FeatureId}",
                    message: $"flag set from saved state ({entry.LastChanged:HH:mm})",
                    featureId: entry.FeatureId)),
                disableAction: null,
                cancellationToken: cancellationToken);
            resumedCount++;
        }
        return resumedCount;
    }
}
