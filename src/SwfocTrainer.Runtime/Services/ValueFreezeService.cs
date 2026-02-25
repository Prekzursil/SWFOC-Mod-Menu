using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;

namespace SwfocTrainer.Runtime.Services;

/// <summary>
/// Background service that continuously re-writes frozen memory values.
/// <para>
/// Regular symbols use a 50 ms <see cref="Timer"/> pulse (plenty fast for values the game doesn't
/// actively overwrite). Symbols registered via <see cref="FreezeIntAggressive"/> use a dedicated
/// high-frequency thread (~1-2 ms writes) backed by <c>timeBeginPeriod(1)</c>, which is fast
/// enough to overpower the game's own float→int credit sync that runs every ~16 ms.
/// </para>
/// </summary>
public sealed class ValueFreezeService : IValueFreezeService
{
    private const int DefaultPulseIntervalMs = 50;

    private readonly IRuntimeAdapter _runtime;
    private readonly ILogger<ValueFreezeService> _logger;
    private readonly ConcurrentDictionary<string, FreezeEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Timer _timer;
    private volatile bool _disposed;

    // ── Aggressive freeze thread ─────────────────────────────────────────
    private readonly ConcurrentDictionary<string, AggressiveFreezeEntry> _aggressiveEntries = new(StringComparer.OrdinalIgnoreCase);
    private Thread? _aggressiveThread;
    private volatile bool _aggressiveRunning;
    private readonly object _aggressiveLock = new();

    public ValueFreezeService(IRuntimeAdapter runtime, ILogger<ValueFreezeService> logger, int pulseIntervalMs)
    {
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
        // Remove from regular entries to avoid double-writing.
        _entries.TryRemove(symbol, out _);

        _aggressiveEntries[symbol] = new AggressiveFreezeEntry(symbol, value);
        EnsureAggressiveThreadRunning();
        _logger.LogInformation("Aggressive freeze registered: {Symbol} = {Value} (int, ~1 ms)", symbol, value);
    }

    public void FreezeFloat(string symbol, float value)
    {
        _entries[symbol] = new FreezeEntry(symbol, FreezeKind.Float, FloatValue: value);
        _logger.LogInformation("Freeze registered: {Symbol} = {Value} (float)", symbol, value);
    }

    public void FreezeBool(string symbol, bool value)
    {
        _entries[symbol] = new FreezeEntry(symbol, FreezeKind.Bool, BoolValue: value);
        _logger.LogInformation("Freeze registered: {Symbol} = {Value} (bool)", symbol, value);
    }

    public bool Unfreeze(string symbol)
    {
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
        return _entries.ContainsKey(symbol) || _aggressiveEntries.ContainsKey(symbol);
    }

    // ── Regular 50 ms pulse (for non-critical symbols) ──────────────────

    private async void PulseCallback(object? state)
    {
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
            catch (Exception ex)
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

    // ── Aggressive ~1 ms thread (for game-overwritten symbols like credits) ──

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint uMilliseconds);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint uMilliseconds);

    private void EnsureAggressiveThreadRunning()
    {
        lock (_aggressiveLock)
        {
            if (_aggressiveRunning)
            {
                return;
            }

            _aggressiveRunning = true;
            _aggressiveThread = new Thread(AggressiveWriteLoop)
            {
                IsBackground = true,
                Priority = ThreadPriority.AboveNormal,
                Name = "ValueFreezeService.AggressiveWriter"
            };
            _aggressiveThread.Start();
        }
    }

    private void StopAggressiveThreadIfEmpty()
    {
        if (_aggressiveEntries.IsEmpty)
        {
            _aggressiveRunning = false;
        }
    }

    private void AggressiveWriteLoop()
    {
        TimeBeginPeriod(1);
        try
        {
            while (_aggressiveRunning && !_disposed)
            {
                if (!_runtime.IsAttached || _aggressiveEntries.IsEmpty)
                {
                    Thread.Sleep(10);
                    continue;
                }

                foreach (var entry in _aggressiveEntries.Values)
                {
                    try
                    {
                        _runtime.WriteAsync(entry.Symbol, entry.Value).GetAwaiter().GetResult();
                    }
                    catch
                    {
                        // Swallow — transient failure, retry on next cycle.
                    }
                }

                Thread.Sleep(1); // ~1-2 ms with timeBeginPeriod(1)
            }
        }
        finally
        {
            TimeEndPeriod(1);
            lock (_aggressiveLock)
            {
                _aggressiveRunning = false;
                _aggressiveThread = null;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _aggressiveRunning = false;
        _timer.Dispose();
        _entries.Clear();
        _aggressiveEntries.Clear();
        _aggressiveThread?.Join(500);
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
