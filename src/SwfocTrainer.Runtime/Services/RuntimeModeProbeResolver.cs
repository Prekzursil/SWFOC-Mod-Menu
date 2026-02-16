using System.Globalization;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Interop;

namespace SwfocTrainer.Runtime.Services;

/// <summary>
/// Resolves runtime mode from live process state rather than launch arguments.
/// </summary>
public sealed class RuntimeModeProbeResolver
{
    private static readonly HashSet<string> TacticalSignals = new(StringComparer.OrdinalIgnoreCase)
    {
        "tactical_god_mode",
        "tactical_one_hit_mode",
        "selected_hp",
        "selected_shield"
    };

    private static readonly HashSet<string> GalacticSignals = new(StringComparer.OrdinalIgnoreCase)
    {
        "planet_owner",
        "hero_respawn_timer"
    };

    internal RuntimeModeProbeResult Resolve(SymbolMap symbols, ProcessMemoryAccessor memory, RuntimeMode hintMode)
    {
        var observations = new List<RuntimeModeProbeObservation>
        {
            ProbeBoolSymbol(symbols, memory, "tactical_god_mode", 1.0),
            ProbeBoolSymbol(symbols, memory, "tactical_one_hit_mode", 1.0),
            ProbeFloatRangeSymbol(symbols, memory, "selected_hp", 0.0, 5_000_000.0, 1.0),
            ProbeFloatRangeSymbol(symbols, memory, "selected_shield", 0.0, 5_000_000.0, 1.0),
            ProbeIntRangeSymbol(symbols, memory, "planet_owner", 0, 16, 2.0),
            ProbeIntRangeSymbol(symbols, memory, "hero_respawn_timer", 0, 86_400, 1.0),
        };

        return Evaluate(hintMode, observations);
    }

    public static RuntimeModeProbeResult Evaluate(
        RuntimeMode hintMode,
        IReadOnlyList<RuntimeModeProbeObservation> observations)
    {
        var tacticalScore = observations
            .Where(x => TacticalSignals.Contains(x.Name))
            .Sum(x => x.Score);
        var galacticScore = observations
            .Where(x => GalacticSignals.Contains(x.Name))
            .Sum(x => x.Score);

        if (tacticalScore >= 2.0 && tacticalScore >= galacticScore + 0.5)
        {
            return new RuntimeModeProbeResult(
                hintMode,
                RuntimeMode.Tactical,
                "probe_tactical_dominant",
                tacticalScore,
                galacticScore,
                observations);
        }

        if (galacticScore >= 2.0 && galacticScore >= tacticalScore + 0.5)
        {
            return new RuntimeModeProbeResult(
                hintMode,
                RuntimeMode.Galactic,
                "probe_galactic_dominant",
                tacticalScore,
                galacticScore,
                observations);
        }

        if (hintMode != RuntimeMode.Unknown)
        {
            return new RuntimeModeProbeResult(
                hintMode,
                hintMode,
                "probe_inconclusive_hint_fallback",
                tacticalScore,
                galacticScore,
                observations);
        }

        return new RuntimeModeProbeResult(
            hintMode,
            RuntimeMode.Unknown,
            "probe_inconclusive_unknown",
            tacticalScore,
            galacticScore,
            observations);
    }

    private static RuntimeModeProbeObservation ProbeBoolSymbol(
        SymbolMap symbols,
        ProcessMemoryAccessor memory,
        string symbolName,
        double scoreOnSuccess)
    {
        if (!symbols.TryGetValue(symbolName, out var symbol) || symbol is null)
        {
            return new RuntimeModeProbeObservation(symbolName, false, "missing", 0, "symbol_missing");
        }

        if (symbol.Address == nint.Zero)
        {
            return new RuntimeModeProbeObservation(symbolName, false, "address=0x0", 0, "address_zero");
        }

        try
        {
            var value = memory.Read<byte>(symbol.Address);
            if (value is 0 or 1)
            {
                return new RuntimeModeProbeObservation(symbolName, true, value.ToString(CultureInfo.InvariantCulture), scoreOnSuccess, "bool_ok");
            }

            return new RuntimeModeProbeObservation(symbolName, true, value.ToString(CultureInfo.InvariantCulture), 0.25, "bool_out_of_range");
        }
        catch (Exception ex)
        {
            return new RuntimeModeProbeObservation(symbolName, false, ex.GetType().Name, 0, "read_error");
        }
    }

    private static RuntimeModeProbeObservation ProbeIntRangeSymbol(
        SymbolMap symbols,
        ProcessMemoryAccessor memory,
        string symbolName,
        int minInclusive,
        int maxInclusive,
        double scoreOnSuccess)
    {
        if (!symbols.TryGetValue(symbolName, out var symbol) || symbol is null)
        {
            return new RuntimeModeProbeObservation(symbolName, false, "missing", 0, "symbol_missing");
        }

        if (symbol.Address == nint.Zero)
        {
            return new RuntimeModeProbeObservation(symbolName, false, "address=0x0", 0, "address_zero");
        }

        try
        {
            var value = memory.Read<int>(symbol.Address);
            var inRange = value >= minInclusive && value <= maxInclusive;
            return new RuntimeModeProbeObservation(
                symbolName,
                true,
                value.ToString(CultureInfo.InvariantCulture),
                inRange ? scoreOnSuccess : 0,
                inRange ? "int_range_ok" : "int_out_of_range");
        }
        catch (Exception ex)
        {
            return new RuntimeModeProbeObservation(symbolName, false, ex.GetType().Name, 0, "read_error");
        }
    }

    private static RuntimeModeProbeObservation ProbeFloatRangeSymbol(
        SymbolMap symbols,
        ProcessMemoryAccessor memory,
        string symbolName,
        double minInclusive,
        double maxInclusive,
        double scoreOnSuccess)
    {
        if (!symbols.TryGetValue(symbolName, out var symbol) || symbol is null)
        {
            return new RuntimeModeProbeObservation(symbolName, false, "missing", 0, "symbol_missing");
        }

        if (symbol.Address == nint.Zero)
        {
            return new RuntimeModeProbeObservation(symbolName, false, "address=0x0", 0, "address_zero");
        }

        try
        {
            var value = memory.Read<float>(symbol.Address);
            var valid = !float.IsNaN(value) && !float.IsInfinity(value) && value >= minInclusive && value <= maxInclusive;
            return new RuntimeModeProbeObservation(
                symbolName,
                true,
                value.ToString("0.###", CultureInfo.InvariantCulture),
                valid ? scoreOnSuccess : 0,
                valid ? "float_range_ok" : "float_out_of_range");
        }
        catch (Exception ex)
        {
            return new RuntimeModeProbeObservation(symbolName, false, ex.GetType().Name, 0, "read_error");
        }
    }
}
