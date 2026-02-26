using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Interop;

namespace SwfocTrainer.Runtime.Services;

internal static class SignatureResolverFallbacks
{
    private readonly record struct FallbackAttempt(
        ILogger<SignatureResolver> Logger,
        ProcessMemoryAccessor Accessor,
        nint BaseAddress,
        string SymbolName,
        SymbolValueType ValueType,
        long Offset,
        IDictionary<string, SymbolInfo> Symbols,
        string DiagnosticsText);

    internal readonly record struct SignatureHitContext(
        IReadOnlyDictionary<string, long> FallbackOffsets,
        ProcessMemoryAccessor Accessor,
        nint BaseAddress,
        byte[] ModuleBytes,
        IDictionary<string, SymbolInfo> Symbols);

    /// <summary>
    /// Validates a fallback offset by attempting a test read at <c>baseAddress + offset</c>.
    /// Profile fallback offsets are author-curated, so rather than guessing module bounds we
    /// simply ask the OS whether the address is readable. If it is, the symbol is registered.
    /// </summary>
    private static bool TryApplyFallback(in FallbackAttempt attempt)
    {
        if (attempt.Offset <= 0)
        {
            return false;
        }

        var address = attempt.BaseAddress + (nint)attempt.Offset;
        try
        {
            attempt.Accessor.Read<int>(address); // test read — throws if not readable
            attempt.Symbols[attempt.SymbolName] = new SymbolInfo(
                attempt.SymbolName,
                address,
                attempt.ValueType,
                AddressSource.Fallback,
                attempt.DiagnosticsText,
                Confidence: 0.65d,
                HealthStatus: SymbolHealthStatus.Degraded,
                HealthReason: "fallback_offset",
                LastValidatedAt: DateTimeOffset.UtcNow);
            return true;
        }
        catch (Exception ex)
        {
            attempt.Logger.LogDebug(
                ex,
                "Fallback test-read failed for {Symbol} at 0x{Address:X} (offset 0x{Offset:X})",
                attempt.SymbolName,
                address.ToInt64(),
                attempt.Offset);
            return false;
        }
    }

    internal static void HandleSignatureHit(
        ILogger<SignatureResolver> logger,
        SignatureSet signatureSet,
        SignatureSpec signature,
        nint hit,
        in SignatureHitContext context)
    {
        if (SignatureResolverAddressing.TryResolveAddress(
                signature,
                hit,
                context.BaseAddress,
                context.ModuleBytes,
                out var address,
                out var diagnostics))
        {
            context.Symbols[signature.Name] = CreateSignatureSymbol(signatureSet, signature, address);
            return;
        }

        if (!context.FallbackOffsets.TryGetValue(signature.Name, out var fallback))
        {
            logger.LogWarning(
                "Signature address resolution failed for {Symbol} and no fallback offset available. Details: {Diagnostics}",
                signature.Name,
                diagnostics ?? "<none>");
            return;
        }

        if (TryApplyFallback(new FallbackAttempt(
                logger,
                context.Accessor,
                context.BaseAddress,
                signature.Name,
                signature.ValueType,
                fallback,
                context.Symbols,
                diagnostics ?? "Fallback after address resolution failure")))
        {
            logger.LogWarning(
                "Signature address resolution failed for {Symbol}; fallback offset applied (0x{Offset:X}). Details: {Diagnostics}",
                signature.Name,
                fallback,
                diagnostics ?? "<none>");
            return;
        }

        logger.LogWarning(
            "Signature address resolution failed for {Symbol} and fallback offset 0x{Offset:X} is not readable. Details: {Diagnostics}",
            signature.Name,
            fallback,
            diagnostics ?? "<none>");
    }

    internal static void HandleSignatureMiss(
        ILogger<SignatureResolver> logger,
        SignatureSpec signature,
        IReadOnlyDictionary<string, long> fallbackOffsets,
        ProcessMemoryAccessor accessor,
        nint baseAddress,
        IDictionary<string, SymbolInfo> symbols)
    {
        if (fallbackOffsets.TryGetValue(signature.Name, out var fallback) && !symbols.ContainsKey(signature.Name))
        {
            if (TryApplyFallback(new FallbackAttempt(
                    logger,
                    accessor,
                    baseAddress,
                    signature.Name,
                    signature.ValueType,
                    fallback,
                    symbols,
                    "Fallback offset")))
            {
                logger.LogWarning("Signature miss for {Symbol}; fallback offset applied (0x{Offset:X})", signature.Name, fallback);
                return;
            }

            logger.LogWarning(
                "Signature miss for {Symbol}; fallback offset 0x{Offset:X} is not readable",
                signature.Name,
                fallback);
            return;
        }

        logger.LogWarning("Signature miss for {Symbol} and no fallback offset available", signature.Name);
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

    internal static void ApplyStandaloneFallbacks(
        ILogger<SignatureResolver> logger,
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

            if (TryApplyFallback(new FallbackAttempt(
                    logger,
                    accessor,
                    baseAddress,
                    fallback.Key,
                    SymbolValueType.Int32,
                    fallback.Value,
                    symbols,
                    "Standalone fallback")))
            {
                continue;
            }

            logger.LogWarning(
                "Standalone fallback for {Symbol} skipped: offset 0x{Offset:X} is not readable",
                fallback.Key,
                fallback.Value);
        }
    }
}
