using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ITelemetryLogTailService
{
    TelemetryModeResolution ResolveLatestMode(
        string? processPath,
        DateTimeOffset nowUtc,
        TimeSpan freshnessWindow);
}
