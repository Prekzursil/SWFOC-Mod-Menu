namespace SwfocTrainer.Savegame;

/// <summary>
/// Recovers a structurally corrupt <c>.PetroglyphFoC64Save</c> file. Two
/// strategies are offered, each verified by re-parsing its result:
/// <see cref="StripBadChunks"/> drops every damaged top-level chunk and
/// re-serialises the survivors — the lowest-loss recovery, since it keeps
/// clean chunks that sit after the damage; <see cref="TruncateAtFailure"/>
/// cuts the file before the first damaged chunk — cruder, but a dependable
/// fallback. <see cref="Fix"/> applies the lowest-loss strategy that works and
/// is what the spec iter-288 &gt;80%-recovery gate is measured against.
/// <see cref="SalvageMicroChunks"/> covers the third spec strategy — selective
/// micro-chunk drop inside a leaf body — at the micro-chunk level.
/// </summary>
public static class SavegameFixer
{
    /// <summary>
    /// Recovers a corrupt savegame buffer with the lowest-loss strategy that
    /// works — <see cref="StripBadChunks"/> first, then
    /// <see cref="TruncateAtFailure"/>. A buffer with an unreadable RGMH header
    /// cannot be recovered and is reported as
    /// <see cref="SavegameFixStrategy.Unrecoverable"/>.
    /// </summary>
    public static SavegameFixReport Fix(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        var strip = StripBadChunks(buffer);
        if (strip.Recovered || strip.Strategy == SavegameFixStrategy.Unrecoverable)
        {
            return strip;
        }

        return TruncateAtFailure(buffer);
    }

    /// <summary>
    /// Drops every damaged top-level chunk — one that overflowed its region or
    /// has an overflowing descendant — and re-serialises the survivors onto the
    /// original header (and BMP thumbnail) prefix. Clean chunks that sit after
    /// the damage are preserved.
    /// </summary>
    public static SavegameFixReport StripBadChunks(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        SavegameHeader header;
        IReadOnlyList<SaveChunk> chunks;
        try
        {
            (header, chunks) = SavegameParser.Parse(buffer);
        }
        catch (SavegameFormatException ex)
        {
            return Unrecoverable(buffer, ex.Message);
        }

        var kept = new List<SaveChunk>(chunks.Count);
        foreach (var chunk in chunks)
        {
            if (!IsDamaged(chunk))
            {
                kept.Add(chunk);
            }
        }

        var dropped = chunks.Count - kept.Count;
        if (dropped == 0)
        {
            return AlreadyClean(buffer, chunks.Count);
        }

        var output = ReserializeKeptChunks(buffer, header.ChunkStreamOffset, kept);
        return Verify(
            output,
            SavegameFixStrategy.StripBadChunks,
            chunks.Count,
            dropped,
            $"stripped {dropped} damaged top-level chunk(s), kept {kept.Count}");
    }

    /// <summary>
    /// Cuts the file immediately before the first damaged top-level chunk.
    /// Everything from the damage onward — including any clean chunks after it
    /// — is lost, so this is a cruder recovery than <see cref="StripBadChunks"/>.
    /// </summary>
    public static SavegameFixReport TruncateAtFailure(byte[] buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);

        IReadOnlyList<SaveChunk> chunks;
        try
        {
            (_, chunks) = SavegameParser.Parse(buffer);
        }
        catch (SavegameFormatException ex)
        {
            return Unrecoverable(buffer, ex.Message);
        }

        var firstDamaged = -1;
        for (var i = 0; i < chunks.Count; i++)
        {
            if (IsDamaged(chunks[i]))
            {
                firstDamaged = i;
                break;
            }
        }

        if (firstDamaged < 0)
        {
            return AlreadyClean(buffer, chunks.Count);
        }

        var cut = (int)chunks[firstDamaged].Offset;
        var output = buffer.AsSpan(0, cut).ToArray();
        var dropped = chunks.Count - firstDamaged;
        return Verify(
            output,
            SavegameFixStrategy.TruncateAtFailure,
            chunks.Count,
            dropped,
            $"truncated at offset 0x{cut:X}, dropped {dropped} chunk(s) from the damage onward");
    }

    /// <summary>
    /// Salvages a leaf chunk body whose micro-chunk stream is damaged. A
    /// corrupt length byte — for instance a mangled type-0x05 string — makes
    /// <see cref="MicroChunk.ReadStream"/> throw; this walks the stream greedily
    /// instead and returns the longest valid micro-chunk prefix, discarding the
    /// first undecodable micro-chunk and everything after it.
    /// </summary>
    public static IReadOnlyList<MicroChunk> SalvageMicroChunks(ReadOnlySpan<byte> body)
    {
        var kept = new List<MicroChunk>();
        var pos = 0;
        while (pos + MicroChunk.HeaderSize <= body.Length)
        {
            var typeCode = body[pos];
            var length = body[pos + 1];
            var dataStart = pos + MicroChunk.HeaderSize;
            var dataEnd = dataStart + length;
            if (dataEnd > body.Length)
            {
                break;
            }

            kept.Add(new MicroChunk
            {
                TypeCode = typeCode,
                Length = length,
                Data = body.Slice(dataStart, length).ToArray(),
            });
            pos = dataEnd;
        }

        return kept;
    }

    /// <summary>
    /// True when a chunk overflowed its region, or any descendant did — such a
    /// chunk cannot be trusted to re-serialise into a walkable stream.
    /// </summary>
    private static bool IsDamaged(SaveChunk chunk)
    {
        if (chunk.IsOverflow)
        {
            return true;
        }

        foreach (var child in chunk.Children)
        {
            if (IsDamaged(child))
            {
                return true;
            }
        }

        return false;
    }

    private static byte[] ReserializeKeptChunks(
        byte[] buffer, long chunkStreamOffset, IReadOnlyList<SaveChunk> kept)
    {
        var prefixLength = (int)chunkStreamOffset;
        var size = prefixLength;
        foreach (var chunk in kept)
        {
            size += SaveChunk.HeaderSize + (int)chunk.DataSize;
        }

        var output = new byte[size];
        Array.Copy(buffer, 0, output, 0, prefixLength);
        var pos = prefixLength;
        foreach (var chunk in kept)
        {
            var length = SaveChunk.HeaderSize + (int)chunk.DataSize;
            Array.Copy(buffer, (int)chunk.Offset, output, pos, length);
            pos += length;
        }

        return output;
    }

    private static SavegameFixReport Verify(
        byte[] output,
        SavegameFixStrategy strategy,
        int inputTopChunkCount,
        int droppedChunkCount,
        string action)
    {
        var report = SavegameParser.Diagnose(output);
        var outputTopChunkCount = report.TopChunkCount;
        var countInvariantHeld = outputTopChunkCount >= inputTopChunkCount - droppedChunkCount;
        var recovered = report.Parsed && !report.HasOverflow && countInvariantHeld;

        var summary = recovered
            ? $"{action}; output re-validated cleanly with {outputTopChunkCount} top-level chunk(s)"
            : $"{action}; output FAILED re-validation: {report.Error ?? "a damaged chunk remains"}";

        return new SavegameFixReport
        {
            Recovered = recovered,
            Strategy = strategy,
            Output = output,
            InputTopChunkCount = inputTopChunkCount,
            OutputTopChunkCount = outputTopChunkCount,
            DroppedChunkCount = droppedChunkCount,
            Summary = summary,
        };
    }

    private static SavegameFixReport AlreadyClean(byte[] buffer, int topChunkCount) =>
        new()
        {
            Recovered = true,
            Strategy = SavegameFixStrategy.None,
            Output = buffer,
            InputTopChunkCount = topChunkCount,
            OutputTopChunkCount = topChunkCount,
            DroppedChunkCount = 0,
            Summary = "input is already structurally sound; no repair needed",
        };

    private static SavegameFixReport Unrecoverable(byte[] buffer, string reason) =>
        new()
        {
            Recovered = false,
            Strategy = SavegameFixStrategy.Unrecoverable,
            Output = buffer,
            InputTopChunkCount = 0,
            OutputTopChunkCount = 0,
            DroppedChunkCount = 0,
            Summary = $"cannot recover: {reason}",
        };
}
