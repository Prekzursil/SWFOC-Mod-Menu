using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;

namespace SwfocTrainer.Runtime.Services;

/// <summary>
/// Background service that continuously re-writes frozen memory values.
/// <para>
/// Regular symbols use a 50 ms <see cref="Timer"/> pulse (plenty fast for values the game doesn't
/// actively overwrite). Symbols registered via <see cref="FreezeIntAggressive"/> use a
/// <see cref="PeriodicTimer"/>-based async pump at a 16 ms cadence (matching the game's
/// ~60 fps frame rate), which is sufficient to win the race against the game's per-frame
/// float→int credit sync without affecting host-system responsiveness.
/// </para>
/// <para>
/// History: an earlier implementation used <c>timeBeginPeriod(1)</c> + a tight <c>Thread.Sleep(1)</c>
/// loop at <c>ThreadPriority.AboveNormal</c> with sync-over-async IPC. That combination raised
/// the system-wide OS scheduler tick rate, hammered the bridge ~1000×/sec, and could freeze the
/// host PC. See <c>knowledge-base/freeze_audit_2026-04-27.md</c> for details. The current
/// implementation deliberately avoids <c>winmm.dll</c>, custom thread priorities, and busy loops.
/// </para>
/// </summary>
public sealed class ValueFreezeService : IValueFreezeService, IAsyncDisposable
{
    private const int DefaultPulseIntervalMs = 50;
    private const int AggressivePulseIntervalMs = 16; // ~60 fps — matches game frame rate

    private readonly IRuntimeAdapter _runtime;
    private readonly ILogger<ValueFreezeService> _logger;
    private readonly ConcurrentDictionary<string, FreezeEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;
    private volatile bool _disposed;

    // ── Aggressive freeze pump (PeriodicTimer-based, 16ms cadence) ───────
    private readonly ConcurrentDictionary<string, AggressiveFreezeEntry> _aggressiveEntries = new(StringComparer.OrdinalIgnoreCase);
    private Task? _aggressivePump;
    private CancellationTokenSource? _aggressiveCts;
    private readonly object _aggressiveLock = new();

    public ValueFreezeService(IRuntimeAdapter runtime, ILogger<ValueFreezeService> logger, int pulseIntervalMs)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(logger);
        _runtime = runtime;
        _logger = logger;
        _timer = new Timer(PulseCallback, null, pulseIntervalMs, pulseIntervalMs);
    }

    public ValueFreezeService(IRuntimeAdapter runtime, ILogger<ValueFreezeService> logger)
        : this(runtime, logger, DefaultPulseIntervalMs)
    {
    }

    public IReadOnlyCollection<string> GetFrozenSymbols()
    {
        return _entries.Keys.Concat(_aggressiveEntries.Keys).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public void FreezeInt(string symbol, int value)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        _entries[symbol] = new FreezeEntry(symbol, FreezeKind.Int32, IntValue: value);
        _logger.LogInformation("Freeze registered: {Symbol} = {Value} (int)", symbol, value);
    }

    /// <summary>
    /// Register a high-frequency int freeze that writes at ~1 ms intervals on a dedicated thread.
    /// This is needed for symbols where the game actively overwrites the value every frame (~16 ms),
    /// such as credits (game float→int sync). The normal 50 ms timer cannot win that race.
    /// </summary>
    public void FreezeIntAggressive(string symbol, int value)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        // Remove from regular entries to avoid double-writing.
        _entries.TryRemove(symbol, out _);

        _aggressiveEntries[symbol] = new AggressiveFreezeEntry(symbol, value);
        EnsureAggressiveThreadRunning();
        _logger.LogInformation("Aggressive freeze registered: {Symbol} = {Value} (int, ~1 ms)", symbol, value);
    }

    public void FreezeFloat(string symbol, float value)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        _entries[symbol] = new FreezeEntry(symbol, FreezeKind.Float, FloatValue: value);
        _logger.LogInformation("Freeze registered: {Symbol} = {Value} (float)", symbol, value);
    }

    public void FreezeBool(string symbol, bool value)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        _entries[symbol] = new FreezeEntry(symbol, FreezeKind.Bool, BoolValue: value);
        _logger.LogInformation("Freeze registered: {Symbol} = {Value} (bool)", symbol, value);
    }

    public bool Unfreeze(string symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        var removedRegular = _entries.TryRemove(symbol, out _);
        var removedAggressive = _aggressiveEntries.TryRemove(symbol, out _);
        var removed = removedRegular || removedAggressive;
        if (removed)
        {
            _logger.LogInformation("Freeze removed: {Symbol}", symbol);
        }
        StopAggressiveThreadIfEmpty();
        return removed;
    }

    public void UnfreezeAll()
    {
        var count = _entries.Count + _aggressiveEntries.Count;
        _entries.Clear();
        _aggressiveEntries.Clear();
        StopAggressiveThreadIfEmpty();
        _logger.LogInformation("All freezes cleared ({Count} entries)", count);
    }

    public bool IsFrozen(string symbol)
    {
        ArgumentNullException.ThrowIfNull(symbol);
        return _entries.ContainsKey(symbol) || _aggressiveEntries.ContainsKey(symbol);
    }

    // ── Regular 50 ms pulse (for non-critical symbols) ──────────────────

    private async void PulseCallback(object? state)
    {
        _ = state;
        if (_disposed || !_runtime.IsAttached || _entries.IsEmpty)
        {
            return;
        }

        foreach (var entry in _entries.Values)
        {
            try
            {
                await WriteEntryAsync(entry);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogDebug(ex, "Freeze pulse write failed for {Symbol}", entry.Symbol);
                // Don't remove the entry — it may succeed on the next pulse (e.g., transient memory protection issue).
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                _logger.LogDebug(ex, "Freeze pulse write failed for {Symbol}", entry.Symbol);
                // Don't remove the entry — it may succeed on the next pulse (e.g., transient memory protection issue).
            }
        }
    }

    private Task WriteEntryAsync(FreezeEntry entry)
    {
        return entry.Kind switch
        {
            FreezeKind.Int32 => _runtime.WriteAsync(entry.Symbol, entry.IntValue),
            FreezeKind.Float => _runtime.WriteAsync(entry.Symbol, entry.FloatValue),
            FreezeKind.Bool => _runtime.WriteAsync(entry.Symbol, entry.BoolValue ? (byte)1 : (byte)0),
            _ => Task.CompletedTask
        };
    }

    // ── Aggressive 16 ms pump (for game-overwritten symbols like credits) ──
    //
    // Implementation note: a previous version of this file used winmm.dll's
    // timeBeginPeriod(1) + Thread.Sleep(1) at AboveNormal priority + sync-over-
    // async IPC. That triple anti-pattern raised the system-wide OS scheduler
    // tick to 1ms, preempted the desktop compositor, and could freeze the host
    // machine. The current implementation uses standard async/await with
    // PeriodicTimer at the natural 16 ms (60 fps) game-frame cadence, which is
    // sufficient to win the race against the game's per-frame float→int sync.

    private void EnsureAggressiveThreadRunning()
    {
        lock (_aggressiveLock)
        {
            if (_aggressivePump is not null && !_aggressivePump.IsCompleted)
            {
                return;
            }

            _aggressiveCts?.Dispose();
            _aggressiveCts = new CancellationTokenSource();
            _aggressivePump = Task.Run(() => AggressivePumpAsync(_aggressiveCts.Token));
        }
    }

    private void StopAggressiveThreadIfEmpty()
    {
        if (_aggressiveEntries.IsEmpty)
        {
            lock (_aggressiveLock)
            {
                _aggressiveCts?.Cancel();
            }
        }
    }

    private async Task AggressivePumpAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(AggressivePulseIntervalMs));
        try
        {
            while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (_disposed)
                {
                    return;
                }
                if (!_runtime.IsAttached || _aggressiveEntries.IsEmpty)
                {
                    continue;
                }

                foreach (var entry in _aggressiveEntries.Values)
                {
                    if (ct.IsCancellationRequested)
                    {
                        return;
                    }
                    try
                    {
                        await _runtime.WriteAsync(entry.Symbol, entry.Value)
                            .ConfigureAwait(false);
                    }
                    catch (InvalidOperationException)
                    {
                        // Transient failure, retry on next tick.
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // Transient failure, retry on next tick.
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path.
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        _entries.Clear();
        _aggressiveEntries.Clear();

        Task? pump;
        CancellationTokenSource? cts;
        lock (_aggressiveLock)
        {
            pump = _aggressivePump;
            cts = _aggressiveCts;
            _aggressivePump = null;
        }
        cts?.Cancel();
        try
        {
            pump?.Wait(TimeSpan.FromMilliseconds(500));
        }
        catch (AggregateException)
        {
            // Pump completed with cancellation — expected.
        }
        cts?.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _timer.Dispose();
        _entries.Clear();
        _aggressiveEntries.Clear();

        Task? pump;
        CancellationTokenSource? cts;
        lock (_aggressiveLock)
        {
            pump = _aggressivePump;
            cts = _aggressiveCts;
            _aggressivePump = null;
        }
        cts?.Cancel();
        if (pump is not null)
        {
            try
            {
                await pump.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown.
            }
        }
        cts?.Dispose();
    }

    private enum FreezeKind { Int32, Float, Bool }

    private sealed record FreezeEntry(
        string Symbol,
        FreezeKind Kind,
        int IntValue = 0,
        float FloatValue = 0f,
        bool BoolValue = false);

    private sealed record AggressiveFreezeEntry(string Symbol, int Value);
}
