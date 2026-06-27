using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.DataIndex.Services;
using SwfocTrainer.Meg;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.DataIndex;

/// <summary>
/// Wave 8 coverage: remaining branches in EffectiveGameDataIndexService and MegaFilesXmlIndexBuilder —
/// null request, empty profileId/gameRootPath, XML not found, MEG parse failure,
/// loose files, mod path, shadowing, XML builder edge cases.
/// </summary>
public sealed class DataIndexWave8CoverageTests
{
    #region EffectiveGameDataIndexService — null and empty guards

    [Fact]
    public void Build_ShouldThrow_WhenRequestIsNull()
    {
        var service = new EffectiveGameDataIndexService();
        var act = () => service.Build(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_ShouldReturnEmptyReport_WhenProfileIdIsWhitespace()
    {
        var service = new EffectiveGameDataIndexService();
        var request = new EffectiveGameDataIndexRequest(
            ProfileId: "   ",
            GameRootPath: @"C:\Games");

        var result = service.Build(request);
        result.Files.Should().BeEmpty();
        result.Diagnostics.Should().Contain(d => d.Contains("required"));
    }

    [Fact]
    public void Build_ShouldReturnEmptyReport_WhenGameRootPathIsWhitespace()
    {
        var service = new EffectiveGameDataIndexService();
        var request = new EffectiveGameDataIndexRequest(
            ProfileId: "base_swfoc",
            GameRootPath: "   ");

        var result = service.Build(request);
        result.Files.Should().BeEmpty();
        result.Diagnostics.Should().Contain(d => d.Contains("required"));
    }

    #endregion

    #region EffectiveGameDataIndexService — XML not found

    [Fact]
    public void Build_ShouldAddDiagnostic_WhenMegaFilesXmlNotFound()
    {
        using var temp = new TempDirectory("swfoc-dataindex-w8");
        var service = new EffectiveGameDataIndexService();
        var request = new EffectiveGameDataIndexRequest(
            ProfileId: "base_swfoc",
            GameRootPath: temp.Path);

        var result = service.Build(request);
        result.Diagnostics.Should().Contain(d => d.Contains("MegaFiles.xml not found"));
    }

    #endregion

    #region EffectiveGameDataIndexService — loose files

    [Fact]
    public void Build_ShouldIncludeLooseFiles()
    {
        using var temp = new TempDirectory("swfoc-dataindex-w8-loose");
        var dataDir = Path.Join(temp.Path, "Data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Join(dataDir, "Units.xml"), "<units/>");

        var service = new EffectiveGameDataIndexService();
        var request = new EffectiveGameDataIndexRequest(
            ProfileId: "base_swfoc",
            GameRootPath: temp.Path);

        var result = service.Build(request);
        result.Files.Should().Contain(f => f.RelativePath.Contains("Units.xml") && f.SourceType == "game_loose");
    }

    #endregion

    #region EffectiveGameDataIndexService — mod path

    [Fact]
    public void Build_ShouldIncludeModFiles_WhenModPathProvided()
    {
        using var temp = new TempDirectory("swfoc-dataindex-w8-mod");
        var modDir = Path.Join(temp.Path, "mod");
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Join(modDir, "Custom.xml"), "<custom/>");

        var service = new EffectiveGameDataIndexService();
        var request = new EffectiveGameDataIndexRequest(
            ProfileId: "base_swfoc",
            GameRootPath: temp.Path,
            ModPath: modDir);

        var result = service.Build(request);
        result.Files.Should().Contain(f => f.RelativePath.Contains("Custom.xml") && f.SourceType == "mod_loose");
    }

    #endregion

    #region EffectiveGameDataIndexService — shadowing

    [Fact]
    public void Build_ShouldMarkShadowedEntries_WhenModOverridesLoose()
    {
        using var temp = new TempDirectory("swfoc-dataindex-w8-shadow");
        var gameDir = Path.Join(temp.Path, "game");
        var modDir = Path.Join(temp.Path, "mod");
        Directory.CreateDirectory(gameDir);
        Directory.CreateDirectory(modDir);
        File.WriteAllText(Path.Join(gameDir, "Units.xml"), "<original/>");
        File.WriteAllText(Path.Join(modDir, "Units.xml"), "<modded/>");

        var service = new EffectiveGameDataIndexService();
        var request = new EffectiveGameDataIndexRequest(
            ProfileId: "base_swfoc",
            GameRootPath: gameDir,
            ModPath: modDir);

        var result = service.Build(request);
        var entries = result.Files.Where(f => f.RelativePath == "Units.xml").ToArray();
        entries.Should().HaveCount(2);
        entries.Should().Contain(f => f.Active && f.SourceType == "mod_loose");
        entries.Should().Contain(f => !f.Active && f.ShadowedBy != null);
    }

    #endregion

    #region EffectiveGameDataIndexService — constructor null guards

    [Fact]
    public void Constructor_ShouldThrow_WhenMegaFilesXmlIndexBuilderIsNull()
    {
        var act = () => new EffectiveGameDataIndexService(null!, new MegArchiveReader());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenMegArchiveReaderIsNull()
    {
        var act = () => new EffectiveGameDataIndexService(new MegaFilesXmlIndexBuilder(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region MegaFilesXmlIndexBuilder — null/empty/invalid content

    [Fact]
    public void Build_ShouldThrow_WhenContentIsNull()
    {
        var builder = new MegaFilesXmlIndexBuilder();
        var act = () => builder.Build(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_ShouldReturnEmpty_WhenContentIsWhitespace()
    {
        var builder = new MegaFilesXmlIndexBuilder();
        var result = builder.Build("   ");
        result.Files.Should().BeEmpty();
    }

    [Fact]
    public void Build_ShouldReturnEmpty_WhenContentIsInvalidXml()
    {
        var builder = new MegaFilesXmlIndexBuilder();
        var result = builder.Build("not-xml{{{");
        result.Files.Should().BeEmpty();
        result.Diagnostics.Should().Contain(d => d.Contains("Invalid MegaFiles XML"));
    }

    #endregion

    #region MegaFilesXmlIndexBuilder — disabled entry

    [Fact]
    public void Build_ShouldMarkDisabledEntries()
    {
        var builder = new MegaFilesXmlIndexBuilder();
        var xml = """
            <MegaFiles>
                <MegaFile Name="test.meg" Enabled="false" />
            </MegaFiles>
            """;
        var result = builder.Build(xml);
        result.Files.Should().ContainSingle();
        result.Files[0].Enabled.Should().BeFalse();
    }

    #endregion

    #region MegaFilesXmlIndexBuilder — load order

    [Fact]
    public void Build_ShouldAssignLoadOrderSequentially()
    {
        var builder = new MegaFilesXmlIndexBuilder();
        var xml = """
            <MegaFiles>
                <MegaFile Name="first.meg" />
                <MegaFile Name="second.meg" />
            </MegaFiles>
            """;
        var result = builder.Build(xml);
        result.Files.Should().HaveCount(2);
        result.Files[0].LoadOrder.Should().Be(0);
        result.Files[0].FileName.Should().Be("first.meg");
        result.Files[1].LoadOrder.Should().Be(1);
        result.Files[1].FileName.Should().Be("second.meg");
    }

    [Fact]
    public void Build_ShouldSkipEntryWithoutFilenameAttribute()
    {
        var builder = new MegaFilesXmlIndexBuilder();
        var xml = """
            <MegaFiles>
                <MegaFile SomeOther="value" />
                <MegaFile Name="valid.meg" />
            </MegaFiles>
            """;
        var result = builder.Build(xml);
        result.Files.Should().ContainSingle();
        result.Files[0].FileName.Should().Be("valid.meg");
        result.Diagnostics.Should().Contain(d => d.Contains("no filename attribute"));
    }

    #endregion

    #region MegaFilesIndex — GetEnabledFilesInLoadOrder

    [Fact]
    public void GetEnabledFilesInLoadOrder_ShouldReturnOnlyEnabled()
    {
        var index = new MegaFilesIndex(
            new[]
            {
                new MegaFileEntry("a.meg", 0, true, new Dictionary<string, string>()),
                new MegaFileEntry("b.meg", 1, false, new Dictionary<string, string>()),
                new MegaFileEntry("c.meg", 2, true, new Dictionary<string, string>())
            },
            Array.Empty<string>());

        var enabled = index.GetEnabledFilesInLoadOrder();
        enabled.Should().HaveCount(2);
        enabled[0].FileName.Should().Be("a.meg");
        enabled[1].FileName.Should().Be("c.meg");
    }

    #endregion
}
