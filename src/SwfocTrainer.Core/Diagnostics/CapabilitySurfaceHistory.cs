using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace SwfocTrainer.Core.Diagnostics;

/// <summary>
/// 2026-04-28 (iter 67) — append-only history of capability surface
/// rollups. Each time the surface report regenerates, an entry is
/// appended to <c>capability_surface_history.jsonl</c> capturing the
/// rollup counts + timestamp. Operators see the engine-effectiveness
/// trend over time — as Phase 2 hooks land, the LIVE percentage
/// ratchets up, and the history is the editor's progress meter.
///
/// JSON-lines format (one record per line) so appends are O(1) and
/// the file is grep-friendly. Same-date entries are deduplicated to
/// keep the file small (one snapshot per day, latest wins).
/// </summary>
public static class CapabilitySurfaceHistory
{
    /// <summary>
    /// One row in the history file. Each field is primitive so the
    /// JSON is forward-compatible if we add fields later.
    /// </summary>
    public sealed record HistoryEntry(
        string Date,
        int TotalActions,
        int LiveCount,
        int LiveOnlyCount,
        int Phase2PendingCount,
        int MixedCount,
        int OtherCount,
        int LivePercent);

    /// <summary>
    /// Append a snapshot of the rollup to the history file. If a
    /// same-date entry already exists, it's replaced (so multiple
    /// commits on the same day collapse to the latest snapshot).
    /// </summary>
    /// <remarks>
    /// File is JSON-lines (one record per line) using
    /// <see cref="System.Text.Json"/> with default property naming.
    /// Creates the file if it doesn't exist.
    /// </remarks>
    public static void Record(
        CapabilitySurfaceReport.SurfaceRollup rollup,
        string historyPath,
        DateTimeOffset timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(rollup);
        ArgumentNullException.ThrowIfNull(historyPath);

        var entry = new HistoryEntry(
            Date: timestampUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            TotalActions: rollup.TotalActions,
            LiveCount: rollup.LiveCount,
            LiveOnlyCount: rollup.LiveOnlyCount,
            Phase2PendingCount: rollup.Phase2PendingCount,
            MixedCount: rollup.MixedCount,
            OtherCount: rollup.OtherCount,
            LivePercent: rollup.LivePercent);

        // Same-date dedup: read existing, drop entries with our date,
        // re-append the new one. Keeps the file at one snapshot per day.
        var existing = LoadAll(historyPath)
            .Where(e => !string.Equals(e.Date, entry.Date, System.StringComparison.Ordinal))
            .ToList();
        existing.Add(entry);

        var lines = existing
            .OrderBy(e => e.Date, System.StringComparer.Ordinal)
            .Select(e => System.Text.Json.JsonSerializer.Serialize(e));
        File.WriteAllText(historyPath, string.Join("\n", lines) + "\n");
    }

    /// <summary>
    /// Read every history entry. Returns an empty list when the file
    /// doesn't exist (first run before any record), making this safe
    /// to call unconditionally.
    /// </summary>
    public static IReadOnlyList<HistoryEntry> LoadAll(string historyPath)
    {
        ArgumentNullException.ThrowIfNull(historyPath);
        if (!File.Exists(historyPath)) return System.Array.Empty<HistoryEntry>();
        var entries = new List<HistoryEntry>();
        foreach (var line in File.ReadAllLines(historyPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var entry = System.Text.Json.JsonSerializer.Deserialize<HistoryEntry>(line);
                if (entry is not null) entries.Add(entry);
            }
            catch
            {
                // Tolerate corrupt lines — skip rather than throw.
                // Operators editing the file by hand sometimes break a
                // line; we don't want one bad row to brick the report.
            }
        }
        return entries;
    }

    /// <summary>
    /// Build a one-line trend summary suitable for embedding in the
    /// capability surface report. Example:
    /// <c>"58% engine-effective (was 56% on 2026-04-26 → +2pp over 2 entries)"</c>.
    /// Returns empty when there's no prior history (first run).
    /// </summary>
    public static string BuildTrendLine(IReadOnlyList<HistoryEntry> history)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (history.Count < 2) return string.Empty;
        var sorted = history.OrderBy(e => e.Date, System.StringComparer.Ordinal).ToList();
        var latest = sorted[^1];
        var earliest = sorted[0];
        var delta = latest.LivePercent - earliest.LivePercent;
        var sign = delta >= 0 ? "+" : "";
        return string.Format(CultureInfo.InvariantCulture,
            "{0}% engine-effective (was {1}% on {2} → {3}{4}pp over {5} entries)",
            latest.LivePercent, earliest.LivePercent, earliest.Date,
            sign, delta, sorted.Count);
    }
}
