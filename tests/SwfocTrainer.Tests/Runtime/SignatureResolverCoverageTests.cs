using System.Diagnostics;
using System.Runtime.InteropServices;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Interop;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class SignatureResolverCoverageTests
{
    [Fact]
    public void TryResolveAddress_HitPlusOffset_ShouldResolveAddress()
    {
        var signature = new SignatureSpec("credits", "90 90", 8, SignatureAddressMode.HitPlusOffset);

        var ok = SignatureResolverAddressing.TryResolveAddress(
            signature,
            hitAddress: (nint)0x1000,
            baseAddress: (nint)0x0800,
            moduleBytes: new byte[32],
            out var resolved,
            out var diagnostics);

        ok.Should().BeTrue();
        resolved.Should().Be((nint)0x1008);
        diagnostics.Should().BeNull();
    }

    [Fact]
    public void TryResolveAddress_ReadAbsolute32AtOffset_ShouldFailWhenOutOfBounds()
    {
        var signature = new SignatureSpec("credits", "AA", 6, SignatureAddressMode.ReadAbsolute32AtOffset);

        var ok = SignatureResolverAddressing.TryResolveAddress(
            signature,
            hitAddress: (nint)0x1000,
            baseAddress: (nint)0x1000,
            moduleBytes: new byte[8],
            out _,
            out var diagnostics);

        ok.Should().BeFalse();
        diagnostics.Should().Contain("Not enough bytes");
    }

    [Fact]
    public void TryResolveAddress_ReadAbsolute32AtOffset_ShouldFailWhenDecodedAddressIsNull()
    {
        var signature = new SignatureSpec("credits", "AA", 0, SignatureAddressMode.ReadAbsolute32AtOffset);

        var ok = SignatureResolverAddressing.TryResolveAddress(
            signature,
            hitAddress: (nint)0x1000,
            baseAddress: (nint)0x1000,
            moduleBytes: new byte[8],
            out _,
            out var diagnostics);

        ok.Should().BeFalse();
        diagnostics.Should().Contain("Decoded null absolute address");
    }

    [Fact]
    public void TryResolveAddress_ReadRipRelative32AtOffset_ShouldApplyImmediateLengthHeuristic()
    {
        var signature = new SignatureSpec(
            "fog",
            "80 3D ?? ?? ?? ?? 00",
            2,
            SignatureAddressMode.ReadRipRelative32AtOffset);

        var moduleBytes = new byte[64];
        BitConverter.GetBytes(16).CopyTo(moduleBytes, 18);

        var ok = SignatureResolverAddressing.TryResolveAddress(
            signature,
            hitAddress: (nint)0x1010,
            baseAddress: (nint)0x1000,
            moduleBytes,
            out var resolved,
            out var diagnostics);

        ok.Should().BeTrue();
        resolved.Should().Be((nint)0x1027);
        diagnostics.Should().BeNull();
    }

    [Fact]
    public void HandleSignatureMiss_WithoutFallback_ShouldLeaveSymbolsUnchanged()
    {
        using var accessor = new ProcessMemoryAccessor(Environment.ProcessId);
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        var signature = new SignatureSpec("missing_symbol", "90", 0);

        SignatureResolverFallbacks.HandleSignatureMiss(
            NullLogger<SignatureResolver>.Instance,
            signature,
            fallbackOffsets: new Dictionary<string, long>(),
            accessor,
            baseAddress: (nint)0x1000,
            symbols);

        symbols.Should().BeEmpty();
    }

    [Fact]
    public void HandleSignatureMiss_WithReadableFallback_ShouldRegisterDegradedSymbol()
    {
        using var accessor = new ProcessMemoryAccessor(Environment.ProcessId);
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        var signature = new SignatureSpec("fallback_symbol", "90", 0);
        var allocated = Marshal.AllocHGlobal(sizeof(int) + 8);
        try
        {
            Marshal.WriteInt32(allocated + 4, 1234);

            SignatureResolverFallbacks.HandleSignatureMiss(
                NullLogger<SignatureResolver>.Instance,
                signature,
                fallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
                {
                    ["fallback_symbol"] = 4
                },
                accessor,
                baseAddress: allocated,
                symbols);

            symbols.Should().ContainKey("fallback_symbol");
            symbols["fallback_symbol"].Source.Should().Be(AddressSource.Fallback);
            symbols["fallback_symbol"].HealthStatus.Should().Be(SymbolHealthStatus.Degraded);
        }
        finally
        {
            Marshal.FreeHGlobal(allocated);
        }
    }

    [Fact]
    public void HandleSignatureHit_UnsupportedModeWithoutFallback_ShouldNotAddSymbol()
    {
        using var accessor = new ProcessMemoryAccessor(Environment.ProcessId);
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        var set = new SignatureSet("base", "test", new[]
        {
            new SignatureSpec("unsupported", "90", 0, (SignatureAddressMode)999)
        });
        var signature = set.Signatures[0];

        SignatureResolverFallbacks.HandleSignatureHit(
            NullLogger<SignatureResolver>.Instance,
            set,
            signature,
            hit: (nint)0x1000,
            new SignatureResolverFallbacks.SignatureHitContext(
                FallbackOffsets: new Dictionary<string, long>(),
                Accessor: accessor,
                BaseAddress: (nint)0x1000,
                ModuleBytes: new byte[32],
                Symbols: symbols));

        symbols.Should().BeEmpty();
    }

    [Fact]
    public void HandleSignatureHit_UnsupportedModeWithReadableFallback_ShouldApplyFallback()
    {
        using var accessor = new ProcessMemoryAccessor(Environment.ProcessId);
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        var set = new SignatureSet("base", "test", new[]
        {
            new SignatureSpec("unsupported", "90", 0, (SignatureAddressMode)999)
        });
        var signature = set.Signatures[0];
        var allocated = Marshal.AllocHGlobal(sizeof(int) + 8);

        try
        {
            Marshal.WriteInt32(allocated + 4, 66);

            SignatureResolverFallbacks.HandleSignatureHit(
                NullLogger<SignatureResolver>.Instance,
                set,
                signature,
                hit: allocated,
                new SignatureResolverFallbacks.SignatureHitContext(
                    FallbackOffsets: new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["unsupported"] = 4
                    },
                    Accessor: accessor,
                    BaseAddress: allocated,
                    ModuleBytes: new byte[32],
                    Symbols: symbols));

            symbols.Should().ContainKey("unsupported");
            symbols["unsupported"].Source.Should().Be(AddressSource.Fallback);
        }
        finally
        {
            Marshal.FreeHGlobal(allocated);
        }
    }

    [Fact]
    public void ApplyStandaloneFallbacks_ShouldSkipExistingAndAddReadableMissingSymbol()
    {
        using var accessor = new ProcessMemoryAccessor(Environment.ProcessId);
        var existing = new SymbolInfo("already", (nint)0x1234, SymbolValueType.Int32, AddressSource.Signature);
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["already"] = existing
        };

        var allocated = Marshal.AllocHGlobal(sizeof(int) + 8);
        try
        {
            Marshal.WriteInt32(allocated + 4, 77);

            SignatureResolverFallbacks.ApplyStandaloneFallbacks(
                NullLogger<SignatureResolver>.Instance,
                new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase)
                {
                    ["already"] = 8,
                    ["new_symbol"] = 4
                },
                accessor,
                allocated,
                symbols);

            symbols["already"].Should().Be(existing);
            symbols.Should().ContainKey("new_symbol");
            symbols["new_symbol"].Source.Should().Be(AddressSource.Fallback);
        }
        finally
        {
            Marshal.FreeHGlobal(allocated);
        }
    }

    [Fact]
    public async Task ResolveAsync_WithCurrentProcessAndNoSignatures_ShouldReturnEmptyMap()
    {
        using var process = Process.GetCurrentProcess();
        var executablePath = process.MainModule?.FileName;
        executablePath.Should().NotBeNullOrWhiteSpace();
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance, ghidraSymbolPackRoot: Path.GetTempPath());
        var build = new ProfileBuild(
            ProfileId: "test",
            GameBuild: "test",
            ExecutablePath: executablePath!,
            ExeTarget: ExeTarget.Swfoc,
            ProcessId: process.Id);

        var map = await resolver.ResolveAsync(
            build,
            signatureSets: Array.Empty<SignatureSet>(),
            fallbackOffsets: new Dictionary<string, long>(),
            CancellationToken.None);

        map.Symbols.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolveAsync_WithMissingProcess_ShouldThrowInvalidOperationException()
    {
        var resolver = new SignatureResolver(NullLogger<SignatureResolver>.Instance, ghidraSymbolPackRoot: Path.GetTempPath());
        var build = new ProfileBuild(
            ProfileId: "test",
            GameBuild: "test",
            ExecutablePath: string.Empty,
            ExeTarget: ExeTarget.Swfoc,
            ProcessId: 0);

        var action = async () => await resolver.ResolveAsync(
            build,
            signatureSets: Array.Empty<SignatureSet>(),
            fallbackOffsets: new Dictionary<string, long>(),
            CancellationToken.None);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Could not find running process*");
    }


    [Fact]
    public void TryResolveAddress_ReadRipRelative32AtOffset_ShouldApplyMovImmediateHeuristic()
    {
        var signature = new SignatureSpec(
            "mov_rip",
            "C6 05 ?? ?? ?? ?? 01",
            2,
            SignatureAddressMode.ReadRipRelative32AtOffset);

        var moduleBytes = new byte[64];
        BitConverter.GetBytes(32).CopyTo(moduleBytes, 2);

        var ok = SignatureResolverAddressing.TryResolveAddress(
            signature,
            hitAddress: (nint)0x1000,
            baseAddress: (nint)0x1000,
            moduleBytes,
            out var resolved,
            out var diagnostics);

        ok.Should().BeTrue();
        resolved.Should().Be((nint)0x1027);
        diagnostics.Should().BeNull();
    }

    [Fact]
    public void TryResolveAddress_ShouldFail_WhenComputedIndexIsNegative()
    {
        var signature = new SignatureSpec("neg_index", "90", -4, SignatureAddressMode.ReadAbsolute32AtOffset);

        var ok = SignatureResolverAddressing.TryResolveAddress(
            signature,
            hitAddress: (nint)0x1000,
            baseAddress: (nint)0x2000,
            moduleBytes: new byte[32],
            out _,
            out var diagnostics);

        ok.Should().BeFalse();
        diagnostics.Should().Contain("Computed value offset out of range");
    }

    [Fact]
    public void TryResolveAddress_ShouldFail_ForUnsupportedAddressMode()
    {
        var signature = new SignatureSpec("unsupported", "90", 0, (SignatureAddressMode)999);

        var ok = SignatureResolverAddressing.TryResolveAddress(
            signature,
            hitAddress: (nint)0x1000,
            baseAddress: (nint)0x1000,
            moduleBytes: new byte[32],
            out _,
            out var diagnostics);

        ok.Should().BeFalse();
        diagnostics.Should().Contain("Unsupported signature address mode");
    }

}
