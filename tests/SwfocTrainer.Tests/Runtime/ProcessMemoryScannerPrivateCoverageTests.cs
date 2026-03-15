using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Runtime.Interop;
using SwfocTrainer.Runtime.Scanning;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ProcessMemoryScannerPrivateCoverageTests
{
    [Fact]
    public void EnsureBufferSize_ShouldReuseOrResizeBuffer()
    {
        var method = typeof(ProcessMemoryScanner).GetMethod("EnsureBufferSize", BindingFlags.NonPublic | BindingFlags.Static)!;
        var original = new byte[16];

        var reused = (byte[])method.Invoke(null, new object?[] { original, 16 })!;
        var resized = (byte[])method.Invoke(null, new object?[] { original, 8 })!;

        ReferenceEquals(reused, original).Should().BeTrue();
        ReferenceEquals(resized, original).Should().BeFalse();
        resized.Length.Should().Be(8);
    }

    [Fact]
    public void TryReadChunk_ShouldReturnFalse_WhenReadProcessMemoryFails()
    {
        var method = typeof(ProcessMemoryScanner).GetMethod("TryReadChunk", BindingFlags.NonPublic | BindingFlags.Static)!;
        var args = new object?[] { nint.Zero, (nint)0x1000, 0L, new byte[16], 16, 0 };

        var ok = (bool)method.Invoke(null, args)!;

        ok.Should().BeFalse();
        args[5].Should().Be(0);
    }

    [Theory]
    [InlineData(NativeMethods.PageReadOnly, true)]
    [InlineData(NativeMethods.PageReadWrite, true)]
    [InlineData(NativeMethods.PageNoAccess, false)]
    [InlineData(NativeMethods.PageGuard, false)]
    public void IsReadable_ShouldRespectProtectionFlags(uint protection, bool expected)
    {
        var method = typeof(ProcessMemoryScanner).GetMethod("IsReadable", BindingFlags.NonPublic | BindingFlags.Static)!;

        var actual = (bool)method.Invoke(null, new object?[] { protection })!;

        actual.Should().Be(expected);
    }

    [Theory]
    [InlineData(NativeMethods.PageReadOnly, false)]
    [InlineData(NativeMethods.PageReadWrite, true)]
    [InlineData(NativeMethods.PageExecuteReadWrite, true)]
    public void IsWritable_ShouldRespectProtectionFlags(uint protection, bool expected)
    {
        var method = typeof(ProcessMemoryScanner).GetMethod("IsWritable", BindingFlags.NonPublic | BindingFlags.Static)!;

        var actual = (bool)method.Invoke(null, new object?[] { protection })!;

        actual.Should().Be(expected);
    }

    [Fact]
    public void TryAdvanceAddress_ShouldHandleNormalAndOverflowCases()
    {
        var method = typeof(ProcessMemoryScanner).GetMethod("TryAdvanceAddress", BindingFlags.NonPublic | BindingFlags.Static)!;

        var successArgs = new object?[] { (nint)0x1000, 0x2000L, nint.Zero };
        var success = (bool)method.Invoke(null, successArgs)!;

        success.Should().BeTrue();
        successArgs[2].Should().Be((nint)0x3000);

        var overflowArgs = new object?[] { nint.MaxValue, 1L, nint.Zero };
        var overflowSuccess = (bool)method.Invoke(null, overflowArgs)!;
        overflowSuccess.Should().BeTrue();
    }

    [Fact]
    public void ScanInt32_AndScanFloatApprox_ShouldReturnEmpty_WhenMaxResultsNonPositive()
    {
        ProcessMemoryScanner.ScanInt32(Environment.ProcessId, value: 1, writableOnly: false, maxResults: 0, CancellationToken.None)
            .Should().BeEmpty();

        ProcessMemoryScanner.ScanFloatApprox(Environment.ProcessId, value: 1.5f, tolerance: -1f, writableOnly: false, maxResults: 0, CancellationToken.None)
            .Should().BeEmpty();
    }

    [Fact]
    public void IsScannableRegion_ShouldRespectStateAndWritableRequirements()
    {
        var method = typeof(ProcessMemoryScanner).GetMethod("IsScannableRegion", BindingFlags.NonPublic | BindingFlags.Static)!;
        var readableWritable = new NativeMethods.MemoryBasicInformation
        {
            State = NativeMethods.MemCommit,
            Protect = NativeMethods.PageReadWrite,
            RegionSize = (nuint)4096,
            BaseAddress = (nint)0x1000
        };

        ((bool)method.Invoke(null, new object?[] { readableWritable, false })!).Should().BeTrue();
        ((bool)method.Invoke(null, new object?[] { readableWritable, true })!).Should().BeTrue();

        var noCommit = readableWritable;
        noCommit.State = 0;
        ((bool)method.Invoke(null, new object?[] { noCommit, false })!).Should().BeFalse();

        var readOnly = readableWritable;
        readOnly.Protect = NativeMethods.PageReadOnly;
        ((bool)method.Invoke(null, new object?[] { readOnly, true })!).Should().BeFalse();
    }

    [Fact]
    public void ScanInt32_ShouldThrow_WhenProcessCannotBeOpened()
    {
        var act = () => ProcessMemoryScanner.ScanInt32(-1, value: 12345, writableOnly: false, maxResults: 1, CancellationToken.None);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ScanFloatApprox_ShouldNormalizeNegativeTolerance_BeforeScan()
    {
        var act = () => ProcessMemoryScanner.ScanFloatApprox(-1, value: 5.5f, tolerance: -1f, writableOnly: false, maxResults: 1, CancellationToken.None);
        act.Should().Throw<InvalidOperationException>();
    }
}
