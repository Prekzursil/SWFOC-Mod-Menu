using System;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.Infrastructure;

/// <summary>
/// Lifecycle tests for the 2026-04-27 (iter 17)
/// <see cref="PeriodicAutoRefreshDriver"/>. The actual periodic firing
/// is hard to test deterministically without injecting an
/// <c>IPeriodicTimer</c> abstraction; we cover Start / Stop / Dispose /
/// idempotency / canRefresh gating instead, plus a single 200ms wait to
/// confirm the worker actually fires the callback.
/// </summary>
public sealed class PeriodicAutoRefreshDriverTests
{
    [Fact]
    public void Constructor_RejectsNonPositiveInterval()
    {
        Action zero = () => new PeriodicAutoRefreshDriver(
            TimeSpan.Zero, _ => Task.CompletedTask);
        zero.Should().Throw<ArgumentOutOfRangeException>();

        Action negative = () => new PeriodicAutoRefreshDriver(
            TimeSpan.FromSeconds(-1), _ => Task.CompletedTask);
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_RejectsNullCallback()
    {
        Action act = () => new PeriodicAutoRefreshDriver(
            TimeSpan.FromMilliseconds(50), refreshAsync: null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Initial_State_IsNotRunning()
    {
        using var driver = new PeriodicAutoRefreshDriver(
            TimeSpan.FromMilliseconds(50), _ => Task.CompletedTask);
        driver.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Start_FlipsToRunning()
    {
        using var driver = new PeriodicAutoRefreshDriver(
            TimeSpan.FromMilliseconds(50), _ => Task.CompletedTask);
        driver.Start();
        driver.IsRunning.Should().BeTrue();
    }

    [Fact]
    public void Stop_FlipsToNotRunning()
    {
        using var driver = new PeriodicAutoRefreshDriver(
            TimeSpan.FromMilliseconds(50), _ => Task.CompletedTask);
        driver.Start();
        driver.Stop();
        driver.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Stop_BeforeStart_IsSafeNoOp()
    {
        using var driver = new PeriodicAutoRefreshDriver(
            TimeSpan.FromMilliseconds(50), _ => Task.CompletedTask);
        Action act = driver.Stop;
        act.Should().NotThrow();
        driver.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Start_TwiceInARow_IsIdempotent()
    {
        using var driver = new PeriodicAutoRefreshDriver(
            TimeSpan.FromMilliseconds(50), _ => Task.CompletedTask);
        driver.Start();
        driver.Start(); // should cancel prior loop and start fresh
        driver.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task Start_FiresRefreshCallback_WithinOneInterval()
    {
        var counter = 0;
        using var driver = new PeriodicAutoRefreshDriver(
            interval: TimeSpan.FromMilliseconds(40),
            refreshAsync: _ =>
            {
                Interlocked.Increment(ref counter);
                return Task.CompletedTask;
            });
        driver.Start();
        // Wait ~120ms — should give us 2-3 ticks.
        await Task.Delay(150).ConfigureAwait(true);
        driver.Stop();

        counter.Should().BeGreaterOrEqualTo(1,
            "the periodic timer must have fired at least once in 150ms");
    }

    [Fact]
    public async Task CanRefresh_False_SkipsCallback()
    {
        var counter = 0;
        using var driver = new PeriodicAutoRefreshDriver(
            interval: TimeSpan.FromMilliseconds(40),
            refreshAsync: _ =>
            {
                Interlocked.Increment(ref counter);
                return Task.CompletedTask;
            },
            canRefresh: () => false);
        driver.Start();
        await Task.Delay(120).ConfigureAwait(true);
        driver.Stop();

        counter.Should().Be(0, "canRefresh returns false; the callback must never fire");
    }

    [Fact]
    public async Task RefreshCallback_Throws_IsRoutedToOnError_LoopContinues()
    {
        var errors = 0;
        var attempts = 0;
        using var driver = new PeriodicAutoRefreshDriver(
            interval: TimeSpan.FromMilliseconds(40),
            refreshAsync: _ =>
            {
                Interlocked.Increment(ref attempts);
                throw new InvalidOperationException("boom");
            },
            onError: _ => Interlocked.Increment(ref errors));
        driver.Start();
        // 2026-04-27 (iter 37): widened from 150ms to 300ms to absorb
        // CPU contention when this test runs in parallel with the rest of
        // the 7000+ test suite. 40ms interval × 300ms window still gives
        // ~6 expected ticks, while the >1 assertion holds even under heavy
        // load. Earlier 150ms budget flaked at ~1 in 50 full-suite runs.
        await Task.Delay(300).ConfigureAwait(true);
        driver.Stop();
        // 2026-05-19: brief drain delay so any in-flight throw → onError completes
        // before the snapshot read below. Without this, attempts++ can happen
        // between the attempts-snapshot and the errors-snapshot, producing
        // attempts=N+1 / errors=N (1-in-flight race).
        await Task.Delay(50).ConfigureAwait(true);

        attempts.Should().BeGreaterThan(1, "the loop must continue after a callback error");
        errors.Should().BeGreaterOrEqualTo(attempts - 1,
            "every error must reach the onError sink; a single in-flight tolerance "
          + "allows the loop's last iteration to complete after Stop() returns");
    }

    [Fact]
    public void Dispose_StopsRunningLoop()
    {
        var driver = new PeriodicAutoRefreshDriver(
            TimeSpan.FromMilliseconds(50), _ => Task.CompletedTask);
        driver.Start();
        driver.Dispose();
        driver.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Start_AfterDispose_Throws()
    {
        var driver = new PeriodicAutoRefreshDriver(
            TimeSpan.FromMilliseconds(50), _ => Task.CompletedTask);
        driver.Dispose();
        Action act = driver.Start;
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_TwiceInARow_IsSafe()
    {
        var driver = new PeriodicAutoRefreshDriver(
            TimeSpan.FromMilliseconds(50), _ => Task.CompletedTask);
        driver.Dispose();
        Action act = driver.Dispose;
        act.Should().NotThrow();
    }
}
