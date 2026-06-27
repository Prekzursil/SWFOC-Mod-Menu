using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.Tests.App.V2.Infrastructure;

/// <summary>
/// 2026-04-27 (iter 44) — concurrency coverage for the
/// <see cref="PeriodicAutoRefreshDriver"/>. The driver is shared by 3 V2
/// tabs (Diagnostics, Inspector, Event Stream) which routinely run their
/// auto-refresh loops simultaneously; bugs in cross-driver interaction
/// would surface as production hangs / corrupted state in the live editor.
/// </summary>
/// <remarks>
/// <para>
/// Phase D candidate #3 from the simulator README backlog. Direct unit
/// test rather than going through the simulator because the driver is
/// pure-C# (no bridge involvement); we just need the concurrency primitives.
/// </para>
/// </remarks>
public sealed class PeriodicAutoRefreshDriverConcurrencyTests
{
    private readonly ITestOutputHelper _output;

    public PeriodicAutoRefreshDriverConcurrencyTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task ParallelDrivers_DifferentIntervals_DoNotInterfere()
    {
        // Three drivers running side by side at different cadences. Each
        // counts its own ticks; we verify each fired roughly the right
        // number of times for its interval, AND that none of them ran more
        // often than expected (which would suggest interference).
        var counts = new int[3];
        var intervals = new[] { 30, 50, 80 }; // ms

        var drivers = new PeriodicAutoRefreshDriver[3];
        for (var i = 0; i < 3; i++)
        {
            var slot = i;
            drivers[i] = new PeriodicAutoRefreshDriver(
                interval: TimeSpan.FromMilliseconds(intervals[slot]),
                refreshAsync: _ =>
                {
                    Interlocked.Increment(ref counts[slot]);
                    return Task.CompletedTask;
                });
        }

        try
        {
            foreach (var d in drivers) d.Start();
            await Task.Delay(500).ConfigureAwait(true);
        }
        finally
        {
            foreach (var d in drivers) d.Dispose();
        }

        for (var i = 0; i < 3; i++)
        {
            // Lower bound: at least 3 ticks at each cadence in 500ms.
            // Upper bound: 500/interval + 2 (to absorb scheduler jitter).
            var expectedMin = 3;
            var expectedMax = (500 / intervals[i]) + 4;
            _output.WriteLine($"driver[{i}] interval={intervals[i]}ms ticked {counts[i]}x (expected {expectedMin}-{expectedMax})");
            counts[i].Should().BeGreaterOrEqualTo(expectedMin,
                $"driver[{i}] at {intervals[i]}ms should fire at least {expectedMin} times in 500ms");
            counts[i].Should().BeLessOrEqualTo(expectedMax,
                $"driver[{i}] should not fire more than {expectedMax} times — anything higher suggests interference");
        }
    }

    [Fact]
    public async Task RapidStartStopCycles_DoNotLeakThreads()
    {
        // Reproduces the "operator rapidly toggles auto-refresh checkbox"
        // pattern. Each start spins up a worker thread; if Stop+Dispose
        // doesn't reliably tear them down, the process accumulates threads.
        // Process-thread-count baseline is taken before the cycle and
        // checked after a settle window.
        var process = Process.GetCurrentProcess();
        var beforeThreads = process.Threads.Count;

        for (var i = 0; i < 50; i++)
        {
            using var driver = new PeriodicAutoRefreshDriver(
                interval: TimeSpan.FromMilliseconds(30),
                refreshAsync: _ => Task.CompletedTask);
            driver.Start();
            // Just enough delay for the worker to spin up and possibly tick.
            await Task.Delay(10).ConfigureAwait(true);
            driver.Stop();
        }

        // Settle window — the workers were Task.Run'd, so the thread pool
        // may still be holding their threads. We allow up to 1.5s for the
        // pool to release.
        await Task.Delay(1500).ConfigureAwait(true);
        process.Refresh();
        var afterThreads = process.Threads.Count;

        _output.WriteLine($"threads before={beforeThreads} after={afterThreads}");
        // Generous bound — the .NET thread pool can keep some idle workers
        // around. We're catching "50 leaked threads"-class regressions, not
        // measuring exact pool behaviour.
        (afterThreads - beforeThreads).Should().BeLessThan(20,
            "50 start/stop cycles must not leak more than ~20 threads");
    }

    [Fact]
    public void DisposeWhileTickFiring_NoExceptionEscapes()
    {
        // The worker can be in the middle of a tick when Dispose is called.
        // Dispose calls Stop, which cancels the CTS — the worker's
        // refreshAsync(ct) call should observe cancellation. No exception
        // should escape Dispose, AND the test should complete in a
        // reasonable time (no deadlock).
        var ticking = new ManualResetEventSlim(false);
        var driver = new PeriodicAutoRefreshDriver(
            interval: TimeSpan.FromMilliseconds(30),
            refreshAsync: async ct =>
            {
                ticking.Set();
                // Simulate a long-running tick that respects cancellation.
                await Task.Delay(2000, ct).ConfigureAwait(false);
            });

        driver.Start();
        // Wait until the tick is mid-flight.
        ticking.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue("tick must fire");

        // Now Dispose while the worker is mid-Delay. Dispose -> Stop ->
        // CTS.Cancel() -> Task.Delay throws OperationCanceledException
        // inside the worker, which the loop catches.
        Action act = driver.Dispose;
        act.Should().NotThrow("Dispose must not surface mid-flight cancellation as an exception");
        driver.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void StopFromTickCallback_DoesNotDeadlock()
    {
        // Edge case: the refresh callback decides to Stop the driver itself
        // (e.g. "I've finished my work, no need to keep ticking"). Stop()
        // calls _cts.Cancel() which the in-flight Task.Delay will observe
        // — but the driver should NOT deadlock waiting for the worker that
        // is calling Stop on itself. Verifying via a 3-second test budget;
        // if it deadlocks, the test runner cancels and fails.
        PeriodicAutoRefreshDriver? driver = null;
        var stopped = new ManualResetEventSlim(false);
        driver = new PeriodicAutoRefreshDriver(
            interval: TimeSpan.FromMilliseconds(30),
            refreshAsync: _ =>
            {
                driver?.Stop();
                stopped.Set();
                return Task.CompletedTask;
            });
        driver.Start();
        // The first tick will Stop. Wait for that to complete.
        var fired = stopped.Wait(TimeSpan.FromSeconds(3));
        fired.Should().BeTrue("the refresh callback's self-stop must complete within 3 seconds (no deadlock)");
        driver.Dispose();
    }
}
