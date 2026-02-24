using System.Text.Json;
using System.Text.Json.Serialization;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class SymbolHealthService : ISymbolHealthService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public SymbolValidationResult Evaluate(SymbolInfo symbol, TrainerProfile profile, RuntimeMode mode)
    {
        if (symbol.Address == nint.Zero)
        {
            return new SymbolValidationResult(
                SymbolHealthStatus.Unresolved,
                "symbol_address_unresolved",
                0.0d);
        }

        var isCritical = IsCriticalSymbol(profile, symbol.Name);
        var status = symbol.Source == AddressSource.Signature
            ? SymbolHealthStatus.Healthy
            : SymbolHealthStatus.Degraded;
        var reason = symbol.Source == AddressSource.Signature
            ? "signature_resolved"
            : "fallback_offset";
        var confidence = symbol.Source == AddressSource.Signature ? 0.95d : 0.65d;

        var matchingRule = GetMatchingRule(profile, symbol.Name, mode);
        if (matchingRule is not null &&
            matchingRule.Mode is not null &&
            mode != RuntimeMode.Unknown &&
            matchingRule.Mode.Value != mode)
        {
            status = SymbolHealthStatus.Degraded;
            reason = $"{reason}+mode_mismatch";
            confidence = Math.Min(confidence, 0.60d);
        }

        if (isCritical && status == SymbolHealthStatus.Degraded)
        {
            reason = $"{reason}+critical";
            confidence = Math.Min(confidence, 0.55d);
        }

        return new SymbolValidationResult(status, reason, confidence, isCritical);
    }

    private static SymbolValidationRule? GetMatchingRule(TrainerProfile profile, string symbolName, RuntimeMode mode)
    {
        var rules = ParseSymbolValidationRules(profile)
            .Where(x => x.Symbol.Equals(symbolName, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (rules.Length == 0)
        {
            return null;
        }

        if (mode != RuntimeMode.Unknown)
        {
            var exact = rules.FirstOrDefault(x => x.Mode == mode);
            if (exact is not null)
            {
                return exact;
            }

            var anyMode = rules.FirstOrDefault(x => x.Mode is null);
            if (anyMode is not null)
            {
                return anyMode;
            }

            // Return a non-matching mode rule so Evaluate can emit explicit mode_mismatch diagnostics.
            return rules[0];
        }

        return rules.FirstOrDefault(x => x.Mode is null) ?? rules[0];
    }

    private static IReadOnlyList<SymbolValidationRule> ParseSymbolValidationRules(TrainerProfile profile)
    {
        if (profile.Metadata is null ||
            !profile.Metadata.TryGetValue("symbolValidationRules", out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return Array.Empty<SymbolValidationRule>();
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<List<SymbolValidationRule>>(raw, JsonOptions);
            return parsed is not null
                ? parsed
                : Array.Empty<SymbolValidationRule>();
        }
        catch
        {
            return Array.Empty<SymbolValidationRule>();
        }
    }

    private static bool IsCriticalSymbol(TrainerProfile profile, string symbolName)
    {
        if (profile.Metadata is null ||
            !profile.Metadata.TryGetValue("criticalSymbols", out var raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var symbols = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return symbols.Any(s => s.Equals(symbolName, StringComparison.OrdinalIgnoreCase));
    }
}
