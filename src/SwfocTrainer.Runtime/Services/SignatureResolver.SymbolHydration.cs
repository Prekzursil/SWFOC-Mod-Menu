using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

internal static class SignatureResolverSymbolHydration
{
    private const string ArtifactIndexFileName = "artifact-index.json";
    private const string GhidraSymbolPackRootOverrideEnv = "SWFOC_GHIDRA_SYMBOL_PACK_ROOT";

    internal static string ResolveDefaultGhidraSymbolPackRoot()
    {
        var envOverride = Environment.GetEnvironmentVariable(GhidraSymbolPackRootOverrideEnv);
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            return envOverride;
        }

        return Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "profiles", "default", "sdk", "ghidra", "symbol-packs"));
    }

    internal static void TryHydrateSymbolsFromGhidraPack(
        string ghidraSymbolPackRoot,
        ILogger<SignatureResolver> logger,
        ProcessModule module,
        IReadOnlyList<SignatureSet> signatureSets,
        IDictionary<string, SymbolInfo> symbols)
    {
        if (!TryLoadGhidraSymbolPack(ghidraSymbolPackRoot, logger, module, out var fingerprintId, out var packPath, out var pack))
        {
            return;
        }

        var valueTypes = BuildSymbolValueTypeIndex(signatureSets);
        foreach (var anchor in pack.Anchors ?? new List<GhidraAnchorDto>())
        {
            if (!TryBuildAnchorSymbol(anchor, valueTypes, fingerprintId, symbols, out var symbol))
            {
                continue;
            }

            symbols[anchor.Id] = symbol;
        }

        logger.LogInformation(
            "Loaded {Count} symbol(s) from ghidra pack {Path} for fingerprint {FingerprintId}",
            symbols.Count,
            packPath,
            fingerprintId);
    }

    private static bool TryLoadGhidraSymbolPack(
        string ghidraSymbolPackRoot,
        ILogger<SignatureResolver> logger,
        ProcessModule module,
        out string fingerprintId,
        out string packPath,
        out GhidraSymbolPackDto pack)
    {
        pack = null!;
        if (!TryResolveGhidraPackPath(ghidraSymbolPackRoot, module, out fingerprintId, out packPath))
        {
            return false;
        }

        if (!TryDeserializeGhidraSymbolPack(logger, packPath, out var candidate))
        {
            return false;
        }

        if (!IsMatchingFingerprint(candidate.BinaryFingerprint?.FingerprintId, fingerprintId))
        {
            logger.LogWarning(
                "Ignoring ghidra symbol pack {Path}: fingerprint mismatch (expected {Expected}, actual {Actual})",
                packPath,
                fingerprintId,
                candidate.BinaryFingerprint?.FingerprintId);
            return false;
        }

        pack = candidate;
        return true;
    }

    private static bool TryResolveGhidraPackPath(
        string ghidraSymbolPackRoot,
        ProcessModule module,
        out string fingerprintId,
        out string packPath)
    {
        fingerprintId = string.Empty;
        packPath = string.Empty;
        if (string.IsNullOrWhiteSpace(ghidraSymbolPackRoot))
        {
            return false;
        }

        if (!TryBuildFingerprintId(module, out fingerprintId, out _))
        {
            return false;
        }

        packPath = SelectBestGhidraPackPath(ghidraSymbolPackRoot, fingerprintId) ?? string.Empty;
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

    private static bool TryDeserializeGhidraSymbolPack(
        ILogger<SignatureResolver> logger,
        string packPath,
        out GhidraSymbolPackDto pack)
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
            logger.LogWarning(ex, "Failed to consume ghidra symbol pack at {Path}", packPath);
            return false;
        }
    }

    private static bool TryBuildAnchorSymbol(
        GhidraAnchorDto anchor,
        IReadOnlyDictionary<string, SymbolValueType> valueTypes,
        string fingerprintId,
        IDictionary<string, SymbolInfo> symbols,
        out SymbolInfo symbol)
    {
        symbol = null!;
        if (string.IsNullOrWhiteSpace(anchor.Id) || symbols.ContainsKey(anchor.Id))
        {
            return false;
        }

        if (!TryParseAddress(anchor.Address, out var address))
        {
            return false;
        }

        var valueType = valueTypes.TryGetValue(anchor.Id, out var resolvedType)
            ? resolvedType
            : SymbolValueType.Int32;
        symbol = new SymbolInfo(
            anchor.Id,
            (nint)address,
            valueType,
            AddressSource.Signature,
            $"ghidra_symbol_pack:{fingerprintId}",
            Confidence: anchor.Confidence > 0 ? anchor.Confidence : 0.99d,
            HealthStatus: SymbolHealthStatus.Healthy,
            HealthReason: "ghidra_symbol_pack",
            LastValidatedAt: DateTimeOffset.UtcNow);
        return true;
    }

    private static bool TryBuildFingerprintId(ProcessModule module, out string fingerprintId, out string moduleName)
    {
        fingerprintId = string.Empty;
        moduleName = Path.GetFileName(module.FileName ?? module.ModuleName ?? "module");
        if (string.IsNullOrWhiteSpace(module.FileName) || !File.Exists(module.FileName))
        {
            return false;
        }

        using var stream = File.OpenRead(module.FileName);
        var hash = SHA256.HashData(stream);
        var sha256 = Convert.ToHexString(hash).ToLowerInvariant();
        var normalizedModule = Path.GetFileNameWithoutExtension(moduleName)
            .ToLowerInvariant()
            .Replace(' ', '_');
        var hashPrefix = sha256[..16];
        fingerprintId = $"{normalizedModule}_{hashPrefix}";
        return true;
    }

    private static IReadOnlyDictionary<string, SymbolValueType> BuildSymbolValueTypeIndex(IReadOnlyList<SignatureSet> signatureSets)
    {
        var index = new Dictionary<string, SymbolValueType>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in signatureSets)
        {
            foreach (var signature in set.Signatures)
            {
                if (!index.ContainsKey(signature.Name))
                {
                    index[signature.Name] = signature.ValueType;
                }
            }
        }

        return index;
    }

    private static bool TryParseAddress(object? value, out long address)
    {
        address = 0;
        return TryParseJsonAddress(value, out address) ||
               TryParseNumericAddress(value, out address) ||
               (value is string str && TryParseAddressString(str, out address));
    }

    private static bool TryParseJsonAddress(object? value, out long address)
    {
        address = 0;
        if (value is not JsonElement element)
        {
            return false;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetInt64(out var number))
        {
            address = number;
            return true;
        }

        return element.ValueKind == JsonValueKind.String &&
               TryParseAddressString(element.GetString() ?? string.Empty, out address);
    }

    private static bool TryParseNumericAddress(object? value, out long address)
    {
        switch (value)
        {
            case long int64:
                address = int64;
                return true;
            case int int32:
                address = int32;
                return true;
            default:
                address = 0;
                return false;
        }
    }

    private static bool TryParseAddressString(string str, out long address)
    {
        address = 0;
        if (string.IsNullOrWhiteSpace(str))
        {
            return false;
        }

        if (str.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(str.AsSpan(2), System.Globalization.NumberStyles.HexNumber, null, out var hex))
        {
            address = hex;
            return true;
        }

        if (long.TryParse(str, out var parsed))
        {
            address = parsed;
            return true;
        }

        return false;
    }

    private static bool IsMatchingFingerprint(string? actualFingerprintId, string expectedFingerprintId)
    {
        return string.Equals(actualFingerprintId, expectedFingerprintId, StringComparison.OrdinalIgnoreCase);
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
