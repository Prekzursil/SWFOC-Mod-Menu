namespace SwfocTrainer.Savegame;

/// <summary>
/// A lightweight parse summary produced by <see cref="SavegameParser.Diagnose"/>.
/// Consumed by the CLI corruption fixer and the savegame editor tab to decide
/// whether a save is structurally sound before deeper work.
/// </summary>
public sealed class SavegameReport
{
    /// <summary>True when the buffer parsed without a format exception.</summary>
    public required bool Parsed { get; init; }

    /// <summary>The format error message; non-null only when <see cref="Parsed"/> is false.</summary>
    public string? Error { get; init; }

    /// <summary>The parsed header; non-null only when <see cref="Parsed"/> is true.</summary>
    public SavegameHeader? Header { get; init; }

    /// <summary>Count of top-level chunks.</summary>
    public int TopChunkCount { get; init; }

    /// <summary>Count of all chunks, including nested sub-chunks at every depth.</summary>
    public int TotalChunkCount { get; init; }

    /// <summary>The sorted, distinct set of chunk ids seen anywhere in the tree.</summary>
    public IReadOnlyList<string> UniqueChunkIds { get; init; } = Array.Empty<string>();

    /// <summary>True when any chunk overflowed its region — the save is malformed.</summary>
    public bool HasOverflow { get; init; }
}
