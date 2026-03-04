#pragma warning disable CA1014
using System.Reflection;
using System.Text.Json;
using SwfocTrainer.Core.Models;
using FluentAssertions;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class SignatureResolverPackSelectionTests
{
    [Fact]
    public void SelectBestGhidraPackPath_ShouldPreferExactFingerprintFile()
    {
        var root = CreateTempRoot();
        try
        {
            var fingerprintId = "starwarsg_deadbeefcafefeed";
            var exactPath = Path.Combine(root, $"{fingerprintId}.json");
            var indexedPath = Path.Combine(root, "from-index.json");
            var fallbackPath = Path.Combine(root, "nested", "fallback.json");

            Directory.CreateDirectory(Path.GetDirectoryName(fallbackPath)!);
            WritePack(exactPath, fingerprintId, "2026-02-25T10:00:00Z");
            WritePack(indexedPath, fingerprintId, "2026-02-26T10:00:00Z");
            WritePack(fallbackPath, fingerprintId, "2026-02-27T10:00:00Z");
            WriteArtifactIndex(root, fingerprintId, "from-index.json");

            var selected = SignatureResolver.SelectBestGhidraPackPath(root, fingerprintId);

            selected.Should().Be(exactPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SelectBestGhidraPackPath_ShouldUseArtifactIndex_WhenExactFileIsMissing()
    {
        var root = CreateTempRoot();
        try
        {
            var fingerprintId = "starwarsg_deadbeefcafefeed";
            var indexedPath = Path.Combine(root, "packs", "from-index.json");
            var fallbackPath = Path.Combine(root, "packs", "fallback.json");

            Directory.CreateDirectory(Path.GetDirectoryName(indexedPath)!);
            WritePack(indexedPath, fingerprintId, "2026-02-20T10:00:00Z");
            WritePack(fallbackPath, fingerprintId, "2026-02-28T10:00:00Z");
            WriteArtifactIndex(root, fingerprintId, Path.Combine("packs", "from-index.json"));

            var selected = SignatureResolver.SelectBestGhidraPackPath(root, fingerprintId);

            selected.Should().Be(indexedPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SelectBestGhidraPackPath_ShouldUseDeterministicFallbackOrdering()
    {
        var root = CreateTempRoot();
        try
        {
            var fingerprintId = "starwarsg_deadbeefcafefeed";
            var newerPath = Path.Combine(root, "packs", "newer.json");
            var tieA = Path.Combine(root, "packs", "a.json");
            var tieB = Path.Combine(root, "packs", "b.json");

            Directory.CreateDirectory(Path.GetDirectoryName(newerPath)!);
            WritePack(newerPath, fingerprintId, "2026-02-28T10:00:00Z");
            WritePack(tieA, fingerprintId, "2026-02-27T10:00:00Z");
            WritePack(tieB, fingerprintId, "2026-02-27T10:00:00Z");

            var selected = SignatureResolver.SelectBestGhidraPackPath(root, fingerprintId);
            selected.Should().Be(newerPath);

            File.Delete(newerPath);
            var tieSelected = SignatureResolver.SelectBestGhidraPackPath(root, fingerprintId);
            tieSelected.Should().Be(tieA);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void SelectBestGhidraPackPath_ShouldRejectFingerprintMismatchEvenForExactFileName()
    {
        var root = CreateTempRoot();
        try
        {
            var expectedFingerprint = "starwarsg_deadbeefcafefeed";
            var exactPath = Path.Combine(root, $"{expectedFingerprint}.json");
            var fallbackPath = Path.Combine(root, "fallback.json");

            WritePack(exactPath, "wrong_fingerprint", "2026-02-20T10:00:00Z");
            WritePack(fallbackPath, expectedFingerprint, "2026-02-21T10:00:00Z");

            var selected = SignatureResolver.SelectBestGhidraPackPath(root, expectedFingerprint);

            selected.Should().Be(fallbackPath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveDefaultGhidraSymbolPackRoot_ShouldHonorEnvironmentOverride()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT", @"D:\packs");

            var root = SignatureResolverSymbolHydration.ResolveDefaultGhidraSymbolPackRoot();

            root.Should().Be(@"D:\packs");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT", previous);
        }
    }

    [Fact]
    public void ResolveDefaultGhidraSymbolPackRoot_ShouldUseFallbackPath_WhenEnvUnset()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT", null);

            var root = SignatureResolverSymbolHydration.ResolveDefaultGhidraSymbolPackRoot();

            root.Should().Contain(Path.Combine("profiles", "default", "sdk", "ghidra", "symbol-packs"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT", previous);
        }
    }

    [Fact]
    public void TryParseAddress_ShouldHandleJsonNumericHexAndInvalidInputs()
    {
        using var jsonNumberDoc = JsonDocument.Parse("1234");
        var jsonNumber = jsonNumberDoc.RootElement.Clone();
        using var jsonHexDoc = JsonDocument.Parse("\"0x2A\"");
        var jsonHex = jsonHexDoc.RootElement.Clone();

        TryInvokeParseAddress(123L, out var fromLong).Should().BeTrue();
        fromLong.Should().Be(123L);

        TryInvokeParseAddress(42, out var fromInt).Should().BeTrue();
        fromInt.Should().Be(42L);

        TryInvokeParseAddress("0x40", out var fromHexString).Should().BeTrue();
        fromHexString.Should().Be(64L);

        TryInvokeParseAddress("55", out var fromString).Should().BeTrue();
        fromString.Should().Be(55L);

        TryInvokeParseAddress(jsonNumber, out var fromJsonNumber).Should().BeTrue();
        fromJsonNumber.Should().Be(1234L);

        TryInvokeParseAddress(jsonHex, out var fromJsonHex).Should().BeTrue();
        fromJsonHex.Should().Be(42L);

        TryInvokeParseAddress("not-an-address", out _).Should().BeFalse();
        TryInvokeParseAddress(null, out _).Should().BeFalse();
    }

    [Fact]
    public void TryBuildAnchorSymbol_ShouldValidateAddressAndDuplicateRules()
    {
        var valueTypes = new Dictionary<string, SymbolValueType>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = SymbolValueType.Float
        };
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);

        var validAnchor = CreateAnchor("credits", "0x1234", 0.75d);
        var method = typeof(SignatureResolverSymbolHydration).GetMethod("TryBuildAnchorSymbol", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var args = new object?[] { validAnchor, valueTypes, "fingerprint", symbols, null };
        var ok = (bool)method!.Invoke(null, args)!;

        ok.Should().BeTrue();
        args[4].Should().BeAssignableTo<SymbolInfo>();
        var symbol = (SymbolInfo)args[4]!;
        symbol.Name.Should().Be("credits");
        symbol.ValueType.Should().Be(SymbolValueType.Float);

        symbols["credits"] = symbol;
        var duplicateArgs = new object?[] { CreateAnchor("credits", "0x2222", 0.95d), valueTypes, "fingerprint", symbols, null };
        ((bool)method.Invoke(null, duplicateArgs)!).Should().BeFalse();

        var invalidArgs = new object?[] { CreateAnchor("new_anchor", "bad-address", 0.95d), valueTypes, "fingerprint", new Dictionary<string, SymbolInfo>(), null };
        ((bool)method.Invoke(null, invalidArgs)!).Should().BeFalse();
    }

    private static bool TryInvokeParseAddress(object? value, out long address)
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod("TryParseAddress", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var args = new object?[] { value, 0L };
        var ok = (bool)method!.Invoke(null, args)!;
        address = (long)args[1]!;
        return ok;
    }

    private static object CreateAnchor(string id, object address, double confidence)
    {
        var anchorType = typeof(SignatureResolverSymbolHydration).GetNestedType("GhidraAnchorDto", BindingFlags.NonPublic);
        anchorType.Should().NotBeNull();

        var ctor = anchorType!.GetConstructor(new[] { typeof(string), typeof(object), typeof(double) });
        ctor.Should().NotBeNull();

        return ctor!.Invoke(new[] { id, address, confidence });
    }
    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), $"swfoc-ghidra-pack-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void WritePack(string path, string fingerprintId, string generatedAtUtc)
    {
        var json = $$"""
                     {
                       "schemaVersion": "1.0",
                       "binaryFingerprint": {
                         "fingerprintId": "{{fingerprintId}}",
                         "moduleName": "StarWarsG.exe",
                         "fileSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                       },
                       "buildMetadata": {
                         "analysisRunId": "test",
                         "generatedAtUtc": "{{generatedAtUtc}}",
                         "toolchain": "test"
                       },
                       "anchors": [],
                       "capabilities": []
                     }
                     """;
        File.WriteAllText(path, json);
    }

    private static void WriteArtifactIndex(string root, string fingerprintId, string symbolPackPath)
    {
        var indexPath = Path.Combine(root, "artifact-index.json");
        var json = $$"""
                     {
                       "schemaVersion": "1.0",
                       "analysisRunId": "test-index",
                       "binaryFingerprint": {
                         "fingerprintId": "{{fingerprintId}}",
                         "moduleName": "StarWarsG.exe",
                         "fileSha256": "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"
                       },
                       "artifactPointers": {
                         "symbolPackPath": "{{symbolPackPath.Replace("\\", "/")}}"
                       }
                     }
                     """;
        File.WriteAllText(indexPath, json);
    }
}



