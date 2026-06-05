using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class SignatureResolverAddressingTests
{
    // ────────────────────────── TryResolveAddress ──────────────────────────

    [Fact]
    public void TryResolveAddress_NullSignature_ThrowsArgumentNullException()
    {
        var act = () => SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(null!, (nint)0x1000, (nint)0x400000, new byte[64]), out _, out _);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryResolveAddress_NullModuleBytes_ThrowsArgumentNullException()
    {
        var sig = new SignatureSpec("test", "AA BB", 0, SignatureAddressMode.HitPlusOffset);

        var act = () => SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, (nint)0x1000, (nint)0x400000, null!), out _, out _);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryResolveAddress_HitPlusOffset_ReturnsHitPlusOffset()
    {
        var sig = new SignatureSpec("test", "AA BB", Offset: 4, AddressMode: SignatureAddressMode.HitPlusOffset);
        var moduleBytes = new byte[64];
        var hitAddress = (nint)0x401000;
        var baseAddress = (nint)0x400000;

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out var resolved, out var diagnostics);

        result.Should().BeTrue();
        resolved.Should().Be(hitAddress + 4);
        diagnostics.Should().BeNull();
    }

    [Fact]
    public void TryResolveAddress_HitPlusOffset_ZeroOffset()
    {
        var sig = new SignatureSpec("test", "AA BB", Offset: 0, AddressMode: SignatureAddressMode.HitPlusOffset);
        var moduleBytes = new byte[64];
        var hitAddress = (nint)0x401000;
        var baseAddress = (nint)0x400000;

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out var resolved, out _);

        result.Should().BeTrue();
        resolved.Should().Be(hitAddress);
    }

    [Fact]
    public void TryResolveAddress_ReadAbsolute32_ValidBytes_ReturnsDecodedAddress()
    {
        var sig = new SignatureSpec("test", "AA BB", Offset: 2, AddressMode: SignatureAddressMode.ReadAbsolute32AtOffset);
        var baseAddress = (nint)0x400000;
        var hitAddress = (nint)(0x400000 + 0x10);
        var moduleBytes = new byte[64];
        // Place a non-zero uint at index (hitOffset + Offset) = (0x10 + 2) = 0x12
        BitConverter.GetBytes(0xDEADBEEF).CopyTo(moduleBytes, 0x12);

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out var resolved, out var diagnostics);

        result.Should().BeTrue();
        resolved.Should().Be(unchecked((nint)(long)0xDEADBEEF));
        diagnostics.Should().BeNull();
    }

    [Fact]
    public void TryResolveAddress_ReadAbsolute32_NullAddress_ReturnsFalse()
    {
        var sig = new SignatureSpec("test", "AA BB", Offset: 2, AddressMode: SignatureAddressMode.ReadAbsolute32AtOffset);
        var baseAddress = (nint)0x400000;
        var hitAddress = (nint)(0x400000 + 0x10);
        var moduleBytes = new byte[64];
        // Leave zeros at index 0x12 => decoded address will be 0

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out _, out var diagnostics);

        result.Should().BeFalse();
        diagnostics.Should().Contain("null absolute address");
    }

    [Fact]
    public void TryResolveAddress_ReadAbsolute32_NotEnoughBytes_ReturnsFalse()
    {
        // Module bytes is very short so there aren't enough bytes at the computed index
        var sig = new SignatureSpec("test", "AA BB", Offset: 0, AddressMode: SignatureAddressMode.ReadAbsolute32AtOffset);
        var baseAddress = (nint)0x400000;
        var hitAddress = (nint)(0x400000 + 2); // index = 2
        var moduleBytes = new byte[4]; // index + sizeof(uint) = 6 > 4

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out _, out var diagnostics);

        result.Should().BeFalse();
        diagnostics.Should().Contain("Not enough bytes");
    }

    [Fact]
    public void TryResolveAddress_ReadRipRelative32_ValidBytes_ReturnsResolvedAddress()
    {
        var sig = new SignatureSpec("test", "CC DD", Offset: 2, AddressMode: SignatureAddressMode.ReadRipRelative32AtOffset);
        var baseAddress = (nint)0x400000;
        var hitAddress = (nint)(0x400000 + 0x10);
        var moduleBytes = new byte[64];
        // Place a disp32 at index (hitOffset + Offset) = (0x10 + 2) = 0x12
        var disp32 = 0x100;
        BitConverter.GetBytes(disp32).CopyTo(moduleBytes, 0x12);

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out var resolved, out var diagnostics);

        result.Should().BeTrue();
        // endOfDisp = hitAddress + Offset + sizeof(int) = 0x400010 + 2 + 4 = 0x400016
        // Pattern is "CC DD" -> bytes[0]=0xCC, bytes[1]=0xDD -> no match for 0x80/0x3D or 0xC6/0x05 -> extra = 0
        // resolved = endOfDisp + extra + disp32 = 0x400016 + 0 + 0x100 = 0x400116
        var expectedEndOfDisp = hitAddress + 2 + sizeof(int);
        resolved.Should().Be(expectedEndOfDisp + disp32);
        diagnostics.Should().BeNull();
    }

    [Fact]
    public void TryResolveAddress_ReadRipRelative32_NotEnoughBytes_ReturnsFalse()
    {
        var sig = new SignatureSpec("test", "CC DD", Offset: 0, AddressMode: SignatureAddressMode.ReadRipRelative32AtOffset);
        var baseAddress = (nint)0x400000;
        var hitAddress = (nint)(0x400000 + 2);
        var moduleBytes = new byte[4]; // index = 2, need 2 + 4 = 6 > 4

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out _, out var diagnostics);

        result.Should().BeFalse();
        diagnostics.Should().Contain("Not enough bytes");
    }

    [Fact]
    public void TryResolveAddress_ReadRipRelative32_CmpBytePtr_AddsExtraByte()
    {
        // Pattern "80 3D ?? ?? ?? ?? 00" => cmp byte ptr [rip+disp32], imm8 -> extra = 1
        var sig = new SignatureSpec("test", "80 3D ?? ?? ?? ?? 00", Offset: 2, AddressMode: SignatureAddressMode.ReadRipRelative32AtOffset);
        var baseAddress = (nint)0x400000;
        var hitAddress = (nint)(0x400000 + 0x10);
        var moduleBytes = new byte[64];
        var disp32 = 0x200;
        BitConverter.GetBytes(disp32).CopyTo(moduleBytes, 0x12); // index = 0x10+2 = 0x12

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out var resolved, out _);

        result.Should().BeTrue();
        var expectedEndOfDisp = hitAddress + 2 + sizeof(int);
        // extra = 1 for cmp byte ptr
        resolved.Should().Be(expectedEndOfDisp + 1 + disp32);
    }

    [Fact]
    public void TryResolveAddress_ReadRipRelative32_MovBytePtr_AddsExtraByte()
    {
        // Pattern "C6 05 ?? ?? ?? ?? 01" => mov byte ptr [rip+disp32], imm8 -> extra = 1
        var sig = new SignatureSpec("test", "C6 05 ?? ?? ?? ?? 01", Offset: 2, AddressMode: SignatureAddressMode.ReadRipRelative32AtOffset);
        var baseAddress = (nint)0x400000;
        var hitAddress = (nint)(0x400000 + 0x10);
        var moduleBytes = new byte[64];
        var disp32 = 0x300;
        BitConverter.GetBytes(disp32).CopyTo(moduleBytes, 0x12);

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out var resolved, out _);

        result.Should().BeTrue();
        var expectedEndOfDisp = hitAddress + 2 + sizeof(int);
        resolved.Should().Be(expectedEndOfDisp + 1 + disp32);
    }

    [Fact]
    public void TryResolveAddress_ReadRipRelative32_NegativeDisp32()
    {
        var sig = new SignatureSpec("test", "FF 15 ?? ?? ?? ??", Offset: 2, AddressMode: SignatureAddressMode.ReadRipRelative32AtOffset);
        var baseAddress = (nint)0x400000;
        var hitAddress = (nint)(0x400000 + 0x100);
        var moduleBytes = new byte[0x200];
        var disp32 = -0x50;
        BitConverter.GetBytes(disp32).CopyTo(moduleBytes, 0x102);

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out var resolved, out _);

        result.Should().BeTrue();
        var expectedEndOfDisp = hitAddress + 2 + sizeof(int);
        resolved.Should().Be(expectedEndOfDisp + disp32);
    }

    [Fact]
    public void TryResolveAddress_UnsupportedAddressMode_ReturnsFalse()
    {
        var sig = new SignatureSpec("test", "AA BB", Offset: 0, AddressMode: (SignatureAddressMode)999);
        var baseAddress = (nint)0x400000;
        var hitAddress = (nint)(0x400000 + 0x10);
        var moduleBytes = new byte[64];

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out _, out var diagnostics);

        result.Should().BeFalse();
        diagnostics.Should().Contain("Unsupported");
    }

    // ──────────────── TryComputeValueIndex edge cases ────────────────

    [Fact]
    public void TryResolveAddress_ValueOffsetNegative_ReturnsFalse()
    {
        // Make hitOffset + signature.Offset < 0
        // hitOffset = hitAddress - baseAddress; if hitAddress < baseAddress, hitOffset is negative
        // With hitAddress = 0x400000 and baseAddress = 0x500000 => hitOffset = -0x100000
        // Offset = 0 => valueOffset = -0x100000 => fails
        var sig = new SignatureSpec("test", "AA BB", Offset: 0, AddressMode: SignatureAddressMode.ReadAbsolute32AtOffset);
        var baseAddress = (nint)0x500000;
        var hitAddress = (nint)0x400000;
        var moduleBytes = new byte[64];

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out _, out var diagnostics);

        result.Should().BeFalse();
        diagnostics.Should().Contain("out of range");
    }

    // ──────────────── GuessRipImmediateLength (private) via public integration ────────────────

    [Fact]
    public void TryResolveAddress_RipRelative_ShortPattern_ZeroExtra()
    {
        // Pattern "48 89" -> not 80 3D or C6 05 -> extra = 0
        var sig = new SignatureSpec("test", "48 89 ?? ?? ?? ??", Offset: 2, AddressMode: SignatureAddressMode.ReadRipRelative32AtOffset);
        var baseAddress = (nint)0x400000;
        var hitAddress = (nint)(0x400000 + 0x10);
        var moduleBytes = new byte[64];
        var disp32 = 0x50;
        BitConverter.GetBytes(disp32).CopyTo(moduleBytes, 0x12);

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out var resolved, out _);

        result.Should().BeTrue();
        var expectedEndOfDisp = hitAddress + 2 + sizeof(int);
        resolved.Should().Be(expectedEndOfDisp + disp32);
    }

    [Fact]
    public void TryResolveAddress_RipRelative_SingleBytePattern_ZeroExtra()
    {
        // Pattern with only 1 byte — too short for 0x80,0x3D or 0xC6,0x05 → extra = 0
        var sig = new SignatureSpec("test", "CC", Offset: 0, AddressMode: SignatureAddressMode.ReadRipRelative32AtOffset);
        var baseAddress = (nint)0x400000;
        var hitAddress = (nint)(0x400000 + 0x10);
        var moduleBytes = new byte[64];
        var disp32 = 0x10;
        BitConverter.GetBytes(disp32).CopyTo(moduleBytes, 0x10);

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes), out var resolved, out _);

        result.Should().BeTrue();
        var expectedEndOfDisp = hitAddress + 0 + sizeof(int);
        resolved.Should().Be(expectedEndOfDisp + disp32);
    }
}
