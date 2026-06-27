namespace SwfocTrainer.Savegame;

/// <summary>
/// Validates — and repairs — the mod hash a <c>.PetroglyphFoC64Save</c> embeds
/// to bind itself to the mod it was created under (spec iter-290).
///
/// <para>
/// A save records a hash of its mod's ObjectType definitions. When the mod is
/// updated or swapped, the loader recomputes that hash, finds it no longer
/// matches the embedded one, and the save crashes or silently desyncs unit
/// definitions, build costs and tech requirements. <see cref="ReAnchor"/> is
/// the highest-leverage recovery: it overwrites the stale embedded hash with a
/// freshly computed one so the save loads cleanly under the current mod.
/// </para>
///
/// <para>
/// The embedded hash lives in the <see cref="ModContextChunkId"/> (0x3E8)
/// mod-context chunk as its type-0x01 int32 micro-chunk — empirically pinned
/// from the iter-288 0x3E8 hex dump and the iter-289 fixture. The hash
/// algorithm defaults to IEEE <see cref="Crc32"/>; the exact engine algorithm
/// is RE open question 3 (decompile the hash routine near
/// <c>GameObjectTypeList @ 0xA172D0</c>). Pass a custom delegate to
/// <see cref="ModHashValidator(System.Func{System.ReadOnlyMemory{byte},uint})"/>
/// to swap the algorithm once that RE lands — the chunk-location, comparison
/// and re-anchor logic stay correct regardless.
/// </para>
/// </summary>
public sealed class ModHashValidator
{
    /// <summary>Chunk id of the mod-context chunk that carries the embedded mod hash.</summary>
    public const uint ModContextChunkId = 0x3E8u;

    /// <summary>
    /// Micro-chunk type code of the embedded mod hash inside the
    /// <see cref="ModContextChunkId"/> chunk — a type-0x01 int32 field.
    /// </summary>
    public const byte ModHashMicroChunkType = MicroChunk.TypeInt32First;

    private readonly Func<ReadOnlyMemory<byte>, uint> _hashFunction;

    /// <summary>
    /// Creates a validator that fingerprints mods with IEEE <see cref="Crc32"/>
    /// — the working assumption for the engine's mod hash (RE open question 3).
    /// </summary>
    public ModHashValidator()
        : this(static data => Crc32.Compute(data.Span))
    {
    }

    /// <summary>
    /// Creates a validator with a custom mod-hash algorithm. Use this once the
    /// engine hash routine near <c>GameObjectTypeList @ 0xA172D0</c> is
    /// decompiled and IEEE CRC-32 is confirmed or replaced.
    /// </summary>
    public ModHashValidator(Func<ReadOnlyMemory<byte>, uint> hashFunction)
    {
        ArgumentNullException.ThrowIfNull(hashFunction);
        _hashFunction = hashFunction;
    }

    /// <summary>
    /// Computes the mod hash of a mod's ObjectType definition bytes — the caller
    /// assembles those bytes from the mod's XML.
    /// </summary>
    public uint ComputeModHash(ReadOnlyMemory<byte> modObjectTypeData) =>
        _hashFunction(modObjectTypeData);

    /// <summary>
    /// Computes the mod hash over a set of mod files, concatenated in a
    /// deterministic order (ordinal by path) so the result is independent of
    /// the order the paths are supplied in.
    /// </summary>
    public uint ComputeModHashFromFiles(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);

        using var buffer = new MemoryStream();
        foreach (var path in filePaths.OrderBy(static p => p, StringComparer.Ordinal))
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            buffer.Write(File.ReadAllBytes(path));
        }

        return ComputeModHash(buffer.ToArray());
    }

    /// <summary>
    /// Reads the mod hash embedded in <paramref name="document"/>. Returns false
    /// when the save has no 0x3E8 mod-context chunk, or that chunk carries no
    /// type-0x01 int32 hash slot.
    /// </summary>
    public bool TryGetEmbeddedHash(SavegameDocument document, out uint embeddedHash)
    {
        ArgumentNullException.ThrowIfNull(document);
        return TryLocate(document, out _, out _, out embeddedHash);
    }

    /// <summary>
    /// Validates the save's embedded mod hash against the hash freshly computed
    /// from <paramref name="modObjectTypeData"/>.
    /// </summary>
    public ModHashValidationResult Validate(
        SavegameDocument document, ReadOnlyMemory<byte> modObjectTypeData)
    {
        ArgumentNullException.ThrowIfNull(document);

        var computed = ComputeModHash(modObjectTypeData);
        if (!TryLocate(document, out _, out var index, out var embedded))
        {
            return new ModHashValidationResult
            {
                Status = ModHashStatus.NoEmbeddedHash,
                EmbeddedHash = 0u,
                ComputedHash = computed,
                MicroChunkIndex = -1,
                Summary =
                    "save carries no embedded mod hash — its 0x3E8 mod-context chunk has no " +
                    "type-0x01 slot; cannot validate or re-anchor",
            };
        }

        var matches = embedded == computed;
        return new ModHashValidationResult
        {
            Status = matches ? ModHashStatus.Match : ModHashStatus.Mismatch,
            EmbeddedHash = embedded,
            ComputedHash = computed,
            MicroChunkIndex = index,
            Summary = matches
                ? $"mod hash matches: embedded 0x{embedded:X8} == computed 0x{computed:X8}"
                : $"mod hash mismatch: embedded 0x{embedded:X8} != computed 0x{computed:X8} — " +
                  "re-anchor to recover the save",
        };
    }

    /// <summary>
    /// Re-anchors the save: overwrites its embedded mod hash with the hash
    /// freshly computed from <paramref name="modObjectTypeData"/> so it loads
    /// cleanly under the current mod. Mutates <paramref name="document"/> in
    /// place — serialise it to write the repaired save. Returns false, leaving
    /// the document untouched, when the save has no embedded hash slot or
    /// already matches the mod.
    /// </summary>
    public bool ReAnchor(SavegameDocument document, ReadOnlyMemory<byte> modObjectTypeData)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!TryLocate(document, out var chunk, out var index, out var embedded))
        {
            return false;
        }

        var computed = ComputeModHash(modObjectTypeData);
        if (embedded == computed)
        {
            return false;
        }

        chunk.SetMicroChunkInt32(index, unchecked((int)computed));
        return true;
    }

    /// <summary>
    /// Locates the embedded mod-hash micro-chunk: the first type-0x01 int32
    /// micro-chunk inside the first 0x3E8 mod-context chunk anywhere in the tree.
    /// </summary>
    private static bool TryLocate(
        SavegameDocument document,
        out EditableChunk chunk,
        out int microChunkIndex,
        out uint embeddedHash)
    {
        var modContext = FindChunk(document.Chunks, ModContextChunkId);
        if (modContext is { IsMicroLeaf: true })
        {
            var micros = modContext.MicroChunks;
            for (var i = 0; i < micros.Count; i++)
            {
                if (micros[i].TypeCode == ModHashMicroChunkType
                    && micros[i].Data.Length >= sizeof(uint))
                {
                    chunk = modContext;
                    microChunkIndex = i;
                    embeddedHash = unchecked((uint)micros[i].AsInt32());
                    return true;
                }
            }
        }

        chunk = null!;
        microChunkIndex = -1;
        embeddedHash = 0u;
        return false;
    }

    /// <summary>Depth-first search for the first chunk with id <paramref name="id"/>.</summary>
    private static EditableChunk? FindChunk(IReadOnlyList<EditableChunk> chunks, uint id)
    {
        foreach (var chunk in chunks)
        {
            if (chunk.Id == id)
            {
                return chunk;
            }

            if (chunk.HasSubChunks)
            {
                var nested = FindChunk(chunk.Children, id);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }
}
