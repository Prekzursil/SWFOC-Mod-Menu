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
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-effective-index-{Guid.NewGuid():N}");
        var gameRoot = Path.Join(tempRoot, "game");
        var modRoot = Path.Join(tempRoot, "mod");
        Directory.CreateDirectory(Path.Join(gameRoot, "Data"));
        Directory.CreateDirectory(Path.Join(gameRoot, "Data", "XML"));
        Directory.CreateDirectory(Path.Join(modRoot, "Data", "XML"));

        try
        {
            var megaFilesXmlPath = Path.Join(gameRoot, "Data", "MegaFiles.xml");
            File.WriteAllText(
                megaFilesXmlPath,
                """
                <MegaFiles>
                  <MegaFile Name="Base.meg" Enabled="true" />
                  <MegaFile Name="Patch.meg" Enabled="true" />
                </MegaFiles>
                """);

            File.WriteAllBytes(
                Path.Join(gameRoot, "Data", "Base.meg"),
                BuildFormat2Archive([
                    new FixtureEntry("Data/XML/Shared.xml", "<from-base-meg/>"u8.ToArray())
                ]));
            File.WriteAllBytes(
                Path.Join(gameRoot, "Data", "Patch.meg"),
                BuildFormat2Archive([
                    new FixtureEntry("Data/XML/Shared.xml", "<from-patch-meg/>"u8.ToArray()),
                    new FixtureEntry("Data/XML/PatchOnly.xml", "<patch-only/>"u8.ToArray())
                ]));

            File.WriteAllText(Path.Join(gameRoot, "Data", "XML", "Shared.xml"), "<from-game-loose/>");
            File.WriteAllText(Path.Join(modRoot, "Data", "XML", "Shared.xml"), "<from-mod-loose/>");

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
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-effective-index-{Guid.NewGuid():N}");
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

    [Fact]
    public void Build_ShouldThrow_WhenRequestIsNull()
    {
        var service = new EffectiveGameDataIndexService();
        var act = () => service.Build(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Build_ShouldReturnEmptyWithDiagnostic_WhenProfileIdIsBlank()
    {
        var service = new EffectiveGameDataIndexService();
        var report = service.Build(new EffectiveGameDataIndexRequest(
            ProfileId: "",
            GameRootPath: "C:/games"));

        report.Diagnostics.Should().Contain("profileId and gameRootPath are required.");
    }

    [Fact]
    public void Build_ShouldReturnEmptyWithDiagnostic_WhenGameRootPathIsBlank()
    {
        var service = new EffectiveGameDataIndexService();
        var report = service.Build(new EffectiveGameDataIndexRequest(
            ProfileId: "base_swfoc",
            GameRootPath: "  "));

        report.Diagnostics.Should().Contain("profileId and gameRootPath are required.");
    }

    [Fact]
    public void Build_ShouldSkipModPath_WhenModPathIsNull()
    {
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-effective-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: tempRoot,
                ModPath: null));

            report.Should().NotBeNull();
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
    public void Build_ShouldEmitDiagnostic_WhenLooseFileRootDoesNotExist()
    {
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-effective-index-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);

        try
        {
            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: tempRoot,
                ModPath: Path.Join(tempRoot, "nonexistent_mod_dir")));

            report.Diagnostics.Should().Contain(x => x.Contains("does not exist"));
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
    public void Build_ShouldEmitDiagnostic_WhenMegFileNotFound()
    {
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-effective-index-{Guid.NewGuid():N}");
        var gameRoot = Path.Join(tempRoot, "game");
        Directory.CreateDirectory(Path.Join(gameRoot, "Data"));

        try
        {
            File.WriteAllText(Path.Join(gameRoot, "Data", "MegaFiles.xml"), """
<MegaFiles>
  <MegaFile Name="Missing.meg" Enabled="true" />
</MegaFiles>
""");

            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot));

            report.Diagnostics.Should().Contain(x => x.Contains("Missing.meg") && x.Contains("was not found"));
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
    public void Build_ShouldEmitDiagnostic_WhenMegFileCannotBeParsed()
    {
        var tempRoot = Path.Join(Path.GetTempPath(), $"swfoc-effective-index-{Guid.NewGuid():N}");
        var gameRoot = Path.Join(tempRoot, "game");
        Directory.CreateDirectory(Path.Join(gameRoot, "Data"));

        try
        {
            File.WriteAllText(Path.Join(gameRoot, "Data", "MegaFiles.xml"), """
<MegaFiles>
  <MegaFile Name="Bad.meg" Enabled="true" />
</MegaFiles>
""");
            File.WriteAllBytes(Path.Join(gameRoot, "Data", "Bad.meg"), new byte[] { 0, 1, 2, 3 });

            var service = new EffectiveGameDataIndexService();
            var report = service.Build(new EffectiveGameDataIndexRequest(
                ProfileId: "base_swfoc",
                GameRootPath: gameRoot));

            report.Diagnostics.Should().Contain(x => x.Contains("MEG parse"));
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
    public void DefaultConstructor_ShouldWork()
    {
        var service = new EffectiveGameDataIndexService();
        service.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_ShouldThrow_WhenDependenciesAreNull()
    {
        var act1 = () => new EffectiveGameDataIndexService(null!, new SwfocTrainer.Meg.MegArchiveReader());
        var act2 = () => new EffectiveGameDataIndexService(new MegaFilesXmlIndexBuilder(), null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
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
