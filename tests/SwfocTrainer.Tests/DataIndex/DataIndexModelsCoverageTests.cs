using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using Xunit;

namespace SwfocTrainer.Tests.DataIndex;

public sealed class DataIndexModelsCoverageTests
{
    [Fact]
    public void MegaFilesIndexEmpty_ShouldExposeNoFilesOrDiagnostics()
    {
        MegaFilesIndex.Empty.Files.Should().BeEmpty();
        MegaFilesIndex.Empty.Diagnostics.Should().BeEmpty();
        MegaFilesIndex.Empty.GetEnabledFilesInLoadOrder().Should().BeEmpty();
    }

    [Fact]
    public void EffectiveGameDataIndexRequest_ShouldPreserveExplicitInputs()
    {
        var request = new EffectiveGameDataIndexRequest(
            ProfileId: "base_swfoc",
            GameRootPath: @"C:\Games\EaW",
            ModPath: @"C:\Mods\AOTR",
            MegaFilesXmlRelativePath: @"Config\MegaFiles.xml");

        request.ProfileId.Should().Be("base_swfoc");
        request.GameRootPath.Should().Be(@"C:\Games\EaW");
        request.ModPath.Should().Be(@"C:\Mods\AOTR");
        request.MegaFilesXmlRelativePath.Should().Be(@"Config\MegaFiles.xml");
    }

    [Fact]
    public void EffectiveFileMapReportEmpty_ShouldExposeExpectedDefaults()
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
            RelativePath: "Data/XML/Test.xml",
            SourceType: "mod_loose",
            SourcePath: @"C:\Mods\Data\XML\Test.xml",
            OverrideRank: 4,
            Active: false,
            ShadowedBy: @"C:\Mods\Data\XML\Override.xml");

        entry.RelativePath.Should().Be("Data/XML/Test.xml");
        entry.SourceType.Should().Be("mod_loose");
        entry.SourcePath.Should().Be(@"C:\Mods\Data\XML\Test.xml");
        entry.OverrideRank.Should().Be(4);
        entry.Active.Should().BeFalse();
        entry.ShadowedBy.Should().Be(@"C:\Mods\Data\XML\Override.xml");
    }
}
