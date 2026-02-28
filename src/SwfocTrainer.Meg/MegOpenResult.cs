namespace SwfocTrainer.Meg;

public sealed record MegOpenResult(
    bool Succeeded,
    MegArchive? Archive,
    string ReasonCode,
    string Message,
    IReadOnlyList<string> Diagnostics)
{
    public static MegOpenResult Success(MegArchive archive, IReadOnlyList<string> diagnostics) =>
        new(
            Succeeded: true,
            Archive: archive,
            ReasonCode: "ok",
            Message: "MEG archive parsed successfully.",
            Diagnostics: diagnostics);

    public static MegOpenResult Fail(string reasonCode, string message) =>
        new(
            Succeeded: false,
            Archive: null,
            ReasonCode: reasonCode,
            Message: message,
            Diagnostics: Array.Empty<string>());

    public static MegOpenResult Fail(string reasonCode, string message, IReadOnlyList<string> diagnostics) =>
        new(
            Succeeded: false,
            Archive: null,
            ReasonCode: reasonCode,
            Message: message,
            Diagnostics: diagnostics);
}
