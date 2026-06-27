using System.Collections.Generic;
using System.IO;
using System.Linq;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-27 (iter 45) — single entry in <see cref="V2BridgeAdapter.RecentCalls"/>.
/// Captures the four pieces of information operator-debugging needs: when, what,
/// did-it-work, and how-fast.
/// </summary>
public sealed record BridgeActivityEntry(
    DateTimeOffset Timestamp,
    string LuaCommand,
    bool Succeeded,
    string ResponseOrError,
    long DurationMs)
{
    /// <summary>
    /// 2026-04-27 (iter 65): operator-facing duration bucket for the
    /// activity DataGrid color-coding. Fast = green-tinted (typical bridge
    /// round-trip is &lt;50ms over the named pipe), Normal = uncoloured,
    /// Slow = amber, VerySlow = red. The XAML binds a brush via a value
    /// converter keyed to this token so the ms column pops visually
    /// without operators having to sort by it.
    /// </summary>
    public string DurationCategory => DurationMs switch
    {
        < 50 => "Fast",
        < 200 => "Normal",
        < 500 => "Slow",
        _ => "VerySlow",
    };
}

/// <summary>
/// 2026-04-28 (iter 74) — per-command aggregation row for the
/// group-by-command DataGrid. Operators see "which helper dominated"
/// at a glance instead of scrolling 50 per-call rows.
/// </summary>
public sealed record BridgeCommandSummary(
    string Command,
    int CallCount,
    int SuccessCount,
    int FailureCount,
    double AverageDurationMs,
    long MaxDurationMs)
{
    /// <summary>Success rate as a fraction in [0, 1]; 0 when CallCount is 0.</summary>
    public double SuccessRate => CallCount == 0 ? 0d : (double)SuccessCount / CallCount;
}

/// <summary>
/// 2026-04-27 (iter 48) — at-a-glance bridge-health summary computed from the
/// recent-calls ring buffer. The Diagnostics tab surfaces this as a single
/// status-line above the activity DataGrid so the operator gets totals
/// without scrolling through 50 entries.
/// </summary>
public sealed record BridgeActivityStats(
    int TotalCalls,
    int SuccessCount,
    int FailureCount,
    double AverageDurationMs,
    string? TopCommand,
    int TopCommandCount)
{
    /// <summary>Success rate as a fraction in [0, 1]; 0 when TotalCalls is 0.</summary>
    public double SuccessRate => TotalCalls == 0 ? 0d : (double)SuccessCount / TotalCalls;

    /// <summary>
    /// 2026-04-28 (iter 70) — operator-facing health bucket for the
    /// bottom status bar dot. Healthy = green (failure rate &lt; 5% or
    /// fewer than 5 calls so far), Degraded = amber (5-15%), Failing =
    /// red (&gt;= 15%). The "fewer than 5 calls" floor avoids the tiny
    /// ring buffer (e.g., one call that happened to fail) flagging the
    /// editor red — the dot only goes amber/red after operators have
    /// actually exercised the bridge.
    /// </summary>
    public string HealthCategory
    {
        get
        {
            if (TotalCalls < 5) return "Healthy";
            var failRate = 1.0 - SuccessRate;
            if (failRate >= 0.15) return "Failing";
            if (failRate >= 0.05) return "Degraded";
            return "Healthy";
        }
    }
}

// ============================================================================
// V2BridgeAdapter
//
// Thin adapter that satisfies the ILuaBridgeExecutor contract using ONLY
// NamedPipeLuaBridgeClient. The real LuaBridgeExecutor in SwfocTrainer.Core
// pulls in TrainerOrchestrator + IProfileRepository + backend routing, which
// is a deep dependency tree we do not need for V2's direct-to-bridge flow.
//
// This adapter exists so we can reuse the existing feature services
// (GodModeService, OneHitKillService, EconomyService, ...) without rebuilding
// their Lua-command-building logic. Each service takes an ILuaBridgeExecutor
// via its constructor; we hand them a V2BridgeAdapter instance and they send
// their pre-built Lua snippets through the pipe.
//
// Zero references to SdkOperationRouter, ActionSymbolRegistry, or any symbol
// table. All state mutation happens inside powrprof.dll's Lua hook.
// ============================================================================

public sealed class V2BridgeAdapter : ILuaBridgeExecutor
{
    private readonly NamedPipeLuaBridgeClient _pipe;
    // 2026-04-27 (iter 45) — in-memory ring buffer of recent SendRawAsync
    // round-trips. Surfaced via RecentCalls for the Diagnostics tab. Cap is
    // 50; concurrent writes are serialised on _activityLock since multiple
    // VMs share one adapter and may emit overlapping calls. Read access is
    // a snapshot copy so UI binding doesn't see torn state.
    private readonly object _activityLock = new();
    private readonly LinkedList<BridgeActivityEntry> _recentCalls = new();
    private const int RecentCallsCap = 50;

    public V2BridgeAdapter(NamedPipeLuaBridgeClient pipe)
    {
        ArgumentNullException.ThrowIfNull(pipe);
        _pipe = pipe;
    }

    /// <summary>
    /// Pipe name the adapter is bound to. Surfaced so the diagnostics tab can
    /// display it without reaching around the adapter.
    /// </summary>
    public string PipeName => _pipe.PipeName;

    /// <summary>
    /// Most-recent-first snapshot of the last 50 SendRawAsync calls. Read-only;
    /// returned as a fresh list per call so callers can iterate without
    /// synchronisation. Useful for "the button isn't doing anything" debugging:
    /// operator opens Diagnostics, sees whether the call even reached the
    /// bridge.
    /// </summary>
    public IReadOnlyList<BridgeActivityEntry> RecentCalls
    {
        get
        {
            lock (_activityLock)
            {
                return _recentCalls.ToList();
            }
        }
    }

    /// <summary>
    /// 2026-04-27 (iter 66) — drop every entry in the activity ring buffer.
    /// Operators reproducing a bug often want to clear the log to a known
    /// empty state, then click the suspect button and see only the calls
    /// from that interaction. Fires <see cref="ActivityRecorded"/> with a
    /// sentinel <c>null</c> argument is NOT done — subscribers see the
    /// next real entry instead. The Diagnostics VM re-reads
    /// <see cref="RecentCalls"/> on the next change-notification.
    /// </summary>
    public void ClearActivityLog()
    {
        lock (_activityLock)
        {
            _recentCalls.Clear();
        }
    }

    // 2026-04-28 (iter 75): pinned entries — separate from the
    // iter-45 ring buffer so operators can bookmark interesting calls
    // without losing them as new traffic rotates the ring. Pinned
    // entries persist for the session lifetime; explicit unpin removes
    // them. Bounded at 50 (same as RecentCallsCap) so a runaway pin
    // habit can't grow unbounded.
    private const int PinnedCallsCap = 50;
    private readonly List<BridgeActivityEntry> _pinnedCalls = new();
    private readonly object _pinnedLock = new();

    /// <summary>
    /// 2026-04-28 (iter 75): snapshot of currently-pinned entries.
    /// Bounded at 50; operators get a status-line warning (via the
    /// Diagnostics VM) when the cap is hit.
    /// </summary>
    public IReadOnlyList<BridgeActivityEntry> PinnedCalls
    {
        get
        {
            lock (_pinnedLock) { return _pinnedCalls.ToList(); }
        }
    }

    /// <summary>
    /// Pin an entry so it survives ring rotation. No-ops if the entry
    /// is already pinned (compared by reference equality). Returns
    /// <c>false</c> if the cap was hit and the entry wasn't added.
    /// </summary>
    public bool PinActivity(BridgeActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_pinnedLock)
        {
            if (_pinnedCalls.Contains(entry)) return true;
            if (_pinnedCalls.Count >= PinnedCallsCap) return false;
            _pinnedCalls.Add(entry);
            return true;
        }
    }

    /// <summary>
    /// Unpin an entry. No-ops if the entry isn't currently pinned.
    /// </summary>
    public void UnpinActivity(BridgeActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        lock (_pinnedLock)
        {
            _pinnedCalls.Remove(entry);
        }
    }

    /// <summary>
    /// Clear every pinned entry. Sibling to <see cref="ClearActivityLog"/>;
    /// distinct because operators usually want to clear traffic without
    /// losing their bookmarks.
    /// </summary>
    public void ClearPinnedActivity()
    {
        lock (_pinnedLock) { _pinnedCalls.Clear(); }
    }

    /// <summary>
    /// 2026-04-27 (iter 48) — at-a-glance summary of the current ring buffer.
    /// Computed on demand from a snapshot copy so concurrent calls can't tear
    /// the totals. Returns zeros when the buffer is empty.
    /// </summary>
    public BridgeActivityStats ComputeStats()
    {
        var snap = RecentCalls;
        if (snap.Count == 0)
        {
            return new BridgeActivityStats(0, 0, 0, 0d, null, 0);
        }
        var success = snap.Count(e => e.Succeeded);
        var avgMs = snap.Average(e => (double)e.DurationMs);
        var top = snap
            .GroupBy(e => e.LuaCommand, StringComparer.Ordinal)
            .Select(g => (Command: g.Key, Count: g.Count()))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Command, StringComparer.Ordinal)
            .First();
        return new BridgeActivityStats(
            TotalCalls: snap.Count,
            SuccessCount: success,
            FailureCount: snap.Count - success,
            AverageDurationMs: avgMs,
            TopCommand: top.Command,
            TopCommandCount: top.Count);
    }

    /// <summary>
    /// 2026-04-28 (iter 74) — per-command rollup across the ring buffer.
    /// Returns rows sorted by CallCount descending (dominant helpers
    /// float to the top), then alphabetically by command. Empty list
    /// when the buffer has no entries.
    /// </summary>
    /// <remarks>
    /// Snapshots <see cref="RecentCalls"/> once so concurrent
    /// SendRawAsync calls can't tear the aggregation. Same pattern as
    /// <see cref="ComputeStats"/>.
    /// </remarks>
    public IReadOnlyList<BridgeCommandSummary> ComputeCommandSummaries()
    {
        var snap = RecentCalls;
        if (snap.Count == 0) return System.Array.Empty<BridgeCommandSummary>();
        return snap
            .GroupBy(e => e.LuaCommand, StringComparer.Ordinal)
            .Select(g => new BridgeCommandSummary(
                Command: g.Key,
                CallCount: g.Count(),
                SuccessCount: g.Count(e => e.Succeeded),
                FailureCount: g.Count(e => !e.Succeeded),
                AverageDurationMs: g.Average(e => (double)e.DurationMs),
                MaxDurationMs: g.Max(e => e.DurationMs)))
            .OrderByDescending(s => s.CallCount)
            .ThenBy(s => s.Command, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// 2026-04-27 (iter 47) — fires synchronously after each SendRawAsync
    /// records its entry. Subscribers (e.g. <c>DiagnosticsTabViewModel</c>)
    /// can use this to push the new entry into the bound UI without waiting
    /// for the next Refresh tick.
    /// </summary>
    /// <remarks>
    /// Fires on the worker thread that completed the round-trip. Subscribers
    /// that touch UI state must marshal to the dispatcher themselves —
    /// keeping the marshal at the consumer side avoids forcing a Dispatcher
    /// dependency into the adapter.
    /// </remarks>
    public event Action<BridgeActivityEntry>? ActivityRecorded;

    /// <summary>
    /// Runs a direct probe of the bridge pipe: writes <paramref name="luaCommand"/>,
    /// reads the raw ASCII response, and returns the envelope the V2 UI actually
    /// shows. Bypasses the <see cref="ILuaBridgeExecutor"/> contract so the
    /// diagnostics and probe tabs can display the exact bridge bytes.
    /// </summary>
    public async Task<BridgeRoundTripResult> SendRawAsync(string luaCommand, CancellationToken cancellationToken)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var rt = await _pipe.SendAsync(luaCommand, cancellationToken).ConfigureAwait(false);
        sw.Stop();
        RecordActivity(luaCommand, rt, sw.ElapsedMilliseconds);
        return rt;
    }

    private void RecordActivity(string luaCommand, BridgeRoundTripResult rt, long elapsedMs)
    {
        var entry = new BridgeActivityEntry(
            Timestamp: DateTimeOffset.Now,
            LuaCommand: luaCommand,
            Succeeded: rt.Succeeded,
            ResponseOrError: rt.Succeeded ? (rt.Response ?? "(empty)") : (rt.ErrorMessage ?? "(no error message)"),
            DurationMs: elapsedMs);
        AppendToActivityRing(entry);
    }

    /// <summary>
    /// 2026-04-28 (iter 79): test-only helper. Injects a fully-constructed
    /// <see cref="BridgeActivityEntry"/> into the activity ring + fires the
    /// ActivityRecorded event, exactly as a real round-trip would. Used by
    /// dismissal-logic tests that need to control Timestamp + Succeeded
    /// directly. Production callers always go through <see cref="SendRawAsync"/>.
    /// </summary>
    internal void RecordForTest(BridgeActivityEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        AppendToActivityRing(entry);
    }

    private void AppendToActivityRing(BridgeActivityEntry entry)
    {
        lock (_activityLock)
        {
            _recentCalls.AddFirst(entry);
            while (_recentCalls.Count > RecentCallsCap)
            {
                _recentCalls.RemoveLast();
            }
        }
        // 2026-04-27 (iter 47): fire AFTER unlocking. Subscribers may
        // re-enter the adapter (e.g. via NotifyRecentBridgeCallsChanged
        // which reads RecentCalls); firing under the lock would deadlock.
        // Snapshot the delegate before invoking so an unsubscribe between
        // the field read and the call can't NRE.
        var handler = ActivityRecorded;
        handler?.Invoke(entry);
    }

    /// <summary>
    /// Checks whether the bridge pipe is currently reachable. Non-blocking beyond
    /// the underlying client's configured connect timeout.
    /// </summary>
    public bool IsBridgeAvailable() => _pipe.IsBridgeAvailable();

    /// <inheritdoc />
    public async Task<ActionExecutionResult> ExecuteLuaAsync(
        string profileId,
        string luaCommand,
        string featureId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(luaCommand);
        ArgumentNullException.ThrowIfNull(featureId);

        BridgeRoundTripResult roundTrip;
        try
        {
            roundTrip = await _pipe.SendAsync(luaCommand, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException ex)
        {
            return Failure(luaCommand, featureId, profileId, $"Bridge IO error: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return Failure(luaCommand, featureId, profileId, $"Bridge state error: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return Failure(luaCommand, featureId, profileId, $"Bridge access denied: {ex.Message}");
        }

        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["lua_call"] = luaCommand,
            ["feature_id"] = featureId,
            ["profile_id"] = profileId,
            ["bridge_pipe_name"] = _pipe.PipeName,
            ["v5ExecutionSource"] = "V2BridgeAdapter"
        };

        if (roundTrip.Succeeded)
        {
            diagnostics["bridge_response"] = roundTrip.Response;
            return new ActionExecutionResult(
                Succeeded: true,
                Message: roundTrip.Response,
                AddressSource: AddressSource.Signature,
                Diagnostics: diagnostics);
        }

        diagnostics["bridge_error"] = roundTrip.ErrorMessage;
        return new ActionExecutionResult(
            Succeeded: false,
            Message: roundTrip.ErrorMessage,
            AddressSource: AddressSource.None,
            Diagnostics: diagnostics);
    }

    private static ActionExecutionResult Failure(
        string luaCommand, string featureId, string profileId, string message)
    {
        var diagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["lua_call"] = luaCommand,
            ["feature_id"] = featureId,
            ["profile_id"] = profileId,
            ["v5ExecutionSource"] = "V2BridgeAdapter"
        };

        return new ActionExecutionResult(
            Succeeded: false,
            Message: message,
            AddressSource: AddressSource.None,
            Diagnostics: diagnostics);
    }
}
