using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed partial class SignatureResolver
{
    private void TryHydrateSymbolsFromGhidraPack(
        ProcessModule module,
        IReadOnlyList<SignatureSet> signatureSets,
        IDictionary<string, SymbolInfo> symbols)
    {
        if (!TryLoadGhidraSymbolPack(module, out var fingerprintId, out var packPath, out var pack))
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

        _logger.LogInformation(
            "Loaded {Count} symbol(s) from ghidra pack {Path} for fingerprint {FingerprintId}",
            symbols.Count, packPath, fingerprintId);
    }

    private bool TryLoadGhidraSymbolPack(
        ProcessModule module,
        out string fingerprintId,
        out string packPath,
        out GhidraSymbolPackDto pack)
    {
        pack = null!;
        if (!TryResolveGhidraPackPath(module, out fingerprintId, out packPath))
        {
            return false;
        }

        if (!TryDeserializeGhidraSymbolPack(packPath, out var candidate))
        {
            return false;
        }

        if (!IsMatchingFingerprint(candidate.BinaryFingerprint?.FingerprintId, fingerprintId))
        {
            _logger.LogWarning(
                "Ignoring ghidra symbol pack {Path}: fingerprint mismatch (expected {Expected}, actual {Actual})",
                packPath, fingerprintId, candidate.BinaryFingerprint?.FingerprintId);
            return false;
        }

        pack = candidate;
        return true;
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
}
