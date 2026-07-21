using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Savegame;
using Xunit;

namespace SwfocTrainer.Tests.Savegame;

/// <summary>
/// Pin tests for the <see cref="SavegameDocument"/> edit / write-back engine —
/// the spec iter-289 "read save → display chunks → edit/delete micro-chunks →
/// write back" core. Covers the round-trip identity guarantee, micro-chunk edit
/// and delete persistence, leaf size-field recomputation, size propagation up
/// through a parent container, and the malformed-save / wrong-chunk-kind guards.
/// </summary>
[Trait("Category", "Savegame")]
public sealed class SavegameDocumentTests
{
    private const uint TestStructSize = SavegameParser.HeaderStructSize;
    private const string ExpectedLabel = "Forces of Corruption game";

    /// <summary>The leaf body of chunk 0x3E8 — 7 micro-chunks, types 5,4,3,1,2,0,6.</summary>
    private static readonly byte[] Vanilla3E8Body =
    {
        0x05, 0x01, 0x00,                    // idx 0 — type 0x05 string blob, 1 data byte
        0x04, 0x04, 0x01, 0x00, 0x00, 0x00,  // idx 1 — type 0x04 int32
        0x03, 0x04, 0x01, 0x00, 0x00, 0x00,  // idx 2 — type 0x03 int32
        0x01, 0x04, 0x72, 0x03, 0xF3, 0x8A,  // idx 3 — type 0x01 int32 (mod CRC 0x8AF30372)
        0x02, 0x04, 0xE0, 0xD7, 0x7E, 0x40,  // idx 4 — type 0x02 int32
        0x00, 0x04, 0x61, 0x00, 0x00, 0x00,  // idx 5 — type 0x00 raw
        0x06, 0x04, 0xE4, 0x9C, 0x0C, 0x00,  // idx 6 — type 0x06 int array
    };

    [Fact]
    public void LoadThenSerialize_RoundTripsByteIdenticalForUneditedSave()
    {
        var save = BuildSimpleSave();

        var doc = SavegameDocument.Load(save);

        doc.IsDirty.Should().BeFalse();
        doc.Serialize().Should().Equal(save);
    }

    [Fact]
    public void SetMicroChunkInt32_PersistsThroughRoundTrip()
    {
        var doc = SavegameDocument.Load(BuildSimpleSave());
        var leaf = doc.Chunks.Single(c => c.Id == 0x3E8u);
        leaf.IsMicroLeaf.Should().BeTrue();

        leaf.SetMicroChunkInt32(3, unchecked((int)0x1234_5678u));
        doc.IsDirty.Should().BeTrue();

        var reloaded = SavegameDocument.Load(doc.Serialize());
        var reLeaf = reloaded.Chunks.Single(c => c.Id == 0x3E8u);
        reLeaf.MicroChunks.Should().HaveCount(7);
        reLeaf.MicroChunks[3].AsInt32().Should().Be(unchecked((int)0x1234_5678u));
    }

    [Fact]
    public void DeleteMicroChunk_RemovesItThroughRoundTrip()
    {
        var doc = SavegameDocument.Load(BuildSimpleSave());
        var leaf = doc.Chunks.Single(c => c.Id == 0x3E8u);

        leaf.DeleteMicroChunk(0);

        var reLeaf = SavegameDocument.Load(doc.Serialize()).Chunks.Single(c => c.Id == 0x3E8u);
        reLeaf.MicroChunks.Should().HaveCount(6);
        reLeaf.MicroChunks[0].TypeCode.Should().Be(0x04);
    }

    [Fact]
    public void DeleteMicroChunk_ShrinksLeafChunkSizeField()
    {
        var doc = SavegameDocument.Load(BuildSimpleSave());
        var leaf = doc.Chunks.Single(c => c.Id == 0x3E8u);
        var removedSize = leaf.MicroChunks[0].TotalSize;

        leaf.DeleteMicroChunk(0);

        var (_, reChunks) = SavegameParser.Parse(doc.Serialize());
        reChunks.Single(c => c.Id == 0x3E8u).DataSize
            .Should().Be(Vanilla3E8Body.Length - removedSize);
    }

    [Fact]
    public void EditNestedMicroChunk_PropagatesSizeFieldToParentContainer()
    {
        var save = BuildNestedSave();
        var originalContainerDataSize =
            SavegameParser.Parse(save).Chunks.Single(c => c.Id == 0xAAAu).DataSize;

        var doc = SavegameDocument.Load(save);
        var container = doc.Chunks.Single(c => c.Id == 0xAAAu);
        container.HasSubChunks.Should().BeTrue();
        var leaf = container.Children.Single(c => c.Id == 0x3E8u);

        // Replace the 1-data-byte micro-chunk with a 40-data-byte one — the
        // leaf body grows, and the container that wraps it must grow with it.
        leaf.SetMicroChunk(0, MicroChunk.Create(MicroChunk.TypeStringBlob, new byte[40]));

        var (_, reChunks) = SavegameParser.Parse(doc.Serialize());
        var reContainer = reChunks.Single(c => c.Id == 0xAAAu);
        var reLeaf = reContainer.Children.Single(c => c.Id == 0x3E8u);

        reContainer.HasSubChunks.Should().BeTrue();
        reContainer.DataSize.Should().Be(SaveChunk.HeaderSize + reLeaf.DataSize);
        reContainer.DataSize.Should().BeGreaterThan(originalContainerDataSize);
    }

    [Fact]
    public void Load_RejectsMalformedSave()
    {
        // Declares a 0x100-byte body but only 4 bytes follow the chunk header.
        var save = Concat(
            BuildHeader(),
            ChunkHeader(0x999, 0x100),
            new byte[] { 1, 2, 3, 4 });

        var act = () => SavegameDocument.Load(save);

        act.Should().Throw<SavegameFormatException>().WithMessage("*malformed*");
    }

    [Fact]
    public void DeleteMicroChunk_OnContainerChunk_Throws()
    {
        var doc = SavegameDocument.Load(BuildNestedSave());
        var container = doc.Chunks.Single(c => c.Id == 0xAAAu);

        var act = () => container.DeleteMicroChunk(0);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*not a micro-chunk leaf*");
    }

    [Fact]
    public void SetMicroChunkInt32_OnNonInt32TypeCode_Throws()
    {
        var doc = SavegameDocument.Load(BuildSimpleSave());
        var leaf = doc.Chunks.Single(c => c.Id == 0x3E8u);

        // Micro-chunk 0 is a type-0x05 string blob — not an int32 field.
        var act = () => leaf.SetMicroChunkInt32(0, 7);

        act.Should().Throw<InvalidOperationException>().WithMessage("*0x01-0x04*");
    }

    /// <summary>
    /// A flat save: the 0x3E8 micro-chunk leaf, a raw (non-micro) leaf, and an
    /// empty leaf — three chunk shapes the round-trip must preserve.
    /// </summary>
    private static byte[] BuildSimpleSave() => Concat(
        BuildHeader(),
        ChunkHeader(0x3E8, (uint)Vanilla3E8Body.Length), Vanilla3E8Body,
        ChunkHeader(0x3E9, 1), new byte[] { 0xAB },
        ChunkHeader(0x3EA, 0));

    /// <summary>A save with the 0x3E8 micro-chunk leaf nested inside a 0xAAA container.</summary>
    private static byte[] BuildNestedSave()
    {
        var leaf = Concat(ChunkHeader(0x3E8, (uint)Vanilla3E8Body.Length), Vanilla3E8Body);
        return Concat(
            BuildHeader(),
            ChunkHeader(0xAAA, (uint)leaf.Length | SaveChunk.SubChunkFlag),
            leaf);
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
        var total = parts.Sum(p => p.Length);
        var result = new byte[total];
        var pos = 0;
        foreach (var part in parts)
        {
            part.CopyTo(result, pos);
            pos += part.Length;
        }

        return result;
    }
}
