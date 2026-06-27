namespace SwfocTrainer.Savegame;

/// <summary>
/// The fixed RGMH header of a <c>.PetroglyphFoC64Save</c> file plus the
/// resolved absolute offset at which the chunk stream begins. Empirically
/// confirmed across vanilla and modded saves (iter-287 / iter-288 RE).
/// </summary>
public sealed class SavegameHeader
{
    /// <summary>The 4-byte file magic; always <c>"RGMH"</c> for a valid save.</summary>
    public required string Magic { get; init; }

    /// <summary>Format version; <c>1</c> for every observed SWFOC save.</summary>
    public required uint Version { get; init; }

    /// <summary>Header struct size as read from the file; <c>0x2028</c> for every observed save.</summary>
    public required uint StructSize { get; init; }

    /// <summary>The 16-byte game UUID rendered as 32 lowercase hex characters.</summary>
    public required string UuidHex { get; init; }

    /// <summary>UTF-16LE label; <c>"Forces of Corruption game"</c> for every observed save.</summary>
    public required string Label { get; init; }

    /// <summary>
    /// Absolute file offset at which the chunk stream starts — past the RGMH
    /// header and past the optional BMP thumbnail screenshot.
    /// </summary>
    public required long ChunkStreamOffset { get; init; }

    /// <summary>True when a BMP thumbnail sits between the header and the chunk stream.</summary>
    public required bool HasBmpThumbnail { get; init; }
}
