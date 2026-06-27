using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Diagnostics;

/// <summary>
/// Iter-403: pin tests for KnownUnitAbilityNames C# const list (69 names recovered
/// from EnumConversionClass&lt;UnitAbilityType&gt; static initializer at RVA
/// 0x5DEA20 via callgraph mining at iter-402).
///
/// These tests guard against silent regressions when future iters either trim
/// the list (validation-too-tight regression) or extend it (expected if iter-402
/// extraction missed truncated entries).
/// </summary>
public class Iter403KnownUnitAbilityNamesTests
{
    [Fact]
    public void KnownUnitAbilityNames_All_HasAtLeast_69_Entries()
    {
        // iter-402 RE extraction recovered 69 unique `a`-prefixed string-label
        // references from the IDA decompile body. Future iters may extend this
        // (e.g., cross-reference against docs/lua-api.md to disambiguate IDA
        // 14-character truncations) but should NEVER drop below 69.
        KnownUnitAbilityNames.All.Should().HaveCountGreaterThanOrEqualTo(69);
    }

    [Fact]
    public void KnownUnitAbilityNames_All_ContainsNoDuplicates()
    {
        var distinct = KnownUnitAbilityNames.All.Distinct().Count();
        distinct.Should().Be(KnownUnitAbilityNames.All.Count,
            because: "duplicates would silently inflate the dropdown without operator value");
    }

    [Fact]
    public void KnownUnitAbilityNames_All_ContainsCanonicalSamples()
    {
        // Spot-check: the 5 most-commonly-used ability names per docs/lua-api.md
        // (operator-facing examples) MUST be present. If any are missing, the
        // C# embed has drifted from the iter-402 RE extraction.
        KnownUnitAbilityNames.All.Should().Contain("Tractor_Beam");
        KnownUnitAbilityNames.All.Should().Contain("Sensor_Jamming");
        KnownUnitAbilityNames.All.Should().Contain("Ion_Cannon_Shot");
        KnownUnitAbilityNames.All.Should().Contain("Force_Lightning");
        KnownUnitAbilityNames.All.Should().Contain("Stealth");
    }

    [Fact]
    public void KnownUnitAbilityNames_All_AreUnderscoreSeparatedTitleCase()
    {
        // SWFOC convention: `Title_Case_With_Underscores`. No spaces, no all-caps,
        // no all-lowercase. Catches accidental editor-side slip-ups during
        // future RE extension (e.g. someone embeds a community-doc form like
        // "tractor_beam" instead of "Tractor_Beam").
        foreach (var name in KnownUnitAbilityNames.All)
        {
            name.Should().NotBeNullOrWhiteSpace();
            name.Should().NotContain(" ", because: $"name '{name}' must use underscores not spaces");
            // First char must be uppercase (TitleCase head).
            char.IsUpper(name[0]).Should().BeTrue(
                because: $"name '{name}' must start with uppercase (SWFOC TitleCase convention)");
        }
    }
}
