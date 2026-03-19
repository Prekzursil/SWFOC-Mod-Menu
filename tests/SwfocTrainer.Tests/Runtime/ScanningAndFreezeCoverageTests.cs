using System.Diagnostics;
using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Scanning;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ScanningAndFreezeCoverageTests
{
    [Fact]
    public void AobPatternParse_ShouldSupportHexAndWildcards()
    {
        var pattern = AobPattern.Parse("AA ?? 10 ? FF");

        pattern.Bytes.Should().Equal(new byte?[] { 0xAA, null, 0x10, null, 0xFF });
    }

    [Fact]
    public void AobScannerFindPattern_ShouldReturnZero_WhenPatternEmpty()
    {
        var memory = new byte[] { 0x00, 0x11, 0x22 };
        var empty = AobPattern.Parse(string.Empty);

        var address = AobScanner.FindPattern(memory, (nint)0x1000, empty);

        address.Should().Be(nint.Zero);
    }

    [Fact]
    public void AobScannerFindPattern_ShouldMatchWildcardPattern()
    {
        var memory = new byte[] { 0x01, 0x02, 0xAB, 0x10, 0x7F, 0x20 };
        var pattern = AobPattern.Parse("AB ?? 7F");

        var address = AobScanner.FindPattern(memory, (nint)0x5000, pattern);

        address.Should().Be((nint)0x5002);
    }

    [Fact]
    public void AobScannerFindPattern_ProcessOverload_ShouldDelegateToMemoryOverload()
    {
        var memory = new byte[] { 0x90, 0x90, 0xCC };
        var pattern = AobPattern.Parse("90 90");

        var address = AobScanner.FindPattern(Process.GetCurrentProcess(), memory, (nint)0x2000, pattern);

        address.Should().Be((nint)0x2000);
    }

    [Fact]
    public void ScanInt32_ShouldReturnEmpty_WhenMaxResultsNonPositive()
    {
        var results = ProcessMemoryScanner.ScanInt32(
            processId: Process.GetCurrentProcess().Id,
            value: 123,
            writableOnly: false,
            maxResults: 0,
            cancellationToken: CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public void ScanFloatApprox_ShouldReturnEmpty_WhenMaxResultsNonPositive()
    {
        var results = ProcessMemoryScanner.ScanFloatApprox(
            processId: Process.GetCurrentProcess().Id,
            value: 1.5f,
            tolerance: 0.1f,
            writableOnly: false,
            maxResults: 0,
            cancellationToken: CancellationToken.None);

        results.Should().BeEmpty();
    }

    [Fact]
    public void ScanFloatApprox_ShouldThrowCancellation_WhenTokenAlreadyCanceled()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => ProcessMemoryScanner.ScanFloatApprox(
            processId: Process.GetCurrentProcess().Id,
            value: 1.5f,
            tolerance: -1f,
            writableOnly: false,
            maxResults: 1,
            cancellationToken: cts.Token);

        act.Should().Throw<OperationCanceledException>();
    }

    [Fact]
    public async Task NoopSdkRuntimeAdapter_ShouldReturnUnavailableResult()
    {
        var adapter = new NoopSdkRuntimeAdapter();
        var request = new SdkOperationRequest(
            OperationId: "spawn_tactical_entity",
            Payload: new JsonObject(),
            IsMutation: true,
            RuntimeMode: RuntimeMode.TacticalLand,
            ProfileId: "base_swfoc");

        var result = await adapter.ExecuteAsync(request);

        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be(CapabilityReasonCode.OperationNotMapped);
        result.CapabilityState.Should().Be(SdkCapabilityStatus.Unavailable);
        result.Diagnostics!["operationId"]!.ToString().Should().Be("spawn_tactical_entity");
        result.Diagnostics!["profileId"]!.ToString().Should().Be("base_swfoc");
    }

    [Fact]
    public async Task ValueFreezeService_ShouldWriteIntFloatAndBoolEntries_OnPulse()
    {
        var runtime = new RuntimeAdapterStub(isAttached: true);
        using var service = new ValueFreezeService(runtime, NullLogger<ValueFreezeService>.Instance, pulseIntervalMs: 5);

        service.FreezeInt("credits", 5000);
        service.FreezeFloat("speed", 1.25f);
        service.FreezeBool("fog", true);

        await WaitUntilAsync(() => runtime.Writes.Count >= 3, TimeSpan.FromSeconds(2));

        runtime.Writes.Should().ContainKey("credits");
        runtime.Writes.Should().ContainKey("speed");
        runtime.Writes.Should().ContainKey("fog");

        service.IsFrozen("credits").Should().BeTrue();
        service.Unfreeze("credits").Should().BeTrue();
        service.Unfreeze("missing").Should().BeFalse();

        var frozen = service.GetFrozenSymbols();
        frozen.Should().Contain(new[] { "speed", "fog" });
    }

    [Fact]
    public void ValueFreezeService_ShouldStartAndStopAggressiveEntries()
    {
        var runtime = new RuntimeAdapterStub(isAttached: false);
        using var service = new ValueFreezeService(runtime, NullLogger<ValueFreezeService>.Instance, pulseIntervalMs: 50);

        service.FreezeIntAggressive("credits", 9999);
        service.IsFrozen("credits").Should().BeTrue();

        service.UnfreezeAll();
        service.IsFrozen("credits").Should().BeFalse();

        service.Dispose();
        service.Dispose();
    }

    [Fact]
    public void ValueFreezeService_ShouldSkipPulse_WhenNotAttached()
    {
        var runtime = new RuntimeAdapterStub(isAttached: false);
        using var service = new ValueFreezeService(runtime, NullLogger<ValueFreezeService>.Instance, pulseIntervalMs: 50);
        service.FreezeInt("credits", 100);

        var pulseCallback = typeof(ValueFreezeService)
            .GetMethod("PulseCallback", BindingFlags.Instance | BindingFlags.NonPublic);
        pulseCallback.Should().NotBeNull();

        pulseCallback!.Invoke(service, new object?[] { null });

        runtime.Writes.Should().BeEmpty();
    }

    private static async Task WaitUntilAsync(Func<bool> predicate, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (!predicate())
        {
            if (sw.Elapsed > timeout)
            {
                throw new TimeoutException("Predicate was not satisfied in time.");
            }

            await Task.Delay(10);
        }
    }

    private sealed class RuntimeAdapterStub : IRuntimeAdapter
    {
        public RuntimeAdapterStub(bool isAttached)
        {
            IsAttached = isAttached;
        }

        public Dictionary<string, object?> Writes { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool IsAttached { get; set; }

        public AttachSession? CurrentSession => null;

        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged
        {
            _ = symbol;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged
        {
            _ = cancellationToken;
            Writes[symbol] = value;
            return Task.CompletedTask;
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            throw new NotSupportedException();
        }

        public Task DetachAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            IsAttached = false;
            return Task.CompletedTask;
        }
    }
}


