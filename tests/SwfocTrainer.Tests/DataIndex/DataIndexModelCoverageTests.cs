using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using Xunit;

namespace SwfocTrainer.Tests.DataIndex;

public sealed class DataIndexModelCoverageTests
{
    [Fact]
    public void MegaFilesIndex_GetEnabledFilesInLoadOrder_ShouldFilterDisabled_AndSort()
    {
        var index = new MegaFilesIndex(
            [
                new MegaFileEntry("late.meg", 5, true, new Dictionary<string, string>()),
                new MegaFileEntry("disabled.meg", 0, false, new Dictionary<string, string>()),
                new MegaFileEntry("early.meg", 1, true, new Dictionary<string, string>())
            ],
            Diagnostics: ["diag"]);

        var enabled = index.GetEnabledFilesInLoadOrder();

        enabled.Select(static file => file.FileName).Should().Equal("early.meg", "late.meg");
    }

    [Fact]
    public void EffectiveFileMapReport_Empty_ShouldExposeEmptyDefaults()
    {
        EffectiveFileMapReport.Empty.ProfileId.Should().BeEmpty();
        EffectiveFileMapReport.Empty.GameRootPath.Should().BeEmpty();
        EffectiveFileMapReport.Empty.ModPath.Should().BeNull();
        EffectiveFileMapReport.Empty.Files.Should().BeEmpty();
        EffectiveFileMapReport.Empty.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void EffectiveFileMapEntry_ShouldRetainShadowingMetadata()
    {
        var entry = new EffectiveFileMapEntry(
            RelativePath: "Data/XML/GameObject.xml",
            SourceType: "mod_loose",
            SourcePath: @"C:\Mods\Data\XML\GameObject.xml",
            OverrideRank: 7,
            Active: false,
            ShadowedBy: @"C:\Mods\Submod\Data\XML\GameObject.xml");

        entry.Active.Should().BeFalse();
        entry.ShadowedBy.Should().Contain("Submod");
        entry.OverrideRank.Should().Be(7);
    }
}
