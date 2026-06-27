namespace SwfocTrainer.Savegame;

/// <summary>
/// Raised when a savegame buffer violates the RGMH chunk format — a bad magic,
/// a truncated header, or a chunk / micro-chunk whose declared size runs past
/// the region it lives in.
/// </summary>
public sealed class SavegameFormatException : Exception
{
    /// <summary>Creates an exception with a default message.</summary>
    public SavegameFormatException()
    {
    }

    /// <summary>Creates an exception with an explanatory message.</summary>
    public SavegameFormatException(string message)
        : base(message)
    {
    }

    /// <summary>Creates an exception that wraps an underlying cause.</summary>
    public SavegameFormatException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
