using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Wave 8 branch-coverage tests for SignatureResolver partial classes — targets remaining
/// uncovered branches in Addressing, Fallbacks, and SymbolHydration.
/// </summary>
public sealed class SignatureResolverWave8Tests
{
    private static readonly BindingFlags NonPublicStatic =
        BindingFlags.Static | BindingFlags.NonPublic;

    // ══════════════════════════════════════════════════════════════════════
    // Addressing
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryResolveAddress_UnsupportedMode_ReturnsFalse()
    {
        var sig = new SignatureSpec("test", "AA BB", Offset: 4, AddressMode: (SignatureAddressMode)999);
        var moduleBytes = new byte[64];
        var hitAddress = (nint)0x401000;
        var baseAddress = (nint)0x400000;

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes),
            out var resolved, out var diagnostics);

        result.Should().BeFalse();
        resolved.Should().Be(nint.Zero);
        diagnostics.Should().Contain("Unsupported");
    }

    [Fact]
    public void TryResolveAddress_NegativeOffset_OutOfRange_ReturnsFalse()
    {
        // Create a scenario where hitOffset + signature.Offset produces a negative valueOffset
        var sig = new SignatureSpec("test", "AA BB", Offset: -0x2000, AddressMode: SignatureAddressMode.ReadAbsolute32AtOffset);
        var moduleBytes = new byte[64];
        var hitAddress = (nint)0x400100;
        var baseAddress = (nint)0x400000;

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes),
            out _, out var diagnostics);

        result.Should().BeFalse();
        diagnostics.Should().Contain("out of range");
    }

    [Fact]
    public void TryResolveAddress_ReadAbsolute32_ValidBytes_ReturnsAddress()
    {
        var sig = new SignatureSpec("test", "AA BB", Offset: 2, AddressMode: SignatureAddressMode.ReadAbsolute32AtOffset);
        var moduleBytes = new byte[64];
        // Write a non-zero 32-bit address at offset (hit-base+2) = index 0x1002
        // Use hit=base+0 so index=0+2=2
        var hitAddress = (nint)0x400000;
        var baseAddress = (nint)0x400000;
        BitConverter.GetBytes((uint)0x00500000).CopyTo(moduleBytes, 2);

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes),
            out var resolved, out _);

        result.Should().BeTrue();
        resolved.Should().Be((nint)0x00500000);
    }

    [Fact]
    public void TryResolveAddress_ReadAbsolute32_ZeroAddress_ReturnsFalse()
    {
        var sig = new SignatureSpec("test", "AA BB", Offset: 2, AddressMode: SignatureAddressMode.ReadAbsolute32AtOffset);
        var moduleBytes = new byte[64]; // all zeros
        var hitAddress = (nint)0x400000;
        var baseAddress = (nint)0x400000;

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes),
            out _, out var diagnostics);

        result.Should().BeFalse();
        diagnostics.Should().Contain("null absolute");
    }

    [Fact]
    public void TryResolveAddress_ReadAbsolute32_NotEnoughBytes_ReturnsFalse()
    {
        var sig = new SignatureSpec("test", "AA BB", Offset: 0, AddressMode: SignatureAddressMode.ReadAbsolute32AtOffset);
        var moduleBytes = new byte[2]; // too short
        var hitAddress = (nint)0x400000;
        var baseAddress = (nint)0x400000;

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes),
            out _, out var diagnostics);

        result.Should().BeFalse();
        diagnostics.Should().Contain("Not enough bytes");
    }

    [Fact]
    public void TryResolveAddress_ReadRipRelative32_ValidDisp_ReturnsAddress()
    {
        var sig = new SignatureSpec("test", "AA BB CC DD", Offset: 2, AddressMode: SignatureAddressMode.ReadRipRelative32AtOffset);
        var moduleBytes = new byte[64];
        var hitAddress = (nint)0x400000;
        var baseAddress = (nint)0x400000;
        // disp32 at index 2
        BitConverter.GetBytes(0x100).CopyTo(moduleBytes, 2);

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes),
            out var resolved, out _);

        result.Should().BeTrue();
        // endOfDisp = hitAddress + offset + 4 = 0x400000 + 2 + 4 = 0x400006
        // extra = 0 (pattern doesn't start with 80 3D or C6 05)
        // resolved = 0x400006 + 0 + 0x100 = 0x400106
        resolved.Should().Be((nint)0x400106);
    }

    [Fact]
    public void TryResolveAddress_ReadRipRelative32_NotEnoughBytes_ReturnsFalse()
    {
        var sig = new SignatureSpec("test", "AA BB", Offset: 0, AddressMode: SignatureAddressMode.ReadRipRelative32AtOffset);
        var moduleBytes = new byte[2]; // too short
        var hitAddress = (nint)0x400000;
        var baseAddress = (nint)0x400000;

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes),
            out _, out var diagnostics);

        result.Should().BeFalse();
        diagnostics.Should().Contain("Not enough bytes");
    }

    [Fact]
    public void GuessRipImmediateLength_CmpPattern_Returns1()
    {
        var method = typeof(SignatureResolverAddressing).GetMethod("GuessRipImmediateLength", NonPublicStatic)!;
        var result = (int)method.Invoke(null, new object[] { "80 3D ?? ?? ?? ?? 00" })!;
        result.Should().Be(1);
    }

    [Fact]
    public void GuessRipImmediateLength_MovPattern_Returns1()
    {
        var method = typeof(SignatureResolverAddressing).GetMethod("GuessRipImmediateLength", NonPublicStatic)!;
        var result = (int)method.Invoke(null, new object[] { "C6 05 ?? ?? ?? ?? 01" })!;
        result.Should().Be(1);
    }

    [Fact]
    public void GuessRipImmediateLength_OtherPattern_Returns0()
    {
        var method = typeof(SignatureResolverAddressing).GetMethod("GuessRipImmediateLength", NonPublicStatic)!;
        var result = (int)method.Invoke(null, new object[] { "48 8B 05 ?? ?? ?? ??" })!;
        result.Should().Be(0);
    }

    [Fact]
    public void TryResolveAddress_RipRelative_WithCmpPattern_Adjusts()
    {
        var sig = new SignatureSpec("test", "80 3D ?? ?? ?? ?? 00", Offset: 2, AddressMode: SignatureAddressMode.ReadRipRelative32AtOffset);
        var moduleBytes = new byte[64];
        var hitAddress = (nint)0x400000;
        var baseAddress = (nint)0x400000;
        BitConverter.GetBytes(0x200).CopyTo(moduleBytes, 2);

        var result = SignatureResolverAddressing.TryResolveAddress(
            new SignatureResolverAddressing.AddressResolutionInput(sig, hitAddress, baseAddress, moduleBytes),
            out var resolved, out _);

        result.Should().BeTrue();
        // endOfDisp = 0x400000 + 2 + 4 = 0x400006; extra = 1; resolved = 0x400007 + 0x200 = 0x400207
        resolved.Should().Be((nint)0x400207);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Fallbacks
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandleSignatureHit_NullLogger_Throws()
    {
        var act = () => SignatureResolverFallbacks.HandleSignatureHit(
            null!,
            new SignatureSet("test", "1.0", Array.Empty<SignatureSpec>()),
            new SignatureSpec("sym", "AA", 0),
            (nint)0x1000,
            new SignatureResolverFallbacks.SignatureHitContext(
                new Dictionary<string, long>(), null!, nint.Zero, new byte[1], new Dictionary<string, SymbolInfo>()));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HandleSignatureHit_NullSignatureSet_Throws()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var act = () => SignatureResolverFallbacks.HandleSignatureHit(
            logger, null!,
            new SignatureSpec("sym", "AA", 0),
            (nint)0x1000,
            new SignatureResolverFallbacks.SignatureHitContext(
                new Dictionary<string, long>(), null!, nint.Zero, new byte[1], new Dictionary<string, SymbolInfo>()));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HandleSignatureHit_NullSignature_Throws()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var act = () => SignatureResolverFallbacks.HandleSignatureHit(
            logger,
            new SignatureSet("test", "1.0", Array.Empty<SignatureSpec>()),
            null!, (nint)0x1000,
            new SignatureResolverFallbacks.SignatureHitContext(
                new Dictionary<string, long>(), null!, nint.Zero, new byte[1], new Dictionary<string, SymbolInfo>()));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HandleSignatureMiss_NullLogger_Throws()
    {
        var act = () => SignatureResolverFallbacks.HandleSignatureMiss(
            null!,
            new SignatureSpec("sym", "AA", 0),
            new SignatureResolverFallbacks.SignatureMissContext(
                new Dictionary<string, long>(), null!, nint.Zero, new Dictionary<string, SymbolInfo>()));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HandleSignatureMiss_NullSignature_Throws()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var act = () => SignatureResolverFallbacks.HandleSignatureMiss(
            logger, null!,
            new SignatureResolverFallbacks.SignatureMissContext(
                new Dictionary<string, long>(), null!, nint.Zero, new Dictionary<string, SymbolInfo>()));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ApplyStandaloneFallbacks_NullLogger_Throws()
    {
        var act = () => SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            null!, new Dictionary<string, long>(), null!, nint.Zero, new Dictionary<string, SymbolInfo>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ApplyStandaloneFallbacks_NullFallbackOffsets_Throws()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var act = () => SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            logger, null!, null!, nint.Zero, new Dictionary<string, SymbolInfo>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ApplyStandaloneFallbacks_NullAccessor_Throws()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var act = () => SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            logger, new Dictionary<string, long>(), null!, nint.Zero, new Dictionary<string, SymbolInfo>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ApplyStandaloneFallbacks_NullSymbols_Throws()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var act = () => SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            logger, new Dictionary<string, long>(), null!, nint.Zero, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void HandleSignatureMiss_Overload_DelegatesToContext()
    {
        var logger = NullLogger<SignatureResolver>.Instance;
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        var sig = new SignatureSpec("missing_sym", "AA BB", 0);

        // No fallback => logs warning, doesn't throw
        SignatureResolverFallbacks.HandleSignatureMiss(
            logger, sig, new Dictionary<string, long>(), null!, nint.Zero, symbols);

        symbols.Should().NotContainKey("missing_sym");
    }

    // ══════════════════════════════════════════════════════════════════════
    // SymbolHydration
    // ══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryParseAddress_NullValue_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod("TryParseAddress", NonPublicStatic)!;
        var args = new object?[] { null, 0L };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseAddress_LongValue_ReturnsTrue()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod("TryParseAddress", NonPublicStatic)!;
        var args = new object?[] { (long)0x12345678, 0L };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
        ((long)args[1]!).Should().Be(0x12345678);
    }

    [Fact]
    public void TryParseAddress_IntValue_ReturnsTrue()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod("TryParseAddress", NonPublicStatic)!;
        var args = new object?[] { (int)0x1234, 0L };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
        ((long)args[1]!).Should().Be(0x1234);
    }

    [Fact]
    public void TryParseAddress_HexString_ReturnsTrue()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod("TryParseAddress", NonPublicStatic)!;
        var args = new object?[] { "0x00401000", 0L };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
        ((long)args[1]!).Should().Be(0x00401000);
    }

    [Fact]
    public void TryParseAddress_DecimalString_ReturnsTrue()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod("TryParseAddress", NonPublicStatic)!;
        var args = new object?[] { "4198400", 0L };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
        ((long)args[1]!).Should().Be(4198400);
    }

    [Fact]
    public void TryParseAddress_InvalidString_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod("TryParseAddress", NonPublicStatic)!;
        var args = new object?[] { "not_a_number", 0L };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseJsonAddress_JsonNumberElement_ReturnsTrue()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod("TryParseJsonAddress", NonPublicStatic)!;
        var element = JsonDocument.Parse("4198400").RootElement;
        var args = new object?[] { element, 0L };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
        ((long)args[1]!).Should().Be(4198400);
    }

    [Fact]
    public void TryParseJsonAddress_JsonStringElement_ReturnsTrue()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod("TryParseJsonAddress", NonPublicStatic)!;
        var element = JsonDocument.Parse("\"0x00401000\"").RootElement;
        var args = new object?[] { element, 0L };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeTrue();
        ((long)args[1]!).Should().Be(0x00401000);
    }

    [Fact]
    public void TryParseJsonAddress_NonJsonElement_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod("TryParseJsonAddress", NonPublicStatic)!;
        var args = new object?[] { "plain_string", 0L };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void ResolveDefaultGhidraSymbolPackRoot_NoEnvOverride_ReturnsDefaultPath()
    {
        var saved = Environment.GetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT", null);
            var result = SignatureResolverSymbolHydration.ResolveDefaultGhidraSymbolPackRoot();
            result.Should().NotBeNullOrWhiteSpace();
            result.Should().Contain("symbol-packs");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT", saved);
        }
    }

    [Fact]
    public void ResolveDefaultGhidraSymbolPackRoot_WithEnvOverride_ReturnsOverride()
    {
        var saved = Environment.GetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT", "/custom/path");
            var result = SignatureResolverSymbolHydration.ResolveDefaultGhidraSymbolPackRoot();
            result.Should().Be("/custom/path");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT", saved);
        }
    }

    [Fact]
    public void SelectBestGhidraPackPath_NullRoot_Throws()
    {
        var act = () => SignatureResolver.SelectBestGhidraPackPath(null!, "fp1");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SelectBestGhidraPackPath_NullFingerprintId_Throws()
    {
        var act = () => SignatureResolver.SelectBestGhidraPackPath("/tmp", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SelectBestGhidraPackPath_EmptyRoot_ReturnsNull()
    {
        var result = SignatureResolver.SelectBestGhidraPackPath("", "fp1");
        result.Should().BeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_NonexistentDir_ReturnsNull()
    {
        var result = SignatureResolver.SelectBestGhidraPackPath("/nonexistent_dir_12345", "fp1");
        result.Should().BeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_EmptyFingerprintId_ReturnsNull()
    {
        var result = SignatureResolver.SelectBestGhidraPackPath(Path.GetTempPath(), "   ");
        result.Should().BeNull();
    }

    [Fact]
    public void TryParseAddress_EmptyString_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod("TryParseAddress", NonPublicStatic)!;
        var args = new object?[] { "", 0L };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseAddress_DoubleValue_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod("TryParseAddress", NonPublicStatic)!;
        var args = new object?[] { 3.14d, 0L };
        var result = (bool)method.Invoke(null, args)!;
        result.Should().BeFalse();
    }
}
