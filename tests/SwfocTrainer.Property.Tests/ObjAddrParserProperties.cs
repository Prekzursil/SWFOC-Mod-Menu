using FluentAssertions;
using FsCheck;
using FsCheck.Xunit;
using SwfocTrainer.Core.Validation;
using Xunit;

namespace SwfocTrainer.PropertyTests;

/// <summary>
/// Property-based assertions for ObjAddrParser.
/// These hold for ALL randomly-generated inputs, not just hand-picked examples.
/// New properties land here when editor-polish ships ObjAddrParser refactors.
/// </summary>
public class ObjAddrParserProperties
{
    // iter-475 included a [Property(Skip=...)] `TryParse_OfNullOrEmpty_AlwaysFails`
    // with a TODO claiming FsCheck surfaced a whitespace counter-example. Per
    // iter-476 7eb7020 adversarial review HIGH 1: ObjAddrParser.TryParse
    // (ObjAddrParser.cs:43-46) returns (false, 0L, "Obj address is empty.")
    // unconditionally when IsNullOrWhiteSpace(input) is true — no such
    // counter-example can exist. The Skip carried a fabricated bug claim;
    // removed to prevent permanent misinformation in test code.
    //
    // The whitespace-rejection invariant is already pinned by the existing
    // concrete unit tests in src/SwfocTrainer.Core's test surface (and the
    // `TryParse_OfWhitespacePadded_StripsAndParses` property below covers the
    // padded-hex success path). No randomization adds value over those.

    [Fact]
    public void TryParse_OfNullOrEmpty_AlwaysFails()
    {
        ObjAddrParser.TryParse((string?)null).Success.Should().BeFalse();
        ObjAddrParser.TryParse(string.Empty).Success.Should().BeFalse();
        ObjAddrParser.TryParse("   ").Success.Should().BeFalse();
        ObjAddrParser.TryParse("\t\n\r ").Success.Should().BeFalse();
    }

    [Property(MaxTest = 500)]
    public Property TryParse_RoundTrip_HexUpper(NonNegativeInt n)
    {
        var addr = (long)n.Item;
        var hex = "0x" + addr.ToString("X");
        var result = ObjAddrParser.TryParse(hex);
        return (result.Success && result.Addr == addr).ToProperty();
    }

    [Property(MaxTest = 500)]
    public Property TryParse_RoundTrip_HexLower(NonNegativeInt n)
    {
        var addr = (long)n.Item;
        var hex = "0x" + addr.ToString("x");
        var result = ObjAddrParser.TryParse(hex);
        return (result.Success && result.Addr == addr).ToProperty();
    }

    // iter-475 included a [Property(Skip=...)] `TryParse_RoundTrip_Decimal`
    // with a TODO framing the parser's hex-by-default behavior as a bug, and
    // suggesting fix path (a) "update the parser to require explicit decimal
    // marker". Per iter-476 7eb7020 adversarial review HIGH 2: ObjAddrParser.cs
    // lines 19-26 + 50-65 documents numeric-only-is-hex as INTENTIONAL design
    // (Cross-Cutting #2 of the v1.0.2 plan, aligned with the
    // SWFOC_ListTacticalUnits hex row format and the iter-1.1.0 hoist
    // consolidation). Existing ObjAddrParserTests.cs:18-19 pins "1234ABCD" ->
    // 0x1234ABCDL. The skipped property challenged a documented contract;
    // removed and replaced with a property that PINS the contract — hex strings
    // WITHOUT the "0x" prefix round-trip via addr.ToString("X"), not
    // addr.ToString(). The previous name `TryParse_NumericString_IsInterpretedAsHex`
    // (iter-476) was imprecise because addr.ToString("X") yields hex-with-A-F
    // for n>=10 (e.g. addr=10 → "A"), which is not a "numeric string" in the
    // strict sense. iter-487 renamed to `TryParse_HexNoPrefix_RoundTrips` per
    // iter-477 b064ddb adversarial review LOW (naming polish) — the new name
    // matches the internal `hexNoPrefix` variable and accurately describes the
    // invariant being pinned. This captures the v1.0.2 invariant under
    // randomized inputs rather than challenging it.
    [Property(MaxTest = 500)]
    public Property TryParse_HexNoPrefix_RoundTrips(NonNegativeInt n)
    {
        var addr = (long)n.Item;
        var hexNoPrefix = addr.ToString("X");
        var result = ObjAddrParser.TryParse(hexNoPrefix);
        return (result.Success && result.Addr == addr).ToProperty();
    }

    [Property(MaxTest = 500)]
    public Property TryParse_OfWhitespacePadded_StripsAndParses(NonNegativeInt n)
    {
        var addr = (long)n.Item;
        var padded = "  0x" + addr.ToString("X") + "  ";
        var result = ObjAddrParser.TryParse(padded);
        return (result.Success && result.Addr == addr).ToProperty();
    }

    [Property(MaxTest = 500)]
    public Property TryParse_GarbageAlpha_AlwaysFails(NonEmptyString input)
    {
        // If input contains chars outside hex/decimal/0x prefix/whitespace, fail
        var s = input.Item;
        var hasGarbage = false;
        foreach (var c in s)
        {
            if (!(char.IsDigit(c) || char.IsWhiteSpace(c)
                  || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')
                  || c == 'x' || c == 'X' || c == '-' || c == '+'))
            {
                hasGarbage = true;
                break;
            }
        }
        return Prop.When(hasGarbage, () => ObjAddrParser.TryParse(s).Success == false);
    }

    [Fact]
    public void TryParse_Overflow_BeyondLongMax_Fails()
    {
        var beyondMax = "0xFFFFFFFFFFFFFFFFFF";  // 18 hex digits > 8 bytes
        var result = ObjAddrParser.TryParse(beyondMax);
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
    }
}
