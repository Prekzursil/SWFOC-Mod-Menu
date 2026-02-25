using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace SwfocTrainer.Runtime.Services;

public sealed partial class SignatureResolver
{
    private bool TryResolveGhidraPackPath(ProcessModule module, out string fingerprintId, out string packPath)
    {
        fingerprintId = string.Empty;
        packPath = string.Empty;
        if (string.IsNullOrWhiteSpace(_ghidraSymbolPackRoot))
        {
            return false;
        }

        if (!TryBuildFingerprintId(module, out fingerprintId, out _))
        {
            return false;
        }

        packPath = SelectBestGhidraPackPath(_ghidraSymbolPackRoot, fingerprintId) ?? string.Empty;
        return !string.IsNullOrWhiteSpace(packPath);
    }

    internal static string? SelectBestGhidraPackPath(string symbolPackRoot, string fingerprintId)
    {
        if (string.IsNullOrWhiteSpace(symbolPackRoot) ||
            string.IsNullOrWhiteSpace(fingerprintId) ||
            !Directory.Exists(symbolPackRoot))
        {
            return null;
        }

        var candidates = new List<PackSelectionCandidate>();
        var exactPath = Path.Combine(symbolPackRoot, $"{fingerprintId}.json");
        TryAddPackCandidate(candidates, exactPath, precedence: 0, fingerprintId);

        var indexedPath = ResolvePackPathFromArtifactIndex(symbolPackRoot, fingerprintId);
        if (!string.IsNullOrWhiteSpace(indexedPath))
        {
            TryAddPackCandidate(candidates, indexedPath!, precedence: 1, fingerprintId);
        }

        foreach (var path in EnumeratePackCandidates(symbolPackRoot))
        {
            if (string.Equals(path, exactPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(indexedPath) &&
                string.Equals(path, indexedPath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            TryAddPackCandidate(candidates, path, precedence: 2, fingerprintId);
        }

        return candidates
            .OrderBy(candidate => candidate.Precedence)
            .ThenByDescending(candidate => candidate.GeneratedAtUtc)
            .ThenBy(candidate => candidate.NormalizedPath, StringComparer.Ordinal)
            .Select(candidate => candidate.PackPath)
            .FirstOrDefault();
    }

    private static IEnumerable<string> EnumeratePackCandidates(string symbolPackRoot)
    {
        try
        {
            return Directory.EnumerateFiles(symbolPackRoot, "*.json", SearchOption.AllDirectories)
                .Where(path => !Path.GetFileName(path).Equals(ArtifactIndexFileName, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static void TryAddPackCandidate(
        ICollection<PackSelectionCandidate> candidates,
        string path,
        int precedence,
        string expectedFingerprintId)
    {
        if (!File.Exists(path))
        {
            return;
        }

        if (!TryReadPackMetadata(path, out var fingerprintId, out var generatedAtUtc))
        {
            return;
        }

        if (!string.Equals(fingerprintId, expectedFingerprintId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var normalizedPath = path.Replace('\\', '/').ToLowerInvariant();
        candidates.Add(new PackSelectionCandidate(path, precedence, generatedAtUtc, normalizedPath));
    }

    private static bool TryReadPackMetadata(string packPath, out string fingerprintId, out DateTimeOffset generatedAtUtc)
    {
        fingerprintId = string.Empty;
        generatedAtUtc = DateTimeOffset.MinValue;
        try
        {
            var pack = JsonSerializer.Deserialize<GhidraSymbolPackDto>(File.ReadAllText(packPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (pack?.BinaryFingerprint is null ||
                string.IsNullOrWhiteSpace(pack.BinaryFingerprint.FingerprintId))
            {
                return false;
            }

            fingerprintId = pack.BinaryFingerprint.FingerprintId;
            generatedAtUtc = pack.BuildMetadata?.GeneratedAtUtc ?? DateTimeOffset.MinValue;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ResolvePackPathFromArtifactIndex(string symbolPackRoot, string fingerprintId)
    {
        var indexPath = Path.Combine(symbolPackRoot, ArtifactIndexFileName);
        if (!TryReadArtifactIndex(indexPath, out var index))
        {
            return null;
        }

        if (!HasMatchingArtifactFingerprint(index, fingerprintId))
        {
            return null;
        }

        return ResolveIndexedPackPath(symbolPackRoot, index.ArtifactPointers?.SymbolPackPath);
    }

    private static bool TryReadArtifactIndex(string indexPath, out GhidraArtifactIndexDto index)
    {
        index = null!;
        if (!File.Exists(indexPath))
        {
            return false;
        }

        try
        {
            var candidate = JsonSerializer.Deserialize<GhidraArtifactIndexDto>(File.ReadAllText(indexPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (candidate is null)
            {
                return false;
            }

            index = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool HasMatchingArtifactFingerprint(GhidraArtifactIndexDto index, string fingerprintId)
    {
        return index.BinaryFingerprint is not null &&
               IsMatchingFingerprint(index.BinaryFingerprint.FingerprintId, fingerprintId);
    }

    private static string? ResolveIndexedPackPath(string symbolPackRoot, string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        if (Path.IsPathRooted(configuredPath))
        {
            return configuredPath;
        }

        return Path.GetFullPath(Path.Combine(symbolPackRoot, configuredPath));
    }

    private bool TryDeserializeGhidraSymbolPack(string packPath, out GhidraSymbolPackDto pack)
    {
        pack = null!;
        try
        {
            var candidate = JsonSerializer.Deserialize<GhidraSymbolPackDto>(File.ReadAllText(packPath), new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (candidate is null || candidate.BinaryFingerprint is null)
            {
                return false;
            }

            pack = candidate;
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to consume ghidra symbol pack at {Path}", packPath);
            return false;
        }
    }

    private static bool IsMatchingFingerprint(string? actualFingerprintId, string expectedFingerprintId)
    {
        return string.Equals(actualFingerprintId, expectedFingerprintId, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveDefaultGhidraSymbolPackRoot()
    {
        var envOverride = Environment.GetEnvironmentVariable(GhidraSymbolPackRootOverrideEnv);
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            return envOverride;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "profiles", "default", "sdk", "ghidra", "symbol-packs"));
    }

    private sealed record GhidraSymbolPackDto(
        string? SchemaVersion,
        GhidraFingerprintDto? BinaryFingerprint,
        GhidraBuildMetadataDto? BuildMetadata,
        List<GhidraAnchorDto>? Anchors);

    private sealed record GhidraBuildMetadataDto(DateTimeOffset GeneratedAtUtc);

    private sealed record GhidraArtifactIndexDto(
        GhidraFingerprintDto? BinaryFingerprint,
        GhidraArtifactPointersDto? ArtifactPointers);

    private sealed record GhidraArtifactPointersDto(string? SymbolPackPath);

    private sealed record GhidraFingerprintDto(string FingerprintId);

    private sealed record GhidraAnchorDto(string Id, object? Address, double Confidence);

    private sealed record PackSelectionCandidate(
        string PackPath,
        int Precedence,
        DateTimeOffset GeneratedAtUtc,
        string NormalizedPath);
}
