using System.Text;
using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.DataIndex.Services;
using Xunit;

namespace SwfocTrainer.Tests.DataIndex;

public sealed class EffectiveGameDataIndexServiceTests
{
    [Fact]
    public void Build_ShouldRespectModGameMegPrecedence_AndTrackShadowing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-effective-index-{Guid.NewGuid():N}");
        var gameRoot = Path.Combine(tempRoot, "game");
        var modRoot = Path.Combine(tempRoot, "mod");
        Directory.CreateDirectory(Path.Combine(gameRoot, "Data"));
        Directory.CreateDirectory(Path.Combine(gameRoot, "Data", "XML"));
        Directory.CreateDirectory(Path.Combine(modRoot, "Data", "XML"));

        try
        {
            var megaFilesXmlPath = Path.Combine(gameRoot, "Data", "MegaFiles.xml");
            File.WriteAllText(
                megaFilesXmlPath,
                """
                <MegaFiles>
                  <MegaFile Name="Base.meg" Enabled="true" />
                  <MegaFile Name="Patch.meg" Enabled="true" />
                </MegaFiles>
                """);

            File.WriteAllBytes(
                Path.Combine(gameRoot, "Data", "Base.meg"),
                BuildFormat2Archive([
                    new FixtureEntry("Data/XML/Shared.xml", "<from-base-meg/>"u8.ToArray())
                ]));
            File.WriteAllBytes(
                Path.Combine(gameRoot, "Data", "Patch.meg"),
                BuildFormat2Archive([
                    new FixtureEntry("Data/XML/Shared.xml", "<from-patch-meg/>"u8.ToArray()),
                    new FixtureEntry("Data/XML/PatchOnly.xml", "<patch-only/>"u8.ToArray())
                ]));

            File.WriteAllText(Path.Combine(gameRoot, "Data", "XML", "Shared.xml"), "<from-game-loose/>");
            File.WriteAllText(Path.Combine(modRoot, "Data", "XML", "Shared.xml"), "<from-mod-loose/>");

            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot,
                ModPath: modRoot));

            report.Diagnostics.Should().BeEmpty();
            report.Files.Should().NotBeEmpty();

            var entries = report.Files
                .Where(x => x.RelativePath.Equals("Data/XML/Shared.xml", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.OverrideRank)
                .ToArray();
            entries.Should().HaveCount(4, "same relative path exists in two MEGs, game loose files, and MODPATH");
            entries.Last().Active.Should().BeTrue();
            entries.Last().SourceType.Should().Be("mod_loose");
            entries.Last().ShadowedBy.Should().BeNull();
            entries[0].SourceType.Should().Be("meg_entry");
            entries[0].ShadowedBy.Should().NotBeNullOrWhiteSpace();

            report.Files.Should().Contain(x =>
                x.RelativePath.Equals("Data/XML/PatchOnly.xml", StringComparison.OrdinalIgnoreCase) &&
                x.Active &&
                x.SourceType == "meg_entry");
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
    public void Build_ShouldEmitDiagnostic_WhenMegaFilesXmlIsMissing()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-effective-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: tempRoot));

            report.Diagnostics.Should().ContainSingle(x => x.Contains("MegaFiles.xml not found", StringComparison.OrdinalIgnoreCase));
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
