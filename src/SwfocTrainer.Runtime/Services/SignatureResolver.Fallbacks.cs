using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Interop;

namespace SwfocTrainer.Runtime.Services;

public sealed partial class SignatureResolver
{
    /// <summary>
    /// Validates a fallback offset by attempting a test read at <c>baseAddress + offset</c>.
    /// Profile fallback offsets are author-curated, so rather than guessing module bounds we
    /// simply ask the OS whether the address is readable. If it is, the symbol is registered.
    /// </summary>
    private bool TryApplyFallback(
        ProcessMemoryAccessor accessor,
        nint baseAddress,
        string symbolName,
        SymbolValueType valueType,
        long offset,
        IDictionary<string, SymbolInfo> symbols,
        string diagnosticsText)
    {
        if (offset <= 0)
        {
            return false;
        }

        var address = baseAddress + (nint)offset;
        try
        {
            accessor.Read<int>(address); // test read — throws if not readable
            symbols[symbolName] = new SymbolInfo(
                symbolName,
                address,
                valueType,
                AddressSource.Fallback,
                diagnosticsText,
                Confidence: 0.65d,
                HealthStatus: SymbolHealthStatus.Degraded,
                HealthReason: "fallback_offset",
                LastValidatedAt: DateTimeOffset.UtcNow);
            return true;
        }
        catch
        {
            _logger.LogDebug(
                "Fallback test-read failed for {Symbol} at 0x{Address:X} (offset 0x{Offset:X})",
                symbolName,
                address.ToInt64(),
                offset);
            return false;
        }
    }

    private void HandleSignatureHit(
        SignatureSet signatureSet,
        SignatureSpec signature,
        nint hit,
        IReadOnlyDictionary<string, long> fallbackOffsets,
        ProcessMemoryAccessor accessor,
        nint baseAddress,
        byte[] moduleBytes,
        IDictionary<string, SymbolInfo> symbols)
    {
        if (TryResolveAddress(signature, hit, baseAddress, moduleBytes, out var address, out var diagnostics))
        {
            symbols[signature.Name] = CreateSignatureSymbol(signatureSet, signature, address);
            return;
        }

        if (!fallbackOffsets.TryGetValue(signature.Name, out var fallback))
        {
            _logger.LogWarning(
                "Signature address resolution failed for {Symbol} and no fallback offset available. Details: {Diagnostics}",
                signature.Name,
                diagnostics ?? "<none>");
            return;
        }

        if (TryApplyFallback(
                accessor,
                baseAddress,
                signature.Name,
                signature.ValueType,
                fallback,
                symbols,
                diagnostics ?? "Fallback after address resolution failure"))
        {
            _logger.LogWarning(
                "Signature address resolution failed for {Symbol}; fallback offset applied (0x{Offset:X}). Details: {Diagnostics}",
                signature.Name,
                fallback,
                diagnostics ?? "<none>");
            return;
        }

        _logger.LogWarning(
            "Signature address resolution failed for {Symbol} and fallback offset 0x{Offset:X} is not readable. Details: {Diagnostics}",
            signature.Name,
            fallback,
            diagnostics ?? "<none>");
    }

    private void HandleSignatureMiss(
        SignatureSpec signature,
        IReadOnlyDictionary<string, long> fallbackOffsets,
        ProcessMemoryAccessor accessor,
        nint baseAddress,
        IDictionary<string, SymbolInfo> symbols)
    {
        if (fallbackOffsets.TryGetValue(signature.Name, out var fallback) && !symbols.ContainsKey(signature.Name))
        {
            if (TryApplyFallback(accessor, baseAddress, signature.Name, signature.ValueType, fallback, symbols, "Fallback offset"))
            {
                _logger.LogWarning("Signature miss for {Symbol}; fallback offset applied (0x{Offset:X})", signature.Name, fallback);
                return;
            }

            _logger.LogWarning(
                "Signature miss for {Symbol}; fallback offset 0x{Offset:X} is not readable",
                signature.Name,
                fallback);
            return;
        }

        _logger.LogWarning("Signature miss for {Symbol} and no fallback offset available", signature.Name);
    }

    private static SymbolInfo CreateSignatureSymbol(SignatureSet signatureSet, SignatureSpec signature, nint address)
    {
        return new SymbolInfo(
            signature.Name,
            address,
            signature.ValueType,
            AddressSource.Signature,
            $"{signatureSet.Name}:{signature.Pattern} [{signature.AddressMode}]",
            Confidence: 0.95d,
            HealthStatus: SymbolHealthStatus.Healthy,
            HealthReason: "signature_resolved",
            LastValidatedAt: DateTimeOffset.UtcNow);
    }

    private void ApplyStandaloneFallbacks(
        IReadOnlyDictionary<string, long> fallbackOffsets,
        ProcessMemoryAccessor accessor,
        nint baseAddress,
        IDictionary<string, SymbolInfo> symbols)
    {
        foreach (var fallback in fallbackOffsets)
        {
            if (symbols.ContainsKey(fallback.Key))
            {
                continue;
            }

            if (TryApplyFallback(
                    accessor,
                    baseAddress,
                    fallback.Key,
                    SymbolValueType.Int32,
                    fallback.Value,
                    symbols,
                    "Standalone fallback"))
            {
                continue;
            }

            _logger.LogWarning(
                "Standalone fallback for {Symbol} skipped: offset 0x{Offset:X} is not readable",
                fallback.Key,
                fallback.Value);
        }
    }
}
