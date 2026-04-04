using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Interop;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Branch-coverage tests for SignatureResolverFallbacks (internal static class).
/// Since TryApplyFallback calls ProcessMemoryAccessor.Read which requires a real process handle,
/// we test the branch structure by exercising the public internal methods and verifying
/// that argument guards, symbol-already-exists checks, offset &lt;= 0 checks, and
/// exception-catching paths are all covered.
/// </summary>
public sealed class SignatureResolverFallbacksTests
{
    private static readonly ILogger<SignatureResolver> Logger = NullLogger<SignatureResolver>.Instance;

    // ──────────────── HandleSignatureMiss — null guards ────────────────

    [Fact]
    public void HandleSignatureMiss_NullLogger_Throws()
    {
        var act = () => SignatureResolverFallbacks.HandleSignatureMiss(
            null!,
            new SignatureSpec("s", "AA", 0),
            new Dictionary<string, long>(),
            CreateFakeAccessor(),
            (nint)0x400000,
            new Dictionary<string, SymbolInfo>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void HandleSignatureMiss_NullSignature_Throws()
    {
        var act = () => SignatureResolverFallbacks.HandleSignatureMiss(
            Logger,
            null!,
            new Dictionary<string, long>(),
            CreateFakeAccessor(),
            (nint)0x400000,
            new Dictionary<string, SymbolInfo>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("signature");
    }

    [Fact]
    public void HandleSignatureMiss_NullFallbackOffsets_Throws()
    {
        // The 6-parameter overload wraps params into a SignatureMissContext struct
        // without explicit null checks. Null fallbackOffsets causes NullReferenceException
        // when the inner method accesses context.FallbackOffsets.TryGetValue.
        var act = () => SignatureResolverFallbacks.HandleSignatureMiss(
            Logger,
            new SignatureSpec("s", "AA", 0),
            null!,
            CreateFakeAccessor(),
            (nint)0x400000,
            new Dictionary<string, SymbolInfo>());

        act.Should().Throw<NullReferenceException>();
    }

    [Fact]
    public void HandleSignatureMiss_NullAccessor_DoesNotThrow_WhenNoFallbackMatches()
    {
        // With empty fallbackOffsets, the accessor is never touched, so null accessor
        // does not cause an exception.
        var act = () => SignatureResolverFallbacks.HandleSignatureMiss(
            Logger,
            new SignatureSpec("s", "AA", 0),
            new Dictionary<string, long>(),
            null!,
            (nint)0x400000,
            new Dictionary<string, SymbolInfo>());

        act.Should().NotThrow();
    }

    [Fact]
    public void HandleSignatureMiss_NullSymbols_DoesNotThrow_WhenNoFallbackMatches()
    {
        // With empty fallbackOffsets, symbols is never accessed, so null symbols
        // does not cause an exception.
        var act = () => SignatureResolverFallbacks.HandleSignatureMiss(
            Logger,
            new SignatureSpec("s", "AA", 0),
            new Dictionary<string, long>(),
            CreateFakeAccessor(),
            (nint)0x400000,
            null!);

        act.Should().NotThrow();
    }

    [Fact]
    public void HandleSignatureMiss_NoFallback_LogsWarningAndReturns()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var sig = new SignatureSpec("MissingSymbol", "AA BB", 0);

        // No fallback offsets at all — should just log warning and return
        SignatureResolverFallbacks.HandleSignatureMiss(
            Logger, sig, new Dictionary<string, long>(),
            CreateFakeAccessor(), (nint)0x400000, symbols);

        symbols.Should().BeEmpty();
    }

    [Fact]
    public void HandleSignatureMiss_SymbolAlreadyResolved_SkipsFallback()
    {
        var existing = new SymbolInfo("Health", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature);
        var symbols = new Dictionary<string, SymbolInfo> { ["Health"] = existing };
        var sig = new SignatureSpec("Health", "AA BB", 0);
        var fallbacks = new Dictionary<string, long> { ["Health"] = 0x500 };

        // Fallback exists but symbol already resolved — should not overwrite
        SignatureResolverFallbacks.HandleSignatureMiss(
            Logger, sig, fallbacks, CreateFakeAccessor(), (nint)0x400000, symbols);

        symbols["Health"].Should().BeSameAs(existing);
    }

    [Fact]
    public void HandleSignatureMiss_FallbackOffsetZero_DoesNotApply()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var sig = new SignatureSpec("Health", "AA BB", 0);
        var fallbacks = new Dictionary<string, long> { ["Health"] = 0 };

        // Offset <= 0 — TryApplyFallback should return false
        SignatureResolverFallbacks.HandleSignatureMiss(
            Logger, sig, fallbacks, CreateFakeAccessor(), (nint)0x400000, symbols);

        symbols.Should().NotContainKey("Health");
    }

    [Fact]
    public void HandleSignatureMiss_FallbackOffsetNegative_DoesNotApply()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var sig = new SignatureSpec("Health", "AA BB", 0);
        var fallbacks = new Dictionary<string, long> { ["Health"] = -100 };

        SignatureResolverFallbacks.HandleSignatureMiss(
            Logger, sig, fallbacks, CreateFakeAccessor(), (nint)0x400000, symbols);

        symbols.Should().NotContainKey("Health");
    }

    [Fact]
    public void HandleSignatureMiss_FallbackPositiveOffset_AccessorThrows_DoesNotApply()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var sig = new SignatureSpec("Health", "AA BB", 0);
        var fallbacks = new Dictionary<string, long> { ["Health"] = 0x1000 };

        // The fake accessor will throw when trying to read (invalid handle)
        // This should be caught and logged
        SignatureResolverFallbacks.HandleSignatureMiss(
            Logger, sig, fallbacks, CreateFakeAccessor(), (nint)0x400000, symbols);

        // The test-read will fail because the handle is invalid, so no symbol added
        symbols.Should().NotContainKey("Health");
    }

    // ──────────────── HandleSignatureHit — null guards ────────────────

    [Fact]
    public void HandleSignatureHit_NullLogger_Throws()
    {
        var ctx = CreateHitContext();
        var act = () => SignatureResolverFallbacks.HandleSignatureHit(
            null!,
            new SignatureSet("s", "1.0", Array.Empty<SignatureSpec>()),
            new SignatureSpec("s", "AA", 0),
            (nint)0x1000,
            ctx);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void HandleSignatureHit_NullSignatureSet_Throws()
    {
        var ctx = CreateHitContext();
        var act = () => SignatureResolverFallbacks.HandleSignatureHit(
            Logger,
            null!,
            new SignatureSpec("s", "AA", 0),
            (nint)0x1000,
            ctx);

        act.Should().Throw<ArgumentNullException>().WithParameterName("signatureSet");
    }

    [Fact]
    public void HandleSignatureHit_NullSignature_Throws()
    {
        var ctx = CreateHitContext();
        var act = () => SignatureResolverFallbacks.HandleSignatureHit(
            Logger,
            new SignatureSet("s", "1.0", Array.Empty<SignatureSpec>()),
            null!,
            (nint)0x1000,
            ctx);

        act.Should().Throw<ArgumentNullException>().WithParameterName("signature");
    }

    // ──────────────── HandleSignatureHit — address resolution succeeds ────────────────

    [Fact]
    public void HandleSignatureHit_AddressResolves_AddsSymbol()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var moduleBytes = new byte[64];
        var baseAddress = (nint)0x400000;
        var sig = new SignatureSpec("Health", "AA BB", Offset: 4, AddressMode: SignatureAddressMode.HitPlusOffset);
        var set = new SignatureSet("TestSet", "1.0", new List<SignatureSpec> { sig });
        var hit = (nint)(0x400000 + 0x10);
        var accessor = CreateFakeAccessor();

        var ctx = new SignatureResolverFallbacks.SignatureHitContext(
            new Dictionary<string, long>(), accessor, baseAddress, moduleBytes, symbols);

        SignatureResolverFallbacks.HandleSignatureHit(Logger, set, sig, hit, ctx);

        symbols.Should().ContainKey("Health");
        symbols["Health"].Address.Should().Be(hit + 4);
        symbols["Health"].Source.Should().Be(AddressSource.Signature);
        symbols["Health"].Confidence.Should().Be(0.95);
    }

    // ──────────────── HandleSignatureHit — address resolution fails, no fallback ────────────────

    [Fact]
    public void HandleSignatureHit_AddressResolutionFails_NoFallback_LogsWarning()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var moduleBytes = new byte[4]; // too short for ReadAbsolute32
        var baseAddress = (nint)0x400000;
        var sig = new SignatureSpec("Health", "AA BB", Offset: 0, AddressMode: SignatureAddressMode.ReadAbsolute32AtOffset);
        var set = new SignatureSet("TestSet", "1.0", new List<SignatureSpec> { sig });
        var hit = (nint)(0x400000 + 2); // index = 2, need 2+4 = 6 > 4
        var accessor = CreateFakeAccessor();

        var ctx = new SignatureResolverFallbacks.SignatureHitContext(
            new Dictionary<string, long>(), accessor, baseAddress, moduleBytes, symbols);

        SignatureResolverFallbacks.HandleSignatureHit(Logger, set, sig, hit, ctx);

        symbols.Should().NotContainKey("Health");
    }

    // ──────────────── HandleSignatureHit — address resolution fails, fallback offset zero ────────────────

    [Fact]
    public void HandleSignatureHit_AddressResolutionFails_FallbackZero_NotApplied()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var moduleBytes = new byte[4];
        var baseAddress = (nint)0x400000;
        var sig = new SignatureSpec("Health", "AA BB", Offset: 0, AddressMode: SignatureAddressMode.ReadAbsolute32AtOffset);
        var set = new SignatureSet("TestSet", "1.0", new List<SignatureSpec> { sig });
        var hit = (nint)(0x400000 + 2);
        var accessor = CreateFakeAccessor();
        var fallbacks = new Dictionary<string, long> { ["Health"] = 0 };

        var ctx = new SignatureResolverFallbacks.SignatureHitContext(
            fallbacks, accessor, baseAddress, moduleBytes, symbols);

        SignatureResolverFallbacks.HandleSignatureHit(Logger, set, sig, hit, ctx);

        symbols.Should().NotContainKey("Health");
    }

    // ──────────────── HandleSignatureHit — address resolution fails, fallback invalid read ────────────────

    [Fact]
    public void HandleSignatureHit_AddressResolutionFails_FallbackInvalidRead_NotApplied()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var moduleBytes = new byte[4];
        var baseAddress = (nint)0x400000;
        var sig = new SignatureSpec("Health", "AA BB", Offset: 0, AddressMode: SignatureAddressMode.ReadAbsolute32AtOffset);
        var set = new SignatureSet("TestSet", "1.0", new List<SignatureSpec> { sig });
        var hit = (nint)(0x400000 + 2);
        var accessor = CreateFakeAccessor();
        var fallbacks = new Dictionary<string, long> { ["Health"] = 0x5000 };

        var ctx = new SignatureResolverFallbacks.SignatureHitContext(
            fallbacks, accessor, baseAddress, moduleBytes, symbols);

        // Accessor test-read will throw (invalid handle) — fallback not applied
        SignatureResolverFallbacks.HandleSignatureHit(Logger, set, sig, hit, ctx);

        symbols.Should().NotContainKey("Health");
    }

    // ──────────────── HandleSignatureHit — unsupported address mode, with fallback ────────────────

    [Fact]
    public void HandleSignatureHit_UnsupportedMode_FallbackNegativeOffset_NotApplied()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var moduleBytes = new byte[64];
        var baseAddress = (nint)0x400000;
        var sig = new SignatureSpec("Health", "AA BB", Offset: 0, AddressMode: (SignatureAddressMode)999);
        var set = new SignatureSet("TestSet", "1.0", new List<SignatureSpec> { sig });
        var hit = (nint)(0x400000 + 0x10);
        var accessor = CreateFakeAccessor();
        var fallbacks = new Dictionary<string, long> { ["Health"] = -50 };

        var ctx = new SignatureResolverFallbacks.SignatureHitContext(
            fallbacks, accessor, baseAddress, moduleBytes, symbols);

        SignatureResolverFallbacks.HandleSignatureHit(Logger, set, sig, hit, ctx);

        symbols.Should().NotContainKey("Health");
    }

    // ──────────────── ApplyStandaloneFallbacks — null guards ────────────────

    [Fact]
    public void ApplyStandaloneFallbacks_NullLogger_Throws()
    {
        var act = () => SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            null!, new Dictionary<string, long>(), CreateFakeAccessor(), (nint)0x400000, new Dictionary<string, SymbolInfo>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void ApplyStandaloneFallbacks_NullFallbackOffsets_Throws()
    {
        var act = () => SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            Logger, null!, CreateFakeAccessor(), (nint)0x400000, new Dictionary<string, SymbolInfo>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("fallbackOffsets");
    }

    [Fact]
    public void ApplyStandaloneFallbacks_NullAccessor_Throws()
    {
        var act = () => SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            Logger, new Dictionary<string, long>(), null!, (nint)0x400000, new Dictionary<string, SymbolInfo>());

        act.Should().Throw<ArgumentNullException>().WithParameterName("accessor");
    }

    [Fact]
    public void ApplyStandaloneFallbacks_NullSymbols_Throws()
    {
        var act = () => SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            Logger, new Dictionary<string, long>(), CreateFakeAccessor(), (nint)0x400000, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("symbols");
    }

    [Fact]
    public void ApplyStandaloneFallbacks_EmptyFallbacks_NoOp()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            Logger, new Dictionary<string, long>(), CreateFakeAccessor(), (nint)0x400000, symbols);

        symbols.Should().BeEmpty();
    }

    [Fact]
    public void ApplyStandaloneFallbacks_SkipsAlreadyResolved()
    {
        var existing = new SymbolInfo("Health", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature);
        var symbols = new Dictionary<string, SymbolInfo> { ["Health"] = existing };
        var fallbacks = new Dictionary<string, long> { ["Health"] = 0x500 };

        SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            Logger, fallbacks, CreateFakeAccessor(), (nint)0x400000, symbols);

        symbols["Health"].Should().BeSameAs(existing);
    }

    [Fact]
    public void ApplyStandaloneFallbacks_ZeroOffset_NotApplied()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var fallbacks = new Dictionary<string, long> { ["Health"] = 0 };

        SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            Logger, fallbacks, CreateFakeAccessor(), (nint)0x400000, symbols);

        symbols.Should().NotContainKey("Health");
    }

    [Fact]
    public void ApplyStandaloneFallbacks_NegativeOffset_NotApplied()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var fallbacks = new Dictionary<string, long> { ["Health"] = -1 };

        SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            Logger, fallbacks, CreateFakeAccessor(), (nint)0x400000, symbols);

        symbols.Should().NotContainKey("Health");
    }

    [Fact]
    public void ApplyStandaloneFallbacks_InvalidRead_NotApplied()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var fallbacks = new Dictionary<string, long> { ["Health"] = 0x5000 };

        SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            Logger, fallbacks, CreateFakeAccessor(), (nint)0x400000, symbols);

        symbols.Should().NotContainKey("Health");
    }

    [Fact]
    public void ApplyStandaloneFallbacks_MultipleFallbacks_ProcessesAll()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var existing = new SymbolInfo("Credits", (nint)0x9000, SymbolValueType.Int32, AddressSource.Signature);
        symbols["Credits"] = existing;

        var fallbacks = new Dictionary<string, long>
        {
            ["Health"] = 0x5000,   // will fail (invalid read) but processed
            ["Mana"] = 0,          // offset <= 0, skipped
            ["Credits"] = 0x6000,  // already resolved, skipped
        };

        SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            Logger, fallbacks, CreateFakeAccessor(), (nint)0x400000, symbols);

        // Health: tried but failed (invalid handle)
        symbols.Should().NotContainKey("Health");
        // Mana: offset 0, not applied
        symbols.Should().NotContainKey("Mana");
        // Credits: already existed, unchanged
        symbols["Credits"].Should().BeSameAs(existing);
    }

    // ──────────────── TryApplyFallback — Win32Exception path ────────────────

    [Fact]
    public void TryApplyFallback_Win32Exception_CaughtAndReturnsFalse()
    {
        // We test this indirectly: CreateFakeAccessor has an invalid handle,
        // which may throw either InvalidOperationException or Win32Exception.
        // Both are caught in TryApplyFallback.
        var symbols = new Dictionary<string, SymbolInfo>();
        var sig = new SignatureSpec("Test", "AA BB", 0);
        var fallbacks = new Dictionary<string, long> { ["Test"] = 0x1000 };

        // This exercises the catch blocks in TryApplyFallback
        SignatureResolverFallbacks.HandleSignatureMiss(
            Logger, sig, fallbacks, CreateFakeAccessor(), (nint)0x400000, symbols);

        symbols.Should().NotContainKey("Test");
    }

    // ──────────────── helpers ────────────────

    /// <summary>
    /// Creates a ProcessMemoryAccessor with an intentionally invalid handle
    /// by using reflection to bypass the constructor's OpenProcess call.
    /// This allows testing the branch logic without a real process.
    /// </summary>
    private static ProcessMemoryAccessor CreateFakeAccessor()
    {
        // Use RuntimeHelpers to create an uninitialized instance (no constructor called)
        var accessor = (ProcessMemoryAccessor)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(ProcessMemoryAccessor));

        // Set _handle to a known-invalid but non-zero value so Dispose doesn't skip
        var handleField = typeof(ProcessMemoryAccessor).GetField(
            "_handle", BindingFlags.NonPublic | BindingFlags.Instance);
        handleField!.SetValue(accessor, (nint)0xDEAD);

        return accessor;
    }

    private SignatureResolverFallbacks.SignatureHitContext CreateHitContext()
    {
        return new SignatureResolverFallbacks.SignatureHitContext(
            new Dictionary<string, long>(),
            CreateFakeAccessor(),
            (nint)0x400000,
            new byte[64],
            new Dictionary<string, SymbolInfo>());
    }
}
