using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Branch-coverage tests for SignatureResolverSymbolHydration (internal static class).
/// Uses file-system temp directories for pack selection and JSON parse paths.
/// </summary>
public sealed class SignatureResolverSymbolHydrationTests : IDisposable
{
    private readonly string _tempRoot;

    public SignatureResolverSymbolHydrationTests()
    {
        _tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-hydration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    // ──────────────── SelectBestGhidraPackPath — null / empty guards ────────────────

    [Fact]
    public void SelectBestGhidraPackPath_NullRoot_ThrowsArgumentNullException()
    {
        var act = () => SignatureResolver.SelectBestGhidraPackPath(null!, "fp");
        act.Should().Throw<ArgumentNullException>().WithParameterName("symbolPackRoot");
    }

    [Fact]
    public void SelectBestGhidraPackPath_NullFingerprint_ThrowsArgumentNullException()
    {
        var act = () => SignatureResolver.SelectBestGhidraPackPath("root", null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("fingerprintId");
    }

    [Fact]
    public void SelectBestGhidraPackPath_EmptyRoot_ReturnsNull()
    {
        var result = SignatureResolver.SelectBestGhidraPackPath("", "fp");
        result.Should().BeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_WhitespaceRoot_ReturnsNull()
    {
        var result = SignatureResolver.SelectBestGhidraPackPath("   ", "fp");
        result.Should().BeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_EmptyFingerprint_ReturnsNull()
    {
        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, "");
        result.Should().BeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_WhitespaceFingerprint_ReturnsNull()
    {
        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, "   ");
        result.Should().BeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_NonExistentRoot_ReturnsNull()
    {
        var result = SignatureResolver.SelectBestGhidraPackPath(
            Path.Join(_tempRoot, "does-not-exist"), "some_fp");
        result.Should().BeNull();
    }

    // ──────────────── SelectBestGhidraPackPath — no matching packs ────────────────

    [Fact]
    public void SelectBestGhidraPackPath_EmptyDirectory_ReturnsNull()
    {
        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, "some_fp_12345678");
        result.Should().BeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_OnlyMismatchedPacks_ReturnsNull()
    {
        WritePack(Path.Join(_tempRoot, "wrong1.json"), "wrong_fp", "2026-01-01T00:00:00Z");
        WritePack(Path.Join(_tempRoot, "wrong2.json"), "other_fp", "2026-01-02T00:00:00Z");

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, "expected_fp");
        result.Should().BeNull();
    }

    // ──────────────── SelectBestGhidraPackPath — artifact index paths ────────────────

    [Fact]
    public void SelectBestGhidraPackPath_ArtifactIndex_AbsolutePath_IsUsed()
    {
        var fp = "game_abc123";
        var packPath = Path.Join(_tempRoot, "sub", "pack.json");
        Directory.CreateDirectory(Path.GetDirectoryName(packPath)!);
        WritePack(packPath, fp, "2026-03-01T00:00:00Z");

        // Write artifact-index with absolute path (uses forward slashes in JSON)
        WriteArtifactIndex(_tempRoot, fp, packPath);

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        // The index stores the path, which may use forward slashes after JSON round-trip
        result.Should().NotBeNull();
        Path.GetFullPath(result!).Should().Be(Path.GetFullPath(packPath));
    }

    [Fact]
    public void SelectBestGhidraPackPath_ArtifactIndex_RelativePath_IsResolved()
    {
        var fp = "game_abc123";
        var packPath = Path.Join(_tempRoot, "packs", "my-pack.json");
        Directory.CreateDirectory(Path.GetDirectoryName(packPath)!);
        WritePack(packPath, fp, "2026-03-01T00:00:00Z");

        // Write artifact-index with relative path
        WriteArtifactIndex(_tempRoot, fp, Path.Join("packs", "my-pack.json"));

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        result.Should().NotBeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_ArtifactIndex_NullSymbolPackPath_Ignored()
    {
        var fp = "game_def456";
        WritePack(Path.Join(_tempRoot, "fallback.json"), fp, "2026-03-01T00:00:00Z");

        // Write artifact-index with null symbolPackPath
        var indexPath = Path.Join(_tempRoot, "artifact-index.json");
        var json = $$"""
                     {
                       "schemaVersion": "1.0",
                       "binaryFingerprint": { "fingerprintId": "{{fp}}" },
                       "artifactPointers": { "symbolPackPath": null }
                     }
                     """;
        File.WriteAllText(indexPath, json);

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        // Should fall through to enumeration fallback and find fallback.json
        result.Should().NotBeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_ArtifactIndex_EmptySymbolPackPath_Ignored()
    {
        var fp = "game_ghi789";
        WritePack(Path.Join(_tempRoot, "fallback.json"), fp, "2026-03-01T00:00:00Z");

        var indexPath = Path.Join(_tempRoot, "artifact-index.json");
        var json = $$"""
                     {
                       "schemaVersion": "1.0",
                       "binaryFingerprint": { "fingerprintId": "{{fp}}" },
                       "artifactPointers": { "symbolPackPath": "" }
                     }
                     """;
        File.WriteAllText(indexPath, json);

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        result.Should().NotBeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_ArtifactIndex_MismatchedFingerprint_Ignored()
    {
        var fp = "game_match";
        WritePack(Path.Join(_tempRoot, "pack.json"), fp, "2026-03-01T00:00:00Z");

        // Write artifact-index with mismatched fingerprint
        WriteArtifactIndex(_tempRoot, "game_mismatch", "pack.json");

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        // Should still find pack.json via enumeration
        result.Should().NotBeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_ArtifactIndex_InvalidJson_Ignored()
    {
        var fp = "game_badjson";
        WritePack(Path.Join(_tempRoot, "pack.json"), fp, "2026-03-01T00:00:00Z");

        var indexPath = Path.Join(_tempRoot, "artifact-index.json");
        File.WriteAllText(indexPath, "NOT VALID JSON {{{{");

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        // Should fall through and find pack.json via enumeration
        result.Should().NotBeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_ArtifactIndex_NullDeserialization_Ignored()
    {
        var fp = "game_nulldeser";
        WritePack(Path.Join(_tempRoot, "pack.json"), fp, "2026-03-01T00:00:00Z");

        var indexPath = Path.Join(_tempRoot, "artifact-index.json");
        File.WriteAllText(indexPath, "null");

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        result.Should().NotBeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_ArtifactIndex_NoBinaryFingerprint_Ignored()
    {
        var fp = "game_nofp";
        WritePack(Path.Join(_tempRoot, "pack.json"), fp, "2026-03-01T00:00:00Z");

        var indexPath = Path.Join(_tempRoot, "artifact-index.json");
        var json = """
                   {
                     "schemaVersion": "1.0",
                     "artifactPointers": { "symbolPackPath": "pack.json" }
                   }
                   """;
        File.WriteAllText(indexPath, json);

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        result.Should().NotBeNull();
    }

    [Fact]
    public void SelectBestGhidraPackPath_ArtifactIndex_NoArtifactPointers_Ignored()
    {
        var fp = "game_noptr";
        WritePack(Path.Join(_tempRoot, "pack.json"), fp, "2026-03-01T00:00:00Z");

        var indexPath = Path.Join(_tempRoot, "artifact-index.json");
        var json = $$"""
                     {
                       "schemaVersion": "1.0",
                       "binaryFingerprint": { "fingerprintId": "{{fp}}" }
                     }
                     """;
        File.WriteAllText(indexPath, json);

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        result.Should().NotBeNull();
    }

    // ──────────────── TryAddPackCandidate — metadata edge cases ────────────────

    [Fact]
    public void SelectBestGhidraPackPath_Pack_InvalidJson_SkippedSilently()
    {
        var fp = "game_goodfp";
        var goodPath = Path.Join(_tempRoot, "good.json");
        WritePack(goodPath, fp, "2026-03-01T00:00:00Z");

        // Write invalid pack
        var badPath = Path.Join(_tempRoot, "bad.json");
        File.WriteAllText(badPath, "NOT JSON");

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        result.Should().Be(goodPath);
    }

    [Fact]
    public void SelectBestGhidraPackPath_Pack_NullBinaryFingerprint_SkippedSilently()
    {
        var fp = "game_nofp2";
        var goodPath = Path.Join(_tempRoot, "good.json");
        WritePack(goodPath, fp, "2026-03-01T00:00:00Z");

        var badPath = Path.Join(_tempRoot, "nofp.json");
        File.WriteAllText(badPath, """{"schemaVersion":"1.0"}""");

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        result.Should().Be(goodPath);
    }

    [Fact]
    public void SelectBestGhidraPackPath_Pack_EmptyFingerprintId_SkippedSilently()
    {
        var fp = "game_emptyfp";
        var goodPath = Path.Join(_tempRoot, "good.json");
        WritePack(goodPath, fp, "2026-03-01T00:00:00Z");

        var badPath = Path.Join(_tempRoot, "emptyfp.json");
        File.WriteAllText(badPath, """{"binaryFingerprint":{"fingerprintId":""}}""");

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        result.Should().Be(goodPath);
    }

    [Fact]
    public void SelectBestGhidraPackPath_Pack_NullGeneratedAt_UsesMinValue()
    {
        var fp = "game_nulldate";
        var pathWithDate = Path.Join(_tempRoot, "dated.json");
        WritePack(pathWithDate, fp, "2026-03-01T00:00:00Z");

        // Pack with no buildMetadata -> GeneratedAtUtc defaults to DateTimeOffset.MinValue
        var pathNoDate = Path.Join(_tempRoot, "nodate.json");
        var json = $$"""
                     {
                       "binaryFingerprint": { "fingerprintId": "{{fp}}" }
                     }
                     """;
        File.WriteAllText(pathNoDate, json);

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        // dated.json has a newer date, so it comes first in fallback ordering
        result.Should().Be(pathWithDate);
    }

    // ──────────────── SelectBestGhidraPackPath — duplicate skip logic ────────────────

    [Fact]
    public void SelectBestGhidraPackPath_SkipsDuplicateExactAndIndexed_InEnumeration()
    {
        var fp = "starwarsg_deadbeefcafefeed";
        var exactPath = Path.Join(_tempRoot, $"{fp}.json");
        WritePack(exactPath, fp, "2026-01-01T00:00:00Z");

        var indexedPath = Path.Join(_tempRoot, "indexed.json");
        WritePack(indexedPath, fp, "2026-01-02T00:00:00Z");

        WriteArtifactIndex(_tempRoot, fp, "indexed.json");

        // Exact has precedence 0, indexed has precedence 1
        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        result.Should().Be(exactPath);
    }

    // ──────────────── TryParseAddress (private) via reflection ────────────────

    private static bool InvokeTryParseAddress(object? value, out long address)
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryParseAddress",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("TryParseAddress should exist");

        var args = new object?[] { value, 0L };
        var result = (bool)method!.Invoke(null, args)!;
        address = (long)args[1]!;
        return result;
    }

    [Fact]
    public void TryParseAddress_Null_ReturnsFalse()
    {
        var result = InvokeTryParseAddress(null, out var address);
        result.Should().BeFalse();
        address.Should().Be(0);
    }

    [Fact]
    public void TryParseAddress_LongValue_ReturnsTrue()
    {
        var result = InvokeTryParseAddress(42L, out var address);
        result.Should().BeTrue();
        address.Should().Be(42);
    }

    [Fact]
    public void TryParseAddress_IntValue_ReturnsTrue()
    {
        var result = InvokeTryParseAddress(100, out var address);
        result.Should().BeTrue();
        address.Should().Be(100);
    }

    [Fact]
    public void TryParseAddress_StringDecimal_ReturnsTrue()
    {
        var result = InvokeTryParseAddress("12345", out var address);
        result.Should().BeTrue();
        address.Should().Be(12345);
    }

    [Fact]
    public void TryParseAddress_StringHex_ReturnsTrue()
    {
        var result = InvokeTryParseAddress("0xFF", out var address);
        result.Should().BeTrue();
        address.Should().Be(255);
    }

    [Fact]
    public void TryParseAddress_StringHexUpperCase_ReturnsTrue()
    {
        var result = InvokeTryParseAddress("0XAB", out var address);
        result.Should().BeTrue();
        address.Should().Be(0xAB);
    }

    [Fact]
    public void TryParseAddress_EmptyString_ReturnsFalse()
    {
        var result = InvokeTryParseAddress("", out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseAddress_WhitespaceString_ReturnsFalse()
    {
        var result = InvokeTryParseAddress("   ", out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseAddress_InvalidString_ReturnsFalse()
    {
        var result = InvokeTryParseAddress("not_a_number", out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseAddress_InvalidHexString_ReturnsFalse()
    {
        var result = InvokeTryParseAddress("0xZZZZ", out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseAddress_DoubleValue_ReturnsFalse()
    {
        // A double is not long, int, string, or JsonElement
        var result = InvokeTryParseAddress(3.14d, out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseAddress_JsonElementNumber_ReturnsTrue()
    {
        var doc = JsonDocument.Parse("42");
        var element = doc.RootElement;
        // JsonElement is a struct, so box it
        var result = InvokeTryParseAddress(element, out var address);
        result.Should().BeTrue();
        address.Should().Be(42);
    }

    [Fact]
    public void TryParseAddress_JsonElementString_ReturnsTrue()
    {
        var doc = JsonDocument.Parse("\"0x1A\"");
        var element = doc.RootElement;
        var result = InvokeTryParseAddress(element, out var address);
        result.Should().BeTrue();
        address.Should().Be(0x1A);
    }

    [Fact]
    public void TryParseAddress_JsonElementStringDecimal_ReturnsTrue()
    {
        var doc = JsonDocument.Parse("\"999\"");
        var element = doc.RootElement;
        var result = InvokeTryParseAddress(element, out var address);
        result.Should().BeTrue();
        address.Should().Be(999);
    }

    [Fact]
    public void TryParseAddress_JsonElementStringInvalid_ReturnsFalse()
    {
        var doc = JsonDocument.Parse("\"not_valid\"");
        var element = doc.RootElement;
        var result = InvokeTryParseAddress(element, out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseAddress_JsonElementStringEmpty_ReturnsFalse()
    {
        var doc = JsonDocument.Parse("\"\"");
        var element = doc.RootElement;
        var result = InvokeTryParseAddress(element, out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseAddress_JsonElementBool_ReturnsFalse()
    {
        var doc = JsonDocument.Parse("true");
        var element = doc.RootElement;
        var result = InvokeTryParseAddress(element, out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseAddress_JsonElementNull_ReturnsFalse()
    {
        var doc = JsonDocument.Parse("null");
        var element = doc.RootElement;
        var result = InvokeTryParseAddress(element, out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseAddress_JsonElementFloatNotInt64_ReturnsFalse()
    {
        // A JSON number like 1.5 cannot be parsed as Int64
        var doc = JsonDocument.Parse("1.5");
        var element = doc.RootElement;
        var result = InvokeTryParseAddress(element, out _);
        result.Should().BeFalse();
    }

    // ──────────────── BuildSymbolValueTypeIndex (private) via reflection ────────────────

    private static IReadOnlyDictionary<string, SymbolValueType> InvokeBuildSymbolValueTypeIndex(
        IReadOnlyList<SignatureSet> signatureSets)
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "BuildSymbolValueTypeIndex",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (IReadOnlyDictionary<string, SymbolValueType>)method!.Invoke(null, new object[] { signatureSets })!;
    }

    [Fact]
    public void BuildSymbolValueTypeIndex_EmptySets_ReturnsEmpty()
    {
        var result = InvokeBuildSymbolValueTypeIndex(Array.Empty<SignatureSet>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildSymbolValueTypeIndex_SingleSet_ReturnsCorrectTypes()
    {
        var sigs = new List<SignatureSpec>
        {
            new("Health", "AA BB", 0, ValueType: SymbolValueType.Float),
            new("Mana", "CC DD", 0, ValueType: SymbolValueType.Int64),
        };
        var sets = new List<SignatureSet> { new("set1", "1.0", sigs) };

        var result = InvokeBuildSymbolValueTypeIndex(sets);

        result.Should().ContainKey("Health").WhoseValue.Should().Be(SymbolValueType.Float);
        result.Should().ContainKey("Mana").WhoseValue.Should().Be(SymbolValueType.Int64);
    }

    [Fact]
    public void BuildSymbolValueTypeIndex_DuplicateName_KeepsFirst()
    {
        var set1 = new SignatureSet("set1", "1.0", new List<SignatureSpec>
        {
            new("Health", "AA BB", 0, ValueType: SymbolValueType.Float)
        });
        var set2 = new SignatureSet("set2", "1.0", new List<SignatureSpec>
        {
            new("Health", "CC DD", 0, ValueType: SymbolValueType.Int32)
        });

        var result = InvokeBuildSymbolValueTypeIndex(new List<SignatureSet> { set1, set2 });

        result["Health"].Should().Be(SymbolValueType.Float);
    }

    // ──────────────── TryBuildAnchorSymbol (private) via reflection ────────────────

    [Fact]
    public void TryBuildAnchorSymbol_NullId_ReturnsFalse()
    {
        var (result, _) = InvokeTryBuildAnchorSymbol(null!, null, 0.9, new Dictionary<string, SymbolValueType>(), "fp", new Dictionary<string, SymbolInfo>());
        result.Should().BeFalse();
    }

    [Fact]
    public void TryBuildAnchorSymbol_EmptyId_ReturnsFalse()
    {
        var (result, _) = InvokeTryBuildAnchorSymbol("", (long)0x1000, 0.9, new Dictionary<string, SymbolValueType>(), "fp", new Dictionary<string, SymbolInfo>());
        result.Should().BeFalse();
    }

    [Fact]
    public void TryBuildAnchorSymbol_WhitespaceId_ReturnsFalse()
    {
        var (result, _) = InvokeTryBuildAnchorSymbol("   ", (long)0x1000, 0.9, new Dictionary<string, SymbolValueType>(), "fp", new Dictionary<string, SymbolInfo>());
        result.Should().BeFalse();
    }

    [Fact]
    public void TryBuildAnchorSymbol_DuplicateId_ReturnsFalse()
    {
        var existing = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["Health"] = new("Health", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature)
        };

        var (result, _) = InvokeTryBuildAnchorSymbol("Health", (long)0x2000, 0.9, new Dictionary<string, SymbolValueType>(), "fp", existing);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryBuildAnchorSymbol_InvalidAddress_ReturnsFalse()
    {
        // Address that can't be parsed — use a non-numeric, non-hex string via boxed object
        var (result, _) = InvokeTryBuildAnchorSymbol("Test", "not_an_address", 0.9, new Dictionary<string, SymbolValueType>(), "fp", new Dictionary<string, SymbolInfo>());
        result.Should().BeFalse();
    }

    [Fact]
    public void TryBuildAnchorSymbol_ValidLongAddress_ReturnsTrue()
    {
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        var valueTypes = new Dictionary<string, SymbolValueType>(StringComparer.OrdinalIgnoreCase)
        {
            ["Test"] = SymbolValueType.Float
        };

        var (result, symbol) = InvokeTryBuildAnchorSymbol("Test", (long)0x401000, 0.85, valueTypes, "fp_123", symbols);
        result.Should().BeTrue();
        symbol.Should().NotBeNull();
        symbol!.ValueType.Should().Be(SymbolValueType.Float);
        symbol.Confidence.Should().Be(0.85);
    }

    [Fact]
    public void TryBuildAnchorSymbol_ZeroConfidence_DefaultsTo099()
    {
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);

        var (result, symbol) = InvokeTryBuildAnchorSymbol("Test", (long)0x401000, 0.0, new Dictionary<string, SymbolValueType>(), "fp", symbols);
        result.Should().BeTrue();
        symbol!.Confidence.Should().Be(0.99);
    }

    [Fact]
    public void TryBuildAnchorSymbol_NegativeConfidence_DefaultsTo099()
    {
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);

        var (result, symbol) = InvokeTryBuildAnchorSymbol("Test", (long)0x401000, -1.0, new Dictionary<string, SymbolValueType>(), "fp", symbols);
        result.Should().BeTrue();
        symbol!.Confidence.Should().Be(0.99);
    }

    [Fact]
    public void TryBuildAnchorSymbol_NoMatchingValueType_DefaultsToInt32()
    {
        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase);
        var emptyValueTypes = new Dictionary<string, SymbolValueType>();

        var (result, symbol) = InvokeTryBuildAnchorSymbol("Test", (long)0x401000, 0.9, emptyValueTypes, "fp", symbols);
        result.Should().BeTrue();
        symbol!.ValueType.Should().Be(SymbolValueType.Int32);
    }

    private static (bool Result, SymbolInfo? Symbol) InvokeTryBuildAnchorSymbol(
        string id, object? address, double confidence,
        IReadOnlyDictionary<string, SymbolValueType> valueTypes,
        string fingerprintId,
        IDictionary<string, SymbolInfo> symbols)
    {
        // Build the GhidraAnchorDto via reflection since it's private
        var anchorType = typeof(SignatureResolverSymbolHydration).GetNestedType(
            "GhidraAnchorDto", BindingFlags.NonPublic);
        anchorType.Should().NotBeNull("GhidraAnchorDto should exist");
        var anchor = Activator.CreateInstance(anchorType!, new object?[] { id, address, confidence });

        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryBuildAnchorSymbol",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var args = new object?[] { anchor, valueTypes, fingerprintId, symbols, null };
        var result = (bool)method!.Invoke(null, args)!;
        var symbol = args[4] as SymbolInfo;
        return (result, symbol);
    }

    // ──────────────── IsMatchingFingerprint (private) via reflection ────────────────

    private static bool InvokeIsMatchingFingerprint(string? actual, string expected)
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "IsMatchingFingerprint",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (bool)method!.Invoke(null, new object?[] { actual, expected })!;
    }

    [Fact]
    public void IsMatchingFingerprint_ExactMatch_ReturnsTrue()
    {
        InvokeIsMatchingFingerprint("abc", "abc").Should().BeTrue();
    }

    [Fact]
    public void IsMatchingFingerprint_CaseInsensitiveMatch_ReturnsTrue()
    {
        InvokeIsMatchingFingerprint("ABC", "abc").Should().BeTrue();
    }

    [Fact]
    public void IsMatchingFingerprint_Mismatch_ReturnsFalse()
    {
        InvokeIsMatchingFingerprint("abc", "def").Should().BeFalse();
    }

    [Fact]
    public void IsMatchingFingerprint_NullActual_ReturnsFalse()
    {
        InvokeIsMatchingFingerprint(null, "abc").Should().BeFalse();
    }

    // ──────────────── ResolveDefaultGhidraSymbolPackRoot ────────────────

    [Fact]
    public void ResolveDefaultGhidraSymbolPackRoot_ReturnsNonEmptyPath()
    {
        // Clear the override env var if set
        var original = Environment.GetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT", null);
            var result = SignatureResolverSymbolHydration.ResolveDefaultGhidraSymbolPackRoot();
            result.Should().NotBeNullOrWhiteSpace();
            result.Should().Contain("ghidra");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT", original);
        }
    }

    [Fact]
    public void ResolveDefaultGhidraSymbolPackRoot_RespectsEnvOverride()
    {
        var original = Environment.GetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT");
        try
        {
            var customPath = Path.Join(_tempRoot, "custom-symbols");
            Environment.SetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT", customPath);
            var result = SignatureResolverSymbolHydration.ResolveDefaultGhidraSymbolPackRoot();
            result.Should().Be(customPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_GHIDRA_SYMBOL_PACK_ROOT", original);
        }
    }

    // ──────────────── TryReadPackMetadata edge cases ────────────────

    [Fact]
    public void SelectBestGhidraPackPath_PackWithNullDeserialize_Skipped()
    {
        var fp = "game_nullpack";
        var goodPath = Path.Join(_tempRoot, "good.json");
        WritePack(goodPath, fp, "2026-03-01T00:00:00Z");

        // A JSON file that deserializes to null
        var nullPath = Path.Join(_tempRoot, "nullpack.json");
        File.WriteAllText(nullPath, "null");

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        result.Should().Be(goodPath);
    }

    // ──────────────── EnumeratePackCandidates — artifact-index.json is excluded ────────────────

    [Fact]
    public void SelectBestGhidraPackPath_ArtifactIndexFile_NotIncludedInEnumeration()
    {
        var fp = "game_excluded";
        // Write artifact-index.json with a matching fingerprint but it should NOT be picked up as a pack
        var indexPath = Path.Join(_tempRoot, "artifact-index.json");
        var json = $$"""
                     {
                       "binaryFingerprint": { "fingerprintId": "{{fp}}" },
                       "buildMetadata": { "generatedAtUtc": "2026-03-01T00:00:00Z" }
                     }
                     """;
        File.WriteAllText(indexPath, json);

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        // artifact-index.json should be filtered out
        result.Should().BeNull();
    }

    // ──────────────── helpers ────────────────

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
        var indexPath = Path.Join(root, "artifact-index.json");
        var json = $$"""
                     {
                       "schemaVersion": "1.0",
                       "analysisRunId": "test-index",
                       "binaryFingerprint": {
                         "fingerprintId": "{{fingerprintId}}",
                         "moduleName": "StarWarsG.exe",
                         "fileSha256": "0123456789abcdef"
                       },
                       "artifactPointers": {
                         "symbolPackPath": "{{symbolPackPath.Replace("\\", "/")}}"
                       }
                     }
                     """;
        File.WriteAllText(indexPath, json);
    }
}
