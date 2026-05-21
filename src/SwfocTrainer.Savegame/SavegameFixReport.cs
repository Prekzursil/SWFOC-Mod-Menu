namespace SwfocTrainer.Savegame;

/// <summary>
/// The outcome of a <see cref="SavegameFixer"/> recovery pass: the strategy
/// applied, the repaired buffer, and the chunk-count bookkeeping the spec
/// iter-288 &gt;80%-recovery acceptance gate is measured against.
/// </summary>
public sealed class SavegameFixReport
{
    /// <summary>
    /// True when <see cref="Output"/> re-parsed cleanly — no overflowing chunk
    /// — and the chunk-count invariant held.
    /// </summary>
    public required bool Recovered { get; init; }

    /// <summary>The strategy that produced <see cref="Output"/>.</summary>
    public required SavegameFixStrategy Strategy { get; init; }

    /// <summary>
    /// The repaired buffer. On an unrecoverable input this is the original
    /// buffer, returned unchanged.
    /// </summary>
    public required byte[] Output { get; init; }

    /// <summary>Count of top-level chunks walked from the input buffer.</summary>
    public required int InputTopChunkCount { get; init; }

    /// <summary>Count of top-level chunks walked from <see cref="Output"/>.</summary>
    public required int OutputTopChunkCount { get; init; }

    /// <summary>Count of damaged top-level chunks the pass discarded.</summary>
    public required int DroppedChunkCount { get; init; }

    /// <summary>A human-readable one-line description of what the pass did.</summary>
    public required string Summary { get; init; }
}
