using System.Collections.Concurrent;
using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Branch-coverage sweep for ValueFreezeService — targets all uncovered
/// freeze/unfreeze, pulse callback, aggressive thread, dispose, and error branches.
/// </summary>
public sealed class ValueFreezeServiceBranchCoverageTests : IDisposable
{
    private readonly StubRuntimeAdapter _runtime = new();
    private readonly StubLogger _logger = new();

    public void Dispose()
    {
        // Intentionally left empty — individual tests dispose their own service instances.
    }

    private static async Task WaitForWrittenSymbolAsync(StubRuntimeAdapter runtime, string symbol, int timeoutMs = 1000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (runtime.WrittenSymbols.Any(written => string.Equals(written, symbol, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            await Task.Delay(20);
        }
    }

    // ── Constructor branches ───────────────────────────────────────────────

    [Fact]
    public void Constructor_ShouldThrow_WhenRuntimeIsNull()
    {
        var act = () => new ValueFreezeService(null!, _logger);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenLoggerIsNull()
    {
        var act = () => new ValueFreezeService(_runtime, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_TwoParam_ShouldCreateInstance()
    {
        using var svc = new ValueFreezeService(_runtime, _logger);
        svc.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ThreeParam_ShouldCreateInstanceWithCustomInterval()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, 100);
        svc.Should().NotBeNull();
    }

    // ── FreezeInt branches ─────────────────────────────────────────────────

    [Fact]
    public void FreezeInt_ShouldThrow_WhenSymbolIsNull()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        var act = () => svc.FreezeInt(null!, 100);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FreezeInt_ShouldRegisterSymbol()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeInt("credits", 1000);
        svc.IsFrozen("credits").Should().BeTrue();
        svc.GetFrozenSymbols().Should().Contain("credits");
    }

    [Fact]
    public void FreezeInt_ShouldOverwritePreviousValue()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeInt("credits", 1000);
        svc.FreezeInt("credits", 2000);
        svc.IsFrozen("credits").Should().BeTrue();
    }

    // ── FreezeFloat branches ──────────────────────────────────────────────

    [Fact]
    public void FreezeFloat_ShouldThrow_WhenSymbolIsNull()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        var act = () => svc.FreezeFloat(null!, 1.0f);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FreezeFloat_ShouldRegisterSymbol()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeFloat("timer", 99.5f);
        svc.IsFrozen("timer").Should().BeTrue();
    }

    // ── FreezeBool branches ───────────────────────────────────────────────

    [Fact]
    public void FreezeBool_ShouldThrow_WhenSymbolIsNull()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        var act = () => svc.FreezeBool(null!, true);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FreezeBool_ShouldRegisterTrueValue()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeBool("fog_reveal", true);
        svc.IsFrozen("fog_reveal").Should().BeTrue();
    }

    [Fact]
    public void FreezeBool_ShouldRegisterFalseValue()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeBool("ai_enabled", false);
        svc.IsFrozen("ai_enabled").Should().BeTrue();
    }

    // ── FreezeIntAggressive branches ──────────────────────────────────────

    [Fact]
    public void FreezeIntAggressive_ShouldThrow_WhenSymbolIsNull()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        var act = () => svc.FreezeIntAggressive(null!, 100);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FreezeIntAggressive_ShouldRegisterInAggressiveEntries()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeIntAggressive("credits", 1000);
        svc.IsFrozen("credits").Should().BeTrue();
        svc.GetFrozenSymbols().Should().Contain("credits");
    }

    [Fact]
    public void FreezeIntAggressive_ShouldRemoveFromRegularEntries()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        // First register as regular
        svc.FreezeInt("credits", 1000);
        svc.IsFrozen("credits").Should().BeTrue();
        // Then upgrade to aggressive — should remove from regular
        svc.FreezeIntAggressive("credits", 2000);
        svc.IsFrozen("credits").Should().BeTrue();
    }

    [Fact]
    public void FreezeIntAggressive_CalledTwice_ShouldNotStartSecondThread()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeIntAggressive("credits", 1000);
        svc.FreezeIntAggressive("timer", 500);
        svc.IsFrozen("credits").Should().BeTrue();
        svc.IsFrozen("timer").Should().BeTrue();
    }

    // ── Unfreeze branches ─────────────────────────────────────────────────

    [Fact]
    public void Unfreeze_ShouldThrow_WhenSymbolIsNull()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        var act = () => svc.Unfreeze(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Unfreeze_ShouldReturnTrue_WhenRegularEntryRemoved()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeInt("credits", 1000);
        svc.Unfreeze("credits").Should().BeTrue();
        svc.IsFrozen("credits").Should().BeFalse();
    }

    [Fact]
    public void Unfreeze_ShouldReturnTrue_WhenAggressiveEntryRemoved()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeIntAggressive("credits", 1000);
        svc.Unfreeze("credits").Should().BeTrue();
        svc.IsFrozen("credits").Should().BeFalse();
    }

    [Fact]
    public void Unfreeze_ShouldReturnFalse_WhenSymbolNotFrozen()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.Unfreeze("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void Unfreeze_ShouldStopAggressiveThread_WhenLastAggressiveEntryRemoved()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeIntAggressive("credits", 1000);
        svc.Unfreeze("credits").Should().BeTrue();
        // Aggressive entries should be empty, thread should stop
        svc.IsFrozen("credits").Should().BeFalse();
    }

    // ── UnfreezeAll branches ──────────────────────────────────────────────

    [Fact]
    public void UnfreezeAll_ShouldClearAllEntries()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeInt("credits", 1000);
        svc.FreezeFloat("timer", 99.5f);
        svc.FreezeBool("fog", true);
        svc.FreezeIntAggressive("fast_credits", 2000);

        svc.UnfreezeAll();

        svc.IsFrozen("credits").Should().BeFalse();
        svc.IsFrozen("timer").Should().BeFalse();
        svc.IsFrozen("fog").Should().BeFalse();
        svc.IsFrozen("fast_credits").Should().BeFalse();
        svc.GetFrozenSymbols().Should().BeEmpty();
    }

    [Fact]
    public void UnfreezeAll_ShouldWorkWhenNoEntriesExist()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.UnfreezeAll();
        svc.GetFrozenSymbols().Should().BeEmpty();
    }

    // ── IsFrozen branches ─────────────────────────────────────────────────

    [Fact]
    public void IsFrozen_ShouldThrow_WhenSymbolIsNull()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        var act = () => svc.IsFrozen(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void IsFrozen_ShouldReturnFalse_WhenNotFrozen()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.IsFrozen("nonexistent").Should().BeFalse();
    }

    [Fact]
    public void IsFrozen_ShouldReturnTrue_ForRegularEntry()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeInt("credits", 100);
        svc.IsFrozen("credits").Should().BeTrue();
    }

    [Fact]
    public void IsFrozen_ShouldReturnTrue_ForAggressiveEntry()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeIntAggressive("credits", 100);
        svc.IsFrozen("credits").Should().BeTrue();
    }

    // ── GetFrozenSymbols branches ─────────────────────────────────────────

    [Fact]
    public void GetFrozenSymbols_ShouldReturnEmpty_WhenNothingFrozen()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.GetFrozenSymbols().Should().BeEmpty();
    }

    [Fact]
    public void GetFrozenSymbols_ShouldDeduplicateAcrossRegularAndAggressive()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeInt("credits", 100);
        svc.FreezeIntAggressive("timer", 200);
        var symbols = svc.GetFrozenSymbols();
        symbols.Should().HaveCount(2);
        symbols.Should().Contain("credits");
        symbols.Should().Contain("timer");
    }

    // ── PulseCallback branches ────────────────────────────────────────────

    [Fact]
    public async Task PulseCallback_ShouldWriteIntValue_WhenAttached()
    {
        _runtime.SetAttached(true);
        using var svc = new ValueFreezeService(_runtime, _logger, 50);
        svc.FreezeInt("credits", 1000);

        await WaitForWrittenSymbolAsync(_runtime, "credits");
        _runtime.WrittenSymbols.Should().Contain("credits");
    }

    [Fact]
    public async Task PulseCallback_ShouldWriteFloatValue_WhenAttached()
    {
        _runtime.SetAttached(true);
        using var svc = new ValueFreezeService(_runtime, _logger, 50);
        svc.FreezeFloat("timer", 99.5f);

        await WaitForWrittenSymbolAsync(_runtime, "timer");
        _runtime.WrittenSymbols.Should().Contain("timer");
    }

    [Fact]
    public async Task PulseCallback_ShouldWriteBoolValue_WhenAttached()
    {
        _runtime.SetAttached(true);
        using var svc = new ValueFreezeService(_runtime, _logger, 50);
        svc.FreezeBool("fog_reveal", true);

        await WaitForWrittenSymbolAsync(_runtime, "fog_reveal");
        _runtime.WrittenSymbols.Should().Contain("fog_reveal");
    }

    [Fact]
    public async Task PulseCallback_ShouldWriteBoolFalseAsZeroByte()
    {
        _runtime.SetAttached(true);
        using var svc = new ValueFreezeService(_runtime, _logger, 50);
        svc.FreezeBool("ai_enabled", false);

        await WaitForWrittenSymbolAsync(_runtime, "ai_enabled");
        _runtime.WrittenSymbols.Should().Contain("ai_enabled");
    }

    [Fact]
    public async Task PulseCallback_ShouldNotWrite_WhenNotAttached()
    {
        _runtime.SetAttached(false);
        using var svc = new ValueFreezeService(_runtime, _logger, 50);
        svc.FreezeInt("credits", 1000);

        await Task.Delay(200);

        _runtime.WrittenSymbols.Should().BeEmpty();
    }

    [Fact]
    public async Task PulseCallback_ShouldNotWrite_WhenDisposed()
    {
        _runtime.SetAttached(true);
        var svc = new ValueFreezeService(_runtime, _logger, 50);
        svc.FreezeInt("credits", 1000);
        svc.Dispose();

        _runtime.ClearWrittenSymbols();
        await Task.Delay(200);

        _runtime.WrittenSymbols.Should().BeEmpty();
    }

    [Fact]
    public async Task PulseCallback_ShouldNotWrite_WhenEntriesEmpty()
    {
        _runtime.SetAttached(true);
        using var svc = new ValueFreezeService(_runtime, _logger, 50);
        // No entries frozen

        await Task.Delay(200);

        _runtime.WrittenSymbols.Should().BeEmpty();
    }

    [Fact]
    public async Task PulseCallback_ShouldSwallowInvalidOperationException()
    {
        _runtime.SetAttached(true);
        _runtime.SetThrowOnWrite(new InvalidOperationException("test error"));
        using var svc = new ValueFreezeService(_runtime, _logger, 50);
        svc.FreezeInt("credits", 1000);

        await Task.Delay(200);

        // Should not throw, entry should still be frozen
        svc.IsFrozen("credits").Should().BeTrue();
    }

    [Fact]
    public async Task PulseCallback_ShouldSwallowWin32Exception()
    {
        _runtime.SetAttached(true);
        _runtime.SetThrowOnWrite(new System.ComponentModel.Win32Exception(5, "access denied"));
        using var svc = new ValueFreezeService(_runtime, _logger, 50);
        svc.FreezeInt("credits", 1000);

        await Task.Delay(200);

        // Should not throw, entry should still be frozen
        svc.IsFrozen("credits").Should().BeTrue();
    }

    // ── Dispose branches ──────────────────────────────────────────────────

    [Fact]
    public void Dispose_ShouldClearAllEntries()
    {
        var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeInt("credits", 1000);
        svc.FreezeIntAggressive("fast", 2000);
        svc.Dispose();

        svc.GetFrozenSymbols().Should().BeEmpty();
    }

    [Fact]
    public void Dispose_CalledTwice_ShouldNotThrow()
    {
        var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.Dispose();
        var act = () => svc.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_ShouldStopAggressiveThread()
    {
        var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeIntAggressive("credits", 1000);
        svc.Dispose();
        // Should not throw and aggressive thread should be stopped
        svc.IsFrozen("credits").Should().BeFalse();
    }

    // ── AggressiveWriteLoop branches (via integration) ────────────────────

    [Fact]
    public async Task AggressiveWriteLoop_ShouldWriteValue_WhenAttached()
    {
        _runtime.SetAttached(true);
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeIntAggressive("credits", 5000);

        await WaitForWrittenSymbolAsync(_runtime, "credits");
        _runtime.WrittenSymbols.Should().Contain("credits");
    }

    [Fact]
    public async Task AggressiveWriteLoop_ShouldNotWrite_WhenNotAttached()
    {
        _runtime.SetAttached(false);
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeIntAggressive("credits", 5000);

        await Task.Delay(200);

        _runtime.WrittenSymbols.Should().BeEmpty();
    }

    [Fact]
    public async Task AggressiveWriteLoop_ShouldSwallowInvalidOperationException()
    {
        _runtime.SetAttached(true);
        _runtime.SetThrowOnWrite(new InvalidOperationException("test"));
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeIntAggressive("credits", 5000);

        await Task.Delay(200);

        svc.IsFrozen("credits").Should().BeTrue();
    }

    [Fact]
    public async Task AggressiveWriteLoop_ShouldSwallowWin32Exception()
    {
        _runtime.SetAttached(true);
        _runtime.SetThrowOnWrite(new System.ComponentModel.Win32Exception(5));
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeIntAggressive("credits", 5000);

        await Task.Delay(200);

        svc.IsFrozen("credits").Should().BeTrue();
    }

    [Fact]
    public async Task AggressiveWriteLoop_ShouldSleepWhenNotAttached()
    {
        _runtime.SetAttached(false);
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeIntAggressive("credits", 5000);

        // Give the thread time to hit the sleep branch
        await Task.Delay(100);

        svc.IsFrozen("credits").Should().BeTrue();
        _runtime.WrittenSymbols.Should().BeEmpty();
    }

    [Fact]
    public async Task AggressiveWriteLoop_ShouldSleepWhenEntriesEmpty()
    {
        _runtime.SetAttached(true);
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeIntAggressive("credits", 5000);
        svc.Unfreeze("credits");

        await Task.Delay(100);

        // Thread should be sleeping since entries are empty
        _runtime.ClearWrittenSymbols();
        await Task.Delay(100);
        // After clearing and waiting, no new writes should occur
    }

    // ── Mixed regular + aggressive ────────────────────────────────────────

    [Fact]
    public void GetFrozenSymbols_ShouldIncludeBothRegularAndAggressive()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeInt("a", 1);
        svc.FreezeFloat("b", 2.0f);
        svc.FreezeBool("c", true);
        svc.FreezeIntAggressive("d", 4);

        var symbols = svc.GetFrozenSymbols();
        symbols.Should().HaveCount(4);
    }

    [Fact]
    public void Unfreeze_ShouldRemoveOnlyTargetSymbol()
    {
        using var svc = new ValueFreezeService(_runtime, _logger, int.MaxValue);
        svc.FreezeInt("a", 1);
        svc.FreezeInt("b", 2);
        svc.Unfreeze("a");
        svc.IsFrozen("a").Should().BeFalse();
        svc.IsFrozen("b").Should().BeTrue();
    }

    // ── Stubs ─────────────────────────────────────────────────────────────

    private sealed class StubRuntimeAdapter : IRuntimeAdapter
    {
        private volatile bool _isAttached;
        private volatile Exception? _throwOnWrite;
        private readonly ConcurrentBag<string> _writtenSymbols = new();

        public bool IsAttached => _isAttached;
        public AttachSession? CurrentSession => null;
        public IReadOnlyCollection<string> WrittenSymbols => _writtenSymbols.ToArray();

        public void SetAttached(bool attached) => _isAttached = attached;
        public void SetThrowOnWrite(Exception? ex) => _throwOnWrite = ex;
        public void ClearWrittenSymbols()
        {
            while (_writtenSymbols.TryTake(out _)) { }
        }

        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged
            => throw new NotImplementedException();

        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged
        {
            if (_throwOnWrite is not null)
            {
                throw _throwOnWrite;
            }

            _writtenSymbols.Add(symbol);
            return Task.CompletedTask;
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();

        public Task DetachAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class StubLogger : ILogger<ValueFreezeService>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Intentionally empty — test logger.
        }
    }
}
