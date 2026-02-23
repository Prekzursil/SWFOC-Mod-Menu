using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

/// <summary>
/// Computes an effective runtime mode using resolved symbol health as evidence.
/// </summary>
public static class RuntimeModeProbeResolver
{
    private static readonly string[] TacticalIndicators =
    [
        "selected_hp",
        "selected_shield",
        "selected_speed",
        "selected_damage_multiplier",
        "selected_cooldown_multiplier",
        "selected_veterancy",
        "selected_owner_faction",
        "tactical_god_mode",
        "tactical_one_hit_mode"
    ];

    private static readonly string[] GalacticIndicators =
    [
        "planet_owner",
        "hero_respawn_timer",
        "unit_cap",
        "credits"
    ];

    public static RuntimeModeProbeResult Resolve(RuntimeMode modeHint, SymbolMap symbols)
    {
        var tacticalSignalCount = CountSignals(symbols, TacticalIndicators);
        var galacticSignalCount = CountSignals(symbols, GalacticIndicators);
        if (TryResolveSingleSignalMode(modeHint, tacticalSignalCount, galacticSignalCount, out var singleSignalResult))
        {
            return singleSignalResult;
        }

        if (tacticalSignalCount > 0 && galacticSignalCount > 0)
        {
            return ResolveAmbiguousMode(modeHint, tacticalSignalCount, galacticSignalCount);
        }

        return ResolveNoSignalMode(modeHint, tacticalSignalCount, galacticSignalCount);
    }

    private static int CountSignals(SymbolMap symbols, IReadOnlyList<string> indicatorNames)
    {
        var count = 0;
        foreach (var name in indicatorNames)
        {
            if (!symbols.TryGetValue(name, out var info) || info is null)
            {
                continue;
            }

            if (info.Address == nint.Zero || info.HealthStatus == SymbolHealthStatus.Unresolved)
            {
                continue;
            }

            count++;
        }

        return count;
    }

    private static bool TryResolveSingleSignalMode(
        RuntimeMode modeHint,
        int tacticalSignalCount,
        int galacticSignalCount,
        out RuntimeModeProbeResult result)
    {
        if (tacticalSignalCount > 0 && galacticSignalCount == 0)
        {
            result = new RuntimeModeProbeResult(
                modeHint,
                RuntimeMode.Tactical,
                "mode_probe_tactical_signals",
                tacticalSignalCount,
                galacticSignalCount);
            return true;
        }

        if (galacticSignalCount > 0 && tacticalSignalCount == 0)
        {
            result = new RuntimeModeProbeResult(
                modeHint,
                RuntimeMode.Galactic,
                "mode_probe_galactic_signals",
                tacticalSignalCount,
                galacticSignalCount);
            return true;
        }

        result = default;
        return false;
    }

    private static RuntimeModeProbeResult ResolveAmbiguousMode(
        RuntimeMode modeHint,
        int tacticalSignalCount,
        int galacticSignalCount)
    {
        if (modeHint is RuntimeMode.Tactical or RuntimeMode.Galactic)
        {
            return new RuntimeModeProbeResult(
                modeHint,
                modeHint,
                "mode_probe_ambiguous_keep_hint",
                tacticalSignalCount,
                galacticSignalCount);
        }

        var tacticalWins = tacticalSignalCount >= galacticSignalCount;
        return new RuntimeModeProbeResult(
            modeHint,
            tacticalWins ? RuntimeMode.Tactical : RuntimeMode.Galactic,
            tacticalWins ? "mode_probe_ambiguous_bias_tactical" : "mode_probe_ambiguous_bias_galactic",
            tacticalSignalCount,
            galacticSignalCount);
    }

    private static RuntimeModeProbeResult ResolveNoSignalMode(
        RuntimeMode modeHint,
        int tacticalSignalCount,
        int galacticSignalCount)
    {
        var hasHint = modeHint is RuntimeMode.Tactical or RuntimeMode.Galactic;
        return new RuntimeModeProbeResult(
            modeHint,
            hasHint ? modeHint : RuntimeMode.Unknown,
            hasHint ? "mode_probe_no_signals_use_hint" : "mode_probe_no_signals_unknown",
            tacticalSignalCount,
            galacticSignalCount);
    }
}

public sealed record RuntimeModeProbeResult(
    RuntimeMode HintMode,
    RuntimeMode EffectiveMode,
    string ReasonCode,
    int TacticalSignalCount,
    int GalacticSignalCount);
