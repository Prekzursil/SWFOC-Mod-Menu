using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class DamageLogService : IDamageLogService
{
    private readonly ILogger<DamageLogService> _logger;
    private readonly List<DamageLogEntry> _accumulated = new();
    private readonly object _lock = new();

    public DamageLogService(ILogger<DamageLogService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public Task<IReadOnlyList<DamageLogEntry>> PollEntriesAsync(
        CancellationToken cancellationToken)
    {
        IReadOnlyList<DamageLogEntry> snapshot;

        lock (_lock)
        {
            snapshot = _accumulated.ToList().AsReadOnly();
        }

        _logger.LogInformation(
            "Polled {Count} accumulated damage log entries",
            snapshot.Count);

        return Task.FromResult(snapshot);
    }

    public Task<BattleStatsSummary> ComputeSummaryAsync(
        IReadOnlyList<DamageLogEntry> entries, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var mvpUnit = ComputeMvpUnit(entries);
        var damagePerFaction = ComputeDamagePerFaction(entries);
        var killsPerFaction = ComputeKillsPerFaction(entries);
        var duration = ComputeBattleDuration(entries);

        var summary = new BattleStatsSummary(
            MvpUnit: mvpUnit,
            DamagePerFaction: damagePerFaction,
            KillsPerFaction: killsPerFaction,
            BattleDuration: duration);

        _logger.LogInformation(
            "Computed battle summary: MVP={Mvp}, Duration={Duration}, Factions={FactionCount}",
            mvpUnit,
            duration,
            damagePerFaction.Count);

        return Task.FromResult(summary);
    }

    /// <summary>
    /// Adds entries to the internal accumulation buffer.
    /// Used by the runtime bridge to push new damage events.
    /// </summary>
    public void AddEntries(IReadOnlyList<DamageLogEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        lock (_lock)
        {
            _accumulated.AddRange(entries);
        }
    }

    /// <summary>
    /// Clears the internal accumulation buffer.
    /// </summary>
    public void ClearEntries()
    {
        lock (_lock)
        {
            _accumulated.Clear();
        }
    }

    internal static string ComputeMvpUnit(IReadOnlyList<DamageLogEntry> entries)
    {
        if (entries.Count == 0)
        {
            return string.Empty;
        }

        var damageByUnit = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (string.IsNullOrEmpty(entry.SourceUnit))
            {
                continue;
            }

            if (!damageByUnit.TryGetValue(entry.SourceUnit, out var current))
            {
                current = 0f;
            }

            damageByUnit[entry.SourceUnit] = current + entry.DamageAmount;
        }

        if (damageByUnit.Count == 0)
        {
            return string.Empty;
        }

        var mvp = string.Empty;
        var maxDamage = float.MinValue;

        foreach (var (unit, total) in damageByUnit)
        {
            if (total > maxDamage)
            {
                maxDamage = total;
                mvp = unit;
            }
        }

        return mvp;
    }

    internal static IReadOnlyDictionary<string, float> ComputeDamagePerFaction(
        IReadOnlyList<DamageLogEntry> entries)
    {
        var damageByFaction = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            var faction = InferFaction(entry.SourceUnit);

            if (!damageByFaction.TryGetValue(faction, out var current))
            {
                current = 0f;
            }

            damageByFaction[faction] = current + entry.DamageAmount;
        }

        return damageByFaction;
    }

    internal static IReadOnlyDictionary<string, int> ComputeKillsPerFaction(
        IReadOnlyList<DamageLogEntry> entries)
    {
        var killsByFaction = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (!string.Equals(entry.DamageType, "kill", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var faction = InferFaction(entry.SourceUnit);

            if (!killsByFaction.TryGetValue(faction, out var current))
            {
                current = 0;
            }

            killsByFaction[faction] = current + 1;
        }

        return killsByFaction;
    }

    internal static TimeSpan ComputeBattleDuration(IReadOnlyList<DamageLogEntry> entries)
    {
        if (entries.Count == 0)
        {
            return TimeSpan.Zero;
        }

        var earliest = entries[0].Timestamp;
        var latest = entries[0].Timestamp;

        for (var i = 1; i < entries.Count; i++)
        {
            var ts = entries[i].Timestamp;

            if (ts < earliest)
            {
                earliest = ts;
            }

            if (ts > latest)
            {
                latest = ts;
            }
        }

        return latest - earliest;
    }

    internal static string InferFaction(string unitName)
    {
        if (string.IsNullOrEmpty(unitName))
        {
            return "Unknown";
        }

        var upper = unitName.ToUpperInvariant();

        if (upper.StartsWith("EMPIRE_", StringComparison.Ordinal)
            || upper.StartsWith("IMPERIAL_", StringComparison.Ordinal))
        {
            return "Empire";
        }

        if (upper.StartsWith("REBEL_", StringComparison.Ordinal))
        {
            return "Rebel";
        }

        if (upper.StartsWith("UNDERWORLD_", StringComparison.Ordinal))
        {
            return "Underworld";
        }

        if (upper.StartsWith("REPUBLIC_", StringComparison.Ordinal))
        {
            return "Republic";
        }

        if (upper.StartsWith("CIS_", StringComparison.Ordinal))
        {
            return "CIS";
        }

        return "Unknown";
    }

    /// <summary>
    /// Builds the Lua command string for enabling or disabling event control.
    /// </summary>
    internal static string BuildEventControlLuaCommand(bool enable)
    {
        return enable ? "SWFOC_EventControl(1)" : "SWFOC_EventControl(0)";
    }
}
