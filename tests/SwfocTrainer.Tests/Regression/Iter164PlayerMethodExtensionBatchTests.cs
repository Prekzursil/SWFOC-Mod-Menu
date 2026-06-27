using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 164) — pins player-method extension LIVE batch
/// across 2 existing dispatchers (1 no-arg + 2 generic 2-arg). LIVE
/// flips #73-75; master loop now at 75 LIVE wires.
/// </summary>
public sealed class Iter164PlayerMethodExtensionBatchTests
{
    [Theory]
    [InlineData("SWFOC_EnableAsActorLua")]
    [InlineData("SWFOC_ReleaseCreditsForTacticalLua")]
    [InlineData("SWFOC_SelectObjectLua")]
    public void PlayerExtensionBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void EnableAsActor_NoteFlagsNoArgViaIter112()
    {
        // Pin: catalog should explain this uses the no-arg helper.
        // Critical for future readers since most player methods take args.
        CapabilityStatusCatalog.Entries["SWFOC_EnableAsActorLua"].Note
            .Should().Contain("iter-112");
    }

    [Fact]
    public void ReleaseCreditsForTactical_NoteMentionsGalacticTacticalTransition()
    {
        CapabilityStatusCatalog.Entries["SWFOC_ReleaseCreditsForTacticalLua"].Note
            .Should().Contain("tactical");
    }

    [Fact]
    public void SelectObject_NoteMentionsUiSelection()
    {
        CapabilityStatusCatalog.Entries["SWFOC_SelectObjectLua"].Note
            .Should().Contain("UI");
    }

    [Fact]
    public void PlayerExtensionBatch_AllTaggedIter164()
    {
        var iter164Entries = new[]
        {
            "SWFOC_EnableAsActorLua",
            "SWFOC_ReleaseCreditsForTacticalLua",
            "SWFOC_SelectObjectLua",
        };
        foreach (var name in iter164Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 164 LIVE",
                    $"{name} should be tagged as iter 164 LIVE in catalog rationale");
        }
    }
}
