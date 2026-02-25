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
        var state = BuildInitialValidationState(symbol.Source);
        state = ApplyModeRuleState(state, GetMatchingRule(profile, symbol.Name, mode), mode);
        state = ApplyCriticalState(state, isCritical);
        return new SymbolValidationResult(state.Status, state.Reason, state.Confidence, isCritical);
    }

    private static SymbolValidationState BuildInitialValidationState(AddressSource source)
    {
        return source == AddressSource.Signature
            ? new SymbolValidationState(SymbolHealthStatus.Healthy, "signature_resolved", 0.95d)
            : new SymbolValidationState(SymbolHealthStatus.Degraded, "fallback_offset", 0.65d);
    }

    private static SymbolValidationState ApplyModeRuleState(
        SymbolValidationState state,
        SymbolValidationRule? matchingRule,
        RuntimeMode mode)
    {
        if (!IsModeMismatch(matchingRule, mode))
        {
            return state;
        }

        return state with
        {
            Status = SymbolHealthStatus.Degraded,
            Reason = $"{state.Reason}+mode_mismatch",
            Confidence = Math.Min(state.Confidence, 0.60d)
        };
    }

    private static bool IsModeMismatch(SymbolValidationRule? matchingRule, RuntimeMode mode)
    {
        return matchingRule?.Mode is not null &&
               mode != RuntimeMode.Unknown &&
               matchingRule.Mode.Value != mode;
    }

    private static SymbolValidationState ApplyCriticalState(SymbolValidationState state, bool isCritical)
    {
        if (!isCritical || state.Status != SymbolHealthStatus.Degraded)
        {
            return state;
        }

        return state with
        {
            Reason = $"{state.Reason}+critical",
            Confidence = Math.Min(state.Confidence, 0.55d)
        };
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

    private sealed record SymbolValidationState(SymbolHealthStatus Status, string Reason, double Confidence);
}
