using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Savegame;
using Xunit;

namespace SwfocTrainer.Tests.Savegame;

/// <summary>
/// Pin tests for <see cref="SavegameEditorTabViewModel"/> (spec iter-289 / iter-289b)
/// — the WPF savegame editor tab view-model over the <c>SwfocTrainer.Savegame</c>
/// engine. Covers buffer load + chunk-tree population, the malformed-save guard,
/// micro-chunk selection, structural diagnose on sound and corrupt saves, int32
/// and hex-byte micro-chunk edits surviving a round-trip, the invalid-edit
/// rejection, micro-chunk delete, corrupt-save fix + reload, and the embedded
/// mod-hash validate / re-anchor workflow.
/// </summary>
[Trait("Category", "Savegame")]
public sealed class SavegameEditorTabTests
{
    private const uint TestStructSize = SavegameParser.HeaderStructSize;
    private const string ExpectedLabel = "Forces of Corruption game";

    /// <summary>Stand-in mod ObjectType XML — the mod a save was created under.</summary>
    private static readonly byte[] ModAlpha =
        "<UnitObject Name='Rebel_Frigate'><Tech_Level>3</Tech_Level></UnitObject>"u8.ToArray();

    /// <summary>A completely different mod — a mod swap.</summary>
    private static readonly byte[] ModBravo =
        "<UnitObject Name='Empire_ISD'><Build_Cost_Credits>5000</Build_Cost_Credits></UnitObject>"u8
            .ToArray();

    [Fact]
    public async Task LoadFromBufferAsync_PopulatesChunkTreeAndHeader()
    {
        var vm = new SavegameEditorTabViewModel();

        await vm.LoadFromBufferAsync(BuildSimpleSave());

        vm.IsLoaded.Should().BeTrue();
        vm.Chunks.Should().HaveCount(3);
        vm.Chunks.Single(c => c.Id == 0x3E8u).Kind.Should().Be("micro-leaf");
        vm.Chunks.Single(c => c.Id == 0x3E8u).MicroChunkCount.Should().Be(7);
        vm.HeaderSummary.Should().Contain("RGMH").And.Contain(ExpectedLabel);
    }

    [Fact]
    public async Task LoadFromBufferAsync_MalformedSave_SetsErrorStatusWithoutThrowing()
    {
        var vm = new SavegameEditorTabViewModel();

        await vm.LoadFromBufferAsync(BuildMalformedSave());

        vm.IsLoaded.Should().BeFalse();
        vm.Chunks.Should().BeEmpty();
        vm.Status.Should().Contain("malformed");
    }

    [Fact]
    public async Task SelectedChunk_PopulatesMicroChunksForTheSelectedLeaf()
    {
        var vm = new SavegameEditorTabViewModel();
        await vm.LoadFromBufferAsync(BuildSimpleSave());

        vm.SelectedChunk = vm.Chunks.Single(c => c.Id == 0x3E8u);
        vm.MicroChunks.Should().HaveCount(7);
        vm.MicroChunks[3].IsInt32Field.Should().BeTrue();

        // Selecting a raw (non-micro) leaf clears the micro-chunk list.
        vm.SelectedChunk = vm.Chunks.Single(c => c.Id == 0x3E9u);
        vm.MicroChunks.Should().BeEmpty();
    }

    [Fact]
    public void DiagnoseBuffer_StructurallySoundSave_ReportsOk()
    {
        var vm = new SavegameEditorTabViewModel();

        vm.DiagnoseBuffer(BuildSimpleSave());

        vm.DiagnosisSummary.Should().StartWith("OK");
        vm.Status.Should().Contain("structurally sound");
    }

    [Fact]
    public void DiagnoseBuffer_CorruptSave_ReportsCorruptWithoutThrowing()
    {
        var vm = new SavegameEditorTabViewModel();

        vm.DiagnoseBuffer(BuildCorruptSave());

        vm.DiagnosisSummary.Should().StartWith("CORRUPT");
        vm.Status.Should().Contain("CORRUPT");
    }

    [Fact]
    public async Task ApplyEdit_Int32MicroChunk_PersistsThroughRoundTrip()
    {
        var vm = new SavegameEditorTabViewModel();
        await vm.LoadFromBufferAsync(BuildSimpleSave());
        vm.SelectedChunk = vm.Chunks.Single(c => c.Id == 0x3E8u);
        vm.SelectedMicroChunk = vm.MicroChunks[3];

        // The mod-CRC slot — a uint past int.MaxValue exercises the unchecked cast.
        vm.EditValue = "0xDEADBEEF";
        vm.ApplyEdit();

        vm.IsDirty.Should().BeTrue();
        var reloaded = SavegameDocument.Load(vm.SerializeCurrentDocument());
        var leaf = reloaded.Chunks.Single(c => c.Id == 0x3E8u);
        leaf.MicroChunks[3].AsInt32().Should().Be(unchecked((int)0xDEADBEEFu));
    }

    [Fact]
    public async Task ApplyEdit_NonInt32MicroChunk_AcceptsHexBytesAndPersists()
    {
        var vm = new SavegameEditorTabViewModel();
        await vm.LoadFromBufferAsync(BuildSimpleSave());
        vm.SelectedChunk = vm.Chunks.Single(c => c.Id == 0x3E8u);

        // Micro-chunk 0 is a type-0x05 string blob — edited as a hex byte string.
        vm.SelectedMicroChunk = vm.MicroChunks[0];
        vm.SelectedMicroChunk.IsInt32Field.Should().BeFalse();
        vm.EditValue = "CA FE BA BE";
        vm.ApplyEdit();

        var reloaded = SavegameDocument.Load(vm.SerializeCurrentDocument());
        var micro = reloaded.Chunks.Single(c => c.Id == 0x3E8u).MicroChunks[0];
        micro.TypeCode.Should().Be(MicroChunk.TypeStringBlob);
        micro.Data.Should().Equal(0xCA, 0xFE, 0xBA, 0xBE);
    }

    [Fact]
    public async Task ApplyEdit_InvalidInt32Value_IsRejectedAndDocumentStaysClean()
    {
        var vm = new SavegameEditorTabViewModel();
        await vm.LoadFromBufferAsync(BuildSimpleSave());
        vm.SelectedChunk = vm.Chunks.Single(c => c.Id == 0x3E8u);
        vm.SelectedMicroChunk = vm.MicroChunks[3];

        vm.EditValue = "not-a-number";
        vm.ApplyEdit();

        vm.IsDirty.Should().BeFalse();
        vm.Status.Should().Contain("not a valid int32");
    }

    [Fact]
    public async Task DeleteSelectedMicroChunk_RemovesItThroughRoundTrip()
    {
        var vm = new SavegameEditorTabViewModel();
        await vm.LoadFromBufferAsync(BuildSimpleSave());
        vm.SelectedChunk = vm.Chunks.Single(c => c.Id == 0x3E8u);
        vm.SelectedMicroChunk = vm.MicroChunks[0];

        vm.DeleteSelectedMicroChunk();

        vm.MicroChunks.Should().HaveCount(6);
        var reloaded = SavegameDocument.Load(vm.SerializeCurrentDocument());
        reloaded.Chunks.Single(c => c.Id == 0x3E8u).MicroChunks.Should().HaveCount(6);
    }

    [Fact]
    public async Task FixFromBufferAsync_CorruptSave_RecoversAndLoadsTheRepairedResult()
    {
        var vm = new SavegameEditorTabViewModel();

        await vm.FixFromBufferAsync(BuildCorruptSave());

        vm.LastFixRecovered.Should().BeTrue();
        vm.IsLoaded.Should().BeTrue();
        vm.Chunks.Should().ContainSingle().Which.Id.Should().Be(0x111L);
    }

    [Fact]
    public async Task ValidateModHash_MatchingMod_ReportsMatch()
    {
        var modHash = new ModHashValidator().ComputeModHash(ModAlpha);
        var vm = new SavegameEditorTabViewModel();
        await vm.LoadFromBufferAsync(BuildSave(modHash));

        vm.ValidateModHash(ModAlpha);

        vm.LastModHashOutcome.Should().Be("Match");
        vm.Status.Should().Contain("matches");
    }

    [Fact]
    public async Task ValidateModHash_SwappedMod_ReportsMismatch()
    {
        var modHash = new ModHashValidator().ComputeModHash(ModAlpha);
        var vm = new SavegameEditorTabViewModel();
        await vm.LoadFromBufferAsync(BuildSave(modHash));

        vm.ValidateModHash(ModBravo);

        vm.LastModHashOutcome.Should().Be("Mismatch");
        vm.Status.Should().Contain("MISMATCH");
    }

    [Fact]
    public async Task ReAnchorModHash_MismatchedSave_RewritesHashMarksDirtyThenValidatesAsMatch()
    {
        var modHash = new ModHashValidator().ComputeModHash(ModAlpha);
        var vm = new SavegameEditorTabViewModel();
        await vm.LoadFromBufferAsync(BuildSave(modHash));
        vm.ValidateModHash(ModBravo);
        vm.LastModHashOutcome.Should().Be("Mismatch");

        var changed = vm.ReAnchorModHash(ModBravo);

        changed.Should().BeTrue();
        vm.IsDirty.Should().BeTrue();

        // The re-anchored document round-trips and now matches the swapped mod.
        var reloaded = SavegameDocument.Load(vm.SerializeCurrentDocument());
        new ModHashValidator().Validate(reloaded, ModBravo).Status
            .Should().Be(ModHashStatus.Match);
    }

    [Fact]
    public async Task SerializeCurrentDocument_UneditedSave_RoundTripsByteIdentical()
    {
        var save = BuildSimpleSave();
        var vm = new SavegameEditorTabViewModel();
        await vm.LoadFromBufferAsync(save);

        vm.IsDirty.Should().BeFalse();
        vm.SerializeCurrentDocument().Should().Equal(save);
    }

    // ── fixtures ──────────────────────────────────────────────────

    /// <summary>A flat save: the 0x3E8 micro-chunk leaf, a raw leaf, and an empty leaf.</summary>
    private static byte[] BuildSimpleSave() => BuildSave(0x8AF30372u);

    /// <summary>A flat save whose 0x3E8 chunk embeds <paramref name="embeddedCrc"/>.</summary>
    private static byte[] BuildSave(uint embeddedCrc)
    {
        var body = Build3E8Body(embeddedCrc);
        return Concat(
            BuildHeader(),
            ChunkHeader(0x3E8, (uint)body.Length), body,
            ChunkHeader(0x3E9, 1), new byte[] { 0xAB },
            ChunkHeader(0x3EA, 0));
    }

    /// <summary>A save with a chunk whose declared body overflows the buffer.</summary>
    private static byte[] BuildMalformedSave() => Concat(
        BuildHeader(),
        ChunkHeader(0x999, 0x100),
        new byte[] { 1, 2, 3, 4 });

    /// <summary>A save with one clean chunk followed by a damaged, overflowing chunk.</summary>
    private static byte[] BuildCorruptSave() => Concat(
        BuildHeader(),
        ChunkHeader(0x111, 1), new byte[] { 0xAB },
        ChunkHeader(0x222, 0x1000), new byte[] { 1, 2, 3, 4 });

    /// <summary>
    /// The 0x3E8 mod-context chunk body — 7 micro-chunks, types 5,4,3,1,2,0,6.
    /// The type-0x01 micro-chunk at index 3 carries <paramref name="modCrc"/>.
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
        BinaryPrimitives.WriteUInt32LittleEndian(body.AsSpan(17), modCrc);
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
