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
