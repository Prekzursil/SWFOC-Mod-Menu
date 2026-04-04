namespace SwfocTrainer.Meg;

public sealed record MegOpenResult(
    bool Succeeded,
    MegArchive? Archive,
    string ReasonCode,
    string Message,
    IReadOnlyList<string> Diagnostics)
{
    public static MegOpenResult Success(MegArchive archive, IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(
            Succeeded: true,
            Archive: archive,
            ReasonCode: "ok",
            Message: "MEG archive parsed successfully.",
            Diagnostics: diagnostics);
    }

    public static MegOpenResult Fail(string reasonCode, string message)
    {
        ArgumentNullException.ThrowIfNull(reasonCode);
        ArgumentNullException.ThrowIfNull(message);
        return new(
            Succeeded: false,
            Archive: null,
            ReasonCode: reasonCode,
            Message: message,
            Diagnostics: Array.Empty<string>());
    }

    public static MegOpenResult Fail(string reasonCode, string message, IReadOnlyList<string> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(reasonCode);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(diagnostics);
        return new(
            Succeeded: false,
            Archive: null,
            ReasonCode: reasonCode,
            Message: message,
            Diagnostics: diagnostics);
    }
}
