using System.Text;
using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.DataIndex.Services;
using Xunit;

namespace SwfocTrainer.Tests.DataIndex;

/// <summary>
/// Wave 2 coverage: fills remaining branches in EffectiveGameDataIndexService —
/// rooted MEG paths, Data/ subfolder fallback, ModPath loose files, and
/// shadowing mechanics.
/// </summary>
public sealed class DataIndexWave2CoverageTests
{
    [Fact]
    public void Build_ShouldResolveAbsoluteMegPath_WhenMegFileNameIsRooted()
    {
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-dataindex-w2-{Guid.NewGuid():N}");
        var gameRoot = Path.Join(tempRoot, "game");
        var megDir = Path.Join(tempRoot, "megs");
        Directory.CreateDirectory(Path.Join(gameRoot, "Data"));
        Directory.CreateDirectory(megDir);

        try
        {
            var absoluteMegPath = Path.Join(megDir, "Absolute.meg");
            File.WriteAllBytes(absoluteMegPath, BuildFormat2Archive(new[]
            {
                new FixtureEntry("Data/XML/Abs.xml", "<absolute />"u8.ToArray())
            }));

            File.WriteAllText(Path.Join(gameRoot, "Data", "MegaFiles.xml"), $"""
<MegaFiles>
  <MegaFile Name="{absoluteMegPath}" Enabled="true" />
</MegaFiles>
""");

            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot));

            report.Files.Should().Contain(x =>
                x.RelativePath.Equals("Data/XML/Abs.xml", StringComparison.OrdinalIgnoreCase) &&
                x.SourceType == "meg_entry");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Build_ShouldFallbackToDataSubfolder_WhenMegNotInRoot()
    {
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-dataindex-w2-{Guid.NewGuid():N}");
        var gameRoot = Path.Join(tempRoot, "game");
        Directory.CreateDirectory(Path.Join(gameRoot, "Data"));

        try
        {
            // MEG file only exists under Data/ subfolder, not directly in gameRoot
            File.WriteAllBytes(Path.Join(gameRoot, "Data", "FallbackMeg.meg"), BuildFormat2Archive(new[]
            {
                new FixtureEntry("Data/XML/Fallback.xml", "<fallback />"u8.ToArray())
            }));

            File.WriteAllText(Path.Join(gameRoot, "Data", "MegaFiles.xml"), """
<MegaFiles>
  <MegaFile Name="FallbackMeg.meg" Enabled="true" />
</MegaFiles>
""");

            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot));

            report.Files.Should().Contain(x =>
                x.RelativePath.Equals("Data/XML/Fallback.xml", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Build_ShouldReturnNull_WhenMegPathIsRootedAndDoesNotExist()
    {
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-dataindex-w2-{Guid.NewGuid():N}");
        var gameRoot = Path.Join(tempRoot, "game");
        Directory.CreateDirectory(Path.Join(gameRoot, "Data"));

        try
        {
            var missingAbsolutePath = Path.Join(tempRoot, "megs", "Missing.meg");
            File.WriteAllText(Path.Join(gameRoot, "Data", "MegaFiles.xml"), $"""
<MegaFiles>
  <MegaFile Name="{missingAbsolutePath}" Enabled="true" />
</MegaFiles>
""");

            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot));

            report.Diagnostics.Should().Contain(x => x.Contains("was not found"));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Build_ShouldHandleModPathWithOverlappingFiles()
    {
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-dataindex-w2-{Guid.NewGuid():N}");
        var gameRoot = Path.Join(tempRoot, "game");
        var modRoot = Path.Join(tempRoot, "mod");
        Directory.CreateDirectory(Path.Join(gameRoot, "Data", "XML"));
        Directory.CreateDirectory(Path.Join(modRoot, "Data", "XML"));

        try
        {
            File.WriteAllText(Path.Join(gameRoot, "Data", "MegaFiles.xml"), "<MegaFiles />");
            File.WriteAllText(Path.Join(gameRoot, "Data", "XML", "Shared.xml"), "<game />");
            File.WriteAllText(Path.Join(modRoot, "Data", "XML", "Shared.xml"), "<mod />");
            File.WriteAllText(Path.Join(modRoot, "Data", "XML", "ModOnly.xml"), "<mod_only />");

            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot,
                ModPath: modRoot));

            // Shared.xml should appear twice, mod version is active
            var sharedEntries = report.Files
                .Where(x => x.RelativePath.Contains("Shared.xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.OverrideRank)
                .ToArray();
            sharedEntries.Should().HaveCount(2);
            sharedEntries[0].Active.Should().BeFalse();
            sharedEntries[0].ShadowedBy.Should().NotBeNullOrWhiteSpace();
            sharedEntries[1].Active.Should().BeTrue();
            sharedEntries[1].SourceType.Should().Be("mod_loose");

            // ModOnly.xml should be active
            report.Files.Should().Contain(x =>
                x.RelativePath.Contains("ModOnly.xml", StringComparison.OrdinalIgnoreCase) &&
                x.Active &&
                x.SourceType == "mod_loose");
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Build_ShouldSkipModPath_WhenModPathIsWhitespace()
    {
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-dataindex-w2-{Guid.NewGuid():N}");
        var gameRoot = Path.Join(tempRoot, "game");
        Directory.CreateDirectory(Path.Join(gameRoot, "Data"));

        try
        {
            File.WriteAllText(Path.Join(gameRoot, "Data", "MegaFiles.xml"), "<MegaFiles />");
            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot,
                ModPath: "   "));

            report.Should().NotBeNull();
            report.Files.Where(x => x.SourceType == "mod_loose").Should().BeEmpty();
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Build_ShouldNormalizePaths_ReplacingBackslashes()
    {
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-dataindex-w2-{Guid.NewGuid():N}");
        var gameRoot = Path.Join(tempRoot, "game");
        Directory.CreateDirectory(Path.Join(gameRoot, "Data", "XML"));

        try
        {
            File.WriteAllText(Path.Join(gameRoot, "Data", "MegaFiles.xml"), "<MegaFiles />");
            File.WriteAllText(Path.Join(gameRoot, "Data", "XML", "Test.xml"), "<test />");

            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot));

            report.Files.Should().OnlyContain(x => !x.RelativePath.Contains('\\'));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Build_ShouldIncludeMegParseDiagnosticDetails()
    {
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-dataindex-w2-{Guid.NewGuid():N}");
        var gameRoot = Path.Join(tempRoot, "game");
        Directory.CreateDirectory(Path.Join(gameRoot, "Data"));

        try
        {
            File.WriteAllText(Path.Join(gameRoot, "Data", "MegaFiles.xml"), """
<MegaFiles>
  <MegaFile Name="Corrupt.meg" Enabled="true" />
</MegaFiles>
""");
            File.WriteAllBytes(Path.Join(gameRoot, "Data", "Corrupt.meg"), new byte[] { 0x99, 0x99, 0x99, 0x99, 0x01, 0x02, 0x03, 0x04 });

            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot));

            report.Diagnostics.Should().Contain(x => x.Contains("MEG parse failed"));
            report.Diagnostics.Should().Contain(x => x.Contains("MEG parse detail"));
        }
        finally
        {
            if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, recursive: true);
        }
    }

    private static byte[] BuildFormat2Archive(IReadOnlyList<FixtureEntry> entries)
    {
        var nameTable = BuildNameTable(entries);
        var fileTableOffset = 20 + nameTable.Length;
        var dataOffset = fileTableOffset + (entries.Count * 20);

        using var stream = new MemoryStream();
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
            writer.Write(0u);
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
        foreach (var encoded in entries.Select(entry => Encoding.ASCII.GetBytes(entry.Path)))
        {
            writer.Write((ushort)encoded.Length);
            writer.Write((ushort)0);
            writer.Write(encoded);
        }

        return stream.ToArray();
    }

    private sealed record FixtureEntry(string Path, byte[] Bytes);
}
