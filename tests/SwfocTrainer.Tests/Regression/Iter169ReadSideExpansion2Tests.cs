using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 169) — pins read-side getter expansion #2: Get_Type,
/// Get_Credits, Get_Faction, Get_Tech_Level. All 4 reuse iter-167
/// unit-getter helper (shape-agnostic for any obj receiver — works
/// for both unit and player). LIVE flips #88-91; master loop now at
/// 91 LIVE wires.
/// </summary>
public sealed class Iter169ReadSideExpansion2Tests
{
    [Theory]
    [InlineData("SWFOC_GetTypeLua")]
    [InlineData("SWFOC_GetCreditsLua")]
    [InlineData("SWFOC_GetFactionLua")]
    [InlineData("SWFOC_GetTechLevelLua")]
    public void ReadSideExpansion2_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void GetCredits_NoteContrastsWithIter155GiveMoney()
    {
        // Pin: read-after-write pairing with iter-155 must survive.
        CapabilityStatusCatalog.Entries["SWFOC_GetCreditsLua"].Note
            .Should().Contain("PlayerGiveMoney");
    }

    [Fact]
    public void GetTechLevel_NoteContrastsWithIter155SetTechLevel()
    {
        CapabilityStatusCatalog.Entries["SWFOC_GetTechLevelLua"].Note
            .Should().Contain("PlayerSetTechLevel");
    }

    [Fact]
    public void GetFaction_NoteMentionsChainabilityWithGetOwner()
    {
        // Pin: the chainability with iter-168 Get_Owner is a key
        // operator workflow ("identify faction of any unit").
        CapabilityStatusCatalog.Entries["SWFOC_GetFactionLua"].Note
            .Should().Contain("Get_Owner");
    }

    [Fact]
    public void ReadSideExpansion2_AllReuseIter167Helper()
    {
        var iter169Entries = new[]
        {
            "SWFOC_GetTypeLua",
            "SWFOC_GetCreditsLua",
            "SWFOC_GetFactionLua",
            "SWFOC_GetTechLevelLua",
        };
        foreach (var name in iter169Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("iter-167",
                    $"{name} should reference reuse of iter-167 unit-getter helper");
        }
    }

    [Fact]
    public void ReadSideExpansion2_AllTaggedIter169()
    {
        var iter169Entries = new[]
        {
            "SWFOC_GetTypeLua",
            "SWFOC_GetCreditsLua",
            "SWFOC_GetFactionLua",
            "SWFOC_GetTechLevelLua",
        };
        foreach (var name in iter169Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 169 LIVE",
                    $"{name} should be tagged as iter 169 LIVE in catalog rationale");
        }
    }
}
