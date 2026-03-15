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


public sealed record HelperOperationVerification(
    bool Verified,
    string ReasonCode,
    string SourcePath,
    DateTimeOffset? TimestampUtc,
    string? RawLine)
{
    public static HelperOperationVerification Unavailable(string reasonCode) =>
        new(
            Verified: false,
            ReasonCode: reasonCode,
            SourcePath: string.Empty,
            TimestampUtc: null,
            RawLine: null);
}

public sealed record HelperAutoloadVerification(
    bool Ready,
    string ReasonCode,
    string SourcePath,
    DateTimeOffset? TimestampUtc,
    string? RawLine,
    string? Strategy,
    string? Script)
{
    public static HelperAutoloadVerification Unavailable(string reasonCode) =>
        new(
            Ready: false,
            ReasonCode: reasonCode,
            SourcePath: string.Empty,
            TimestampUtc: null,
            RawLine: null,
            Strategy: null,
            Script: null);
}
