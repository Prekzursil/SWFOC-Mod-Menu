namespace SwfocTrainer.Savegame;

/// <summary>
/// The recovery strategy a <see cref="SavegameFixer"/> pass applied to a
/// corrupt <c>.PetroglyphFoC64Save</c> buffer.
/// </summary>
public enum SavegameFixStrategy
{
    /// <summary>The input was already structurally sound; no bytes were changed.</summary>
    None,

    /// <summary>
    /// Damaged top-level chunks were dropped and the survivors re-serialised —
    /// the lowest-loss recovery; it keeps clean chunks that sit after the damage.
    /// </summary>
    StripBadChunks,

    /// <summary>
    /// The file was cut immediately before the first damaged top-level chunk —
    /// the cruder fallback; everything from the damage onward is lost.
    /// </summary>
    TruncateAtFailure,

    /// <summary>
    /// The buffer could not be recovered — typically an unreadable RGMH header,
    /// which leaves no anchor for the chunk stream. See the report summary.
    /// </summary>
    Unrecoverable,
}
