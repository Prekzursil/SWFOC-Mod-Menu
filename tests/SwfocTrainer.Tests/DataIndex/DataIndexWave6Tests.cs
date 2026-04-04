using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.DataIndex.Services;
using SwfocTrainer.Meg;
using Xunit;

namespace SwfocTrainer.Tests.DataIndex;

/// <summary>
/// Wave 6 — push DataIndex to 100% branch coverage.
/// Covers EffectiveGameDataIndexService: empty/whitespace profileId/gameRootPath,
/// missing MegaFiles.xml, MEG file not found, MEG parse failure, loose file paths,
/// MODPATH loose files, shadowing entries, ResolveMegaPath rooted/direct/underData branches,
/// AddLooseEntries whitespace root.
/// </summary>
public sealed class DataIndexWave6Tests : IDisposable
{
    private readonly string _tempRoot;

    public DataIndexWave6Tests()
    {
        _tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-dataindex-wave6-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, true);
        }
    }

    #region Build — empty/whitespace inputs

    [Fact]
    public void Build_NullRequest_ShouldThrow()
    {
        var service = new EffectiveGameDataIndexService();
        var act = () => service.Build(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_WhitespaceProfileId_ShouldReturnEmptyWithDiagnostic()
    {
        var service = new EffectiveGameDataIndexService();
        var request = new EffectiveGameDataIndexRequest("   ", _tempRoot);
        var result = service.Build(request);
        result.Diagnostics.Should().Contain(d => d.Contains("required"));
    }

    [Fact]
    public void Build_WhitespaceGameRootPath_ShouldReturnEmptyWithDiagnostic()
    {
        var service = new EffectiveGameDataIndexService();
        var request = new EffectiveGameDataIndexRequest("profile", "   ");
        var result = service.Build(request);
        result.Diagnostics.Should().Contain(d => d.Contains("required"));
    }

    #endregion

    #region Build — MegaFiles.xml missing

    [Fact]
    public void Build_MegaFilesXmlMissing_ShouldAddDiagnostic()
    {
        var gameRoot = Path.Join(_tempRoot, "game");
        Directory.CreateDirectory(gameRoot);
        var service = new EffectiveGameDataIndexService();
        var request = new EffectiveGameDataIndexRequest("profile", gameRoot);
        var result = service.Build(request);
        result.Diagnostics.Should().Contain(d => d.Contains("MegaFiles.xml not found"));
    }

    #endregion

    #region Build — MEG file not found

    [Fact]
    public void Build_MegFileNotFound_ShouldAddDiagnostic()
    {
        var gameRoot = Path.Join(_tempRoot, "game2");
        var dataDir = Path.Join(gameRoot, "Data");
        Directory.CreateDirectory(dataDir);
        var megaFilesXml = @"<MegaFiles><File Name=""missing.meg"" /></MegaFiles>";
        File.WriteAllText(Path.Join(dataDir, "MegaFiles.xml"), megaFilesXml);

        var service = new EffectiveGameDataIndexService();
        var request = new EffectiveGameDataIndexRequest("profile", gameRoot);
        var result = service.Build(request);
        result.Diagnostics.Should().Contain(d => d.Contains("missing.meg") && d.Contains("not found"));
    }

    #endregion

    #region Build — MEG parse failure

    [Fact]
    public void Build_MegParseFailure_ShouldAddDiagnostic()
    {
        var gameRoot = Path.Join(_tempRoot, "game3");
        var dataDir = Path.Join(gameRoot, "Data");
        Directory.CreateDirectory(dataDir);
        var megaFilesXml = @"<MegaFiles><File Name=""corrupt.meg"" /></MegaFiles>";
        File.WriteAllText(Path.Join(dataDir, "MegaFiles.xml"), megaFilesXml);
        // Write a tiny invalid MEG file
        File.WriteAllBytes(Path.Join(dataDir, "corrupt.meg"), new byte[] { 0x00, 0x01 });

        var service = new EffectiveGameDataIndexService();
        var request = new EffectiveGameDataIndexRequest("profile", gameRoot);
        var result = service.Build(request);
        result.Diagnostics.Should().Contain(d => d.Contains("MEG parse failed"));
    }

    #endregion

    #region Build — loose files and MODPATH

    [Fact]
    public void Build_LooseFiles_ShouldBeIncluded()
    {
        var gameRoot = Path.Join(_tempRoot, "game4");
        Directory.CreateDirectory(gameRoot);
        File.WriteAllText(Path.Join(gameRoot, "test.xml"), "<root/>");

        var service = new EffectiveGameDataIndexService(
            new MegaFilesXmlIndexBuilder(),
            new FakeMegArchiveReader());

        // No MegaFiles.xml => just loose files
        var request = new EffectiveGameDataIndexRequest("profile", gameRoot);
        var result = service.Build(request);
        result.Files.Should().Contain(f => f.RelativePath == "test.xml" && f.SourceType == "game_loose");
    }

    [Fact]
    public void Build_ModPathLooseFiles_ShouldShadowGameLoose()
    {
        var gameRoot = Path.Join(_tempRoot, "game5");
        Directory.CreateDirectory(gameRoot);
        File.WriteAllText(Path.Join(gameRoot, "shared.xml"), "<game/>");

        var modPath = Path.Join(_tempRoot, "mod5");
        Directory.CreateDirectory(modPath);
        File.WriteAllText(Path.Join(modPath, "shared.xml"), "<mod/>");

        var service = new EffectiveGameDataIndexService(
            new MegaFilesXmlIndexBuilder(),
            new FakeMegArchiveReader());

        var request = new EffectiveGameDataIndexRequest("profile", gameRoot, modPath);
        var result = service.Build(request);

        var gameEntry = result.Files.FirstOrDefault(f => f.SourceType == "game_loose" && f.RelativePath == "shared.xml");
        var modEntry = result.Files.FirstOrDefault(f => f.SourceType == "mod_loose" && f.RelativePath == "shared.xml");

        gameEntry.Should().NotBeNull();
        gameEntry!.Active.Should().BeFalse();
        gameEntry.ShadowedBy.Should().NotBeNullOrWhiteSpace();

        modEntry.Should().NotBeNull();
        modEntry!.Active.Should().BeTrue();
    }

    [Fact]
    public void Build_ModPathDoesNotExist_ShouldAddDiagnostic()
    {
        var gameRoot = Path.Join(_tempRoot, "game6");
        Directory.CreateDirectory(gameRoot);

        var service = new EffectiveGameDataIndexService(
            new MegaFilesXmlIndexBuilder(),
            new FakeMegArchiveReader());

        var request = new EffectiveGameDataIndexRequest("profile", gameRoot, Path.Join(_tempRoot, "nonexistent_mod"));
        var result = service.Build(request);
        result.Diagnostics.Should().Contain(d => d.Contains("does not exist"));
    }

    [Fact]
    public void Build_GameRootDoesNotExist_ShouldAddDiagnostic()
    {
        var gameRoot = Path.Join(_tempRoot, "nonexistent_game");

        var service = new EffectiveGameDataIndexService(
            new MegaFilesXmlIndexBuilder(),
            new FakeMegArchiveReader());

        var request = new EffectiveGameDataIndexRequest("profile", gameRoot);
        var result = service.Build(request);
        result.Diagnostics.Should().Contain(d => d.Contains("does not exist"));
    }

    #endregion

    #region Build — MEG entries with valid archive

    [Fact]
    public void Build_ValidMegArchive_ShouldIncludeEntries()
    {
        var gameRoot = Path.Join(_tempRoot, "game7");
        var dataDir = Path.Join(gameRoot, "Data");
        Directory.CreateDirectory(dataDir);
        var megaFilesXml = @"<MegaFiles><File Name=""valid.meg"" /></MegaFiles>";
        File.WriteAllText(Path.Join(dataDir, "MegaFiles.xml"), megaFilesXml);
        File.WriteAllBytes(Path.Join(dataDir, "valid.meg"), new byte[] { 0x00 });

        var megEntries = new[]
        {
            new MegEntry("Data/units.xml", 0, 0, 100, 0),
            new MegEntry("Data/heroes.xml", 0, 1, 200, 100)
        };
        var archive = new MegArchive("valid.meg", "v1", megEntries, new byte[300], Array.Empty<string>());
        var fakeMeg = new FakeMegArchiveReader(archive);

        var service = new EffectiveGameDataIndexService(
            new MegaFilesXmlIndexBuilder(),
            fakeMeg);

        var request = new EffectiveGameDataIndexRequest("profile", gameRoot);
        var result = service.Build(request);
        result.Files.Should().Contain(f => f.RelativePath == "Data/units.xml" && f.SourceType == "meg_entry");
        result.Files.Should().Contain(f => f.RelativePath == "Data/heroes.xml" && f.SourceType == "meg_entry");
    }

    [Fact]
    public void Build_MegEntriesShadowedByLooseFiles_ShouldMarkInactive()
    {
        var gameRoot = Path.Join(_tempRoot, "game8");
        var dataDir = Path.Join(gameRoot, "Data");
        Directory.CreateDirectory(dataDir);
        var megaFilesXml = @"<MegaFiles><File Name=""base.meg"" /></MegaFiles>";
        File.WriteAllText(Path.Join(dataDir, "MegaFiles.xml"), megaFilesXml);
        File.WriteAllBytes(Path.Join(dataDir, "base.meg"), new byte[] { 0x00 });
        // Create a loose file that shadows the MEG entry
        File.WriteAllText(Path.Join(gameRoot, "units.xml"), "<loose/>");

        var megEntries = new[] { new MegEntry("units.xml", 0, 0, 100, 0) };
        var archive = new MegArchive("base.meg", "v1", megEntries, new byte[100], Array.Empty<string>());
        var fakeMeg = new FakeMegArchiveReader(archive);

        var service = new EffectiveGameDataIndexService(
            new MegaFilesXmlIndexBuilder(),
            fakeMeg);

        var request = new EffectiveGameDataIndexRequest("profile", gameRoot);
        var result = service.Build(request);

        var megEntry = result.Files.FirstOrDefault(f => f.SourceType == "meg_entry" && f.RelativePath == "units.xml");
        megEntry.Should().NotBeNull();
        megEntry!.Active.Should().BeFalse();
    }

    #endregion

    #region Build — default constructor

    [Fact]
    public void DefaultConstructor_ShouldNotThrow()
    {
        var service = new EffectiveGameDataIndexService();
        service.Should().NotBeNull();
    }

    #endregion

    #region Build — constructor null guards

    [Fact]
    public void Constructor_NullMegaFilesXmlIndexBuilder_ShouldThrow()
    {
        var act = () => new EffectiveGameDataIndexService(null!, new MegArchiveReader());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullMegArchiveReader_ShouldThrow()
    {
        var act = () => new EffectiveGameDataIndexService(new MegaFilesXmlIndexBuilder(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region Build — ResolveMegaPath with rooted path

    [Fact]
    public void Build_RootedMegPath_Exists_ShouldUseDirectly()
    {
        var gameRoot = Path.Join(_tempRoot, "game9");
        var dataDir = Path.Join(gameRoot, "Data");
        Directory.CreateDirectory(dataDir);

        var absoluteMegPath = Path.Join(_tempRoot, "absolute.meg");
        File.WriteAllBytes(absoluteMegPath, new byte[] { 0x00 });

        var megaFilesXml = $@"<MegaFiles><File Name=""{absoluteMegPath.Replace("\\", "\\\\")}"" /></MegaFiles>";
        File.WriteAllText(Path.Join(dataDir, "MegaFiles.xml"), megaFilesXml);

        var megEntries = new[] { new MegEntry("test.xml", 0, 0, 10, 0) };
        var archive = new MegArchive("abs.meg", "v1", megEntries, new byte[10], Array.Empty<string>());
        var fakeMeg = new FakeMegArchiveReader(archive);

        var service = new EffectiveGameDataIndexService(
            new MegaFilesXmlIndexBuilder(),
            fakeMeg);

        var request = new EffectiveGameDataIndexRequest("profile", gameRoot);
        var result = service.Build(request);
        result.Files.Should().Contain(f => f.RelativePath == "test.xml");
    }

    [Fact]
    public void Build_RootedMegPath_NotExists_ShouldAddDiagnostic()
    {
        var gameRoot = Path.Join(_tempRoot, "game10");
        var dataDir = Path.Join(gameRoot, "Data");
        Directory.CreateDirectory(dataDir);

        var megaFilesXml = @"<MegaFiles><File Name=""C:\nonexistent\path\test.meg"" /></MegaFiles>";
        File.WriteAllText(Path.Join(dataDir, "MegaFiles.xml"), megaFilesXml);

        var service = new EffectiveGameDataIndexService();
        var request = new EffectiveGameDataIndexRequest("profile", gameRoot);
        var result = service.Build(request);
        result.Diagnostics.Should().Contain(d => d.Contains("not found"));
    }

    #endregion

    #region Build — NormalizePath

    [Fact]
    public void Build_BackslashPaths_ShouldNormalize()
    {
        var gameRoot = Path.Join(_tempRoot, "game11");
        var subDir = Path.Join(gameRoot, "Sub");
        Directory.CreateDirectory(subDir);
        File.WriteAllText(Path.Join(subDir, "file.xml"), "<root/>");

        var service = new EffectiveGameDataIndexService(
            new MegaFilesXmlIndexBuilder(),
            new FakeMegArchiveReader());

        var request = new EffectiveGameDataIndexRequest("profile", gameRoot);
        var result = service.Build(request);
        result.Files.Should().Contain(f => f.RelativePath == "Sub/file.xml");
    }

    #endregion

    #region Stubs

    private sealed class FakeMegArchiveReader : IMegArchiveReader
    {
        private readonly MegArchive? _archive;

        public FakeMegArchiveReader(MegArchive? archive = null)
        {
            _archive = archive;
        }

        public MegOpenResult Open(string megPath)
        {
            if (_archive is not null)
            {
                return MegOpenResult.Success(_archive, Array.Empty<string>());
            }

            return new MegOpenResult(false, null, "parse_failed", "Fake parse failure", Array.Empty<string>());
        }

        public MegOpenResult Open(ReadOnlyMemory<byte> payload)
        {
            return Open(payload, "memory");
        }

        public MegOpenResult Open(ReadOnlyMemory<byte> payload, string sourceName)
        {
            if (_archive is not null)
            {
                return MegOpenResult.Success(_archive, Array.Empty<string>());
            }

            return new MegOpenResult(false, null, "parse_failed", "Fake parse failure", Array.Empty<string>());
        }
    }

    #endregion
}
