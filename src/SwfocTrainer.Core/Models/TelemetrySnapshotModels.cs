namespace SwfocTrainer.Core.Models;

/// <summary>
/// A deterministic export of runtime telemetry counters.
/// </summary>
public sealed record TelemetrySnapshot(
    DateTimeOffset GeneratedAtUtc,
    IReadOnlyDictionary<string, int> ActionSuccessCounters,
    IReadOnlyDictionary<string, int> ActionFailureCounters,
    IReadOnlyDictionary<string, int> AddressSourceCounters,
    int TotalActions,
    double FailureRate,
    double FallbackRate,
    double UnresolvedRate);
