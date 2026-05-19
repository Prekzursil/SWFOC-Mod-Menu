using System.Collections.Generic;
using System.Linq;

namespace SwfocTrainer.Core.Diagnostics;

/// <summary>
/// 2026-04-27 (iter 55) — reusable capability-aware action descriptor.
/// Wraps a UI button's display name + the SWFOC_X helper names it
/// invokes, computes a composite badge against
/// <see cref="CapabilityStatusCatalog"/>, and exposes
/// <see cref="IsMixed"/> / <see cref="IsAllLive"/> flags so the host
/// view-model can render warnings without re-deriving status.
/// </summary>
/// <remarks>
/// Originated as the iter-54 <c>CompositeMetadata</c> private to
/// <c>QuickActionsTabViewModel</c>. Extracted in iter 55 so Battle
/// Control (and any future tab with composite actions) can apply the
/// same per-button badge + tab-level "N/N OK ≠ engine effect" warning
/// pattern.
/// </remarks>
public sealed class CapabilityAwareAction
{
    public CapabilityAwareAction(string name, params string[] helperNames)
        : this(name, (IReadOnlyList<string>)helperNames) { }

    public CapabilityAwareAction(string name, IReadOnlyList<string> helperNames)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(helperNames);
        Name = name;
        HelperNames = helperNames;
        Badge = CapabilityStatusCatalog.ComposeBadge(helperNames.ToArray());
        var entries = helperNames.Select(CapabilityStatusCatalog.Lookup).ToList();
        IsMixed = entries.Select(e => e.Status).Distinct().Count() > 1;
        IsAllLive = entries.All(e => e.Status == CapabilityStatus.Live);
        // 2026-04-27 (iter 61): Note propagation — operator-facing
        // explanation of why a button has its badge. Pulled from the
        // catalog's per-helper Note field. When a single helper backs
        // the action, surfaces that helper's note. When multiple helpers
        // back it, joins distinct notes with " · " for the mixed case.
        var notes = entries
            .Select(e => e.Note)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Note = notes.Length switch
        {
            0 => string.Empty,
            1 => notes[0]!,
            _ => string.Join(" · ", notes),
        };
    }

    /// <summary>
    /// Build a descriptor from a sequence of full Lua command strings of
    /// the form <c>"return SWFOC_X(args)"</c>. Used by tabs that store
    /// composites as Lua arrays (Quick Actions tab) so they don't have to
    /// duplicate the parser.
    /// </summary>
    public static CapabilityAwareAction FromLuaCommands(string name, IReadOnlyList<string> luaCommands)
    {
        ArgumentNullException.ThrowIfNull(luaCommands);
        var helperNames = luaCommands.Select(ExtractHelperName).ToArray();
        return new CapabilityAwareAction(name, helperNames);
    }

    public string Name { get; }
    public IReadOnlyList<string> HelperNames { get; }

    /// <summary>"LIVE" or "MIXED (m/n LIVE)" or "PHASE 2 PENDING" etc.</summary>
    public string Badge { get; }

    /// <summary>True when helpers have heterogeneous catalog statuses.</summary>
    public bool IsMixed { get; }

    /// <summary>True when every helper is catalogued as LIVE.</summary>
    public bool IsAllLive { get; }

    /// <summary>
    /// 2026-04-27 (iter 61): operator-facing explanation derived from
    /// the per-helper <c>Note</c> in <see cref="CapabilityStatusCatalog"/>.
    /// Empty when no helper has a note. The XAML binds this as the
    /// per-button tooltip so operators see "BLOCKED-NO-RVA — AI scheduler"
    /// (etc.) at hover time without leaving the editor.
    /// </summary>
    public string Note { get; }

    /// <summary>
    /// 2026-04-27 (iter 62): full operator-facing string for per-button
    /// tooltips. Format: <c>"Name · Badge · Note"</c> — drops the
    /// trailing segment when Note is empty. XAML binds this directly
    /// without needing a converter.
    /// </summary>
    public string Tooltip => string.IsNullOrEmpty(Note)
        ? $"{Name} · {Badge}"
        : $"{Name} · {Badge} · {Note}";

    /// <summary>
    /// Strips <c>"return "</c> prefix and <c>(args)</c> suffix from a Lua
    /// command, returning just the helper name. Used by
    /// <see cref="FromLuaCommands"/> and reused by callers that need to
    /// summarise a failed-call name.
    /// </summary>
    public static string ExtractHelperName(string luaCommand)
    {
        if (string.IsNullOrEmpty(luaCommand)) return string.Empty;
        var space = luaCommand.IndexOf(' ');
        var paren = luaCommand.IndexOf('(');
        var start = space + 1;
        var end = paren > 0 ? paren : luaCommand.Length;
        if (start >= end) return luaCommand;
        return luaCommand.Substring(start, end - start);
    }
}
