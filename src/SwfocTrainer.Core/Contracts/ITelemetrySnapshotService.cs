using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Captures action-level runtime telemetry for diagnostics and drift analysis.
/// </summary>
public interface ITelemetrySnapshotService
{
    void RecordAction(string actionId, AddressSource source, bool succeeded);

    TelemetrySnapshot CreateSnapshot();

    Task<string> ExportSnapshotAsync(string outputDirectory, CancellationToken cancellationToken = default);

    void Reset();
}
