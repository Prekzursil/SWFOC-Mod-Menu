using FluentAssertions;
using SwfocTrainer.Runtime.Interop;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ProcessMemoryAccessorCoverageTests
{
    [Fact]
    public void Constructor_ShouldThrow_ForInvalidProcess()
    {
        var act = () => new ProcessMemoryAccessor(-1);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ReadWriteAllocateFree_ShouldRoundTripValues()
    {
        using var accessor = new ProcessMemoryAccessor(Environment.ProcessId);
        var address = accessor.Allocate(64, executable: false);
        address.Should().NotBe(nint.Zero);

        try
        {
            accessor.Write(address, 1337);
            accessor.Read<int>(address).Should().Be(1337);

            var bytes = new byte[] { 1, 2, 3, 4 };
            accessor.WriteBytes(address, bytes, executablePatch: false);
            accessor.ReadBytes(address, bytes.Length).Should().Equal(bytes);

            accessor.WriteBytes(address, Array.Empty<byte>(), executablePatch: true);
        }
        finally
        {
            accessor.Free(address).Should().BeTrue();
        }
    }

    [Fact]
    public void WriteBytes_WithExecutablePatch_ShouldSucceed()
    {
        using var accessor = new ProcessMemoryAccessor(Environment.ProcessId);
        var address = accessor.Allocate(64, executable: true);
        address.Should().NotBe(nint.Zero);

        try
        {
            accessor.WriteBytes(address, new byte[] { 0x90, 0x90, 0xC3 }, executablePatch: true);
            accessor.ReadBytes(address, 3).Should().Equal(0x90, 0x90, 0xC3);
        }
        finally
        {
            accessor.Free(address).Should().BeTrue();
        }
    }

    [Fact]
    public void ReadBytes_ShouldThrow_ForInvalidAddress()
    {
        using var accessor = new ProcessMemoryAccessor(Environment.ProcessId);
        var act = () => accessor.ReadBytes(nint.Zero, 4);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Free_ShouldReturnTrue_ForZeroAddress()
    {
        using var accessor = new ProcessMemoryAccessor(Environment.ProcessId);
        accessor.Free(nint.Zero).Should().BeTrue();
    }
}
