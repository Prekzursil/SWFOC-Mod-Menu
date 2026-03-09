using System.Text;
using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.DataIndex.Services;
using Xunit;

namespace SwfocTrainer.Tests.DataIndex;

public sealed class EffectiveGameDataIndexAdditionalCoverageTests
{
    [Fact]
    public void Build_ShouldEmitDiagnostic_WhenRequiredRequestFieldsMissing()
    {
        var service = new EffectiveGameDataIndexService();

        var report = service.Build(new EffectiveGameDataIndexRequest(
            ProfileId: "",
            GameRootPath: ""));

        report.Files.Should().BeEmpty();
        report.Diagnostics.Should().ContainSingle("profileId and gameRootPath are required.");
    }

    [Fact]
    public void Build_ShouldEmitDiagnostics_ForMissingMegAndMissingLooseModRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-effective-index-missing-{Guid.NewGuid():N}");
        var gameRoot = Path.Combine(tempRoot, "game");
        var missingModRoot = Path.Combine(tempRoot, "missing-mod");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Data"));

        try
        {
            File.WriteAllText(
                Path.Combine(gameRoot, "Data", "MegaFiles.xml"),
                """
                <MegaFiles>
                  <MegaFile Name="Missing.meg" Enabled="true" />
                </MegaFiles>
                """);

            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot,
                ModPath: missingModRoot));

            report.Diagnostics.Should().Contain(x => x.Contains("MEG file 'Missing.meg' was not found", StringComparison.OrdinalIgnoreCase));
            report.Diagnostics.Should().Contain(x => x.Contains("Loose-file root", StringComparison.OrdinalIgnoreCase) && x.Contains("does not exist", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void Build_ShouldEmitDiagnostics_WhenMegParseFails()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-effective-index-invalid-meg-{Guid.NewGuid():N}");
        var gameRoot = Path.Combine(tempRoot, "game");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Data"));

        try
        {
            File.WriteAllText(
                Path.Combine(gameRoot, "Data", "MegaFiles.xml"),
                """
                <MegaFiles>
                  <MegaFile Name="Broken.meg" Enabled="true" />
                </MegaFiles>
                """);
            File.WriteAllBytes(Path.Combine(gameRoot, "Data", "Broken.meg"), Encoding.ASCII.GetBytes("not-a-valid-meg"));

            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot));

            report.Diagnostics.Should().Contain(x => x.Contains("MEG parse failed", StringComparison.OrdinalIgnoreCase));
            report.Diagnostics.Should().Contain(x => x.Contains("MEG parse detail", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
