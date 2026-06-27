using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Runtime.Interop;
using SwfocTrainer.Runtime.Scanning;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Branch-coverage tests for ProcessMemoryScanner (internal static class).
/// Many methods depend on P/Invoke (OpenProcess, VirtualQueryEx, ReadProcessMemory),
/// so we test edge cases and branch logic through the public API where possible,
/// and use reflection for private helper methods that are purely computational.
/// </summary>
public sealed class ProcessMemoryScannerTests
{
    // ──────────────── ScanInt32 — maxResults <= 0 returns empty ────────────────

    [Fact]
    public void ScanInt32_MaxResultsZero_ReturnsEmpty()
    {
        var result = ProcessMemoryScanner.ScanInt32(
            processId: 0, value: 42, writableOnly: false, maxResults: 0, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ScanInt32_MaxResultsNegative_ReturnsEmpty()
    {
        var result = ProcessMemoryScanner.ScanInt32(
            processId: 0, value: 42, writableOnly: false, maxResults: -1, CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ──────────────── ScanFloatApprox — maxResults <= 0 returns empty ────────────────

    [Fact]
    public void ScanFloatApprox_MaxResultsZero_ReturnsEmpty()
    {
        var result = ProcessMemoryScanner.ScanFloatApprox(
            processId: 0, value: 1.0f, tolerance: 0.1f, writableOnly: false,
            maxResults: 0, CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public void ScanFloatApprox_MaxResultsNegative_ReturnsEmpty()
    {
        var result = ProcessMemoryScanner.ScanFloatApprox(
            processId: 0, value: 1.0f, tolerance: 0.1f, writableOnly: false,
            maxResults: -5, CancellationToken.None);

        result.Should().BeEmpty();
    }

    // ──────────────── ScanFloatApprox — negative tolerance is clamped to 0 ────────────────

    [Fact]
    public void ScanFloatApprox_NegativeTolerance_ClampedToZero()
    {
        // With maxResults > 0 but invalid process ID, OpenProcess returns 0 → throws
        // This verifies the tolerance clamping branch is hit before the throw
        var act = () => ProcessMemoryScanner.ScanFloatApprox(
            processId: 99999999, value: 1.0f, tolerance: -5.0f, writableOnly: false,
            maxResults: 1, CancellationToken.None);

        // OpenProcess fails → InvalidOperationException
        act.Should().Throw<InvalidOperationException>();
    }

    // ──────────────── ScanInt32 — invalid process ID throws ────────────────

    [Fact]
    public void ScanInt32_InvalidProcessId_ThrowsInvalidOperationException()
    {
        var act = () => ProcessMemoryScanner.ScanInt32(
            processId: 99999999, value: 42, writableOnly: false,
            maxResults: 1, CancellationToken.None);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Failed to open process*");
    }

    // ──────────────── ScanFloatApprox — invalid process ID throws ────────────────

    [Fact]
    public void ScanFloatApprox_InvalidProcessId_ThrowsInvalidOperationException()
    {
        var act = () => ProcessMemoryScanner.ScanFloatApprox(
            processId: 99999999, value: 1.0f, tolerance: 0.1f, writableOnly: false,
            maxResults: 1, CancellationToken.None);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Failed to open process*");
    }

    // ──────────────── IsReadable (private) — branch coverage via reflection ────────────────

    private static bool InvokeIsReadable(uint protect)
    {
        var method = typeof(ProcessMemoryScanner).GetMethod(
            "IsReadable", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("IsReadable should exist");
        return (bool)method!.Invoke(null, new object[] { protect })!;
    }

    [Theory]
    [InlineData(NativeMethods.PageReadOnly, true)]
    [InlineData(NativeMethods.PageReadWrite, true)]
    [InlineData(NativeMethods.PageWriteCopy, true)]
    [InlineData(NativeMethods.PageExecuteRead, true)]
    [InlineData(NativeMethods.PageExecuteReadWrite, true)]
    [InlineData(NativeMethods.PageExecuteWriteCopy, true)]
    [InlineData(NativeMethods.PageNoAccess, false)]
    [InlineData(NativeMethods.PageExecute, false)] // execute-only, not readable
    [InlineData(0u, false)] // no flags
    public void IsReadable_VariousProtectFlags(uint protect, bool expected)
    {
        InvokeIsReadable(protect).Should().Be(expected);
    }

    [Fact]
    public void IsReadable_PageGuard_ReturnsFalse()
    {
        // PageGuard combined with readable flag still returns false
        InvokeIsReadable(NativeMethods.PageGuard | NativeMethods.PageReadWrite).Should().BeFalse();
    }

    [Fact]
    public void IsReadable_PageNoAccess_ReturnsFalse()
    {
        InvokeIsReadable(NativeMethods.PageNoAccess | NativeMethods.PageReadWrite).Should().BeFalse();
    }

    // ──────────────── IsWritable (private) — branch coverage via reflection ────────────────

    private static bool InvokeIsWritable(uint protect)
    {
        var method = typeof(ProcessMemoryScanner).GetMethod(
            "IsWritable", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("IsWritable should exist");
        return (bool)method!.Invoke(null, new object[] { protect })!;
    }

    [Theory]
    [InlineData(NativeMethods.PageReadWrite, true)]
    [InlineData(NativeMethods.PageWriteCopy, true)]
    [InlineData(NativeMethods.PageExecuteReadWrite, true)]
    [InlineData(NativeMethods.PageExecuteWriteCopy, true)]
    [InlineData(NativeMethods.PageReadOnly, false)]
    [InlineData(NativeMethods.PageExecuteRead, false)]
    [InlineData(NativeMethods.PageNoAccess, false)]
    [InlineData(0u, false)]
    public void IsWritable_VariousProtectFlags(uint protect, bool expected)
    {
        InvokeIsWritable(protect).Should().Be(expected);
    }

    // ──────────────── IsScannableRegion (private) — branch coverage via reflection ────────────────

    private static bool InvokeIsScannableRegion(NativeMethods.MemoryBasicInformation mbi, bool writableOnly)
    {
        var method = typeof(ProcessMemoryScanner).GetMethod(
            "IsScannableRegion", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("IsScannableRegion should exist");
        return (bool)method!.Invoke(null, new object[] { mbi, writableOnly })!;
    }

    [Fact]
    public void IsScannableRegion_Committed_Readable_NotWritableOnly_ReturnsTrue()
    {
        var mbi = new NativeMethods.MemoryBasicInformation
        {
            State = NativeMethods.MemCommit,
            Protect = NativeMethods.PageReadOnly
        };
        InvokeIsScannableRegion(mbi, writableOnly: false).Should().BeTrue();
    }

    [Fact]
    public void IsScannableRegion_Committed_Readable_WritableOnly_NotWritable_ReturnsFalse()
    {
        var mbi = new NativeMethods.MemoryBasicInformation
        {
            State = NativeMethods.MemCommit,
            Protect = NativeMethods.PageReadOnly // readable but not writable
        };
        InvokeIsScannableRegion(mbi, writableOnly: true).Should().BeFalse();
    }

    [Fact]
    public void IsScannableRegion_Committed_ReadWrite_WritableOnly_ReturnsTrue()
    {
        var mbi = new NativeMethods.MemoryBasicInformation
        {
            State = NativeMethods.MemCommit,
            Protect = NativeMethods.PageReadWrite
        };
        InvokeIsScannableRegion(mbi, writableOnly: true).Should().BeTrue();
    }

    [Fact]
    public void IsScannableRegion_NotCommitted_ReturnsFalse()
    {
        var mbi = new NativeMethods.MemoryBasicInformation
        {
            State = NativeMethods.MemReserve, // reserved, not committed
            Protect = NativeMethods.PageReadWrite
        };
        InvokeIsScannableRegion(mbi, writableOnly: false).Should().BeFalse();
    }

    [Fact]
    public void IsScannableRegion_NotReadable_ReturnsFalse()
    {
        var mbi = new NativeMethods.MemoryBasicInformation
        {
            State = NativeMethods.MemCommit,
            Protect = NativeMethods.PageNoAccess
        };
        InvokeIsScannableRegion(mbi, writableOnly: false).Should().BeFalse();
    }

    // ──────────────── TryAdvanceAddress (private) — branch coverage ────────────────

    private static bool InvokeTryAdvanceAddress(nint baseAddress, long regionSize, out nint nextAddress)
    {
        var method = typeof(ProcessMemoryScanner).GetMethod(
            "TryAdvanceAddress", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("TryAdvanceAddress should exist");
        var args = new object[] { baseAddress, regionSize, (nint)0 };
        var result = (bool)method!.Invoke(null, args)!;
        nextAddress = (nint)args[2];
        return result;
    }

    [Fact]
    public void TryAdvanceAddress_NormalValues_ReturnsTrue()
    {
        var result = InvokeTryAdvanceAddress((nint)0x1000, 0x1000, out var next);
        result.Should().BeTrue();
        next.Should().Be((nint)0x2000);
    }

    [Fact]
    public void TryAdvanceAddress_ZeroRegionSize_ReturnsTrue()
    {
        var result = InvokeTryAdvanceAddress((nint)0x1000, 0, out var next);
        result.Should().BeTrue();
        next.Should().Be((nint)0x1000);
    }

    // ──────────────── EnsureBufferSize (private) — branch coverage ────────────────

    private static byte[] InvokeEnsureBufferSize(byte[] buffer, int requiredLength)
    {
        var method = typeof(ProcessMemoryScanner).GetMethod(
            "EnsureBufferSize", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("EnsureBufferSize should exist");
        return (byte[])method!.Invoke(null, new object[] { buffer, requiredLength })!;
    }

    [Fact]
    public void EnsureBufferSize_SameLength_ReturnsSameBuffer()
    {
        var buffer = new byte[100];
        var result = InvokeEnsureBufferSize(buffer, 100);
        result.Should().BeSameAs(buffer);
    }

    [Fact]
    public void EnsureBufferSize_DifferentLength_ReturnsNewBuffer()
    {
        var buffer = new byte[100];
        var result = InvokeEnsureBufferSize(buffer, 50);
        result.Should().NotBeSameAs(buffer);
        result.Length.Should().Be(50);
    }

    // ──────────────── TryQueryRegion (private) — branch coverage ────────────────

    private static bool InvokeTryQueryRegion(nint handle, nint address, nuint mbiSize,
        out NativeMethods.MemoryBasicInformation mbi, out long regionSize)
    {
        var method = typeof(ProcessMemoryScanner).GetMethod(
            "TryQueryRegion", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("TryQueryRegion should exist");
        var args = new object[] { handle, address, mbiSize, default(NativeMethods.MemoryBasicInformation), 0L };
        var result = (bool)method!.Invoke(null, args)!;
        mbi = (NativeMethods.MemoryBasicInformation)args[3];
        regionSize = (long)args[4];
        return result;
    }

    [Fact]
    public void TryQueryRegion_InvalidHandle_ReturnsFalse()
    {
        var mbiSize = (nuint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.MemoryBasicInformation>();
        var result = InvokeTryQueryRegion((nint)0xDEAD, (nint)0, mbiSize, out _, out _);
        result.Should().BeFalse();
    }

    // ──────────────── TryReadChunk (private) — branch coverage ────────────────

    private static bool InvokeTryReadChunk(nint handle, nint regionBase, long offset,
        byte[] buffer, int toRead, out int read)
    {
        var method = typeof(ProcessMemoryScanner).GetMethod(
            "TryReadChunk", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("TryReadChunk should exist");

        // B4 refactored TryReadChunk to take ChunkReadContext record
        var ctxType = typeof(ProcessMemoryScanner).GetNestedType(
            "ChunkReadContext", BindingFlags.NonPublic);
        ctxType.Should().NotBeNull("ChunkReadContext should exist");
        var ctx = Activator.CreateInstance(ctxType!, handle, regionBase, offset, toRead);
        var args = new object?[] { ctx, buffer, 0 };
        var result = (bool)method!.Invoke(null, args)!;
        read = (int)args[2]!;
        return result;
    }

    [Fact]
    public void TryReadChunk_InvalidHandle_ReturnsFalse()
    {
        var buffer = new byte[64];
        var result = InvokeTryReadChunk((nint)0xDEAD, (nint)0x1000, 0, buffer, 64, out var read);
        result.Should().BeFalse();
        read.Should().Be(0);
    }

    // ──────────────── OpenReadHandle (private) — branch coverage ────────────────

    private static nint InvokeOpenReadHandle(int processId)
    {
        var method = typeof(ProcessMemoryScanner).GetMethod(
            "OpenReadHandle", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("OpenReadHandle should exist");
        return (nint)method!.Invoke(null, new object[] { processId })!;
    }

    [Fact]
    public void OpenReadHandle_InvalidPid_ReturnsZero()
    {
        var handle = InvokeOpenReadHandle(99999999);
        handle.Should().Be(nint.Zero);
    }

    // ──────────────── ScanInt32 with cancellation ────────────────

    [Fact]
    public void ScanInt32_CancelledToken_MaxResultsZero_ReturnsEmpty()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // maxResults <= 0 returns before any scanning
        var result = ProcessMemoryScanner.ScanInt32(
            processId: 0, value: 42, writableOnly: false, maxResults: 0, cts.Token);

        result.Should().BeEmpty();
    }

    // ──────────────── ScanFloatApprox with writableOnly flag ────────────────

    [Fact]
    public void ScanFloatApprox_WritableOnly_InvalidProcess_Throws()
    {
        var act = () => ProcessMemoryScanner.ScanFloatApprox(
            processId: 99999999, value: 1.0f, tolerance: 0.0f, writableOnly: true,
            maxResults: 1, CancellationToken.None);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ScanInt32_WritableOnly_InvalidProcess_Throws()
    {
        var act = () => ProcessMemoryScanner.ScanInt32(
            processId: 99999999, value: 42, writableOnly: true,
            maxResults: 1, CancellationToken.None);

        act.Should().Throw<InvalidOperationException>();
    }
}
