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

    [Fact]
    public void Build_ShouldResolveAbsoluteMegPaths_FromCustomMegaFilesXmlLocation()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-effective-index-absolute-meg-{Guid.NewGuid():N}");
        var gameRoot = Path.Combine(tempRoot, "game");
        var configRoot = Path.Combine(gameRoot, "Config");
        var absoluteMegPath = Path.Combine(tempRoot, "Absolute.meg");
        Directory.CreateDirectory(configRoot);

        try
        {
            File.WriteAllText(
                Path.Combine(configRoot, "MegaFiles.xml"),
                $"""
                <MegaFiles>
                  <MegaFile Name="{absoluteMegPath}" Enabled="true" />
                </MegaFiles>
                """);
            File.WriteAllBytes(
                absoluteMegPath,
                BuildFormat2Archive(
                [
                    new FixtureEntry("Data/XML/AbsoluteOnly.xml", "<absolute/>"u8.ToArray())
                ]));

            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot,
                ModPath: "   ",
                MegaFilesXmlRelativePath: Path.Combine("Config", "MegaFiles.xml")));

            report.Diagnostics.Should().BeEmpty();
            report.Files.Should().ContainSingle(x =>
                x.RelativePath == "Data/XML/AbsoluteOnly.xml" &&
                x.SourceType == "meg_entry" &&
                x.Active);
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
    public void Build_ShouldResolveMegFilesStoredAtGameRoot()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-effective-index-root-meg-{Guid.NewGuid():N}");
        var gameRoot = Path.Combine(tempRoot, "game");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Data"));

        try
        {
            File.WriteAllText(
                Path.Combine(gameRoot, "Data", "MegaFiles.xml"),
                """
                <MegaFiles>
                  <MegaFile Name="Root.meg" Enabled="true" />
                </MegaFiles>
                """);
            File.WriteAllBytes(
                Path.Combine(gameRoot, "Root.meg"),
                BuildFormat2Archive(
                [
                    new FixtureEntry("Data/XML/RootOnly.xml", "<root/>"u8.ToArray())
                ]));

            var report = new EffectiveGameDataIndexService().Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot));

            report.Diagnostics.Should().BeEmpty();
            report.Files.Should().ContainSingle(x =>
                x.RelativePath == "Data/XML/RootOnly.xml" &&
                x.SourcePath.Contains("/Root.meg:", StringComparison.OrdinalIgnoreCase));
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
    public void Build_ShouldNormalizeDotPrefixedMegEntryPaths()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-effective-index-dot-paths-{Guid.NewGuid():N}");
        var gameRoot = Path.Combine(tempRoot, "game");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Data"));

        try
        {
            File.WriteAllText(
                Path.Combine(gameRoot, "Data", "MegaFiles.xml"),
                """
                <MegaFiles>
                  <MegaFile Name="Root.meg" Enabled="true" />
                </MegaFiles>
                """);
            File.WriteAllBytes(
                Path.Combine(gameRoot, "Root.meg"),
                BuildFormat2Archive(
                [
                    new FixtureEntry("./Data\\XML\\Shadowed.xml", "<meg/>"u8.ToArray())
                ]));

            var report = new EffectiveGameDataIndexService().Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot));

            report.Diagnostics.Should().BeEmpty();
            report.Files.Should().ContainSingle(x =>
                x.RelativePath == "Data/XML/Shadowed.xml" &&
                x.SourceType == "meg_entry" &&
                x.SourcePath.EndsWith("Root.meg:./Data/XML/Shadowed.xml", StringComparison.OrdinalIgnoreCase));
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
    public void Build_ShouldPrefixMegaFilesXmlDiagnostics_AndStillIndexLooseFiles()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-effective-index-bad-xml-{Guid.NewGuid():N}");
        var gameRoot = Path.Combine(tempRoot, "game");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Data", "XML"));

        try
        {
            File.WriteAllText(Path.Combine(gameRoot, "Data", "MegaFiles.xml"), "<MegaFiles>");
            File.WriteAllText(Path.Combine(gameRoot, "Data", "XML", "LooseOnly.xml"), "<loose/>");

            var report = new EffectiveGameDataIndexService().Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot));

            report.Diagnostics.Should().ContainSingle(x => x.StartsWith("MegaFiles.xml:", StringComparison.OrdinalIgnoreCase));
            report.Files.Should().ContainSingle(x =>
                x.RelativePath == "Data/XML/LooseOnly.xml" &&
                x.SourceType == "game_loose" &&
                x.Active);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static byte[] BuildFormat2Archive(IReadOnlyList<FixtureEntry> entries)
    {
        var nameTable = BuildNameTable(entries);
        var fileTableOffset = 20 + nameTable.Length;
        var dataOffset = fileTableOffset + (entries.Count * 20);
        var totalDataBytes = entries.Sum(x => x.Bytes.Length);

        using var stream = new MemoryStream(capacity: dataOffset + totalDataBytes);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        writer.Write(0xFFFFFFFFu);
        writer.Write(0x3F7D70A4u);
        writer.Write((uint)dataOffset);
        writer.Write((uint)entries.Count);
        writer.Write((uint)entries.Count);
        writer.Write(nameTable);

        var cursor = dataOffset;
        for (var i = 0; i < entries.Count; i++)
        {
            writer.Write((uint)0);
            writer.Write((uint)i);
            writer.Write((uint)entries[i].Bytes.Length);
            writer.Write((uint)cursor);
            writer.Write((uint)i);
            cursor += entries[i].Bytes.Length;
        }

        foreach (var entry in entries)
        {
            writer.Write(entry.Bytes);
        }

        return stream.ToArray();
    }

    private static byte[] BuildNameTable(IReadOnlyList<FixtureEntry> entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        foreach (var entry in entries)
        {
            var encoded = Encoding.ASCII.GetBytes(entry.Path);
            writer.Write((ushort)encoded.Length);
            writer.Write((ushort)0);
            writer.Write(encoded);
        }

        return stream.ToArray();
    }

    private sealed record FixtureEntry(string Path, byte[] Bytes);
}
