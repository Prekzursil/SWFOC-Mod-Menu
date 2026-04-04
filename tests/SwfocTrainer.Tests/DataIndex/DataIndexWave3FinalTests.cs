using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using Xunit;

namespace SwfocTrainer.Tests.DataIndex;

/// <summary>
/// Wave 3 Final coverage for DataIndex: model record constructors,
/// MegaFilesIndex.GetEnabledFilesInLoadOrder, EffectiveFileMapReport.Empty,
/// EffectiveGameDataIndexRequest optional fields, EffectiveFileMapEntry constructor.
/// </summary>
public sealed class DataIndexWave3FinalTests
{
    [Fact]
    public void MegaFileEntry_ShouldStoreAllProperties()
    {
        var attrs = new Dictionary<string, string> { ["key"] = "val" };
        var entry = new MegaFileEntry("test.meg", 1, true, attrs);
        entry.FileName.Should().Be("test.meg");
        entry.LoadOrder.Should().Be(1);
        entry.Enabled.Should().BeTrue();
        entry.Attributes.Should().ContainKey("key");
    }

    [Fact]
    public void MegaFilesIndex_Empty_ShouldHaveEmptyCollections()
    {
        MegaFilesIndex.Empty.Files.Should().BeEmpty();
        MegaFilesIndex.Empty.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void MegaFilesIndex_GetEnabledFilesInLoadOrder_ShouldFilterAndSort()
    {
        var files = new[]
        {
            new MegaFileEntry("b.meg", 2, true, new Dictionary<string, string>()),
            new MegaFileEntry("disabled.meg", 3, false, new Dictionary<string, string>()),
            new MegaFileEntry("a.meg", 1, true, new Dictionary<string, string>())
        };
        var index = new MegaFilesIndex(files, Array.Empty<string>());
        var enabled = index.GetEnabledFilesInLoadOrder();
        enabled.Should().HaveCount(2);
        enabled[0].FileName.Should().Be("a.meg");
        enabled[1].FileName.Should().Be("b.meg");
    }

    [Fact]
    public void EffectiveGameDataIndexRequest_DefaultValues_ShouldSetCorrectly()
    {
        var request = new EffectiveGameDataIndexRequest("profile", @"C:\Game");
        request.ModPath.Should().BeNull();
        request.MegaFilesXmlRelativePath.Should().Be(@"Data\MegaFiles.xml");
    }

    [Fact]
    public void EffectiveGameDataIndexRequest_WithOptionalValues_ShouldSetCorrectly()
    {
        var request = new EffectiveGameDataIndexRequest("profile", @"C:\Game", @"C:\Mods\test", @"Custom\Mega.xml");
        request.ModPath.Should().Be(@"C:\Mods\test");
        request.MegaFilesXmlRelativePath.Should().Be(@"Custom\Mega.xml");
    }

    [Fact]
    public void EffectiveFileMapEntry_ShouldStoreAllProperties()
    {
        var entry = new EffectiveFileMapEntry("data/units.xml", "meg_entry", "patch.meg:data/units.xml", 5, true, null);
        entry.RelativePath.Should().Be("data/units.xml");
        entry.SourceType.Should().Be("meg_entry");
        entry.OverrideRank.Should().Be(5);
        entry.Active.Should().BeTrue();
        entry.ShadowedBy.Should().BeNull();
    }

    [Fact]
    public void EffectiveFileMapEntry_Shadowed_ShouldStoreShadowedBy()
    {
        var entry = new EffectiveFileMapEntry("data/units.xml", "meg_entry", "base.meg:data/units.xml", 1, false, "patch.meg:data/units.xml");
        entry.Active.Should().BeFalse();
        entry.ShadowedBy.Should().Be("patch.meg:data/units.xml");
    }

    [Fact]
    public void EffectiveFileMapReport_Empty_ShouldHaveDefaults()
    {
        EffectiveFileMapReport.Empty.ProfileId.Should().BeEmpty();
        EffectiveFileMapReport.Empty.GameRootPath.Should().BeEmpty();
        EffectiveFileMapReport.Empty.ModPath.Should().BeNull();
        EffectiveFileMapReport.Empty.Files.Should().BeEmpty();
        EffectiveFileMapReport.Empty.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void EffectiveFileMapReport_WithValues_ShouldStoreAll()
    {
        var files = new[] { new EffectiveFileMapEntry("test.xml", "loose", "/path", 0, true, null) };
        var report = new EffectiveFileMapReport("profile", @"C:\Game", @"C:\Mods", files, new[] { "diag" });
        report.Files.Should().HaveCount(1);
        report.Diagnostics.Should().Contain("diag");
    }
}
