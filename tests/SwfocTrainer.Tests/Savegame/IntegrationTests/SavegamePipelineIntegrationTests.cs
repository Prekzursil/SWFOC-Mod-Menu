using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Savegame;
using Xunit;

namespace SwfocTrainer.Tests.Savegame.IntegrationTests;

/// <summary>
/// End-to-end pipeline regression cases for spec iter-291. Each case runs a
/// corrupt savegame buffer through the full four-stage chain the spec calls
/// out — <see cref="SavegameFixer"/> recovery → <see cref="SavegameParser"/>
/// re-validation → <see cref="SavegameDocument"/> open + mutate + write back →
/// <see cref="SavegameParser"/> re-verify — with one case adding the
/// <see cref="ModHashValidator"/> stage so all four stages of the spec's
/// "parser → fixer → editor → mod-hash validator" acceptance chain are
/// exercised together.
///
/// <para>
/// The spec describes each case as "a corrupted input fixture + expected
/// post-fix chunk inventory + expected post-edit chunk inventory". Because the
/// entire savegame test corpus is synthetic and in-memory (there is no
/// physical <c>Fixtures/</c> directory), the expected inventories are captured
/// in-code as explicit <see cref="SavegameInventory"/> records rather than
/// JSON sidecars — the same evidence, with no file-copy indirection and
/// consistent with the five sibling savegame test files.
/// </para>
/// </summary>
[Trait("Category", "Savegame")]
public sealed class SavegamePipelineIntegrationTests
{
    private const uint TestStructSize = SavegameParser.HeaderStructSize;
    private const string ExpectedLabel = "Forces of Corruption game";

    /// <summary>Offset, inside a 0x3E8 mod-context body, of the type-0x01 mod-CRC payload.</summary>
    private const int ModCrcDataOffset = 17;

    /// <summary>The vanilla 0x3E8 leaf body — 7 micro-chunks, types 5,4,3,1,2,0,6.</summary>
    private static readonly byte[] Vanilla3E8Body =
    {
        0x05, 0x01, 0x00,
        0x04, 0x04, 0x01, 0x00, 0x00, 0x00,
        0x03, 0x04, 0x01, 0x00, 0x00, 0x00,
        0x01, 0x04, 0x72, 0x03, 0xF3, 0x8A,
        0x02, 0x04, 0xE0, 0xD7, 0x7E, 0x40,
        0x00, 0x04, 0x61, 0x00, 0x00, 0x00,
        0x06, 0x04, 0xE4, 0x9C, 0x0C, 0x00,
    };

    /// <summary>Stand-in mod ObjectType XML — the mod a save was created under.</summary>
    private static readonly byte[] ModAlpha =
        "<UnitObject Name='Rebel_Frigate'><Tech_Level>3</Tech_Level></UnitObject>"u8.ToArray();

    /// <summary>The same mod with one ObjectType value edited — a content drift.</summary>
    private static readonly byte[] ModAlphaEdited =
        "<UnitObject Name='Rebel_Frigate'><Tech_Level>5</Tech_Level></UnitObject>"u8.ToArray();

    [Fact]
    public void TruncatedFinalChunk_FixStrips_MicroChunkEditRoundTrips()
    {
        // Stage 1-2 — recover the truncated trailing chunk, re-validate.
        var corrupt = Concat(
            BuildHeader(),
            Leaf(0x3E8, Vanilla3E8Body),
            Leaf(0x3E9, 9, 9),
            Truncated(0x3EA, declaredBody: 0x400, actualBody: 4));

        var fix = SavegameFixer.Fix(corrupt);

        fix.Recovered.Should().BeTrue();
        fix.Strategy.Should().Be(SavegameFixStrategy.StripBadChunks);
        fix.DroppedChunkCount.Should().Be(1);
        Inventory(fix.Output).Should().Be(
            new SavegameInventory(true, false, 2, 2, "0x000003E8, 0x000003E9"));

        // Stage 3 — open the recovered save, edit a micro-chunk, write it back.
        var doc = SavegameDocument.Load(fix.Output);
        doc.Chunks.Single(c => c.Id == 0x3E8u)
            .SetMicroChunkInt32(3, unchecked((int)0xC0FFEE11u));
        doc.IsDirty.Should().BeTrue();
        var edited = doc.Serialize();

        // Stage 4 — re-verify: structure intact, edit persisted.
        Inventory(edited).Should().Be(
            new SavegameInventory(true, false, 2, 2, "0x000003E8, 0x000003E9"));
        SavegameDocument.Load(edited).Chunks.Single(c => c.Id == 0x3E8u)
            .MicroChunks[3].AsInt32().Should().Be(unchecked((int)0xC0FFEE11u));
    }

    [Fact]
    public void MalformedChunkHeader_FixStrips_MicroChunkEditRoundTrips()
    {
        // A garbage-large size field — the second of the spec's three corruption
        // types — is flagged the same way a truncation is: an overflow.
        var corrupt = Concat(
            BuildHeader(),
            Leaf(0x3E8, Vanilla3E8Body),
            Leaf(0x3E9, 2, 2),
            Truncated(0x3EA, declaredBody: 0xFFFFF, actualBody: 4));

        var fix = SavegameFixer.Fix(corrupt);

        fix.Recovered.Should().BeTrue();
        fix.Strategy.Should().Be(SavegameFixStrategy.StripBadChunks);
        fix.DroppedChunkCount.Should().Be(1);
        Inventory(fix.Output).Should().Be(
            new SavegameInventory(true, false, 2, 2, "0x000003E8, 0x000003E9"));

        var doc = SavegameDocument.Load(fix.Output);
        doc.Chunks.Single(c => c.Id == 0x3E8u).SetMicroChunkInt32(1, 0x2A);
        var edited = doc.Serialize();

        Inventory(edited).Should().Be(
            new SavegameInventory(true, false, 2, 2, "0x000003E8, 0x000003E9"));
        SavegameDocument.Load(edited).Chunks.Single(c => c.Id == 0x3E8u)
            .MicroChunks[1].AsInt32().Should().Be(0x2A);
    }

    [Fact]
    public void DamagedMidContainer_FixKeepsTrailingLeaves_EditRoundTrips()
    {
        // The damaged container sits between clean leaves — strip-bad-chunk
        // keeps the trailing leaves a truncate would have lost.
        var corrupt = Concat(
            BuildHeader(),
            Leaf(0x3E8, Vanilla3E8Body),
            Container(0x3E9, Truncated(0x111, declaredBody: 0x300, actualBody: 4)),
            Leaf(0x200, 7, 7),
            Leaf(0x201, 8, 8));

        var fix = SavegameFixer.Fix(corrupt);

        fix.Recovered.Should().BeTrue();
        fix.Strategy.Should().Be(SavegameFixStrategy.StripBadChunks);
        fix.DroppedChunkCount.Should().Be(1);
        Inventory(fix.Output).Should().Be(
            new SavegameInventory(true, false, 3, 3, "0x00000200, 0x00000201, 0x000003E8"));

        var doc = SavegameDocument.Load(fix.Output);
        doc.Chunks.Single(c => c.Id == 0x3E8u).SetMicroChunkInt32(2, 0x5151);
        var edited = doc.Serialize();

        Inventory(edited).Should().Be(
            new SavegameInventory(true, false, 3, 3, "0x00000200, 0x00000201, 0x000003E8"));
        SavegameDocument.Load(edited).Chunks.Single(c => c.Id == 0x3E8u)
            .MicroChunks[2].AsInt32().Should().Be(0x5151);
    }

    [Fact]
    public void AlreadyCleanSave_FixIsNoOp_MicroChunkDeleteRoundTrips()
    {
        // A structurally sound save passes the fixer through untouched, then the
        // editor deletes a micro-chunk and the deletion survives the round-trip.
        var clean = Concat(
            BuildHeader(),
            Leaf(0x3E8, Vanilla3E8Body),
            Leaf(0x3E9, 0xAB));

        var fix = SavegameFixer.Fix(clean);

        fix.Recovered.Should().BeTrue();
        fix.Strategy.Should().Be(SavegameFixStrategy.None);
        fix.DroppedChunkCount.Should().Be(0);
        fix.Output.Should().BeSameAs(clean);
        Inventory(fix.Output).Should().Be(
            new SavegameInventory(true, false, 2, 2, "0x000003E8, 0x000003E9"));

        var doc = SavegameDocument.Load(fix.Output);
        doc.Chunks.Single(c => c.Id == 0x3E8u).DeleteMicroChunk(0);
        var edited = doc.Serialize();

        // The chunk inventory is unchanged — a micro-chunk delete shrinks a leaf
        // body, it does not add or remove a chunk.
        Inventory(edited).Should().Be(
            new SavegameInventory(true, false, 2, 2, "0x000003E8, 0x000003E9"));
        var reLeaf = SavegameDocument.Load(edited).Chunks.Single(c => c.Id == 0x3E8u);
        reLeaf.MicroChunks.Should().HaveCount(6);
        reLeaf.MicroChunks[0].TypeCode.Should().Be(0x04);
    }

    [Fact]
    public void ModContextSave_FixThenValidateThenReAnchor_AllFourStagesGreen()
    {
        // The spec's full acceptance chain: parser → fixer → editor → mod-hash
        // validator, all four stages green on one fixture.
        var validator = new ModHashValidator();
        var modBody = Build3E8Body(validator.ComputeModHash(ModAlpha));
        var corrupt = Concat(
            BuildHeader(),
            Leaf(0x3E8, modBody),
            Truncated(0x999, declaredBody: 0x400, actualBody: 4));

        // Stage 1-2 — fixer recovers the mod-context chunk, parser re-validates.
        var fix = SavegameFixer.Fix(corrupt);
        fix.Recovered.Should().BeTrue();
        fix.Strategy.Should().Be(SavegameFixStrategy.StripBadChunks);
        fix.DroppedChunkCount.Should().Be(1);
        Inventory(fix.Output).Should().Be(
            new SavegameInventory(true, false, 1, 1, "0x000003E8"));

        // Stage 3 — editor opens the recovered save.
        var doc = SavegameDocument.Load(fix.Output);

        // Stage 4 — mod-hash validator flags the drifted mod, then re-anchors it.
        var before = validator.Validate(doc, ModAlphaEdited);
        before.Status.Should().Be(ModHashStatus.Mismatch);
        before.NeedsReAnchor.Should().BeTrue();
        before.EmbeddedHash.Should().NotBe(before.ComputedHash);

        validator.ReAnchor(doc, ModAlphaEdited).Should().BeTrue();
        doc.IsDirty.Should().BeTrue();
        var repaired = doc.Serialize();

        Inventory(repaired).Should().Be(
            new SavegameInventory(true, false, 1, 1, "0x000003E8"));
        validator.Validate(SavegameDocument.Load(repaired), ModAlphaEdited)
            .Status.Should().Be(ModHashStatus.Match);
    }

    [Fact]
    public void BmpThumbnailSave_FixPreservesThumbnail_EditRoundTrips()
    {
        // The BMP thumbnail prefix must survive both the fixer and the editor.
        var corrupt = Concat(
            BuildHeader(),
            BmpBlock(64),
            Leaf(0x3E8, Vanilla3E8Body),
            Leaf(0x3E9, 9, 9),
            Truncated(0x3EA, declaredBody: 0x400, actualBody: 4));

        var fix = SavegameFixer.Fix(corrupt);

        fix.Recovered.Should().BeTrue();
        fix.DroppedChunkCount.Should().Be(1);
        SavegameParser.ParseHeader(fix.Output).HasBmpThumbnail.Should().BeTrue();
        Inventory(fix.Output).Should().Be(
            new SavegameInventory(true, false, 2, 2, "0x000003E8, 0x000003E9"));

        var doc = SavegameDocument.Load(fix.Output);
        doc.Header.HasBmpThumbnail.Should().BeTrue();
        doc.Chunks.Single(c => c.Id == 0x3E8u).SetMicroChunkInt32(4, 0x77);
        var edited = doc.Serialize();

        SavegameParser.ParseHeader(edited).HasBmpThumbnail.Should().BeTrue();
        var reloaded = SavegameDocument.Load(edited);
        reloaded.Header.HasBmpThumbnail.Should().BeTrue();
        reloaded.Chunks.Single(c => c.Id == 0x3E8u)
            .MicroChunks[4].AsInt32().Should().Be(0x77);
    }

    [Fact]
    public void NestedContainerSurvivesFix_NestedMicroChunkEdit_SizePropagates()
    {
        // A clean nested container survives the fixer; growing a micro-chunk in
        // its child leaf must propagate the new size up to the container.
        var corrupt = Concat(
            BuildHeader(),
            Container(0xAAA, Leaf(0x3E8, Vanilla3E8Body)),
            Truncated(0xBBB, declaredBody: 0x800, actualBody: 4));

        var fix = SavegameFixer.Fix(corrupt);

        fix.Recovered.Should().BeTrue();
        fix.Strategy.Should().Be(SavegameFixStrategy.StripBadChunks);
        fix.DroppedChunkCount.Should().Be(1);
        Inventory(fix.Output).Should().Be(
            new SavegameInventory(true, false, 1, 2, "0x000003E8, 0x00000AAA"));

        var doc = SavegameDocument.Load(fix.Output);
        var container = doc.Chunks.Single(c => c.Id == 0xAAAu);
        container.HasSubChunks.Should().BeTrue();
        container.Children.Single(c => c.Id == 0x3E8u)
            .SetMicroChunk(0, MicroChunk.Create(MicroChunk.TypeStringBlob, new byte[40]));
        var edited = doc.Serialize();

        Inventory(edited).Should().Be(
            new SavegameInventory(true, false, 1, 2, "0x000003E8, 0x00000AAA"));
        var (_, reChunks) = SavegameParser.Parse(edited);
        var reContainer = reChunks.Single(c => c.Id == 0xAAAu);
        var reLeaf = reContainer.Children.Single(c => c.Id == 0x3E8u);
        reContainer.DataSize.Should().Be(SaveChunk.HeaderSize + reLeaf.DataSize);
        reLeaf.MicroChunks[0].Data.Length.Should().Be(40);
    }

    [Fact]
    public void DeepTruncation_FixRecovers_DeleteAndEditCombined_RoundTrips()
    {
        // A deep truncation, then the editor both deletes and edits micro-chunks
        // in the same leaf — two mutations must survive one round-trip.
        var corrupt = Concat(
            BuildHeader(),
            Leaf(0x3E8, Vanilla3E8Body),
            Leaf(0x10, 1),
            Leaf(0x20, 2),
            Truncated(0x30, declaredBody: 0x1000, actualBody: 8));

        var fix = SavegameFixer.Fix(corrupt);

        fix.Recovered.Should().BeTrue();
        fix.Strategy.Should().Be(SavegameFixStrategy.StripBadChunks);
        fix.DroppedChunkCount.Should().Be(1);
        Inventory(fix.Output).Should().Be(
            new SavegameInventory(true, false, 3, 3, "0x00000010, 0x00000020, 0x000003E8"));

        var doc = SavegameDocument.Load(fix.Output);
        var leaf = doc.Chunks.Single(c => c.Id == 0x3E8u);
        leaf.DeleteMicroChunk(6);
        leaf.SetMicroChunkInt32(3, unchecked((int)0xABCD1234u));
        var edited = doc.Serialize();

        Inventory(edited).Should().Be(
            new SavegameInventory(true, false, 3, 3, "0x00000010, 0x00000020, 0x000003E8"));
        var reLeaf = SavegameDocument.Load(edited).Chunks.Single(c => c.Id == 0x3E8u);
        reLeaf.MicroChunks.Should().HaveCount(6);
        reLeaf.MicroChunks[3].AsInt32().Should().Be(unchecked((int)0xABCD1234u));
    }

    /// <summary>
    /// A chunk-level inventory of a savegame buffer — the in-code form of the
    /// spec's "expected post-fix / post-edit chunk inventory".
    /// </summary>
    private sealed record SavegameInventory(
        bool Parsed, bool HasOverflow, int TopCount, int TotalCount, string UniqueIds);

    private static SavegameInventory Inventory(byte[] save)
    {
        var report = SavegameParser.Diagnose(save);
        return new SavegameInventory(
            report.Parsed,
            report.HasOverflow,
            report.TopChunkCount,
            report.TotalChunkCount,
            string.Join(", ", report.UniqueChunkIds));
    }

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

    private static byte[] BmpBlock(int totalSize)
    {
        var block = new byte[totalSize];
        "BM"u8.CopyTo(block);
        BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(2), (uint)totalSize);
        return block;
    }

    private static byte[] ChunkHeader(uint id, uint rawSizeField)
    {
        var header = new byte[SaveChunk.HeaderSize];
        BinaryPrimitives.WriteUInt32LittleEndian(header, id);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(4), rawSizeField);
        return header;
    }

    private static byte[] Leaf(uint id, params byte[] body) =>
        Concat(ChunkHeader(id, (uint)body.Length), body);

    private static byte[] Truncated(uint id, uint declaredBody, int actualBody) =>
        Concat(ChunkHeader(id, declaredBody), new byte[actualBody]);

    private static byte[] Container(uint id, params byte[][] children)
    {
        var body = Concat(children);
        return Concat(ChunkHeader(id, (uint)body.Length | SaveChunk.SubChunkFlag), body);
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
