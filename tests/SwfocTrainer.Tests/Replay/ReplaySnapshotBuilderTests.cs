using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Pure unit tests for <see cref="ReplaySnapshotBuilder"/>. These tests
/// reconstruct the byte layout described in
/// <c>swfoc_lua_bridge/SNAPSHOT_FORMAT.md</c> and verify the builder writes
/// it exactly. They run without launching the replay binary so they can
/// catch encoding regressions during local development.
/// </summary>
public sealed class ReplaySnapshotBuilderTests
{
    [Fact]
    [Trait("Category", "Replay")]
    public void EncodeBytes_writes_canonical_magic_and_header()
    {
        var bytes = ReplaySnapshotBuilder.Create()
            .WithPlayer("REBEL", credits: 1.0, techLevel: 1)
            .WithObjects("TIE_Fighter", 1)
            .EncodeBytes();

        // Magic bytes (16): "SWFOCSNAPv1" + 5 nulls
        var magic = bytes[..16];
        Encoding.ASCII.GetString(magic[..11]).Should().Be("SWFOCSNAPv1");
        magic.AsSpan(11, 5).ToArray().Should().AllSatisfy(b => b.Should().Be(0));

        // format_version at 0x10
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x10, 4)).Should().Be(1u);

        // game_mode at 0x3C
        bytes[0x3C].Should().Be(1); // default galactic

        // First section starts at 0x44 with section id == 1
        BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0x44, 4)).Should().Be(1u);
    }

    [Fact]
    [Trait("Category", "Replay")]
    public void EncodeBytes_round_trips_through_a_minimal_reader()
    {
        var snap = ReplaySnapshotBuilder.Create()
            .WithPlayer("REBEL", credits: 5000.0, techLevel: 1)
            .WithPlayer("EMPIRE", credits: 10000.0, techLevel: 3)
            .WithPlayer("UNDERWORLD", credits: 12345.0, techLevel: 2)
            .WithLocalPlayerSlot(2)
            .WithObjects("TIE_Fighter", 12)
            .WithMetadata("mod_name", "snap_builder_unit_test")
            .EncodeBytes();

        // Verify CRC32 by recomputing over everything-before-the-end-marker-CRC.
        // Length structure: header (68) + sections + end marker (12). The CRC
        // covers all bytes except the trailing 4-byte CRC field.
        var crcPayload = snap.AsSpan(0, snap.Length - 4);
        var fileCrc = BinaryPrimitives.ReadUInt32LittleEndian(snap.AsSpan(snap.Length - 4, 4));
        var expected = SwfocTrainer.Saves.Checksum.Crc32.Compute(crcPayload);
        fileCrc.Should().Be(expected);

        // The local player should be at slot index 0 in the encoded
        // player_array because WithLocalPlayerSlot(2) reorders.
        var playerArrayOffset = 0x44 + 8 + 4; // header + section header + player_count
        var firstSlot = BinaryPrimitives.ReadUInt32LittleEndian(snap.AsSpan(playerArrayOffset, 4));
        firstSlot.Should().Be(2u, because: "WithLocalPlayerSlot(2) hoists slot=2 to player_array[0] so the replay reads UNDERWORLD as local");
    }

    [Fact]
    [Trait("Category", "Replay")]
    public void Build_writes_file_to_disk_and_returns_absolute_path()
    {
        var temp = Path.Combine(Path.GetTempPath(), $"swfoc_builder_test_{Guid.NewGuid():N}");
        try
        {
            var path = ReplaySnapshotBuilder.Create()
                .WithPlayer("REBEL", credits: 1.0, techLevel: 1)
                .Build(temp);

            File.Exists(path).Should().BeTrue();
            Path.IsPathFullyQualified(path).Should().BeTrue();
            new FileInfo(path).Length.Should().BeGreaterThan(80);
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    [Trait("Category", "Replay")]
    public void Build_rejects_null_or_empty_outputDir()
    {
        var b = ReplaySnapshotBuilder.Create().WithPlayer("REBEL", 1.0, 1);
        FluentActions.Invoking(() => b.Build(null!)).Should().Throw<ArgumentNullException>();
        FluentActions.Invoking(() => b.Build("   ")).Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("Category", "Replay")]
    public void EncodeBytes_writes_metadata_keys_with_lengths()
    {
        var bytes = ReplaySnapshotBuilder.Create()
            .WithPlayer("REBEL", 1.0, 1)
            .WithMetadata("mod_name", "phase9_test")
            .EncodeBytes();

        // The encoded snapshot should contain the literal "mod_name" and
        // "phase9_test" ASCII strings somewhere inside the metadata section.
        var ascii = Encoding.ASCII.GetString(bytes);
        ascii.Should().Contain("mod_name");
        ascii.Should().Contain("phase9_test");
    }
}
