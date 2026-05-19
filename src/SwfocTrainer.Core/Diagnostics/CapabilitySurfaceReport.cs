using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SwfocTrainer.Core.Diagnostics;

/// <summary>
/// 2026-04-27 (iter 61) — aggregator for the editor-wide capability
/// surface. Walks every V2 tab's <c>AllActions</c> list and emits a
/// single markdown document operators can scan to see what's LIVE vs
/// PHASE 2 PENDING vs LIVE ONLY across the entire editor.
///
/// Sister to <see cref="CapabilityStatusCatalog.GenerateMarkdownReport"/>:
/// the catalog report is keyed by helper name; the surface report is
/// keyed by tab + button. Together they're the authoritative view of
/// "what's the editor's current capability story" for bug reports and
/// hand-offs.
/// </summary>
public static class CapabilitySurfaceReport
{
    /// <summary>
    /// One row in the report: tab name + the action's name + badge + note.
    /// </summary>
    public sealed record Row(string TabName, string ActionName, string Badge, string Note);

    /// <summary>
    /// 2026-04-27 (iter 63): summary roll-up for the editor's bottom
    /// status bar. Aggregated across every V2 tab's <c>AllActions</c>;
    /// the operator sees this constantly while using the editor so it
    /// doubles as a "how much of the editor is engine-effective today"
    /// progress meter.
    /// </summary>
    /// <param name="TotalActions">Total actions across all tabs walked.</param>
    /// <param name="LiveCount">Number with badge <c>LIVE</c>.</param>
    /// <param name="LiveOnlyCount">Number with badge <c>LIVE ONLY</c>
    /// (RequiresLiveSwfoc — needs running game).</param>
    /// <param name="Phase2PendingCount">Number with badge <c>PHASE 2 PENDING</c>.</param>
    /// <param name="MixedCount">Number with composite <c>MIXED (m/n LIVE)</c>.</param>
    /// <param name="OtherCount">Anything else (UNAVAILABLE / unknown).</param>
    public sealed record SurfaceRollup(
        int TotalActions,
        int LiveCount,
        int LiveOnlyCount,
        int Phase2PendingCount,
        int MixedCount,
        int OtherCount)
    {
        /// <summary>
        /// Percentage of actions that are uniformly LIVE (engine-verified).
        /// LIVE ONLY counts toward this — those still have engine effect,
        /// just gated on a running game session.
        /// </summary>
        public int LivePercent => TotalActions == 0
            ? 0
            : (int)System.Math.Round(100.0 * (LiveCount + LiveOnlyCount) / TotalActions);

        /// <summary>
        /// Single-line summary suitable for the bottom status bar.
        /// Format: <c>"Capability: 52 LIVE / 37 PHASE 2 / 3 LIVE ONLY · 94 actions · 58% engine-effective"</c>.
        /// </summary>
        public string SummaryLine => string.Format(
            System.Globalization.CultureInfo.InvariantCulture,
            "Capability: {0} LIVE / {1} PHASE 2 / {2} LIVE ONLY · {3} actions · {4}% engine-effective",
            LiveCount, Phase2PendingCount, LiveOnlyCount, TotalActions, LivePercent);
    }

    /// <summary>
    /// Aggregate the editor's capability surface into a roll-up record.
    /// Same input shape as <see cref="GenerateMarkdownReport"/> but
    /// returns the headline numbers without rendering markdown.
    /// </summary>
    public static SurfaceRollup ComputeRollup(
        IEnumerable<(string TabName, IReadOnlyList<CapabilityAwareAction> Actions)> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        var live = 0;
        var liveOnly = 0;
        var phase2 = 0;
        var mixed = 0;
        var other = 0;
        var total = 0;
        foreach (var (_, actions) in tabs)
        {
            foreach (var action in actions)
            {
                total++;
                switch (action.Badge)
                {
                    case "LIVE": live++; break;
                    case "LIVE ONLY": liveOnly++; break;
                    case "PHASE 2 PENDING": phase2++; break;
                    case var b when b.StartsWith("MIXED", System.StringComparison.Ordinal): mixed++; break;
                    default: other++; break;
                }
            }
        }
        return new SurfaceRollup(total, live, liveOnly, phase2, mixed, other);
    }

    /// <summary>
    /// Build a markdown report from a sequence of (tab name, actions)
    /// pairs. The order is preserved — caller is expected to feed tabs
    /// in the order they appear in the editor's TabControl.
    /// </summary>
    /// <remarks>
    /// Output is deterministic and uses <c>\n</c> line endings so the
    /// drift-protection test isn't whitespace-flaky on Windows checkouts.
    /// 2026-04-28 (iter 68): optional <paramref name="history"/> renders
    /// the iter-67 trend line under the headline so the report is
    /// self-documenting — operators see "was 56% on D, +2pp over N
    /// entries" without running the trend CLI separately.
    /// </remarks>
    public static string GenerateMarkdownReport(
        IEnumerable<(string TabName, IReadOnlyList<CapabilityAwareAction> Actions)> tabs,
        IReadOnlyList<CapabilitySurfaceHistory.HistoryEntry>? history = null)
    {
        ArgumentNullException.ThrowIfNull(tabs);

        var sb = new StringBuilder(16 * 1024);
        sb.Append("# SWFOC Editor Capability Surface\n\n");
        sb.Append("**Auto-generated from `CapabilitySurfaceReport.GenerateMarkdownReport()` ");
        sb.Append("walking every V2 tab view-model's `AllActions` property. ");
        sb.Append("Do not edit by hand — change the per-tab view-model and regenerate.**\n\n");
        sb.Append("Each row is one operator-facing button on the editor's tab surface, ");
        sb.Append("keyed against `CapabilityStatusCatalog` so the badge / note column is ");
        sb.Append("the source-of-truth for what the bridge actually does.\n\n");

        // 2026-04-28 (iter 68): trend line. Empty-string fallback when
        // there's no prior history (first run) keeps the layout stable.
        if (history is { Count: > 1 })
        {
            var trend = CapabilitySurfaceHistory.BuildTrendLine(history);
            if (!string.IsNullOrEmpty(trend))
            {
                sb.Append("**Trend:** ").Append(trend).Append("\n\n");
            }
        }

        var rows = new List<Row>();
        foreach (var (tabName, actions) in tabs)
        {
            foreach (var action in actions)
            {
                rows.Add(new Row(tabName, action.Name, action.Badge, action.Note));
            }
        }

        // Roll-up by badge — the headline numbers operators care about.
        var byBadge = rows
            .GroupBy(r => r.Badge, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        sb.Append("## Roll-up by badge\n\n");
        sb.Append("| Badge | Count |\n|---|---|\n");
        foreach (var group in byBadge)
        {
            sb.Append("| `").Append(group.Key).Append("` | ").Append(group.Count()).Append(" |\n");
        }
        sb.Append("\n");

        // Per-tab breakdown.
        var byTab = rows
            .GroupBy(r => r.TabName, StringComparer.Ordinal)
            .ToList();
        sb.Append("## Per-tab actions\n\n");
        foreach (var group in byTab)
        {
            sb.Append("### ").Append(group.Key).Append('\n').Append('\n');
            sb.Append("| Action | Badge | Note |\n|---|---|---|\n");
            foreach (var row in group)
            {
                sb.Append("| ").Append(EscapeCell(row.ActionName))
                  .Append(" | `").Append(row.Badge).Append("`")
                  .Append(" | ").Append(EscapeCell(row.Note))
                  .Append(" |\n");
            }
            sb.Append('\n');
        }

        // 2026-04-28 (iter 71): cross-tab "Sorted by badge" section.
        // Operators triaging "what's not LIVE yet?" need a single-list
        // view of every action across every tab. Sort by (Badge,
        // TabName, ActionName) so PHASE 2 PENDING entries cluster
        // together regardless of which tab they live on.
        sb.Append("## Sorted by badge (cross-tab)\n\n");
        sb.Append("Every action across every tab in a single list, ");
        sb.Append("sorted by badge → tab → action name. Useful when ");
        sb.Append("triaging \"what's not LIVE yet?\" — `PHASE 2 PENDING` ");
        sb.Append("rows cluster together regardless of which tab they live on.\n\n");
        sb.Append("| Badge | Tab | Action | Note |\n|---|---|---|---|\n");
        var sortedRows = rows
            .OrderBy(r => r.Badge, System.StringComparer.Ordinal)
            .ThenBy(r => r.TabName, System.StringComparer.Ordinal)
            .ThenBy(r => r.ActionName, System.StringComparer.Ordinal);
        foreach (var row in sortedRows)
        {
            sb.Append("| `").Append(row.Badge).Append("` | ");
            sb.Append(EscapeCell(row.TabName)).Append(" | ");
            sb.Append(EscapeCell(row.ActionName)).Append(" | ");
            sb.Append(EscapeCell(row.Note)).Append(" |\n");
        }
        sb.Append('\n');

        sb.Append("## Status legend\n\n");
        sb.Append("- **LIVE** — direct engine call, observable mutation.\n");
        sb.Append("- **MIXED (m/n LIVE)** — composite action with mixed-status primitives.\n");
        sb.Append("- **PHASE 2 PENDING** — Phase-1 mirror works, Phase 2 detour BLOCKED-NO-RVA.\n");
        sb.Append("- **LIVE ONLY** — needs running game; offline harness can't exercise.\n");
        sb.Append("- **UNAVAILABLE** — registered but out-of-scope for current release.\n");

        return sb.ToString();
    }

    /// <summary>
    /// 2026-04-28 (iter 72) — JSON sibling to
    /// <see cref="GenerateMarkdownReport"/>. Same input shape; renders
    /// a structured document for tools/scripts that don't want to
    /// scrape the markdown tables.
    /// </summary>
    /// <remarks>
    /// Schema (stable; downstream consumers can rely on field names):
    /// <code>
    /// {
    ///   "generatedUtc": "2026-04-28T12:00:00Z",
    ///   "rollup": { "totalActions": 96, "liveCount": 53, "phase2PendingCount": 37,
    ///               "liveOnlyCount": 3, "mixedCount": 3, "otherCount": 0,
    ///               "livePercent": 58 },
    ///   "trend": "58% engine-effective (was 56% on 2026-04-26 → +2pp over 2 entries)",
    ///   "tabs": [
    ///     { "tabName": "Combat", "actions": [
    ///         { "actionName": "Toggle god mode", "badge": "LIVE", "note": "..." },
    ///         ...
    ///     ]}, ...
    ///   ]
    /// }
    /// </code>
    /// Pretty-printed (WriteIndented = true) so the on-disk file
    /// stays diff-friendly. JSON serialisation uses
    /// camelCase property naming so consumers in Python/JS/etc don't
    /// need a property mapper.
    /// </remarks>
    public static string GenerateJsonReport(
        IEnumerable<(string TabName, IReadOnlyList<CapabilityAwareAction> Actions)> tabs,
        IReadOnlyList<CapabilitySurfaceHistory.HistoryEntry>? history = null,
        DateTimeOffset? generatedUtc = null)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        var tabsList = tabs.ToList();
        var rollup = ComputeRollup(tabsList);
        var trend = history is { Count: > 1 }
            ? CapabilitySurfaceHistory.BuildTrendLine(history)
            : string.Empty;

        var doc = new JsonReport(
            GeneratedUtc: (generatedUtc ?? DateTimeOffset.UtcNow).UtcDateTime
                .ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            Rollup: new JsonRollup(
                TotalActions: rollup.TotalActions,
                LiveCount: rollup.LiveCount,
                Phase2PendingCount: rollup.Phase2PendingCount,
                LiveOnlyCount: rollup.LiveOnlyCount,
                MixedCount: rollup.MixedCount,
                OtherCount: rollup.OtherCount,
                LivePercent: rollup.LivePercent),
            Trend: trend,
            Tabs: tabsList.Select(t => new JsonTab(
                TabName: t.TabName,
                Actions: t.Actions.Select(a => new JsonAction(
                    ActionName: a.Name,
                    Badge: a.Badge,
                    Note: a.Note)).ToArray())).ToArray());

        var options = new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
        return System.Text.Json.JsonSerializer.Serialize(doc, options) + "\n";
    }

    private sealed record JsonReport(
        string GeneratedUtc,
        JsonRollup Rollup,
        string Trend,
        IReadOnlyList<JsonTab> Tabs);

    private sealed record JsonRollup(
        int TotalActions,
        int LiveCount,
        int Phase2PendingCount,
        int LiveOnlyCount,
        int MixedCount,
        int OtherCount,
        int LivePercent);

    private sealed record JsonTab(
        string TabName,
        IReadOnlyList<JsonAction> Actions);

    private sealed record JsonAction(
        string ActionName,
        string Badge,
        string Note);

    /// <summary>
    /// Escape a markdown-table cell — pipe characters break table layout
    /// and newlines spill into the next row. Replace both.
    /// </summary>
    private static string EscapeCell(string text)
    {
        if (string.IsNullOrEmpty(text)) return "(none)";
        return text.Replace("|", "\\|").Replace('\n', ' ').Replace('\r', ' ');
    }
}
