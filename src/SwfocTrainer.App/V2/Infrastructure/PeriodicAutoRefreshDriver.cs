namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-27 (iter 17): shared driver for the V2 tabs' "auto-refresh"
/// checkboxes. Encapsulates the <see cref="PeriodicTimer"/> + cancellation
/// + lifetime pattern that <see cref="ViewModels.DiagnosticsTabViewModel"/>,
/// <see cref="ViewModels.InspectorTabViewModel"/>, and
/// <see cref="ViewModels.EventStreamViewModel"/> were each open-coding.
/// </summary>
/// <remarks>
/// <para>
/// The host VM constructs one driver, supplies the interval + refresh
/// callback, and exposes <see cref="IsRunning"/> as the WPF binding for
/// the "Auto-refresh" checkbox. Each driver owns exactly one background
/// loop at a time; <see cref="Start"/> is idempotent (cancels any prior
/// loop), <see cref="Stop"/> is idempotent (no-op when stopped), and
/// <see cref="Dispose"/> cancels + disposes cleanly.
/// </para>
/// <para>
/// The refresh callback is invoked on a worker thread (not the WPF
/// dispatcher). Callers that update INPC properties from inside the
/// callback should marshal to the UI thread themselves; the existing
/// callsites use <c>OnPropertyChanged</c> via <c>SetField</c>, which is
/// thread-safe in WPF for property change notifications.
/// </para>
/// <para>
/// Errors thrown by the refresh callback are routed to <c>onError</c> and
/// the loop continues — toggling the checkbox off / on is the recovery
/// path. <see cref="OperationCanceledException"/> is treated as graceful
/// shutdown and never reaches the error sink.
/// </para>
/// </remarks>
public sealed class PeriodicAutoRefreshDriver : IDisposable
{
    private readonly TimeSpan _interval;
    private readonly Func<CancellationToken, Task> _refreshAsync;
    private readonly Func<bool>? _canRefresh;
    private readonly Action<Exception>? _onError;

    private CancellationTokenSource? _cts;
    private bool _disposed;

    public PeriodicAutoRefreshDriver(
        TimeSpan interval,
        Func<CancellationToken, Task> refreshAsync,
        Func<bool>? canRefresh = null,
        Action<Exception>? onError = null)
    {
        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval),
                "Interval must be positive (a zero-or-negative period would spin the worker thread).");
        }
        ArgumentNullException.ThrowIfNull(refreshAsync);

        _interval = interval;
        _refreshAsync = refreshAsync;
        _canRefresh = canRefresh;
        _onError = onError;
    }

    /// <summary>
    /// True while a background refresh loop is alive (Start was called and
    /// neither Stop nor Dispose has fired since).
    /// </summary>
    public bool IsRunning => _cts is { IsCancellationRequested: false };

    /// <summary>
    /// Idempotent: cancels any prior loop and starts a new one. Safe to
    /// call from any thread; the actual refresh runs on a worker thread.
    /// </summary>
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Stop();
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>
    /// Idempotent: cancels the running loop (if any) and disposes its CTS.
    /// Safe to call after <see cref="Dispose"/> (Dispose itself calls Stop).
    /// </summary>
    public void Stop()
    {
        // 2026-04-27 (iter 20): do NOT short-circuit on _disposed here.
        // Dispose() sets _disposed=true BEFORE calling Stop(); if Stop
        // bails early on _disposed, the CTS never gets cancelled and the
        // background loop continues, leaving IsRunning truthy after
        // Dispose. Test PeriodicAutoRefreshDriverTests.Dispose_StopsRunningLoop
        // catches that regression.
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { /* already disposed by a prior Stop */ }
        _cts?.Dispose();
        _cts = null;
    }

    private async Task RunAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(_interval);
            while (!ct.IsCancellationRequested
                && await timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
            {
                if (_canRefresh is not null && !_canRefresh())
                {
                    continue;
                }
                try
                {
                    await _refreshAsync(ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _onError?.Invoke(ex);
                    // Don't crash the loop on a transient error; the user
                    // can toggle off + on to recover from a stuck state.
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on Stop / Dispose.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
    }
}
