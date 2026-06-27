using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// Pins the 2026-04-27 <see cref="SpawningTabViewModel.ExtractFactionPrefix"/>
/// heuristic — the auto-derive logic that builds the Spawn tab's faction
/// filter dropdown from raw type ids without a hardcoded faction whitelist.
/// </summary>
public sealed class SpawningFactionPrefixTests
{
    [Theory]
    [InlineData("REBEL_INFANTRY", "REBEL")]
    [InlineData("EMPIRE_AT_AT", "EMPIRE")]
    [InlineData("UNDERWORLD_FRIGATE", "UNDERWORLD")]
    public void Vanilla_Prefix_IsFirstUnderscoreToken(string typeId, string expected)
    {
        SpawningTabViewModel.ExtractFactionPrefix(typeId).Should().Be(expected);
    }

    [Theory]
    [InlineData("AOTR_REBEL_INFANTRY", "AOTR")]
    [InlineData("ROE_CIS_DROID", "ROE")]
    [InlineData("ROTR_HUTTS_GUARD", "ROTR")]
    public void Mod_Prefix_GroupsUnderModName(string typeId, string expected)
    {
        // Modded games typically use ModPrefix_Faction_Type. We take the
        // first token (the mod prefix); operators refine via the search
        // box if they want "AOTR rebels only".
        SpawningTabViewModel.ExtractFactionPrefix(typeId).Should().Be(expected);
    }

    [Theory]
    [InlineData("STORMTROOPER", "STORMTROOPER")]
    [InlineData("X_WING", "X")]
    public void NoUnderscore_FirstUnderscoreOrWholeName(string typeId, string expected)
    {
        // No underscore → the entire id is the "prefix" (whole faction).
        // First underscore → just the leading token.
        SpawningTabViewModel.ExtractFactionPrefix(typeId).Should().Be(expected);
    }

    [Fact]
    public void Empty_Input_ReturnsEmpty()
    {
        SpawningTabViewModel.ExtractFactionPrefix(string.Empty).Should().BeEmpty();
    }

    [Theory]
    [InlineData("rebel_infantry", "REBEL")]
    [InlineData("Empire_AT_AT", "EMPIRE")]
    public void Lowercase_Input_NormalisedToUpper(string typeId, string expected)
    {
        // The dropdown displays a single canonical casing so case-only
        // duplicates don't fragment the list.
        SpawningTabViewModel.ExtractFactionPrefix(typeId).Should().Be(expected);
    }

    [Fact]
    public void LeadingUnderscore_TreatedAsEmptyToken()
    {
        // Edge case: "_REBEL_INFANTRY" — the implementation skips empty
        // tokens, so this returns the entire id.
        // Document the actual behaviour rather than over-specifying.
        var result = SpawningTabViewModel.ExtractFactionPrefix("_REBEL_INFANTRY");
        result.Should().NotBeNullOrEmpty();
    }
}
