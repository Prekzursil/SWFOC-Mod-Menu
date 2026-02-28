using System.Text.Json.Nodes;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class ActionReliabilityService : IActionReliabilityService
{
    private static readonly IReadOnlyDictionary<string, string> FallbackFeatureFlags =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["toggle_fog_reveal_patch_fallback"] = "allow_fog_patch_fallback",
            ["set_unit_cap_patch_fallback"] = "allow_unit_cap_patch_fallback"
        };

    private static readonly IReadOnlySet<string> StrictBundleActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "spawn_unit_helper",
        "set_selected_hp",
        "set_selected_shield",
        "set_selected_speed",
        "set_selected_damage_multiplier",
        "set_selected_cooldown_multiplier",
        "set_selected_veterancy",
        "set_selected_owner_faction",
        "toggle_tactical_god_mode",
        "toggle_tactical_one_hit_mode"
    };

    public IReadOnlyList<ActionReliabilityInfo> Evaluate(
        TrainerProfile profile,
        AttachSession session,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
    {
        var disabledActions = ParseCsvSet(session.Process.Metadata, "dependencyDisabledActions");
        var criticalSymbols = ParseCsvSet(profile.Metadata, "criticalSymbols");
        var results = new List<ActionReliabilityInfo>(profile.Actions.Count);

        foreach (var (actionId, action) in profile.Actions.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            results.Add(EvaluateAction(
                profile,
                actionId,
                action,
                session,
                disabledActions,
                criticalSymbols,
                catalog));
        }

        return results;
    }

    public IReadOnlyList<ActionReliabilityInfo> Evaluate(
        TrainerProfile profile,
        AttachSession session)
    {
        return Evaluate(profile, session, null);
    }

    private static ActionReliabilityInfo EvaluateAction(  // NOSONAR
        TrainerProfile profile,
        string actionId,
        ActionSpec action,
        AttachSession session,
        IReadOnlySet<string> disabledActions,
        IReadOnlySet<string> criticalSymbols,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
    {
        if (FallbackFeatureFlags.TryGetValue(actionId, out var featureFlag) &&
            (!profile.FeatureFlags.TryGetValue(featureFlag, out var enabled) || !enabled))
        {
            return new ActionReliabilityInfo(
                actionId,
                ActionReliabilityState.Unavailable,
                "fallback_disabled",
                1.00d,
                $"Fallback action is disabled by feature flag '{featureFlag}'.");
        }

        if (FallbackFeatureFlags.ContainsKey(actionId))
        {
            return new ActionReliabilityInfo(
                actionId,
                ActionReliabilityState.Experimental,
                "fallback_experimental",
                0.45d,
                "Fallback action is enabled but remains experimental pending live validation.");
        }

        if (disabledActions.Contains(actionId))
        {
            return new ActionReliabilityInfo(
                actionId,
                ActionReliabilityState.Unavailable,
                "dependency_soft_blocked",
                0.99d,
                "Action disabled by dependency validator.");
        }

        if (session.Process.Mode == RuntimeMode.Unknown &&
            StrictBundleActions.Contains(actionId))
        {
            return new ActionReliabilityInfo(
                actionId,
                ActionReliabilityState.Unavailable,
                "mode_unknown_strict_gate",
                0.90d,
                "Action belongs to a strict bundle and requires concrete runtime mode detection.");
        }

        if (action.Mode != RuntimeMode.Unknown)
        {
            if (session.Process.Mode == RuntimeMode.Unknown)
            {
                return new ActionReliabilityInfo(
                    actionId,
                    ActionReliabilityState.Unavailable,
                    "mode_unknown_strict_gate",
                    0.90d,
                    $"Action requires runtime mode {action.Mode}.");
            }

            if (session.Process.Mode != action.Mode)
            {
                return new ActionReliabilityInfo(
                    actionId,
                    ActionReliabilityState.Unavailable,
                    "mode_mismatch",
                    1.00d,
                    $"Action requires mode {action.Mode}, current mode is {session.Process.Mode}.");
            }
        }

        if (action.ExecutionKind == ExecutionKind.Helper)
        {
            if (catalog is null || !catalog.TryGetValue("unit_catalog", out var units) || units.Count == 0)
            {
                return new ActionReliabilityInfo(
                    actionId,
                    ActionReliabilityState.Experimental,
                    "catalog_unavailable",
                    0.60d,
                    "Catalog data is unavailable for helper-guided workflows.");
            }

            return new ActionReliabilityInfo(actionId, ActionReliabilityState.Stable, "helper_ready", 0.85d);
        }

        if (!RequiresSymbol(action))
        {
            return new ActionReliabilityInfo(actionId, ActionReliabilityState.Stable, "non_symbol_action", 0.85d);
        }

        if (!ActionSymbolRegistry.TryGetSymbol(actionId, out var symbol))
        {
            return new ActionReliabilityInfo(
                actionId,
                ActionReliabilityState.Experimental,
                "symbol_hint_missing",
                0.45d,
                "Action has a symbol payload but no symbol hint mapping.");
        }

        if (!session.Symbols.TryGetValue(symbol, out var symbolInfo) ||
            symbolInfo is null ||
            symbolInfo.Address == nint.Zero ||
            symbolInfo.HealthStatus == SymbolHealthStatus.Unresolved)
        {
            return new ActionReliabilityInfo(
                actionId,
                ActionReliabilityState.Unavailable,
                "symbol_unresolved",
                0.95d,
                $"Required symbol '{symbol}' is unresolved.");
        }

        var isCritical = criticalSymbols.Contains(symbol);
        if (isCritical && symbolInfo.HealthStatus != SymbolHealthStatus.Healthy)
        {
            return new ActionReliabilityInfo(
                actionId,
                ActionReliabilityState.Unavailable,
                "critical_symbol_degraded",
                ClampConfidence(symbolInfo.Confidence),
                $"Critical symbol '{symbol}' is {symbolInfo.HealthStatus}.");
        }

        if (symbolInfo.HealthStatus == SymbolHealthStatus.Degraded || symbolInfo.Source == AddressSource.Fallback)
        {
            return new ActionReliabilityInfo(
                actionId,
                ActionReliabilityState.Experimental,
                "fallback_or_degraded",
                ClampConfidence(symbolInfo.Confidence),
                $"Symbol '{symbol}' is {symbolInfo.HealthStatus} via {symbolInfo.Source}.");
        }

        return new ActionReliabilityInfo(
            actionId,
            ActionReliabilityState.Stable,
            symbolInfo.Source == AddressSource.Signature ? "healthy_signature" : "healthy_non_signature",
            ClampConfidence(symbolInfo.Confidence));
    }

    private static bool RequiresSymbol(ActionSpec action)
    {
        if (!action.PayloadSchema.TryGetPropertyValue("required", out var node) || node is not JsonArray required)
        {
            return false;
        }

        return required.Any(x => string.Equals(x?.GetValue<string>(), "symbol", StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlySet<string> ParseCsvSet(IReadOnlyDictionary<string, string>? metadata, string key)
    {
        if (metadata is null || !metadata.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw))
        {
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        return raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static double ClampConfidence(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0.50d;
        }

        if (value < 0d)
        {
            return 0d;
        }

        if (value > 1d)
        {
            return 1d;
        }

        return value;
    }
}
