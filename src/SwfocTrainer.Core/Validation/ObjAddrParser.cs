using System.Globalization;

namespace SwfocTrainer.Core.Validation;

/// <summary>
/// Canonical parser for SWFOC object addresses (engine pointers).
///
/// Hoisted from UnitControlTabViewModel.TryParseObjAddr in v1.1.0 because at
/// least 7 V2 tabs (UnitControl / Inspector / Combat / Speed / Hero Lab /
/// Hardpoint / Cross-Faction) each parsed addresses with slightly different
/// rules — accepting only decimal in some places, only hex in others,
/// confusing operators who copy a `0x...` from one tab into a "decimal only"
/// TextBox on another. This shared parser accepts BOTH conventions everywhere
/// (per v1.0.2 improvement plan Cross-Cutting #2).
///
/// Accepted forms:
///   - `0x12345678`         (hex with prefix)
///   - `0X12345678`         (case-insensitive prefix)
///   - `12345678`           (hex without prefix — the canonical Tactical Units row format)
///   - `1234567890`         (also hex; numeric-only strings are interpreted as hex)
///
/// Rationale for "numeric-only is hex": every obj_addr the engine produces is
/// emitted as hex by the bridge (`SWFOC_ListTacticalUnits` returns rows with
/// hex addresses without prefix). Operators have NEVER seen a decimal obj_addr
/// in practice, so treating bare digits as hex matches expectations and aligns
/// with the existing UnitControl behavior at line 1380.
///
/// Returns (success, parsedAddr, errorMessage). On failure, errorMessage is
/// human-readable and ready to display to the operator (no internal jargon).
/// </summary>
public static class ObjAddrParser
{
    /// <summary>
    /// Try to parse an obj_addr string. Pure function — no side effects.
    /// Returns the parsed value as a <c>long</c> for CLS compliance with
    /// <c>SwfocTrainer.Core</c>'s assembly-level <c>[CLSCompliant(true)]</c> — see
    /// project CLAUDE.md "C# Conventions" section. Engine addresses fit in 48 bits
    /// on x64 user-space so the long/ulong cast is lossless, and the parser
    /// bounds-checks against <see cref="long.MaxValue"/> before casting.
    /// </summary>
    public static (bool Success, long Addr, string Error) TryParse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return (false, 0L, "Obj address is empty.");
        }

        var trimmed = input.Trim();
        var span = trimmed.AsSpan();
        if (span.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            span = span[2..];
        }

        if (span.IsEmpty)
        {
            return (false, 0L, $"Obj address '{input}' has no digits after the 0x prefix.");
        }

        // Parse internally as ulong (full hex range) then bounds-check before
        // casting to long, per the project's C# convention for engine pointers.
        if (!ulong.TryParse(span, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var addr64))
        {
            return (false, 0L, $"Obj address '{input}' is not a valid hex value.");
        }

        if (addr64 > (ulong)long.MaxValue)
        {
            return (false, 0L, $"Obj address '{input}' is beyond long.MaxValue (user-space x64 pointers fit in 48 bits).");
        }

        return (true, (long)addr64, string.Empty);
    }

    /// <summary>Convenience: returns true on success, addr via out param. Discards error message.</summary>
    public static bool TryParse(string? input, out long addr)
    {
        var (ok, parsed, _) = TryParse(input);
        addr = parsed;
        return ok;
    }
}
