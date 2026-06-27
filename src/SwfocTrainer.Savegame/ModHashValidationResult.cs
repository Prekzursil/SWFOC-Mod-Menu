namespace SwfocTrainer.Savegame;

/// <summary>
/// The result of a <see cref="ModHashValidator.Validate"/> pass: the match
/// status, both hashes, and where the embedded hash was found.
/// </summary>
public sealed class ModHashValidationResult
{
    /// <summary>Whether the save's embedded hash matched the freshly computed mod hash.</summary>
    public required ModHashStatus Status { get; init; }

    /// <summary>
    /// The mod hash embedded in the save, or <c>0</c> when <see cref="Status"/>
    /// is <see cref="ModHashStatus.NoEmbeddedHash"/>.
    /// </summary>
    public required uint EmbeddedHash { get; init; }

    /// <summary>The mod hash freshly computed from the supplied mod ObjectType data.</summary>
    public required uint ComputedHash { get; init; }

    /// <summary>
    /// Index of the embedded-hash micro-chunk inside the 0x3E8 mod-context
    /// chunk, or <c>-1</c> when the save carries no embedded hash.
    /// </summary>
    public required int MicroChunkIndex { get; init; }

    /// <summary>A human-readable one-line description of the validation outcome.</summary>
    public required string Summary { get; init; }

    /// <summary>True when the save matches its mod — no re-anchor is needed.</summary>
    public bool IsMatch => Status == ModHashStatus.Match;

    /// <summary>
    /// True when the save has an embedded hash that no longer matches its mod —
    /// <see cref="ModHashValidator.ReAnchor"/> can recover it.
    /// </summary>
    public bool NeedsReAnchor => Status == ModHashStatus.Mismatch;
}
