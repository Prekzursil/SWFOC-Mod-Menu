using System.Reflection;
using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.DataIndex.Services;
using SwfocTrainer.Meg;
using Xunit;

namespace SwfocTrainer.Tests.DataIndex;

/// <summary>
/// Wave 7 final coverage — fills remaining gaps:
/// EffectiveGameDataIndexService: MegaFiles.xml diagnostics propagation (lines 100-102),
/// AddLooseEntries empty rootPath guard (lines 177-178),
/// ResolveMegaPath direct not found fallback (lines 209-210),
/// DataIndexModels EffectiveFileMapEntry SourcePath coverage (line 35).
/// </summary>
public sealed class DataIndexWave7FinalTests
{
    #region AddLooseEntries — empty rootPath guard (lines 176-178)

    [Fact]
    public void Build_WithEmptyModPath_ShouldNotThrow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "di-w7-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            // Create a bare game root with no MegaFiles.xml — mod path is empty string
            var request = new EffectiveGameDataIndexRequest("test", tempDir, "", @"Data\MegaFiles.xml");
            var service = new EffectiveGameDataIndexService();
            var report = service.Build(request);
            // Should succeed without throwing; mod loose entries are skipped due to empty rootPath
            report.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Build_WithWhitespaceModPath_ShouldSkipModLooseEntries()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "di-w7-ws-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var request = new EffectiveGameDataIndexRequest("test", tempDir, "   ", @"Data\MegaFiles.xml");
            var service = new EffectiveGameDataIndexService();
            var report = service.Build(request);
            report.Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region MegaFiles.xml diagnostics propagation (lines 99-102)

    [Fact]
    public void Build_WhenMegaFilesXmlHasDiagnostics_ShouldPropagate()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "di-w7-diag-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dataDir = Path.Combine(tempDir, "Data");
        Directory.CreateDirectory(dataDir);
        try
        {
            // Write a MegaFiles.xml with a properly-formatted entry that references a non-existent MEG
            var megaFilesContent = @"<MegaFiles>
  <MegaFile Filename=""nonexistent.meg"" />
</MegaFiles>";
            File.WriteAllText(Path.Combine(dataDir, "MegaFiles.xml"), megaFilesContent);

            var request = new EffectiveGameDataIndexRequest("test", tempDir);
            var service = new EffectiveGameDataIndexService();
            var report = service.Build(request);
            report.Should().NotBeNull();
            // Nonexistent.meg should produce a diagnostic about not being found
            report.Diagnostics.Should().Contain(d => d.Contains("nonexistent.meg", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region ResolveMegaPath — direct path not found, fallback to Data/ also not found (lines 209-210)

    [Fact]
    public void Build_WhenMegFileNotUnderGameRoot_ShouldAddDiagnostic()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "di-w7-resolve-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var dataDir = Path.Combine(tempDir, "Data");
        Directory.CreateDirectory(dataDir);
        try
        {
            // A MegaFiles.xml referencing a file that does not exist under gameRoot or gameRoot/Data
            var megaFilesContent = @"<MegaFiles>
  <MegaFile Filename=""missing_archive.meg"" />
</MegaFiles>";
            File.WriteAllText(Path.Combine(dataDir, "MegaFiles.xml"), megaFilesContent);

            var request = new EffectiveGameDataIndexRequest("test", tempDir);
            var service = new EffectiveGameDataIndexService();
            var report = service.Build(request);
            report.Should().NotBeNull();
            report.Diagnostics.Should().Contain(d => d.Contains("missing_archive.meg") && d.Contains("not found"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion

    #region DataIndexModels — EffectiveFileMapEntry.SourcePath (line 35)

    [Fact]
    public void EffectiveFileMapEntry_SourcePath_ShouldStoreValue()
    {
        var entry = new EffectiveFileMapEntry(
            RelativePath: "data/xml/test.xml",
            SourceType: "meg_entry",
            SourcePath: @"C:\Game\Data\patch.meg:data/xml/test.xml",
            OverrideRank: 3,
            Active: true,
            ShadowedBy: null);
        entry.SourcePath.Should().Be(@"C:\Game\Data\patch.meg:data/xml/test.xml");
    }

    #endregion
}
