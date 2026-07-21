using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using SwfocTrainer.Savegame;
using Xunit;

namespace SwfocTrainer.Tests.Savegame;

/// <summary>
/// Pin tests for <see cref="SavegameFixer"/>, the spec iter-288 corruption
/// recovery layer. Exercises strip-bad-chunk, truncate-at-failure, and
/// selective micro-chunk drop against a synthetic corrupt corpus spanning the
/// three corruption types the spec calls out — truncated mid-chunk, malformed
/// chunk header, and a damaged type-0x05 micro-chunk — and asserts the
/// &gt;80% recovery-rate acceptance gate.
/// </summary>
[Trait("Category", "Savegame")]
public sealed class SavegameFixerTests
{
    private const uint TestStructSize = SavegameParser.HeaderStructSize;
    private const string ExpectedLabel = "Forces of Corruption game";

    /// <summary>
    /// The vanilla 0x3E8 mod-context chunk body from the iter-288 hex dump —
    /// seven micro-chunks (types 5, 4, 3, 1, 2, 0, 6).
    /// </summary>
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

    [Fact]
    public void StripBadChunks_RecoversTruncatedMidChunk()
    {
        var save = Concat(
            ValidHeader(),
            Leaf(0x3E8, 1, 2, 3, 4),
            Leaf(0x3E9, 9, 9),
            Truncated(0x3EA, declaredBody: 0x400, actualBody: 4));

        var report = SavegameFixer.StripBadChunks(save);

        report.Recovered.Should().BeTrue();
        report.Strategy.Should().Be(SavegameFixStrategy.StripBadChunks);
        report.DroppedChunkCount.Should().Be(1);
        report.OutputTopChunkCount.Should().Be(2);
        SavegameParser.Diagnose(report.Output).HasOverflow.Should().BeFalse();
    }

    [Fact]
    public void StripBadChunks_RecoversMalformedChunkHeader()
    {
        // Chunk 0x3EA's size field is garbage-large — a malformed header, not a
        // truncation — but the parser flags it the same way: an overflow.
        var save = Concat(
            ValidHeader(),
            Leaf(0x3E8, 1, 1),
            Leaf(0x3E9, 2, 2),
            Truncated(0x3EA, declaredBody: 0xFFFFF, actualBody: 4));

        var report = SavegameFixer.StripBadChunks(save);

        report.Recovered.Should().BeTrue();
        report.DroppedChunkCount.Should().Be(1);
        report.OutputTopChunkCount.Should().Be(2);
    }

    [Fact]
    public void StripBadChunks_KeepsCleanChunksAfterDamagedContainer()
    {
        var save = Concat(
            ValidHeader(),
            Leaf(0x100, 1, 2),
            Container(0x3E9, Truncated(0x111, declaredBody: 0x300, actualBody: 4)),
            Leaf(0x200, 7, 7),
            Leaf(0x201, 8, 8));

        var report = SavegameFixer.StripBadChunks(save);

        report.Recovered.Should().BeTrue();
        report.DroppedChunkCount.Should().Be(1);

        // The damaged container is dropped, but the two clean leaves that sit
        // after it survive — the win that distinguishes strip from truncate.
        report.OutputTopChunkCount.Should().Be(3);
    }

    [Fact]
    public void StripBadChunks_PreservesBmpThumbnailPrefix()
    {
        var save = TruncatedMidChunkWithBmp();

        var report = SavegameFixer.StripBadChunks(save);

        report.Recovered.Should().BeTrue();
        var header = SavegameParser.ParseHeader(report.Output);
        header.HasBmpThumbnail.Should().BeTrue();
        report.OutputTopChunkCount.Should().Be(2);
    }

    [Fact]
    public void StripBadChunks_ReturnsNoneForAlreadyCleanSave()
    {
        var clean = Concat(
            ValidHeader(),
            Leaf(0x3E8, 1, 2, 3, 4),
            Leaf(0x3E9, 9));

        var report = SavegameFixer.StripBadChunks(clean);

        report.Recovered.Should().BeTrue();
        report.Strategy.Should().Be(SavegameFixStrategy.None);
        report.DroppedChunkCount.Should().Be(0);
        report.Output.Should().BeSameAs(clean);
    }

    [Fact]
    public void TruncateAtFailure_CutsAtFirstDamagedChunk()
    {
        var save = Concat(
            ValidHeader(),
            Leaf(0x100, 1, 2),
            Container(0x3E9, Truncated(0x111, declaredBody: 0x300, actualBody: 4)),
            Leaf(0x200, 7, 7),
            Leaf(0x201, 8, 8));

        var report = SavegameFixer.TruncateAtFailure(save);

        report.Recovered.Should().BeTrue();
        report.Strategy.Should().Be(SavegameFixStrategy.TruncateAtFailure);

        // Truncate cuts at the damaged container, so only the leading clean
        // leaf survives — the trailing clean leaves are lost.
        report.OutputTopChunkCount.Should().Be(1);
    }

    [Fact]
    public void Fix_PrefersStripOverTruncateWhenItKeepsMoreChunks()
    {
        var save = Concat(
            ValidHeader(),
            Leaf(0x100, 1, 2),
            Container(0x3E9, Truncated(0x111, declaredBody: 0x300, actualBody: 4)),
            Leaf(0x200, 7, 7),
            Leaf(0x201, 8, 8));

        var fix = SavegameFixer.Fix(save);
        var truncate = SavegameFixer.TruncateAtFailure(save);

        fix.Recovered.Should().BeTrue();
        fix.Strategy.Should().Be(SavegameFixStrategy.StripBadChunks);
        fix.OutputTopChunkCount.Should().Be(3);
        fix.OutputTopChunkCount.Should().BeGreaterThan(truncate.OutputTopChunkCount);
    }

    [Fact]
    public void Fix_ReturnsUnrecoverableForBadHeaderMagic()
    {
        var report = SavegameFixer.Fix(BadHeaderMagic());

        report.Recovered.Should().BeFalse();
        report.Strategy.Should().Be(SavegameFixStrategy.Unrecoverable);
        report.Summary.Should().Contain("magic");
    }

    [Fact]
    public void Fix_ReturnsUnrecoverableForTruncatedHeader()
    {
        // A buffer shorter than the RGMH header struct has no chunk-stream
        // anchor, so neither strategy can find anything to recover.
        var report = SavegameFixer.Fix(new byte[64]);

        report.Recovered.Should().BeFalse();
        report.Strategy.Should().Be(SavegameFixStrategy.Unrecoverable);
        report.Summary.Should().Contain("shorter");
    }

    [Fact]
    public void SalvageMicroChunks_RecoversFullVanilla3E8Stream()
    {
        var salvaged = SavegameFixer.SalvageMicroChunks(Vanilla3E8Body);

        salvaged.Should().HaveCount(7);
        salvaged.Select(m => m.TypeCode).Should().Equal(0x05, 0x04, 0x03, 0x01, 0x02, 0x00, 0x06);
    }

    [Fact]
    public void SalvageMicroChunks_DropsCorruptType05MicroChunkTail()
    {
        // A clean type-1 int32 micro-chunk, then a type-0x05 string whose
        // length byte (0xC8 = 200) runs far past the body — the iter-288
        // "bad UTF-16LE in type-0x05 string" corruption type.
        var body = Concat(
            new byte[] { MicroChunk.TypeInt32First, 0x04, 0x72, 0x03, 0xF3, 0x8A },
            new byte[] { MicroChunk.TypeStringBlob, 0xC8, 0x11, 0x22, 0x33 });

        var salvaged = SavegameFixer.SalvageMicroChunks(body);

        salvaged.Should().ContainSingle();
        salvaged[0].TypeCode.Should().Be(MicroChunk.TypeInt32First);
        salvaged[0].AsInt32().Should().Be(unchecked((int)0x8AF30372u));
    }

    [Fact]
    public void SalvageMicroChunks_ReturnsEmptyForUndersizedBody()
    {
        SavegameFixer.SalvageMicroChunks(Array.Empty<byte>()).Should().BeEmpty();
        SavegameFixer.SalvageMicroChunks(new byte[] { 0x00 }).Should().BeEmpty();
    }

    [Fact]
    public void Fix_RecoversCorruptCorpusAboveEightyPercent()
    {
        var corpus = CorruptCorpus();
        var recovered = 0;

        foreach (var (name, bytes, recoverable) in corpus)
        {
            var report = SavegameFixer.Fix(bytes);

            if (report.Recovered)
            {
                recovered++;

                // Spec measure (b): output chunk count >= input - dropped.
                report.OutputTopChunkCount.Should().BeGreaterThanOrEqualTo(
                    report.InputTopChunkCount - report.DroppedChunkCount,
                    $"recovered fixture '{name}' must honour the chunk-count invariant");

                // Spec measure (a): output passes parser re-validation.
                SavegameParser.Diagnose(report.Output).HasOverflow.Should().BeFalse(
                    $"recovered fixture '{name}' must re-parse without an overflow chunk");
            }

            report.Recovered.Should().Be(
                recoverable, $"fixture '{name}' should match its expected recoverability");
        }

        var rate = (double)recovered / corpus.Length;
        rate.Should().BeGreaterThan(
            0.80, "spec iter-288 requires a >80% recovery rate on the corrupt corpus");
    }

    /// <summary>
    /// A synthetic corrupt corpus: seven structurally damaged saves the fixer
    /// must recover plus one unrecoverable save (a destroyed RGMH magic), so
    /// the &gt;80% gate is genuinely exercised rather than trivially met.
    /// </summary>
    private static (string Name, byte[] Bytes, bool Recoverable)[] CorruptCorpus() => new[]
    {
        ("truncated-mid-chunk", Concat(
            ValidHeader(),
            Leaf(0x3E8, 1, 2, 3, 4),
            Leaf(0x3E9, 9, 9),
            Truncated(0x3EA, declaredBody: 0x400, actualBody: 4)), true),

        ("truncated-mid-chunk-with-bmp", TruncatedMidChunkWithBmp(), true),

        ("truncated-deep", Concat(
            ValidHeader(),
            Leaf(0x1, 1), Leaf(0x2, 2), Leaf(0x3, 3), Leaf(0x4, 4),
            Truncated(0x5, declaredBody: 0x1000, actualBody: 8)), true),

        ("malformed-mid-header", Concat(
            ValidHeader(),
            Leaf(0x3E8, 1, 1),
            Leaf(0x3E9, 2, 2),
            Truncated(0x3EA, declaredBody: 0xFFFFF, actualBody: 4)), true),

        ("damaged-child-container", Concat(
            ValidHeader(),
            Leaf(0x100, 1, 2),
            Container(0x3E9, Truncated(0x111, declaredBody: 0x300, actualBody: 4)),
            Leaf(0x200, 7, 7),
            Leaf(0x201, 8, 8)), true),

        ("malformed-second-of-two", Concat(
            ValidHeader(),
            Leaf(0x3E8, 1, 2, 3, 4),
            Truncated(0x3E9, declaredBody: 0xABCDE, actualBody: 6)), true),

        ("overflowing-container", Concat(
            ValidHeader(),
            Leaf(0x3E8, 5, 5),
            Concat(ChunkHeader(0x3EA, 0x500u | SaveChunk.SubChunkFlag), new byte[4])), true),

        ("bad-header-magic", BadHeaderMagic(), false),
    };

    private static byte[] TruncatedMidChunkWithBmp() => Concat(
        ValidHeader(),
        BmpBlock(64),
        Leaf(0x3E8, 1, 2, 3, 4),
        Leaf(0x3E9, 9, 9),
        Truncated(0x3EA, declaredBody: 0x400, actualBody: 4));

    private static byte[] BadHeaderMagic()
    {
        var save = Concat(ValidHeader(), Leaf(0x3E8, 1, 2));
        save[0] = (byte)'X';
        return save;
    }

    private static byte[] ValidHeader()
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
