using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Interop;
using SwfocTrainer.Runtime.Scanning;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Wave 6 branch-coverage tests targeting remaining uncovered branches in:
/// - SignatureResolver.SymbolHydration.cs
/// - NamedPipeExtenderBackend.cs
/// - ProcessMemoryScanner.cs
/// - SignatureResolver.cs
/// - SignatureResolver.Fallbacks.cs
/// - ProcessLocator.cs
/// - LaunchContextResolver.cs
/// - AobScanner.cs
/// </summary>
public sealed class RuntimeInfraWave6Tests : IDisposable
{
    private static readonly ILogger<SignatureResolver> Logger = NullLogger<SignatureResolver>.Instance;
    private readonly string _tempRoot;

    public RuntimeInfraWave6Tests()
    {
        _tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-wave6-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  AobScanner — all branches
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AobScanner_FindPattern_NullMemory_Throws()
    {
        var act = () => AobScanner.FindPattern(null!, (nint)0, AobPattern.Parse("AA"));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AobScanner_FindPattern_NullPattern_Throws()
    {
        var act = () => AobScanner.FindPattern(new byte[4], (nint)0, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AobScanner_FindPattern_EmptySignature_ReturnsZero()
    {
        var pattern = AobPattern.Parse("");
        var result = AobScanner.FindPattern(new byte[] { 0xAA, 0xBB }, (nint)0x1000, pattern);
        result.Should().Be(nint.Zero);
    }

    [Fact]
    public void AobScanner_FindPattern_ExactMatch_ReturnsCorrectAddress()
    {
        var memory = new byte[] { 0x00, 0xAA, 0xBB, 0xCC, 0x00 };
        var pattern = AobPattern.Parse("AA BB CC");
        var result = AobScanner.FindPattern(memory, (nint)0x1000, pattern);
        result.Should().Be((nint)(0x1000 + 1));
    }

    [Fact]
    public void AobScanner_FindPattern_WildcardMatch_ReturnsCorrectAddress()
    {
        var memory = new byte[] { 0x00, 0xAA, 0xFF, 0xCC, 0x00 };
        var pattern = AobPattern.Parse("AA ?? CC");
        var result = AobScanner.FindPattern(memory, (nint)0x2000, pattern);
        result.Should().Be((nint)(0x2000 + 1));
    }

    [Fact]
    public void AobScanner_FindPattern_NoMatch_ReturnsZero()
    {
        var memory = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44 };
        var pattern = AobPattern.Parse("AA BB CC");
        var result = AobScanner.FindPattern(memory, (nint)0x1000, pattern);
        result.Should().Be(nint.Zero);
    }

    [Fact]
    public void AobScanner_FindPattern_PatternLongerThanMemory_ReturnsZero()
    {
        var memory = new byte[] { 0xAA };
        var pattern = AobPattern.Parse("AA BB CC");
        var result = AobScanner.FindPattern(memory, (nint)0x1000, pattern);
        result.Should().Be(nint.Zero);
    }

    [Fact]
    public void AobScanner_FindPattern_PatternEqualsMemoryLength_Matches()
    {
        var memory = new byte[] { 0xAA, 0xBB };
        var pattern = AobPattern.Parse("AA BB");
        var result = AobScanner.FindPattern(memory, (nint)0x1000, pattern);
        result.Should().Be((nint)0x1000);
    }

    [Fact]
    public void AobScanner_FindPattern_FirstMatchReturned()
    {
        var memory = new byte[] { 0xAA, 0xBB, 0xAA, 0xBB };
        var pattern = AobPattern.Parse("AA BB");
        var result = AobScanner.FindPattern(memory, (nint)0x1000, pattern);
        result.Should().Be((nint)0x1000);
    }

    [Fact]
    public void AobScanner_FindPattern_PartialMismatchDoesNotMatch()
    {
        var memory = new byte[] { 0xAA, 0xBB, 0xDD };
        var pattern = AobPattern.Parse("AA BB CC");
        var result = AobScanner.FindPattern(memory, (nint)0x1000, pattern);
        result.Should().Be(nint.Zero);
    }

    [Fact]
    public void AobScanner_FindPattern_AllWildcard_MatchesFirstPosition()
    {
        var memory = new byte[] { 0x11, 0x22, 0x33 };
        var pattern = AobPattern.Parse("?? ??");
        var result = AobScanner.FindPattern(memory, (nint)0x1000, pattern);
        result.Should().Be((nint)0x1000);
    }

    [Fact]
    public void AobScanner_FindPattern_ProcessOverload_NullProcess_Throws()
    {
        var act = () => AobScanner.FindPattern(null!, new byte[4], (nint)0, AobPattern.Parse("AA"));
        act.Should().Throw<ArgumentNullException>();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  AobPattern.Parse — branches
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AobPattern_Parse_NullPattern_Throws()
    {
        var act = () => AobPattern.Parse(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void AobPattern_Parse_QuestionMark_IsWildcard()
    {
        var pattern = AobPattern.Parse("AA ? BB");
        pattern.Bytes.Should().HaveCount(3);
        pattern.Bytes[0].Should().Be(0xAA);
        pattern.Bytes[1].Should().BeNull();
        pattern.Bytes[2].Should().Be(0xBB);
    }

    [Fact]
    public void AobPattern_Parse_DoubleQuestionMark_IsWildcard()
    {
        var pattern = AobPattern.Parse("AA ?? BB");
        pattern.Bytes[1].Should().BeNull();
    }

    [Fact]
    public void AobPattern_Parse_EmptyString_ReturnsEmptyBytes()
    {
        var pattern = AobPattern.Parse("");
        pattern.Bytes.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ProcessMemoryScanner — FloatApproxScanRequest overload
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ScanFloatApprox_RequestOverload_MaxResultsZero_ReturnsEmpty()
    {
        var request = new ProcessMemoryScanner.FloatApproxScanRequest(0, 1.0f, 0.1f, false, 0);
        var result = ProcessMemoryScanner.ScanFloatApprox(request, CancellationToken.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ScanFloatApprox_RequestOverload_MaxResultsNegative_ReturnsEmpty()
    {
        var request = new ProcessMemoryScanner.FloatApproxScanRequest(0, 1.0f, 0.1f, false, -1);
        var result = ProcessMemoryScanner.ScanFloatApprox(request, CancellationToken.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ScanFloatApprox_RequestOverload_InvalidProcess_Throws()
    {
        var request = new ProcessMemoryScanner.FloatApproxScanRequest(99999999, 1.0f, 0.1f, false, 1);
        var act = () => ProcessMemoryScanner.ScanFloatApprox(request, CancellationToken.None);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ScanFloatApprox_ZeroTolerance_InvalidProcess_Throws()
    {
        var act = () => ProcessMemoryScanner.ScanFloatApprox(99999999, 1.0f, 0.0f, false, 1, CancellationToken.None);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ScanInt32_Cancellation_WithValidMaxResults_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        // maxResults > 0 but token already cancelled - OpenProcess returns 0 which throws before cancel check
        var act = () => ProcessMemoryScanner.ScanInt32(99999999, 42, false, 1, cts.Token);
        act.Should().Throw<Exception>(); // either InvalidOperationException or OperationCanceledException
    }

    [Fact]
    public void ScanFloatApprox_WritableOnly_MaxResultsZero_ReturnsEmpty()
    {
        var result = ProcessMemoryScanner.ScanFloatApprox(0, 1.0f, 0.1f, true, 0, CancellationToken.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ScanInt32_WritableOnly_MaxResultsZero_ReturnsEmpty()
    {
        var result = ProcessMemoryScanner.ScanInt32(0, 42, true, 0, CancellationToken.None);
        result.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SignatureResolver.SymbolHydration — deeper branch coverage
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void TryHydrateSymbolsFromGhidraPack_NullPackRoot_Throws()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryHydrateSymbolsFromGhidraPack", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);
        method.Should().NotBeNull();

        var act = () => method!.Invoke(null, new object[]
        {
            null!, Logger, null!, Array.Empty<SignatureSet>(), new Dictionary<string, SymbolInfo>()
        });
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void TryHydrateSymbolsFromGhidraPack_NullLogger_Throws()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryHydrateSymbolsFromGhidraPack", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);

        var act = () => method!.Invoke(null, new object[]
        {
            _tempRoot, null!, null!, Array.Empty<SignatureSet>(), new Dictionary<string, SymbolInfo>()
        });
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void TryHydrateSymbolsFromGhidraPack_NullSignatureSets_Throws()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryHydrateSymbolsFromGhidraPack", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);

        var act = () => method!.Invoke(null, new object[]
        {
            _tempRoot, Logger, null!, null!, new Dictionary<string, SymbolInfo>()
        });
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void TryHydrateSymbolsFromGhidraPack_NullSymbols_Throws()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryHydrateSymbolsFromGhidraPack", BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Public);

        var act = () => method!.Invoke(null, new object[]
        {
            _tempRoot, Logger, null!, Array.Empty<SignatureSet>(), null!
        });
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<ArgumentNullException>();
    }

    [Fact]
    public void SelectBestGhidraPackPath_MultiplePacks_OrdersByPrecedenceThenDate()
    {
        var fp = "module_wave6test0001";
        // exact match has precedence 0
        WritePack(Path.Join(_tempRoot, $"{fp}.json"), fp, "2026-01-01T00:00:00Z");
        // enumeration candidate has precedence 2
        var subDir = Path.Join(_tempRoot, "sub");
        Directory.CreateDirectory(subDir);
        WritePack(Path.Join(subDir, "other.json"), fp, "2026-06-01T00:00:00Z");

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        // Exact match (precedence 0) wins over enumeration (precedence 2)
        result.Should().Contain(fp);
    }

    [Fact]
    public void SelectBestGhidraPackPath_SamePrecedence_NewerDateWins()
    {
        var fp = "module_wave6test0002";
        var subDir = Path.Join(_tempRoot, "sub");
        Directory.CreateDirectory(subDir);
        WritePack(Path.Join(subDir, "old.json"), fp, "2026-01-01T00:00:00Z");
        WritePack(Path.Join(subDir, "new.json"), fp, "2026-06-01T00:00:00Z");

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        result.Should().NotBeNull();
        // Should pick new.json (newer date at same precedence 2)
        result!.Should().Contain("new.json");
    }

    [Fact]
    public void SelectBestGhidraPackPath_IndexedPath_SkippedInEnumeration()
    {
        var fp = "module_wave6test0003";
        var indexedPath = Path.Join(_tempRoot, "indexed.json");
        WritePack(indexedPath, fp, "2026-06-01T00:00:00Z");
        WriteArtifactIndex(_tempRoot, fp, "indexed.json");

        // Also write exact match
        WritePack(Path.Join(_tempRoot, $"{fp}.json"), fp, "2026-01-01T00:00:00Z");

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        // Exact match (precedence 0) beats indexed (precedence 1)
        result.Should().Contain(fp + ".json");
    }

    // ─── TryResolveGhidraPackPath — empty root branch ───

    [Fact]
    public void TryResolveGhidraPackPath_EmptyGhidraPackRoot_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryResolveGhidraPackPath", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var args = new object?[] { "", null!, null!, null! };
        args[0] = "";
        args[1] = null!; // module (not used if root is empty)
        args[2] = ""; // fingerprintId out
        args[3] = ""; // packPath out
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    // ─── ResolveIndexedPackPath branches ───

    [Fact]
    public void ResolveIndexedPackPath_NullPath_ReturnsNull()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "ResolveIndexedPackPath", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { _tempRoot, null });
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveIndexedPackPath_EmptyPath_ReturnsNull()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "ResolveIndexedPackPath", BindingFlags.NonPublic | BindingFlags.Static);

        var result = method!.Invoke(null, new object?[] { _tempRoot, "" });
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveIndexedPackPath_WhitespacePath_ReturnsNull()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "ResolveIndexedPackPath", BindingFlags.NonPublic | BindingFlags.Static);

        var result = method!.Invoke(null, new object?[] { _tempRoot, "   " });
        result.Should().BeNull();
    }

    [Fact]
    public void ResolveIndexedPackPath_RootedPath_ReturnsAsIs()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "ResolveIndexedPackPath", BindingFlags.NonPublic | BindingFlags.Static);

        var absolutePath = Path.Join(_tempRoot, "absolute.json");
        var result = (string?)method!.Invoke(null, new object?[] { _tempRoot, absolutePath });
        result.Should().Be(absolutePath);
    }

    [Fact]
    public void ResolveIndexedPackPath_RelativePath_ReturnsFullPath()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "ResolveIndexedPackPath", BindingFlags.NonPublic | BindingFlags.Static);

        var result = (string?)method!.Invoke(null, new object?[] { _tempRoot, "sub/file.json" });
        result.Should().NotBeNull();
        result!.Should().Contain("sub");
    }

    // ─── TryDeserializeGhidraSymbolPack branches ───

    [Fact]
    public void TryDeserializeGhidraSymbolPack_NullBinaryFingerprint_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryDeserializeGhidraSymbolPack", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var packPath = Path.Join(_tempRoot, "nofp.json");
        File.WriteAllText(packPath, """{"schemaVersion":"1.0"}""");

        var packType = typeof(SignatureResolverSymbolHydration).GetNestedType(
            "GhidraSymbolPackDto", BindingFlags.NonPublic);
        var args = new object?[] { Logger, packPath, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryDeserializeGhidraSymbolPack_NullDeserialization_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryDeserializeGhidraSymbolPack", BindingFlags.NonPublic | BindingFlags.Static);

        var packPath = Path.Join(_tempRoot, "nullpack.json");
        File.WriteAllText(packPath, "null");

        var args = new object?[] { Logger, packPath, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryDeserializeGhidraSymbolPack_InvalidJson_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryDeserializeGhidraSymbolPack", BindingFlags.NonPublic | BindingFlags.Static);

        var packPath = Path.Join(_tempRoot, "badjson.json");
        File.WriteAllText(packPath, "NOT JSON {{");

        var args = new object?[] { Logger, packPath, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryDeserializeGhidraSymbolPack_IoError_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryDeserializeGhidraSymbolPack", BindingFlags.NonPublic | BindingFlags.Static);

        var nonExistent = Path.Join(_tempRoot, "nonexistent", "pack.json");
        var args = new object?[] { Logger, nonExistent, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryDeserializeGhidraSymbolPack_ValidPack_ReturnsTrue()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryDeserializeGhidraSymbolPack", BindingFlags.NonPublic | BindingFlags.Static);

        var packPath = Path.Join(_tempRoot, "good.json");
        WritePack(packPath, "fp_test", "2026-01-01T00:00:00Z");

        var args = new object?[] { Logger, packPath, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
        args[2].Should().NotBeNull();
    }

    // ─── TryParseAddress — additional edge cases ───

    [Fact]
    public void TryParseAddress_JsonElementArray_ReturnsFalse()
    {
        var doc = JsonDocument.Parse("[1,2,3]");
        var element = doc.RootElement;
        var result = InvokeTryParseAddress(element, out _);
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseAddress_StringHex0xPrefix_LowerCase_ReturnsTrue()
    {
        var result = InvokeTryParseAddress("0xab", out var address);
        result.Should().BeTrue();
        address.Should().Be(0xAB);
    }

    [Fact]
    public void TryParseAddress_BoolValue_ReturnsFalse()
    {
        var result = InvokeTryParseAddress(true, out _);
        result.Should().BeFalse();
    }

    // ─── EnumeratePackCandidates — IOException and UnauthorizedAccess ───

    [Fact]
    public void EnumeratePackCandidates_ValidRoot_ReturnsFiles()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "EnumeratePackCandidates", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        File.WriteAllText(Path.Join(_tempRoot, "test.json"), "{}");
        var result = (IEnumerable<string>)method!.Invoke(null, new object[] { _tempRoot })!;
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void EnumeratePackCandidates_ExcludesArtifactIndex()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "EnumeratePackCandidates", BindingFlags.NonPublic | BindingFlags.Static);

        File.WriteAllText(Path.Join(_tempRoot, "artifact-index.json"), "{}");
        File.WriteAllText(Path.Join(_tempRoot, "pack.json"), "{}");
        var result = ((IEnumerable<string>)method!.Invoke(null, new object[] { _tempRoot })!).ToArray();
        result.Should().OnlyContain(p => !Path.GetFileName(p).Equals("artifact-index.json", StringComparison.OrdinalIgnoreCase));
    }

    // ─── HasMatchingArtifactFingerprint ───

    [Fact]
    public void HasMatchingArtifactFingerprint_NullBinaryFingerprint_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "HasMatchingArtifactFingerprint", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var indexType = typeof(SignatureResolverSymbolHydration).GetNestedType(
            "GhidraArtifactIndexDto", BindingFlags.NonPublic);
        var pointersType = typeof(SignatureResolverSymbolHydration).GetNestedType(
            "GhidraArtifactPointersDto", BindingFlags.NonPublic);

        var index = Activator.CreateInstance(indexType!, new object?[] { null, null });
        var result = (bool)method!.Invoke(null, new[] { index, "some_fp" })!;
        result.Should().BeFalse();
    }

    // ─── TryBuildAnchorSymbol — Anchors with null list ───

    [Fact]
    public void SelectBestGhidraPackPath_PackWithNullAnchors_SucceedsInSelection()
    {
        var fp = "module_wave6_nullanchors";
        var packPath = Path.Join(_tempRoot, "pack.json");
        var json = $$"""
                     {
                       "binaryFingerprint": { "fingerprintId": "{{fp}}" },
                       "buildMetadata": { "generatedAtUtc": "2026-01-01T00:00:00Z" },
                       "anchors": null
                     }
                     """;
        File.WriteAllText(packPath, json);

        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, fp);
        result.Should().NotBeNull();
    }

    // ─── TryReadArtifactIndex — IOException path ───

    [Fact]
    public void TryReadArtifactIndex_NonExistentFile_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryReadArtifactIndex", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var indexType = typeof(SignatureResolverSymbolHydration).GetNestedType(
            "GhidraArtifactIndexDto", BindingFlags.NonPublic);
        var args = new object?[] { Path.Join(_tempRoot, "nonexistent.json"), null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadArtifactIndex_InvalidJson_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryReadArtifactIndex", BindingFlags.NonPublic | BindingFlags.Static);

        var indexPath = Path.Join(_tempRoot, "bad-index.json");
        File.WriteAllText(indexPath, "NOT JSON");

        var args = new object?[] { indexPath, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadArtifactIndex_NullDeserialization_ReturnsFalse()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryReadArtifactIndex", BindingFlags.NonPublic | BindingFlags.Static);

        var indexPath = Path.Join(_tempRoot, "null-index.json");
        File.WriteAllText(indexPath, "null");

        var args = new object?[] { indexPath, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryReadArtifactIndex_ValidIndex_ReturnsTrue()
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryReadArtifactIndex", BindingFlags.NonPublic | BindingFlags.Static);

        var indexPath = Path.Join(_tempRoot, "good-index.json");
        File.WriteAllText(indexPath, """
            {
              "binaryFingerprint": { "fingerprintId": "test_fp" },
              "artifactPointers": { "symbolPackPath": "pack.json" }
            }
            """);

        var args = new object?[] { indexPath, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SignatureResolver.cs — constructor and TryGetProcess branches
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SignatureResolver_NullLogger_Throws()
    {
        var act = () => new SignatureResolver(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SignatureResolver_NullGhidraPackRoot_Throws()
    {
        var act = () => new SignatureResolver(Logger, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SignatureResolver_ValidArgs_Creates()
    {
        var resolver = new SignatureResolver(Logger, _tempRoot);
        resolver.Should().NotBeNull();
    }

    [Fact]
    public async Task ResolveAsync_NullProfileBuild_Throws()
    {
        var resolver = new SignatureResolver(Logger, _tempRoot);
        var act = () => resolver.ResolveAsync(null!, Array.Empty<SignatureSet>(), new Dictionary<string, long>());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsync_NullSignatureSets_Throws()
    {
        var resolver = new SignatureResolver(Logger, _tempRoot);
        var build = new ProfileBuild("test", "1.0", "test.exe", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(build, null!, new Dictionary<string, long>());
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsync_NullFallbackOffsets_Throws()
    {
        var resolver = new SignatureResolver(Logger, _tempRoot);
        var build = new ProfileBuild("test", "1.0", "test.exe", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(build, Array.Empty<SignatureSet>(), null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsync_WithCancellationToken_NullProfileBuild_Throws()
    {
        var resolver = new SignatureResolver(Logger, _tempRoot);
        var act = () => resolver.ResolveAsync(null!, Array.Empty<SignatureSet>(), new Dictionary<string, long>(), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsync_WithCancellationToken_NullSignatureSets_Throws()
    {
        var resolver = new SignatureResolver(Logger, _tempRoot);
        var build = new ProfileBuild("test", "1.0", "test.exe", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(build, null!, new Dictionary<string, long>(), CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsync_WithCancellationToken_NullFallbackOffsets_Throws()
    {
        var resolver = new SignatureResolver(Logger, _tempRoot);
        var build = new ProfileBuild("test", "1.0", "test.exe", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(build, Array.Empty<SignatureSet>(), null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ResolveAsync_NoProcess_ThrowsInvalidOperationException()
    {
        var resolver = new SignatureResolver(Logger, _tempRoot);
        var build = new ProfileBuild("test", "1.0", "nonexistent_exe_99999.exe", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(build, Array.Empty<SignatureSet>(), new Dictionary<string, long>());
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Could not find running process*");
    }

    [Fact]
    public async Task ResolveAsync_InvalidPid_FallsBackToName_NotFound()
    {
        var resolver = new SignatureResolver(Logger, _tempRoot);
        var build = new ProfileBuild("test", "1.0", "nonexistent_exe_99999.exe", ExeTarget.Swfoc, ProcessId: 99999999);
        var act = () => resolver.ResolveAsync(build, Array.Empty<SignatureSet>(), new Dictionary<string, long>());
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Could not find running process*");
    }

    [Fact]
    public async Task ResolveAsync_EmptyExeName_NullPid_Throws()
    {
        var resolver = new SignatureResolver(Logger, _tempRoot);
        var build = new ProfileBuild("test", "1.0", "", ExeTarget.Swfoc);
        var act = () => resolver.ResolveAsync(build, Array.Empty<SignatureSet>(), new Dictionary<string, long>());
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public void SelectBestGhidraPackPath_Delegation_Works()
    {
        // Tests the delegation wrapper on SignatureResolver
        var result = SignatureResolver.SelectBestGhidraPackPath(_tempRoot, "nonexistent_fp");
        result.Should().BeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  SignatureResolver.Fallbacks — additional branch coverage
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void HandleSignatureHit_AddressResolutionFails_FallbackPositive_InvalidRead_LogsFinalWarning()
    {
        // Exercises the final "fallback offset is not readable" path in HandleSignatureHit
        var symbols = new Dictionary<string, SymbolInfo>();
        var moduleBytes = new byte[4];
        var baseAddress = (nint)0x400000;
        var sig = new SignatureSpec("TestSym", "AA BB", Offset: 0, AddressMode: SignatureAddressMode.ReadAbsolute32AtOffset);
        var set = new SignatureSet("Set1", "1.0", new[] { sig });
        var hit = (nint)(0x400000 + 2); // index=2, need 6 > 4
        var accessor = CreateFakeAccessor();
        var fallbacks = new Dictionary<string, long> { ["TestSym"] = 0x2000 };

        var ctx = new SignatureResolverFallbacks.SignatureHitContext(fallbacks, accessor, baseAddress, moduleBytes, symbols);
        SignatureResolverFallbacks.HandleSignatureHit(Logger, set, sig, hit, ctx);

        symbols.Should().NotContainKey("TestSym");
    }

    [Fact]
    public void HandleSignatureMiss_FallbackExists_SymbolNotAlreadyResolved_OffsetNegative_NotApplied()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var sig = new SignatureSpec("Test", "AA BB", 0);
        var fallbacks = new Dictionary<string, long> { ["Test"] = -500 };

        SignatureResolverFallbacks.HandleSignatureMiss(Logger, sig, fallbacks, CreateFakeAccessor(), (nint)0x400000, symbols);

        symbols.Should().NotContainKey("Test");
    }

    [Fact]
    public void HandleSignatureMiss_SixParamOverload_DelegatesToStruct()
    {
        // Verify the 6-param overload creates the struct correctly and delegates
        var symbols = new Dictionary<string, SymbolInfo>();
        var sig = new SignatureSpec("TestDel", "AA BB", 0);
        var fallbacks = new Dictionary<string, long>();

        SignatureResolverFallbacks.HandleSignatureMiss(Logger, sig, fallbacks, CreateFakeAccessor(), (nint)0x400000, symbols);

        symbols.Should().BeEmpty();
    }

    [Fact]
    public void ApplyStandaloneFallbacks_MultipleItems_MixedResults()
    {
        var symbols = new Dictionary<string, SymbolInfo>();
        var existing = new SymbolInfo("Resolved", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature);
        symbols["Resolved"] = existing;

        var fallbacks = new Dictionary<string, long>
        {
            ["Resolved"] = 0x2000,    // already resolved, skipped
            ["ZeroOff"] = 0,           // zero offset, not applied
            ["NegOff"] = -100,         // negative offset, not applied
            ["BadRead"] = 0x5000       // positive but invalid handle
        };

        SignatureResolverFallbacks.ApplyStandaloneFallbacks(
            Logger, fallbacks, CreateFakeAccessor(), (nint)0x400000, symbols);

        symbols["Resolved"].Should().BeSameAs(existing);
        symbols.Should().NotContainKey("ZeroOff");
        symbols.Should().NotContainKey("NegOff");
        symbols.Should().NotContainKey("BadRead");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ProcessLocator — static helper branch coverage
    // ═══════════════════════════════════════════════════════════════════════

    // ─── GetProcessDetection branches ───

    [Fact]
    public void GetProcessDetection_SweawProcessName_ReturnsSwEaw()
    {
        var result = InvokeGetProcessDetection("sweaw", null, null);
        result.Should().NotBeNull();
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Sweaw);
    }

    [Fact]
    public void GetProcessDetection_SwfocProcessName_ReturnsSwfoc()
    {
        var result = InvokeGetProcessDetection("swfoc", null, null);
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_SweawInPath_ReturnsSwEaw()
    {
        var result = InvokeGetProcessDetection("other", @"C:\games\sweaw.exe", null);
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Sweaw);
    }

    [Fact]
    public void GetProcessDetection_SwfocInPath_ReturnsSwfoc()
    {
        var result = InvokeGetProcessDetection("other", @"C:\games\swfoc.exe", null);
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_SweawInCmdLine_ReturnsSwEaw()
    {
        var result = InvokeGetProcessDetection("other", null, "sweaw.exe -nosound");
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Sweaw);
    }

    [Fact]
    public void GetProcessDetection_SwfocInCmdLine_ReturnsSwfoc()
    {
        var result = InvokeGetProcessDetection("other", null, "swfoc.exe -nosound");
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_StarWarsG_Cmdline_SweawHint_NoModMarkers()
    {
        var result = InvokeGetProcessDetection("starwarsg", null, "sweaw.exe");
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Sweaw);
    }

    [Fact]
    public void GetProcessDetection_StarWarsG_Cmdline_SweawHint_WithSteammod()
    {
        // sweaw.exe in cmdline -> TryDetectDirectTarget picks it up first via ContainsToken(commandLine, "sweaw.exe")
        var result = InvokeGetProcessDetection("starwarsg", null, "sweaw.exe steammod=123");
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Sweaw);
    }

    [Fact]
    public void GetProcessDetection_StarWarsG_Cmdline_SwfocExe()
    {
        var result = InvokeGetProcessDetection("starwarsg", null, "swfoc.exe launch");
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_StarWarsG_Cmdline_Corruption()
    {
        var result = InvokeGetProcessDetection("starwarsg", null, "corruption mode");
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_StarWarsG_Cmdline_Steammod()
    {
        var result = InvokeGetProcessDetection("starwarsg", null, "steammod=12345");
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_StarWarsG_Cmdline_Modpath()
    {
        var result = InvokeGetProcessDetection("starwarsg", null, "modpath=C:\\mods\\test");
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_StarWarsG_PathCorruption()
    {
        var result = InvokeGetProcessDetection("starwarsg", @"C:\games\corruption\StarWarsG.exe", null);
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_StarWarsG_PathGamedata()
    {
        var result = InvokeGetProcessDetection("starwarsg", @"C:\games\gamedata\StarWarsG.exe", null);
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_StarWarsG_DefaultFallback()
    {
        // StarWarsG with no specific hints => default FoC safe
        var result = InvokeGetProcessDetection("starwarsg", @"C:\games\StarWarsG.exe", null);
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_StarWarsGInPath_NotProcessName()
    {
        var result = InvokeGetProcessDetection("other", @"C:\games\starwarsg.exe", null);
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_StarWarsGInCmdLine_NotProcessName()
    {
        var result = InvokeGetProcessDetection("other", null, "starwarsg.exe launch");
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_SteammodMarker_NonStarWarsG()
    {
        var result = InvokeGetProcessDetection("other", null, "steammod=12345");
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_ModpathMarker_NonStarWarsG()
    {
        var result = InvokeGetProcessDetection("other", null, "modpath=test");
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_Unknown_Process()
    {
        var result = InvokeGetProcessDetection("chrome", null, null);
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Unknown);
    }

    [Fact]
    public void GetProcessDetection_SweawDotExeProcessName()
    {
        var result = InvokeGetProcessDetection("sweaw.exe", null, null);
        GetDetectionExeTarget(result!).Should().Be(ExeTarget.Sweaw);
    }

    // ─── InferMode branches ───

    [Theory]
    [InlineData(null, RuntimeMode.Unknown)]
    [InlineData("", RuntimeMode.Unknown)]
    [InlineData("   ", RuntimeMode.Unknown)]
    [InlineData("app.exe LAND", RuntimeMode.TacticalLand)]
    [InlineData("app.exe SPACE", RuntimeMode.TacticalSpace)]
    [InlineData("app.exe SKIRMISH", RuntimeMode.AnyTactical)]
    [InlineData("app.exe TACTICAL", RuntimeMode.AnyTactical)]
    [InlineData("app.exe CAMPAIGN", RuntimeMode.Galactic)]
    [InlineData("app.exe GALACTIC", RuntimeMode.Galactic)]
    [InlineData("app.exe -nosound", RuntimeMode.Unknown)]
    public void InferMode_Variations(string? commandLine, RuntimeMode expected)
    {
        var result = InvokeInferMode(commandLine);
        result.Should().Be(expected);
    }

    // ─── IsProcessName branches ───

    [Theory]
    [InlineData(null, "sweaw", false)]
    [InlineData("", "sweaw", false)]
    [InlineData("   ", "sweaw", false)]
    [InlineData("sweaw", "sweaw", true)]
    [InlineData("SWEAW", "sweaw", true)]
    [InlineData("sweaw.exe", "sweaw", true)]
    [InlineData("SWEAW.EXE", "sweaw", true)]
    [InlineData("other", "sweaw", false)]
    public void IsProcessName_Variations(string? processName, string expected, bool expectedResult)
    {
        InvokeIsProcessName(processName, expected).Should().Be(expectedResult);
    }

    // ─── ContainsToken branches ───

    [Theory]
    [InlineData(null, "test", false)]
    [InlineData("", "test", false)]
    [InlineData("test value", "test", true)]
    [InlineData("TEST VALUE", "test", true)]
    [InlineData("something", "test", false)]
    public void ContainsToken_Variations(string? value, string token, bool expected)
    {
        InvokeContainsToken(value, token).Should().Be(expected);
    }

    // ─── ExtractSteamModIds branches ───

    [Fact]
    public void ExtractSteamModIds_NullCommandLine_ReturnsEmpty()
    {
        var result = InvokeExtractSteamModIds(null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSteamModIds_EmptyCommandLine_ReturnsEmpty()
    {
        var result = InvokeExtractSteamModIds("");
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSteamModIds_WithSteammod_ReturnsSteamModId()
    {
        var result = InvokeExtractSteamModIds("app.exe STEAMMOD=3447786229");
        result.Should().Contain("3447786229");
    }

    [Fact]
    public void ExtractSteamModIds_WithWorkshopPath_ReturnsId()
    {
        var result = InvokeExtractSteamModIds(@"app.exe modpath=C:\steamapps\workshop\content\32470\1234567\");
        result.Should().Contain("1234567");
    }

    [Fact]
    public void ExtractSteamModIds_MultipleSteamMods_ReturnsAll()
    {
        var result = InvokeExtractSteamModIds("app.exe STEAMMOD=111 STEAMMOD=222");
        result.Should().Contain("111");
        result.Should().Contain("222");
    }

    // ─── ExtractModPath branches ───

    [Fact]
    public void ExtractModPath_NullCommandLine_ReturnsNull()
    {
        var result = InvokeExtractModPath(null);
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractModPath_NoModPath_ReturnsNull()
    {
        var result = InvokeExtractModPath("app.exe -nosound");
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractModPath_QuotedModPath_ReturnsValue()
    {
        var result = InvokeExtractModPath("app.exe modpath=\"C:\\mods\\test mod\"");
        result.Should().NotBeNull();
        result.Should().Contain("test mod");
    }

    [Fact]
    public void ExtractModPath_UnquotedModPath_ReturnsValue()
    {
        var result = InvokeExtractModPath("app.exe modpath=C:\\mods\\test");
        result.Should().NotBeNull();
        result.Should().Contain("test");
    }

    // ─── DetermineHostRole branches ───

    [Fact]
    public void DetermineHostRole_StarWarsG_ReturnsGameHost()
    {
        var result = InvokeDetermineHostRole(true, ExeTarget.Swfoc);
        result.Should().Be(ProcessHostRole.GameHost);
    }

    [Fact]
    public void DetermineHostRole_Swfoc_NotStarWarsG_ReturnsLauncher()
    {
        var result = InvokeDetermineHostRole(false, ExeTarget.Swfoc);
        result.Should().Be(ProcessHostRole.Launcher);
    }

    [Fact]
    public void DetermineHostRole_Sweaw_NotStarWarsG_ReturnsLauncher()
    {
        var result = InvokeDetermineHostRole(false, ExeTarget.Sweaw);
        result.Should().Be(ProcessHostRole.Launcher);
    }

    [Fact]
    public void DetermineHostRole_Unknown_ReturnsUnknown()
    {
        var result = InvokeDetermineHostRole(false, ExeTarget.Unknown);
        result.Should().Be(ProcessHostRole.Unknown);
    }

    // ─── NormalizeWorkshopIds branches ───

    [Fact]
    public void NormalizeWorkshopIds_Null_ReturnsEmpty()
    {
        var result = InvokeNormalizeWorkshopIds(null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeWorkshopIds_EmptyList_ReturnsEmpty()
    {
        var result = InvokeNormalizeWorkshopIds(Array.Empty<string>());
        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeWorkshopIds_WithValues_ReturnsSortedUnique()
    {
        var result = InvokeNormalizeWorkshopIds(new[] { "222,111", "333" });
        result.Should().BeEquivalentTo(new[] { "111", "222", "333" });
    }

    [Fact]
    public void NormalizeWorkshopIds_WithWhitespace_FiltersEmpty()
    {
        var result = InvokeNormalizeWorkshopIds(new[] { "  ", "", "111" });
        result.Should().HaveCount(1);
        result.Should().Contain("111");
    }

    [Fact]
    public void NormalizeWorkshopIds_Duplicates_Deduplicated()
    {
        var result = InvokeNormalizeWorkshopIds(new[] { "111", "111" });
        result.Should().HaveCount(1);
    }

    // ─── NormalizeForcedProfileId ───

    [Fact]
    public void NormalizeForcedProfileId_Null_ReturnsNull()
    {
        InvokeNormalizeForcedProfileId(null).Should().BeNull();
    }

    [Fact]
    public void NormalizeForcedProfileId_Whitespace_ReturnsNull()
    {
        InvokeNormalizeForcedProfileId("   ").Should().BeNull();
    }

    [Fact]
    public void NormalizeForcedProfileId_WithValue_ReturnsTrimmed()
    {
        InvokeNormalizeForcedProfileId("  test_profile  ").Should().Be("test_profile");
    }

    // ─── ResolveForcedContext branches ───

    [Fact]
    public void ResolveForcedContext_HasModMarkers_ReturnsDetected()
    {
        var result = InvokeResolveForcedContext("cmd", "modpath", new[] { "111" },
            new ProcessLocatorOptions());
        GetForcedContextSource(result!).Should().Be("detected");
    }

    [Fact]
    public void ResolveForcedContext_NoModMarkers_NoForcedHints_ReturnsDetected()
    {
        var result = InvokeResolveForcedContext("cmd", null, Array.Empty<string>(),
            ProcessLocatorOptions.None);
        GetForcedContextSource(result!).Should().Be("detected");
    }

    [Fact]
    public void ResolveForcedContext_NoModMarkers_WithForcedWorkshopIds_ReturnsForced()
    {
        var options = new ProcessLocatorOptions(new[] { "999" }, null);
        var result = InvokeResolveForcedContext("cmd", null, Array.Empty<string>(), options);
        GetForcedContextSource(result!).Should().Be("forced");
    }

    [Fact]
    public void ResolveForcedContext_NoModMarkers_WithForcedProfileId_ReturnsForced()
    {
        var options = new ProcessLocatorOptions(null, "custom_profile");
        var result = InvokeResolveForcedContext("cmd", null, Array.Empty<string>(), options);
        GetForcedContextSource(result!).Should().Be("forced");
    }

    // ─── ProcessLocator constructors ───

    [Fact]
    public void ProcessLocator_DefaultConstructor_Works()
    {
        var locator = new ProcessLocator();
        locator.Should().NotBeNull();
    }

    [Fact]
    public void ProcessLocator_LaunchContextResolverOnly_Works()
    {
        var locator = new ProcessLocator(new LaunchContextResolver());
        locator.Should().NotBeNull();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  LaunchContextResolver — deeper branch coverage
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_NullProcess_Throws()
    {
        var resolver = new LaunchContextResolver();
        var act = () => resolver.Resolve(null!, Array.Empty<TrainerProfile>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Resolve_NullProfiles_Throws()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("test", "", null, ExeTarget.Unknown);
        var act = () => resolver.Resolve(process, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Resolve_UnknownTarget_NoModMarkers_ReturnsUnknownKind()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("other", @"C:\other.exe", null, ExeTarget.Unknown);
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.LaunchKind.Should().Be(LaunchKind.Unknown);
        context.Recommendation.ReasonCode.Should().Be("unknown");
    }

    [Fact]
    public void Resolve_SwfocTarget_NoMod_ReturnsBaseGame()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("swfoc", @"C:\swfoc.exe", "swfoc.exe", ExeTarget.Swfoc);
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.LaunchKind.Should().Be(LaunchKind.BaseGame);
        context.Recommendation.ProfileId.Should().Be("base_swfoc");
    }

    [Fact]
    public void Resolve_SweawTarget_NoMod_ReturnsBaseGame()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("sweaw", @"C:\sweaw.exe", "sweaw.exe", ExeTarget.Sweaw);
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.LaunchKind.Should().Be(LaunchKind.BaseGame);
        context.Recommendation.ProfileId.Should().Be("base_sweaw");
    }

    [Fact]
    public void Resolve_MixedLaunchKind_HasBothSteamModAndModPath()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("StarWarsG", @"C:\StarWarsG.exe",
            "StarWarsG.exe STEAMMOD=111 MODPATH=C:\\mods\\test", ExeTarget.Swfoc,
            new Dictionary<string, string> { ["isStarWarsG"] = "true" });
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.LaunchKind.Should().Be(LaunchKind.Mixed);
    }

    [Fact]
    public void Resolve_WorkshopOnlyLaunchKind()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("StarWarsG", @"C:\StarWarsG.exe",
            "StarWarsG.exe STEAMMOD=111", ExeTarget.Swfoc,
            new Dictionary<string, string> { ["isStarWarsG"] = "true" });
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.LaunchKind.Should().Be(LaunchKind.Workshop);
    }

    [Fact]
    public void Resolve_LocalModPathLaunchKind()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("StarWarsG", @"C:\StarWarsG.exe",
            "StarWarsG.exe MODPATH=C:\\mods\\test", ExeTarget.Swfoc,
            new Dictionary<string, string> { ["isStarWarsG"] = "true" });
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.LaunchKind.Should().Be(LaunchKind.LocalModPath);
    }

    [Fact]
    public void Resolve_StarWarsGProcess_FocFallback_LowerConfidence()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("StarWarsG", @"C:\StarWarsG.exe",
            null, ExeTarget.Swfoc,
            new Dictionary<string, string> { ["isStarWarsG"] = "true" });
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.Recommendation.Confidence.Should().BeLessThan(0.60);
    }

    [Fact]
    public void Resolve_SwfocNotStarWarsG_HigherFallbackConfidence()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("swfoc", @"C:\swfoc.exe", "swfoc.exe", ExeTarget.Swfoc);
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.Recommendation.Confidence.Should().Be(0.65);
    }

    [Fact]
    public void Resolve_ForcedContextSource_WithForcedProfileId()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("StarWarsG", @"C:\StarWarsG.exe", null, ExeTarget.Swfoc,
            new Dictionary<string, string>
            {
                ["launchContextSource"] = "forced",
                ["forcedProfileId"] = "custom_123",
                ["isStarWarsG"] = "true"
            });
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.Source.Should().Be("forced");
        context.Recommendation.ProfileId.Should().Be("custom_123");
        context.Recommendation.ReasonCode.Should().Be("forced_profile_id");
        context.Recommendation.Confidence.Should().Be(1.0);
    }

    [Fact]
    public void Resolve_ForcedContextSource_NoForcedProfileId_FallsThrough()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("swfoc", @"C:\swfoc.exe", "swfoc.exe", ExeTarget.Swfoc,
            new Dictionary<string, string>
            {
                ["launchContextSource"] = "forced"
                // No forcedProfileId
            });
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        // No forced profile -> falls through to normal recommendation
        context.Recommendation.ReasonCode.Should().NotBe("forced_profile_id");
    }

    [Fact]
    public void Resolve_SteamModExistingMetadata_Merged()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("StarWarsG", @"C:\StarWarsG.exe", null, ExeTarget.Swfoc,
            new Dictionary<string, string>
            {
                ["steamModIdsDetected"] = "999",
                ["isStarWarsG"] = "true"
            });
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.SteamModIds.Should().Contain("999");
    }

    [Fact]
    public void Resolve_IsStarWarsGProcess_ByProcessName()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("StarWarsG", @"C:\other.exe", null, ExeTarget.Swfoc);
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.Recommendation.ReasonCode.Should().Be("foc_safe_starwarsg_fallback");
    }

    [Fact]
    public void Resolve_IsStarWarsGProcess_ByProcessNameExe()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("StarWarsG.exe", @"C:\other.exe", null, ExeTarget.Swfoc);
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.Recommendation.ReasonCode.Should().Be("foc_safe_starwarsg_fallback");
    }

    [Fact]
    public void Resolve_IsStarWarsGProcess_ByMetadata()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("other", @"C:\other.exe", null, ExeTarget.Swfoc,
            new Dictionary<string, string> { ["isStarWarsG"] = "true" });
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.Recommendation.ReasonCode.Should().Be("foc_safe_starwarsg_fallback");
    }

    [Fact]
    public void Resolve_IsStarWarsGProcess_ByPath()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("other", @"C:\StarWarsG.exe", null, ExeTarget.Swfoc);
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.Recommendation.ReasonCode.Should().Be("foc_safe_starwarsg_fallback");
    }

    [Fact]
    public void Resolve_IsStarWarsGProcess_MetadataFalse_NotStarWarsG()
    {
        var resolver = new LaunchContextResolver();
        var process = CreateProcessMetadata("other", @"C:\other.exe", "other.exe", ExeTarget.Swfoc,
            new Dictionary<string, string> { ["isStarWarsG"] = "false" });
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.Recommendation.Confidence.Should().Be(0.65);
    }

    [Fact]
    public void Resolve_ReadMetadata_NullMetadata_ReturnsNull()
    {
        var resolver = new LaunchContextResolver();
        var process = new ProcessMetadata(1, "test", "", null, ExeTarget.Unknown, RuntimeMode.Unknown, null);
        var context = resolver.Resolve(process, Array.Empty<TrainerProfile>());
        context.Should().NotBeNull();
    }

    // ─── RecommendByWorkshop — profile scoring ───

    [Fact]
    public void RecommendByWorkshop_NoSteamModIds_ReturnsNull()
    {
        var resolver = new LaunchContextResolver();
        var profiles = new List<TrainerProfile>
        {
            CreateProfile("roe_123_swfoc", "123")
        };
        var process = CreateProcessMetadata("swfoc", @"C:\swfoc.exe", "swfoc.exe", ExeTarget.Swfoc);
        var context = resolver.Resolve(process, profiles);
        // No steam mod IDs -> no workshop recommendation
        context.Recommendation.ReasonCode.Should().NotContain("steammod");
    }

    [Fact]
    public void RecommendByWorkshop_MatchingSteamWorkshopId_ReturnsProfile()
    {
        var resolver = new LaunchContextResolver();
        var profiles = new List<TrainerProfile>
        {
            CreateProfile("roe_123_swfoc", "123")
        };
        var process = CreateProcessMetadata("StarWarsG", @"C:\StarWarsG.exe",
            "StarWarsG.exe STEAMMOD=123", ExeTarget.Swfoc,
            new Dictionary<string, string> { ["isStarWarsG"] = "true" });
        var context = resolver.Resolve(process, profiles);
        context.Recommendation.ProfileId.Should().Be("roe_123_swfoc");
        context.Recommendation.ReasonCode.Should().Be("steammod_exact_roe");
        context.Recommendation.Confidence.Should().Be(1.0);
    }

    [Fact]
    public void RecommendByWorkshop_AotrProfile_ReasonCode()
    {
        var resolver = new LaunchContextResolver();
        var profiles = new List<TrainerProfile>
        {
            CreateProfile("aotr_456_swfoc", "456")
        };
        var process = CreateProcessMetadata("StarWarsG", @"C:\StarWarsG.exe",
            "StarWarsG.exe STEAMMOD=456", ExeTarget.Swfoc,
            new Dictionary<string, string> { ["isStarWarsG"] = "true" });
        var context = resolver.Resolve(process, profiles);
        context.Recommendation.ProfileId.Should().Be("aotr_456_swfoc");
        context.Recommendation.ReasonCode.Should().Be("steammod_exact_aotr");
    }

    [Fact]
    public void RecommendByWorkshop_GenericProfile_ReasonCode()
    {
        var resolver = new LaunchContextResolver();
        var profiles = new List<TrainerProfile>
        {
            CreateProfile("custom_789", "789")
        };
        var process = CreateProcessMetadata("StarWarsG", @"C:\StarWarsG.exe",
            "StarWarsG.exe STEAMMOD=789", ExeTarget.Swfoc,
            new Dictionary<string, string> { ["isStarWarsG"] = "true" });
        var context = resolver.Resolve(process, profiles);
        context.Recommendation.ProfileId.Should().Be("custom_789");
        context.Recommendation.ReasonCode.Should().Be("steammod_exact_profile");
    }

    // ─── RecommendByModPath ───

    [Fact]
    public void RecommendByModPath_MatchingHint_ReturnsProfile()
    {
        var resolver = new LaunchContextResolver();
        var profiles = new List<TrainerProfile>
        {
            CreateProfile("roe_123_swfoc", "123", "roe,3447786229", "roe_foc")
        };
        var process = CreateProcessMetadata("StarWarsG", @"C:\StarWarsG.exe",
            "StarWarsG.exe MODPATH=C:\\mods\\roe_foc_v2", ExeTarget.Swfoc,
            new Dictionary<string, string> { ["isStarWarsG"] = "true" });
        var context = resolver.Resolve(process, profiles);
        context.Recommendation.ProfileId.Should().Be("roe_123_swfoc");
        context.Recommendation.ReasonCode.Should().Be("modpath_hint_roe");
    }

    [Fact]
    public void RecommendByModPath_AotrHint_ReasonCode()
    {
        var resolver = new LaunchContextResolver();
        var profiles = new List<TrainerProfile>
        {
            CreateProfile("aotr_456_swfoc", "456", "aotr", "aotr_foc")
        };
        var process = CreateProcessMetadata("StarWarsG", @"C:\StarWarsG.exe",
            "StarWarsG.exe MODPATH=C:\\mods\\aotr_foc", ExeTarget.Swfoc,
            new Dictionary<string, string> { ["isStarWarsG"] = "true" });
        var context = resolver.Resolve(process, profiles);
        context.Recommendation.ReasonCode.Should().Be("modpath_hint_aotr");
    }

    [Fact]
    public void RecommendByModPath_GenericHint_ReasonCode()
    {
        var resolver = new LaunchContextResolver();
        var profiles = new List<TrainerProfile>
        {
            CreateProfile("custom_999", "999", "custom_mod", "custom_alias")
        };
        var process = CreateProcessMetadata("StarWarsG", @"C:\StarWarsG.exe",
            "StarWarsG.exe MODPATH=C:\\mods\\custom_mod", ExeTarget.Swfoc,
            new Dictionary<string, string> { ["isStarWarsG"] = "true" });
        var context = resolver.Resolve(process, profiles);
        context.Recommendation.ReasonCode.Should().Be("modpath_hint_profile");
    }

    // ─── NormalizeToken ───

    [Fact]
    public void NormalizeToken_NullInput_ReturnsNull()
    {
        InvokeNormalizeToken(null).Should().BeNull();
    }

    [Fact]
    public void NormalizeToken_WhitespaceInput_ReturnsNull()
    {
        InvokeNormalizeToken("   ").Should().BeNull();
    }

    [Fact]
    public void NormalizeToken_BackslashesNormalized()
    {
        var result = InvokeNormalizeToken(@"C:\mods\test");
        result.Should().Contain("/");
        result.Should().NotContain("\\");
    }

    [Fact]
    public void NormalizeToken_DoubleSlashesReduced()
    {
        var result = InvokeNormalizeToken("C://mods//test");
        result.Should().NotContain("//");
    }

    [Fact]
    public void NormalizeToken_QuotesRemoved()
    {
        var result = InvokeNormalizeToken("\"quoted\"");
        result.Should().NotContain("\"");
    }

    // ─── BuildRequiredWorkshopIds ───

    [Fact]
    public void BuildRequiredWorkshopIds_NoMetadata_ReturnsSteamWorkshopIdOnly()
    {
        var profile = CreateProfile("test", "123");
        var result = InvokeBuildRequiredWorkshopIds(profile);
        result.Should().Contain("123");
    }

    [Fact]
    public void BuildRequiredWorkshopIds_WithRequiredWorkshopIds_IncludesAll()
    {
        var profile = CreateProfile("test", "123", metadata: new Dictionary<string, string>
        {
            ["requiredWorkshopIds"] = "456,789"
        });
        var result = InvokeBuildRequiredWorkshopIds(profile);
        result.Should().Contain("123");
        result.Should().Contain("456");
        result.Should().Contain("789");
    }

    [Fact]
    public void BuildRequiredWorkshopIds_WithRequiredWorkshopId_IncludesAll()
    {
        var profile = CreateProfile("test", "123", metadata: new Dictionary<string, string>
        {
            ["requiredWorkshopId"] = "456"
        });
        var result = InvokeBuildRequiredWorkshopIds(profile);
        result.Should().Contain("456");
    }

    [Fact]
    public void BuildRequiredWorkshopIds_NullMetadata_ReturnsSteamIdOnly()
    {
        var profile = new TrainerProfile("test", "test", null, ExeTarget.Swfoc, "123",
            Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(), new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(), null, Array.Empty<HelperHookSpec>(), null);
        var result = InvokeBuildRequiredWorkshopIds(profile);
        result.Should().Contain("123");
    }

    [Fact]
    public void BuildRequiredWorkshopIds_EmptySteamWorkshopId_ReturnsEmpty()
    {
        var profile = new TrainerProfile("test", "test", null, ExeTarget.Swfoc, "",
            Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(), new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(), null, Array.Empty<HelperHookSpec>(), null);
        var result = InvokeBuildRequiredWorkshopIds(profile);
        result.Should().BeEmpty();
    }

    // ─── ScoreWorkshopMatch partial and full overlap ───

    [Fact]
    public void ScoreWorkshopMatch_PartialOverlap_LowerScore()
    {
        var profile = CreateProfile("test", "100", metadata: new Dictionary<string, string>
        {
            ["requiredWorkshopIds"] = "200,300"
        });
        var steamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "200" };
        var score = InvokeScoreWorkshopMatch(profile, steamIds);
        // partial overlap: 700 + 1 = 701
        score.Should().BeGreaterThan(700);
        score.Should().BeLessThan(900);
    }

    [Fact]
    public void ScoreWorkshopMatch_FullOverlap_HigherScore()
    {
        var profile = CreateProfile("test", "100", metadata: new Dictionary<string, string>
        {
            ["requiredWorkshopIds"] = "200,300"
        });
        var steamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "100", "200", "300" };
        var score = InvokeScoreWorkshopMatch(profile, steamIds);
        score.Should().Be(1000); // SteamWorkshopId exact match = 1000
    }

    [Fact]
    public void ScoreWorkshopMatch_NoMatch_ZeroScore()
    {
        var profile = CreateProfile("test", "100");
        var steamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "999" };
        var score = InvokeScoreWorkshopMatch(profile, steamIds);
        score.Should().Be(0);
    }

    // ─── IsPreferredProfile tiebreaker ───

    [Fact]
    public void IsPreferredProfile_RoePrefersOverAotr()
    {
        var roe = CreateProfile("roe_123", "123");
        var aotr = CreateProfile("aotr_456", "456");
        var result = InvokeIsPreferredProfile(roe, aotr);
        result.Should().BeTrue();
    }

    [Fact]
    public void IsPreferredProfile_AotrPrefersOverGeneric()
    {
        var aotr = CreateProfile("aotr_456", "456");
        var generic = CreateProfile("custom_789", "789");
        var result = InvokeIsPreferredProfile(aotr, generic);
        result.Should().BeTrue();
    }

    [Fact]
    public void IsPreferredProfile_SamePriority_AlphabeticalWins()
    {
        var a = CreateProfile("a_profile", "111");
        var b = CreateProfile("b_profile", "222");
        var result = InvokeIsPreferredProfile(a, b);
        result.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  NamedPipeExtenderBackend — additional branches
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void NamedPipeExtenderBackend_Constructor_NullPipeName_UsesDefault()
    {
        var backend = new NamedPipeExtenderBackend(null);
        backend.BackendKind.Should().Be(ExecutionBackendKind.Extender);
    }

    [Fact]
    public async Task NamedPipeExtenderBackend_GetHealthAsync_NoParams_ReturnsUnhealthy()
    {
        var backend = new NamedPipeExtenderBackend("nonexistent_pipe_wave6", false);
        var health = await backend.GetHealthAsync();
        health.IsHealthy.Should().BeFalse();
        health.BackendId.Should().Be("extender");
    }

    [Fact]
    public async Task NamedPipeExtenderBackend_GetHealthAsync_WithCancellationToken_ReturnsUnhealthy()
    {
        var backend = new NamedPipeExtenderBackend("nonexistent_pipe_wave6_ct", false);
        var health = await backend.GetHealthAsync(CancellationToken.None);
        health.IsHealthy.Should().BeFalse();
    }

    [Fact]
    public async Task NamedPipeExtenderBackend_ExecuteAsync_TwoParam_NullCommand_Throws()
    {
        var backend = new NamedPipeExtenderBackend("test_pipe_w6", false);
        var cap = CapabilityReport.Unknown("test", RuntimeReasonCode.CAPABILITY_UNKNOWN);
        var act = () => backend.ExecuteAsync(null!, cap);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task NamedPipeExtenderBackend_ExecuteAsync_TwoParam_NullCapabilityReport_Throws()
    {
        var backend = new NamedPipeExtenderBackend("test_pipe_w6", false);
        var action = new ActionSpec("test", ActionCategory.Global, RuntimeMode.Unknown, ExecutionKind.Memory, new JsonObject(), false, 0);
        var req = new ActionExecutionRequest(action, new JsonObject(), "profile", RuntimeMode.Unknown, null);
        var act = () => backend.ExecuteAsync(req, null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ─── ShouldSeedProbeDefaults ───

    [Theory]
    [InlineData("base_swfoc", true)]
    [InlineData("base_sweaw", true)]
    [InlineData("aotr_test", true)]
    [InlineData("roe_test", true)]
    [InlineData("custom_profile", false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void ShouldSeedProbeDefaults_Variations(string profileId, bool expected)
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "ShouldSeedProbeDefaults", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var result = (bool)method!.Invoke(null, new object[] { profileId })!;
        result.Should().Be(expected);
    }

    // ─── IsAllowedBridgeHostPath ───

    [Theory]
    [InlineData(@"C:\path\SwfocExtender.Host.exe", true)]
    [InlineData(@"C:\path\..\SwfocExtender.Host.exe", false)]
    [InlineData(@"C:\path\other.exe", false)]
    public void IsAllowedBridgeHostPath_Variations(string path, bool expected)
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "IsAllowedBridgeHostPath", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var result = (bool)method!.Invoke(null, new object[] { path })!;
        result.Should().Be(expected);
    }

    // ─── ParseResponse ───

    [Fact]
    public void ParseResponse_NullLine_ReturnsFailure()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "ParseResponse", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var result = method!.Invoke(null, new object?[] { "cmd1", null });
        result.Should().NotBeNull();
    }

    [Fact]
    public void ParseResponse_EmptyLine_ReturnsFailure()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "ParseResponse", BindingFlags.NonPublic | BindingFlags.Static);
        var result = method!.Invoke(null, new object?[] { "cmd1", "" });
        result.Should().NotBeNull();
    }

    [Fact]
    public void ParseResponse_WhitespaceLine_ReturnsFailure()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "ParseResponse", BindingFlags.NonPublic | BindingFlags.Static);
        var result = method!.Invoke(null, new object?[] { "cmd1", "   " });
        result.Should().NotBeNull();
    }

    [Fact]
    public void ParseResponse_ValidJson_ReturnsResult()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "ParseResponse", BindingFlags.NonPublic | BindingFlags.Static);
        var json = JsonSerializer.Serialize(new ExtenderResult(
            "cmd1", true, RuntimeReasonCode.CAPABILITY_PROBE_PASS, "extender", "active", "ok"),
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        var result = method!.Invoke(null, new object?[] { "cmd1", json });
        result.Should().NotBeNull();
    }

    [Fact]
    public void ParseResponse_InvalidJson_ReturnsInvalidResult()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "ParseResponse", BindingFlags.NonPublic | BindingFlags.Static);
        // "null" deserializes to null
        var result = method!.Invoke(null, new object?[] { "cmd1", "null" });
        result.Should().NotBeNull();
    }

    // ─── TryAddRoot ───

    [Fact]
    public void TryAddRoot_NullPath_NoOp()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "TryAddRoot", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { roots, null! });
        roots.Should().BeEmpty();
    }

    [Fact]
    public void TryAddRoot_EmptyPath_NoOp()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "TryAddRoot", BindingFlags.NonPublic | BindingFlags.Static);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { roots, "" });
        roots.Should().BeEmpty();
    }

    [Fact]
    public void TryAddRoot_ValidPath_Adds()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "TryAddRoot", BindingFlags.NonPublic | BindingFlags.Static);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { roots, _tempRoot });
        roots.Should().NotBeEmpty();
    }

    // ─── TryAddAncestorRoots ───

    [Fact]
    public void TryAddAncestorRoots_NullPath_NoOp()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "TryAddAncestorRoots", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { roots, null!, 3 });
        roots.Should().BeEmpty();
    }

    [Fact]
    public void TryAddAncestorRoots_ValidPath_AddsAncestors()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "TryAddAncestorRoots", BindingFlags.NonPublic | BindingFlags.Static);
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { roots, _tempRoot, 3 });
        roots.Should().NotBeEmpty();
    }

    // ─── AddDiscoveredNativeBuildCandidates ───

    [Fact]
    public void AddDiscoveredNativeBuildCandidates_NoNativeDir_NoOp()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "AddDiscoveredNativeBuildCandidates", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { candidates, _tempRoot });
        // No "native" dir exists, so nothing found
        candidates.Should().BeEmpty();
    }

    [Fact]
    public void AddDiscoveredNativeBuildCandidates_NativeDirExists_FindsFiles()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "AddDiscoveredNativeBuildCandidates", BindingFlags.NonPublic | BindingFlags.Static);
        var nativeDir = Path.Join(_tempRoot, "native");
        Directory.CreateDirectory(nativeDir);
        File.WriteAllText(Path.Join(nativeDir, "SwfocExtender.Host.exe"), "fake");

        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        method!.Invoke(null, new object[] { candidates, _tempRoot });
        candidates.Should().NotBeEmpty();
    }

    // ─── MergeProbeAnchorsFromMetadata ───

    [Fact]
    public void BuildProbeAnchors_NoMetadata_SeedsDefaultsForBaseSwfoc()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "BuildProbeAnchors", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var process = new ProcessMetadata(100, "StarWarsG", "", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown, null);
        var result = (JsonObject)method!.Invoke(null, new object[] { "base_swfoc", process })!;
        result.Should().ContainKey("credits");
    }

    [Fact]
    public void BuildProbeAnchors_CustomProfile_NoDefaults()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "BuildProbeAnchors", BindingFlags.NonPublic | BindingFlags.Static);

        var process = new ProcessMetadata(100, "StarWarsG", "", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown, null);
        var result = (JsonObject)method!.Invoke(null, new object[] { "custom_profile", process })!;
        result.Should().NotContainKey("credits");
    }

    [Fact]
    public void BuildProbeAnchors_ZeroProcessId_ReturnsEmpty()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "BuildProbeAnchors", BindingFlags.NonPublic | BindingFlags.Static);

        var process = new ProcessMetadata(0, "test", "", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown, null);
        var result = (JsonObject)method!.Invoke(null, new object[] { "base_swfoc", process })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildProbeAnchors_WithProbeAnchorsMetadata_MergesAnchors()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "BuildProbeAnchors", BindingFlags.NonPublic | BindingFlags.Static);

        var metadata = new Dictionary<string, string>
        {
            ["probeResolvedAnchorsJson"] = """{"custom_anchor": "0x1234"}"""
        };
        var process = new ProcessMetadata(100, "StarWarsG", "", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown, metadata);
        var result = (JsonObject)method!.Invoke(null, new object[] { "base_swfoc", process })!;
        result.Should().ContainKey("custom_anchor");
    }

    [Fact]
    public void BuildProbeAnchors_InvalidProbeAnchorsJson_SeedsDefaultsOnly()
    {
        var method = typeof(NamedPipeExtenderBackend).GetMethod(
            "BuildProbeAnchors", BindingFlags.NonPublic | BindingFlags.Static);

        var metadata = new Dictionary<string, string>
        {
            ["probeResolvedAnchorsJson"] = "NOT VALID JSON"
        };
        var process = new ProcessMetadata(100, "StarWarsG", "", null, ExeTarget.Swfoc,
            RuntimeMode.Unknown, metadata);
        var result = (JsonObject)method!.Invoke(null, new object[] { "base_swfoc", process })!;
        // Defaults still seeded despite invalid JSON
        result.Should().ContainKey("credits");
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  ProcessMemoryScanner — additional edge case branches
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void EnsureBufferSize_LargerRequired_ReturnsNew()
    {
        var method = typeof(ProcessMemoryScanner).GetMethod(
            "EnsureBufferSize", BindingFlags.NonPublic | BindingFlags.Static);
        var buffer = new byte[50];
        var result = (byte[])method!.Invoke(null, new object[] { buffer, 100 })!;
        result.Should().NotBeSameAs(buffer);
        result.Length.Should().Be(100);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════════

    private static bool InvokeTryParseAddress(object? value, out long address)
    {
        var method = typeof(SignatureResolverSymbolHydration).GetMethod(
            "TryParseAddress", BindingFlags.NonPublic | BindingFlags.Static);
        var args = new object?[] { value, 0L };
        var result = (bool)method!.Invoke(null, args)!;
        address = (long)args[1]!;
        return result;
    }

    private static ProcessMemoryAccessor CreateFakeAccessor()
    {
        var accessor = (ProcessMemoryAccessor)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(ProcessMemoryAccessor));
        var handleField = typeof(ProcessMemoryAccessor).GetField(
            "_handle", BindingFlags.NonPublic | BindingFlags.Instance);
        handleField!.SetValue(accessor, (nint)0xDEAD);
        return accessor;
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
        var indexPath = Path.Join(root, "artifact-index.json");
        var json = $$"""
                     {
                       "schemaVersion": "1.0",
                       "binaryFingerprint": {
                         "fingerprintId": "{{fingerprintId}}"
                       },
                       "artifactPointers": {
                         "symbolPackPath": "{{symbolPackPath.Replace("\\", "/")}}"
                       }
                     }
                     """;
        File.WriteAllText(indexPath, json);
    }

    private static ProcessMetadata CreateProcessMetadata(
        string name, string path, string? commandLine, ExeTarget target,
        Dictionary<string, string>? metadata = null)
    {
        return new ProcessMetadata(9999, name, path, commandLine, target, RuntimeMode.Unknown,
            metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static TrainerProfile CreateProfile(
        string id, string workshopId,
        string? localPathHints = null, string? profileAliases = null,
        Dictionary<string, string>? metadata = null)
    {
        var meta = metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (localPathHints is not null)
            meta["localPathHints"] = localPathHints;
        if (profileAliases is not null)
            meta["profileAliases"] = profileAliases;

        return new TrainerProfile(id, id, null, ExeTarget.Swfoc, workshopId,
            Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(), new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(), null, Array.Empty<HelperHookSpec>(), meta);
    }

    // ─── Reflection helpers for ProcessLocator private methods ───

    private static object? InvokeGetProcessDetection(string processName, string? processPath, string? commandLine)
    {
        var method = typeof(ProcessLocator).GetMethod(
            "GetProcessDetection", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return method!.Invoke(null, new object?[] { processName, processPath, commandLine });
    }

    private static ExeTarget GetDetectionExeTarget(object detection)
    {
        var prop = detection.GetType().GetProperty("ExeTarget");
        prop.Should().NotBeNull();
        return (ExeTarget)prop!.GetValue(detection)!;
    }

    private static RuntimeMode InvokeInferMode(string? commandLine)
    {
        var method = typeof(ProcessLocator).GetMethod(
            "InferMode", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (RuntimeMode)method!.Invoke(null, new object?[] { commandLine })!;
    }

    private static bool InvokeIsProcessName(string? processName, string expected)
    {
        var method = typeof(ProcessLocator).GetMethod(
            "IsProcessName", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (bool)method!.Invoke(null, new object?[] { processName, expected })!;
    }

    private static bool InvokeContainsToken(string? value, string token)
    {
        var method = typeof(ProcessLocator).GetMethod(
            "ContainsToken", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (bool)method!.Invoke(null, new object?[] { value, token })!;
    }

    private static string[] InvokeExtractSteamModIds(string? commandLine)
    {
        var method = typeof(ProcessLocator).GetMethod(
            "ExtractSteamModIds", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (string[])method!.Invoke(null, new object?[] { commandLine })!;
    }

    private static string? InvokeExtractModPath(string? commandLine)
    {
        var method = typeof(ProcessLocator).GetMethod(
            "ExtractModPath", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (string?)method!.Invoke(null, new object?[] { commandLine });
    }

    private static ProcessHostRole InvokeDetermineHostRole(bool isStarWarsG, ExeTarget exeTarget)
    {
        var method = typeof(ProcessLocator).GetMethod(
            "DetermineHostRole", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var detectionType = typeof(ProcessLocator).GetNestedType(
            "ProcessDetection", BindingFlags.NonPublic);
        detectionType.Should().NotBeNull();
        var detection = Activator.CreateInstance(detectionType!, new object[] { exeTarget, isStarWarsG, "test" });
        return (ProcessHostRole)method!.Invoke(null, new[] { detection })!;
    }

    private static IReadOnlyList<string> InvokeNormalizeWorkshopIds(IReadOnlyList<string>? workshopIds)
    {
        var method = typeof(ProcessLocator).GetMethod(
            "NormalizeWorkshopIds", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (IReadOnlyList<string>)method!.Invoke(null, new object?[] { workshopIds })!;
    }

    private static string? InvokeNormalizeForcedProfileId(string? profileId)
    {
        var method = typeof(ProcessLocator).GetMethod(
            "NormalizeForcedProfileId", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (string?)method!.Invoke(null, new object?[] { profileId });
    }

    private static object? InvokeResolveForcedContext(string? commandLine, string? modPathRaw,
        IReadOnlyList<string> detectedSteamModIds, ProcessLocatorOptions options)
    {
        var method = typeof(ProcessLocator).GetMethod(
            "ResolveForcedContext", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return method!.Invoke(null, new object?[] { commandLine, modPathRaw, detectedSteamModIds, options });
    }

    private static string GetForcedContextSource(object forcedContext)
    {
        var prop = forcedContext.GetType().GetProperty("Source");
        prop.Should().NotBeNull();
        return (string)prop!.GetValue(forcedContext)!;
    }

    // ─── Reflection helpers for LaunchContextResolver private methods ───

    private static string? InvokeNormalizeToken(string? input)
    {
        var method = typeof(LaunchContextResolver).GetMethod(
            "NormalizeToken", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (string?)method!.Invoke(null, new object?[] { input });
    }

    private static IReadOnlyList<string> InvokeBuildRequiredWorkshopIds(TrainerProfile profile)
    {
        var method = typeof(LaunchContextResolver).GetMethod(
            "BuildRequiredWorkshopIds", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (IReadOnlyList<string>)method!.Invoke(null, new object[] { profile })!;
    }

    private static int InvokeScoreWorkshopMatch(TrainerProfile profile, IReadOnlySet<string> steamModIds)
    {
        var method = typeof(LaunchContextResolver).GetMethod(
            "ScoreWorkshopMatch", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (int)method!.Invoke(null, new object[] { profile, steamModIds })!;
    }

    private static bool InvokeIsPreferredProfile(TrainerProfile candidate, TrainerProfile current)
    {
        var method = typeof(LaunchContextResolver).GetMethod(
            "IsPreferredProfile", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        return (bool)method!.Invoke(null, new object[] { candidate, current })!;
    }
}
