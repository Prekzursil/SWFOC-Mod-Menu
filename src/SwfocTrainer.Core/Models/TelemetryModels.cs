namespace SwfocTrainer.Core.Models;

public sealed record TelemetryModeResolution(
    bool Available,
    RuntimeMode Mode,
    string ReasonCode,
    string SourcePath,
    DateTimeOffset? TimestampUtc,
    string? RawLine)
{
    public static TelemetryModeResolution Unavailable(string reasonCode) =>
        new(
            Available: false,
            Mode: RuntimeMode.Unknown,
            ReasonCode: reasonCode,
            SourcePath: string.Empty,
            TimestampUtc: null,
            RawLine: null);
}
