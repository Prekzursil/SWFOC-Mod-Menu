using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Runtime.Interop;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Branch-coverage tests for ProcessMemoryAccessor (internal sealed class).
/// Since the class wraps P/Invoke calls, we test:
/// - Constructor guard (invalid PID → OpenProcess returns 0 → throws)
/// - Dispose idempotency
/// - ReadBytes negative count guard
/// - WriteBytes null/empty guards
/// - Free with nint.Zero returns true
/// - All methods that throw on invalid handles
/// </summary>
public sealed class ProcessMemoryAccessorTests
{
    // ──────────────── Constructor ────────────────

    [Fact]
    public void Constructor_InvalidProcessId_ThrowsInvalidOperationException()
    {
        var act = () => new ProcessMemoryAccessor(99999999);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Failed to open process*");
    }

    // ──────────────── Dispose ────────────────

    [Fact]
    public void Dispose_ZeroHandle_NoOp()
    {
        // Create an uninitialized instance with handle = 0 → Dispose should do nothing
        var accessor = CreateWithHandle(nint.Zero);
        var act = () => accessor.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_NonZeroHandle_ClearsHandle()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        accessor.Dispose();

        // Calling Dispose again should be safe (handle is now 0)
        var act = () => accessor.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_CalledTwice_IsSafe()
    {
        var accessor = CreateWithHandle((nint)0xBEEF);
        accessor.Dispose();
        accessor.Dispose(); // Should not throw
    }

    // ──────────────── ReadBytes ────────────────

    [Fact]
    public void ReadBytes_NegativeCount_ThrowsArgumentOutOfRangeException()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            var act = () => accessor.ReadBytes((nint)0x1000, -1);
            act.Should().Throw<ArgumentOutOfRangeException>()
                .WithParameterName("count");
        }
        finally
        {
            accessor.Dispose();
        }
    }

    [Fact]
    public void ReadBytes_InvalidHandle_ThrowsInvalidOperationException()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            var act = () => accessor.ReadBytes((nint)0x1000, 4);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*ReadProcessMemory*");
        }
        finally
        {
            accessor.Dispose();
        }
    }

    [Fact]
    public void ReadBytes_ZeroCount_InvalidHandle_MaySucceedOrThrow()
    {
        // count = 0 is allowed (not negative). ReadProcessMemory with 0 bytes on an
        // invalid handle may either return true with 0 bytes read (matching count)
        // or return false. We only verify no ArgumentOutOfRangeException is thrown.
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            try
            {
                var result = accessor.ReadBytes((nint)0x1000, 0);
                result.Should().BeEmpty();
            }
            catch (InvalidOperationException)
            {
                // Also acceptable — depends on OS behavior
            }
        }
        finally
        {
            accessor.Dispose();
        }
    }

    // ──────────────── Read<T> ────────────────

    [Fact]
    public void Read_InvalidHandle_ThrowsInvalidOperationException()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            var act = () => accessor.Read<int>((nint)0x1000);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*ReadProcessMemory*");
        }
        finally
        {
            accessor.Dispose();
        }
    }

    // ──────────────── Write<T> ────────────────

    [Fact]
    public void Write_InvalidHandle_ThrowsInvalidOperationException()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            var act = () => accessor.Write((nint)0x1000, 42);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*WriteProcessMemory*");
        }
        finally
        {
            accessor.Dispose();
        }
    }

    // ──────────────── WriteBytes ────────────────

    [Fact]
    public void WriteBytes_NullBuffer_ThrowsArgumentNullException()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            var act = () => accessor.WriteBytes((nint)0x1000, null!, false);
            act.Should().Throw<ArgumentNullException>().WithParameterName("buffer");
        }
        finally
        {
            accessor.Dispose();
        }
    }

    [Fact]
    public void WriteBytes_EmptyBuffer_ReturnsImmediately()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            // Empty buffer → early return, no P/Invoke call
            var act = () => accessor.WriteBytes((nint)0x1000, Array.Empty<byte>(), false);
            act.Should().NotThrow();
        }
        finally
        {
            accessor.Dispose();
        }
    }

    [Fact]
    public void WriteBytes_NonEmptyBuffer_InvalidHandle_Throws()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            var act = () => accessor.WriteBytes((nint)0x1000, new byte[] { 0x90 }, false);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*WriteProcessMemory*");
        }
        finally
        {
            accessor.Dispose();
        }
    }

    [Fact]
    public void WriteBytes_ExecutablePatch_InvalidHandle_Throws()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            // executablePatch = true → tries VirtualProtectEx first
            var act = () => accessor.WriteBytes((nint)0x1000, new byte[] { 0x90 }, true);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*VirtualProtectEx*");
        }
        finally
        {
            accessor.Dispose();
        }
    }

    // ──────────────── Allocate ────────────────

    [Fact]
    public void Allocate_InvalidHandle_ReturnsZero()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            var result = accessor.Allocate((nuint)4096, executable: false);
            result.Should().Be(nint.Zero);
        }
        finally
        {
            accessor.Dispose();
        }
    }

    [Fact]
    public void Allocate_Executable_InvalidHandle_ReturnsZero()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            var result = accessor.Allocate((nuint)4096, executable: true);
            result.Should().Be(nint.Zero);
        }
        finally
        {
            accessor.Dispose();
        }
    }

    [Fact]
    public void Allocate_WithPreferredAddress_InvalidHandle_ReturnsZero()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            var result = accessor.Allocate((nuint)4096, executable: false, preferredAddress: (nint)0x10000);
            result.Should().Be(nint.Zero);
        }
        finally
        {
            accessor.Dispose();
        }
    }

    // ──────────────── Free ────────────────

    [Fact]
    public void Free_ZeroAddress_ReturnsTrue()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            var result = accessor.Free(nint.Zero);
            result.Should().BeTrue();
        }
        finally
        {
            accessor.Dispose();
        }
    }

    [Fact]
    public void Free_NonZeroAddress_InvalidHandle_ReturnsFalse()
    {
        var accessor = CreateWithHandle((nint)0xDEAD);
        try
        {
            var result = accessor.Free((nint)0x1000);
            result.Should().BeFalse();
        }
        finally
        {
            accessor.Dispose();
        }
    }

    // ──────────────── helpers ────────────────

    /// <summary>
    /// Creates a ProcessMemoryAccessor with a specific handle value, bypassing the constructor.
    /// </summary>
    private static ProcessMemoryAccessor CreateWithHandle(nint handle)
    {
        var accessor = (ProcessMemoryAccessor)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(ProcessMemoryAccessor));
        var handleField = typeof(ProcessMemoryAccessor).GetField(
            "_handle", BindingFlags.NonPublic | BindingFlags.Instance);
        handleField!.SetValue(accessor, handle);
        return accessor;
    }
}
