using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Savegame;
using Xunit;

namespace SwfocTrainer.Tests.Savegame;

/// <summary>
/// Pin tests for <see cref="ModHashValidator"/> (spec iter-290) — the mod-hash
/// validator + re-anchor engine. Covers the IEEE CRC-32 check vector, the
/// known-good mod match, three mod-mismatch causes (content drift, mod swap,
/// vanilla), the re-anchor round-trip, the no-embedded-hash and nested-chunk
/// paths, the surgical 4-byte re-anchor, the 100%-precision acceptance gate,
/// the deterministic file-set hash, and the pluggable-algorithm seam.
/// </summary>
[Trait("Category", "Savegame")]
public sealed class ModHashValidatorTests
{
    private const uint TestStructSize = SavegameParser.HeaderStructSize;
    private const string ExpectedLabel = "Forces of Corruption game";

    /// <summary>Offset, inside the 0x3E8 chunk body, of the type-0x01 mod-CRC payload.</summary>
    private const int ModCrcDataOffset = 17;

    /// <summary>Stand-in mod ObjectType XML — the mod a save was created under.</summary>
    private static readonly byte[] ModAlpha =
        "<UnitObject Name='Rebel_Frigate'><Tech_Level>3</Tech_Level></UnitObject>"u8.ToArray();

    /// <summary>The same mod with one ObjectType value edited — a content drift.</summary>
    private static readonly byte[] ModAlphaEdited =
        "<UnitObject Name='Rebel_Frigate'><Tech_Level>5</Tech_Level></UnitObject>"u8.ToArray();

    /// <summary>A completely different mod — a mod swap.</summary>
    private static readonly byte[] ModBravo =
        "<UnitObject Name='Empire_ISD'><Build_Cost_Credits>5000</Build_Cost_Credits></UnitObject>"u8
            .ToArray();

    [Fact]
    public void Crc32_MatchesIeeeCheckVector()
    {
        // The universal IEEE CRC-32 check value for ASCII "123456789".
        Crc32.Compute("123456789"u8).Should().Be(0xCBF43926u);
    }

    [Fact]
    public void Validate_ReportsMatch_WhenEmbeddedHashEqualsModHash()
    {
        var validator = new ModHashValidator();
        var modHash = validator.ComputeModHash(ModAlpha);
        var doc = SavegameDocument.Load(BuildSave(modHash));

        var result = validator.Validate(doc, ModAlpha);

        result.Status.Should().Be(ModHashStatus.Match);
        result.IsMatch.Should().BeTrue();
        result.EmbeddedHash.Should().Be(modHash);
        result.ComputedHash.Should().Be(modHash);
        result.MicroChunkIndex.Should().Be(3);
    }

    [Fact]
    public void Validate_ReportsMismatch_WhenModContentDrifted()
    {
        // The save was made under ModAlpha; the mod's XML has since been edited.
        var validator = new ModHashValidator();
        var doc = SavegameDocument.Load(BuildSave(validator.ComputeModHash(ModAlpha)));

        var result = validator.Validate(doc, ModAlphaEdited);

        result.Status.Should().Be(ModHashStatus.Mismatch);
        result.NeedsReAnchor.Should().BeTrue();
        result.EmbeddedHash.Should().NotBe(result.ComputedHash);
    }

    [Fact]
    public void Validate_ReportsMismatch_WhenModSwapped()
    {
        // The save was made under ModAlpha; a different mod is now loaded.
        var validator = new ModHashValidator();
        var doc = SavegameDocument.Load(BuildSave(validator.ComputeModHash(ModAlpha)));

        validator.Validate(doc, ModBravo).Status.Should().Be(ModHashStatus.Mismatch);
    }

    [Fact]
    public void Validate_ReportsMismatch_WhenValidatedAgainstVanillaEmptyMod()
    {
        // The save was made under ModAlpha; validated as if no mod (vanilla) were loaded.
        var validator = new ModHashValidator();
        var doc = SavegameDocument.Load(BuildSave(validator.ComputeModHash(ModAlpha)));

        validator.Validate(doc, Array.Empty<byte>()).Status.Should().Be(ModHashStatus.Mismatch);
    }

    [Fact]
    public void ReAnchor_ConvertsMismatchToMatch_AndRoundTrips()
    {
        var validator = new ModHashValidator();
        // The save embeds ModAlpha's hash; the live mod is now ModAlphaEdited.
        var doc = SavegameDocument.Load(BuildSave(validator.ComputeModHash(ModAlpha)));
        validator.Validate(doc, ModAlphaEdited).Status.Should().Be(ModHashStatus.Mismatch);

        var changed = validator.ReAnchor(doc, ModAlphaEdited);

        changed.Should().BeTrue();
        doc.IsDirty.Should().BeTrue();

        // The re-anchored save round-trips through the parser and now matches.
        var reloaded = SavegameDocument.Load(doc.Serialize());
        validator.Validate(reloaded, ModAlphaEdited).Status.Should().Be(ModHashStatus.Match);
    }

    [Fact]
    public void Validate_ReportsNoEmbeddedHash_WhenModContextChunkLacksCrcSlot()
    {
        var validator = new ModHashValidator();
        var doc = SavegameDocument.Load(BuildSaveWithoutModHash());

        var result = validator.Validate(doc, ModAlpha);

        result.Status.Should().Be(ModHashStatus.NoEmbeddedHash);
        result.MicroChunkIndex.Should().Be(-1);
        result.IsMatch.Should().BeFalse();
        result.NeedsReAnchor.Should().BeFalse();
    }

    [Fact]
    public void Validate_FindsModHash_WhenModContextChunkIsNested()
    {
        var validator = new ModHashValidator();
        var doc = SavegameDocument.Load(BuildNestedSave(validator.ComputeModHash(ModAlpha)));

        validator.Validate(doc, ModAlpha).Status.Should().Be(ModHashStatus.Match);
    }

    [Fact]
    public void ReAnchor_ReturnsFalse_WhenSaveAlreadyMatches()
    {
        var validator = new ModHashValidator();
        var doc = SavegameDocument.Load(BuildSave(validator.ComputeModHash(ModAlpha)));

        validator.ReAnchor(doc, ModAlpha).Should().BeFalse();
        doc.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void ReAnchor_ReturnsFalse_WhenSaveHasNoEmbeddedHash()
    {
        var validator = new ModHashValidator();
        var doc = SavegameDocument.Load(BuildSaveWithoutModHash());

        validator.ReAnchor(doc, ModAlpha).Should().BeFalse();
        doc.IsDirty.Should().BeFalse();
    }

    [Fact]
    public void ReAnchor_ChangesOnlyTheEmbeddedHashBytes_LeavingEveryOtherByteIdentical()
    {
        var validator = new ModHashValidator();
        var original = BuildSave(validator.ComputeModHash(ModAlpha));
        var doc = SavegameDocument.Load(original);

        validator.ReAnchor(doc, ModAlphaEdited).Should().BeTrue();
        var repaired = doc.Serialize();

        repaired.Length.Should().Be(original.Length);
        var crcStart = (int)TestStructSize + SaveChunk.HeaderSize + ModCrcDataOffset;
        var differingOffsets = Enumerable.Range(0, original.Length)
            .Where(i => original[i] != repaired[i])
            .ToList();
        // Every changed byte lies inside the 4-byte embedded-hash payload window.
        differingOffsets.Should().NotBeEmpty();
        differingOffsets.Should().OnlyContain(i => i >= crcStart && i < crcStart + sizeof(uint));
    }

    [Fact]
    public void Validate_HasNoFalsePositives_OnAKnownMatchingCorpus()
    {
        // Spec acceptance: 100% precision — no false positives on known-matching saves.
        var validator = new ModHashValidator();
        byte[][] corpus = { ModAlpha, ModAlphaEdited, ModBravo, Array.Empty<byte>() };

        foreach (var mod in corpus)
        {
            // A save whose embedded hash is, by construction, this mod's hash.
            var doc = SavegameDocument.Load(BuildSave(validator.ComputeModHash(mod)));
            validator.Validate(doc, mod).Status.Should().Be(ModHashStatus.Match);
        }
    }

    [Fact]
    public void TryGetEmbeddedHash_ReadsTheEmbeddedCrc_AndReportsAbsenceOtherwise()
    {
        var validator = new ModHashValidator();

        validator.TryGetEmbeddedHash(SavegameDocument.Load(BuildSave(0x8AF30372u)), out var embedded)
            .Should().BeTrue();
        embedded.Should().Be(0x8AF30372u);

        validator.TryGetEmbeddedHash(
                SavegameDocument.Load(BuildSaveWithoutModHash()), out _)
            .Should().BeFalse();
    }

    [Fact]
    public void ComputeModHashFromFiles_IsDeterministicRegardlessOfPathOrder()
    {
        var validator = new ModHashValidator();
        var dir = Directory.CreateTempSubdirectory("swfoc_modhash_");
        try
        {
            var unitsPath = Path.Combine(dir.FullName, "a_units.xml");
            var factionsPath = Path.Combine(dir.FullName, "b_factions.xml");
            File.WriteAllBytes(unitsPath, ModAlpha);
            File.WriteAllBytes(factionsPath, ModBravo);

            var forward = validator.ComputeModHashFromFiles(new[] { unitsPath, factionsPath });
            var reversed = validator.ComputeModHashFromFiles(new[] { factionsPath, unitsPath });

            forward.Should().Be(reversed);
            // Equivalent to hashing the ordinal-sorted concatenation a||b.
            forward.Should().Be(validator.ComputeModHash(Concat(ModAlpha, ModBravo)));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void ModHashValidator_UsesInjectedHashFunction_WhenProvided()
    {
        // A stand-in for a future RE-confirmed engine hash — a constant.
        const uint stubHash = 0xDEADBEEFu;
        var validator = new ModHashValidator(_ => stubHash);

        validator.ComputeModHash(ModAlpha).Should().Be(stubHash);

        // The save embeds stubHash; the stub computes stubHash for any mod → Match.
        var doc = SavegameDocument.Load(BuildSave(stubHash));
        validator.Validate(doc, ModBravo).Status.Should().Be(ModHashStatus.Match);
    }

    /// <summary>
    /// A flat save: the 0x3E8 mod-context chunk with its embedded mod CRC, a raw
    /// leaf, and an empty leaf — re-anchor must leave the latter two untouched.
    /// </summary>
    private static byte[] BuildSave(uint embeddedCrc)
    {
        var body = Build3E8Body(embeddedCrc);
        return Concat(
            BuildHeader(),
            ChunkHeader(0x3E8, (uint)body.Length), body,
            ChunkHeader(0x3E9, 1), new byte[] { 0xAB },
            ChunkHeader(0x3EA, 0));
    }

    /// <summary>A save with the 0x3E8 mod-context chunk nested inside a 0xAAA container.</summary>
    private static byte[] BuildNestedSave(uint embeddedCrc)
    {
        var body = Build3E8Body(embeddedCrc);
        var leaf = Concat(ChunkHeader(0x3E8, (uint)body.Length), body);
        return Concat(
            BuildHeader(),
            ChunkHeader(0xAAA, (uint)leaf.Length | SaveChunk.SubChunkFlag),
            leaf);
    }

    /// <summary>A save whose 0x3E8 chunk has micro-chunks but no type-0x01 mod-hash slot.</summary>
    private static byte[] BuildSaveWithoutModHash()
    {
        var body = new byte[]
        {
            0x05, 0x01, 0x00,                    // type 0x05 string blob
            0x00, 0x04, 0x61, 0x00, 0x00, 0x00,  // type 0x00 raw — no type-0x01 anywhere
        };
        return Concat(BuildHeader(), ChunkHeader(0x3E8, (uint)body.Length), body);
    }

    /// <summary>
    /// The 0x3E8 mod-context chunk body — 7 micro-chunks, types 5,4,3,1,2,0,6,
    /// mirroring the iter-289 fixture. The type-0x01 micro-chunk at index 3
    /// carries <paramref name="modCrc"/> as the embedded mod hash.
    /// </summary>
    private static byte[] Build3E8Body(uint modCrc)
    {
        var body = new byte[]
        {
            0x05, 0x01, 0x00,                    // idx 0 — type 0x05 string blob
            0x04, 0x04, 0x01, 0x00, 0x00, 0x00,  // idx 1 — type 0x04 int32
            0x03, 0x04, 0x01, 0x00, 0x00, 0x00,  // idx 2 — type 0x03 int32
            0x01, 0x04, 0x00, 0x00, 0x00, 0x00,  // idx 3 — type 0x01 int32 = embedded mod CRC
            0x02, 0x04, 0xE0, 0xD7, 0x7E, 0x40,  // idx 4 — type 0x02 int32
            0x00, 0x04, 0x61, 0x00, 0x00, 0x00,  // idx 5 — type 0x00 raw
            0x06, 0x04, 0xE4, 0x9C, 0x0C, 0x00,  // idx 6 — type 0x06 int array
        };
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(ModCrcDataOffset), modCrc);
        return body;
    }

    private static byte[] BuildHeader()
    {
        var header = new byte[TestStructSize];
        "RGMH"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(8), TestStructSize);
        for (var i = 0; i < 16; i++)
        {
            header[24 + i] = (byte)(i + 1);
        }

        Encoding.Unicode.GetBytes(ExpectedLabel).CopyTo(header, 40);
        return header;
    }

    private static byte[] ChunkHeader(uint id, uint rawSizeField)
    {
        var header = new byte[SaveChunk.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, id);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), rawSizeField);
        return header;
    }

    private static byte[] Concat(params byte[][] parts)
    {
        var result = new byte[parts.Sum(p => p.Length)];
        var pos = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, pos);
            pos += part.Length;
        }

        return result;
    }
}
