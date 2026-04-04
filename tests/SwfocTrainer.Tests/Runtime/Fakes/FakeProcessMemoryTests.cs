using FluentAssertions;
using SwfocTrainer.Tests.Runtime.Fakes;
using Xunit;

namespace SwfocTrainer.Tests.Runtime.Fakes;

public sealed class FakeProcessMemoryTests
{
    [Fact]
    public void IsValid_ShouldReturnTrue_WhenNotDisposedAndNotInvalid()
    {
        using var memory = new FakeProcessMemory();

        memory.IsValid.Should().BeTrue();
    }

    [Fact]
    public void IsValid_ShouldReturnFalse_AfterDispose()
    {
        var memory = new FakeProcessMemory();
        memory.Dispose();

        memory.IsValid.Should().BeFalse();
    }

    [Fact]
    public void IsValid_ShouldReturnFalse_WhenSimulateInvalid()
    {
        using var memory = new FakeProcessMemory { SimulateInvalid = true };

        memory.IsValid.Should().BeFalse();
    }

    [Fact]
    public void ReadWrite_Int32_ShouldRoundTrip()
    {
        using var memory = new FakeProcessMemory();
        var address = (nint)0x1000;

        memory.Write(address, 42);
        var result = memory.Read<int>(address);

        result.Should().Be(42);
    }

    [Fact]
    public void ReadWrite_Float_ShouldRoundTrip()
    {
        using var memory = new FakeProcessMemory();
        var address = (nint)0x2000;

        memory.Write(address, 3.14f);
        var result = memory.Read<float>(address);

        result.Should().Be(3.14f);
    }

    [Fact]
    public void ReadWrite_Long_ShouldRoundTrip()
    {
        using var memory = new FakeProcessMemory();
        var address = (nint)0x3000;

        memory.Write(address, 0x7FFF_FFFF_FFFF_FFFFL);
        var result = memory.Read<long>(address);

        result.Should().Be(0x7FFF_FFFF_FFFF_FFFFL);
    }

    [Fact]
    public void ReadWrite_Byte_ShouldRoundTrip()
    {
        using var memory = new FakeProcessMemory();
        var address = (nint)0x4000;

        memory.Write(address, (byte)0xAB);
        var result = memory.Read<byte>(address);

        result.Should().Be(0xAB);
    }

    [Fact]
    public void ReadBytes_ShouldReturnZeros_ForUninitializedMemory()
    {
        using var memory = new FakeProcessMemory();
        var address = (nint)0x5000;

        var result = memory.ReadBytes(address, 4);

        result.Should().HaveCount(4);
        result.Should().AllBeEquivalentTo((byte)0);
    }

    [Fact]
    public void WriteBytes_ThenReadBytes_ShouldRoundTrip()
    {
        using var memory = new FakeProcessMemory();
        var address = (nint)0x6000;
        var data = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF };

        memory.WriteBytes(address, data, executablePatch: false);
        var result = memory.ReadBytes(address, 4);

        result.Should().BeEquivalentTo(data);
    }

    [Fact]
    public void WriteBytes_WithExecutablePatch_ShouldStillWork()
    {
        using var memory = new FakeProcessMemory();
        var address = (nint)0x7000;
        var data = new byte[] { 0xE9, 0x00, 0x10, 0x00, 0x00 };

        memory.WriteBytes(address, data, executablePatch: true);
        var result = memory.ReadBytes(address, 5);

        result.Should().BeEquivalentTo(data);
    }

    [Fact]
    public void WriteBytes_EmptyBuffer_ShouldNotThrow()
    {
        using var memory = new FakeProcessMemory();

        var act = () => memory.WriteBytes((nint)0x1000, Array.Empty<byte>(), false);

        act.Should().NotThrow();
        memory.WriteCount.Should().Be(0);
    }

    [Fact]
    public void WriteBytes_NullBuffer_ShouldThrow()
    {
        using var memory = new FakeProcessMemory();

        var act = () => memory.WriteBytes((nint)0x1000, null!, false);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReadBytes_NegativeCount_ShouldThrow()
    {
        using var memory = new FakeProcessMemory();

        var act = () => memory.ReadBytes((nint)0x1000, -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Read_AfterDispose_ShouldThrow()
    {
        var memory = new FakeProcessMemory();
        memory.Dispose();

        var act = () => memory.Read<int>((nint)0x1000);

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Write_AfterDispose_ShouldThrow()
    {
        var memory = new FakeProcessMemory();
        memory.Dispose();

        var act = () => memory.Write((nint)0x1000, 42);

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Read_WhenSimulateInvalid_ShouldThrow()
    {
        using var memory = new FakeProcessMemory { SimulateInvalid = true };

        var act = () => memory.Read<int>((nint)0x1000);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Simulated*");
    }

    [Fact]
    public void Write_WhenSimulateInvalid_ShouldThrow()
    {
        using var memory = new FakeProcessMemory { SimulateInvalid = true };

        var act = () => memory.WriteBytes((nint)0x1000, new byte[] { 1 }, false);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Simulated*");
    }

    [Fact]
    public void Allocate_ShouldReturnNonZeroAddress()
    {
        using var memory = new FakeProcessMemory();

        var address = memory.Allocate(4096, executable: true);

        address.Should().NotBe(nint.Zero);
    }

    [Fact]
    public void Allocate_MultipleCallsShouldReturnDifferentAddresses()
    {
        using var memory = new FakeProcessMemory();

        var a1 = memory.Allocate(4096, executable: true);
        var a2 = memory.Allocate(4096, executable: false);

        a1.Should().NotBe(a2);
    }

    [Fact]
    public void Allocate_WithPreferredAddress_ShouldReturnPreferred()
    {
        using var memory = new FakeProcessMemory();
        var preferred = (nint)0xBEEF_0000;

        var address = memory.Allocate(4096, executable: true, preferredAddress: preferred);

        address.Should().Be(preferred);
    }

    [Fact]
    public void Allocate_WhenSimulateInvalid_ShouldReturnZero()
    {
        using var memory = new FakeProcessMemory { SimulateInvalid = true };

        var address = memory.Allocate(4096, executable: true);

        address.Should().Be(nint.Zero);
    }

    [Fact]
    public void Free_ZeroAddress_ShouldReturnTrue()
    {
        using var memory = new FakeProcessMemory();

        memory.Free(nint.Zero).Should().BeTrue();
    }

    [Fact]
    public void Free_ValidAddress_ShouldReturnTrue()
    {
        using var memory = new FakeProcessMemory();

        memory.Free((nint)0x1000).Should().BeTrue();
    }

    [Fact]
    public void Free_WhenSimulateFreeFail_ShouldReturnFalse()
    {
        using var memory = new FakeProcessMemory { SimulateFreeFail = true };

        memory.Free((nint)0x1000).Should().BeFalse();
    }

    [Fact]
    public void Seed_ShouldPrePopulateMemory()
    {
        using var memory = new FakeProcessMemory();
        var address = (nint)0x8000;
        memory.Seed(address, new byte[] { 0x48, 0x8B, 0x74, 0x24 });

        var result = memory.ReadBytes(address, 4);

        result.Should().BeEquivalentTo(new byte[] { 0x48, 0x8B, 0x74, 0x24 });
    }

    [Fact]
    public void SeedTyped_ShouldPrePopulateWithValue()
    {
        using var memory = new FakeProcessMemory();
        var address = (nint)0x9000;
        memory.Seed(address, 12345);

        var result = memory.Read<int>(address);

        result.Should().Be(12345);
    }

    [Fact]
    public void WriteCount_ShouldTrackWrites()
    {
        using var memory = new FakeProcessMemory();

        memory.Write((nint)0x1000, 1);
        memory.Write((nint)0x2000, 2);

        memory.WriteCount.Should().Be(2);
    }

    [Fact]
    public void ReadCount_ShouldTrackReads()
    {
        using var memory = new FakeProcessMemory();
        memory.Seed((nint)0x1000, 42);

        _ = memory.Read<int>((nint)0x1000);
        _ = memory.Read<int>((nint)0x1000);

        memory.ReadCount.Should().Be(2);
    }

    [Fact]
    public void OverlappingWrites_ShouldPreserveLast()
    {
        using var memory = new FakeProcessMemory();
        var address = (nint)0xA000;

        memory.Write(address, 100);
        memory.Write(address, 200);

        memory.Read<int>(address).Should().Be(200);
    }

    [Fact]
    public void Allocate_AfterDispose_ShouldThrow()
    {
        var memory = new FakeProcessMemory();
        memory.Dispose();

        var act = () => memory.Allocate(4096, executable: true);

        act.Should().Throw<ObjectDisposedException>();
    }
}
