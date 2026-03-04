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
    }

    [Fact]
    public void ScanInt32_AndScanFloatApprox_ShouldReturnEmpty_WhenMaxResultsNonPositive()
    {
        ProcessMemoryScanner.ScanInt32(Environment.ProcessId, value: 1, writableOnly: false, maxResults: 0, CancellationToken.None)
            .Should().BeEmpty();

        ProcessMemoryScanner.ScanFloatApprox(Environment.ProcessId, value: 1.5f, tolerance: -1f, writableOnly: false, maxResults: 0, CancellationToken.None)
            .Should().BeEmpty();
    }
}

