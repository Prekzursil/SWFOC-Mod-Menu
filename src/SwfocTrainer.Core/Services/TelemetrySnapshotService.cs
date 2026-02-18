using System.Text.Json;
using System.Text.Json.Serialization;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Services;

public sealed class TelemetrySnapshotService : ITelemetrySnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _lock = new();
    private readonly Dictionary<string, int> _actionSuccess = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _actionFailure = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _sourceCounters = new(StringComparer.OrdinalIgnoreCase);

    public void RecordAction(string actionId, AddressSource source, bool succeeded)
    {
        if (string.IsNullOrWhiteSpace(actionId))
        {
            return;
        }

        lock (_lock)
        {
            if (succeeded)
            {
                _actionSuccess[actionId] = _actionSuccess.GetValueOrDefault(actionId) + 1;
            }
            else
            {
                _actionFailure[actionId] = _actionFailure.GetValueOrDefault(actionId) + 1;
            }

            var sourceKey = source.ToString();
            _sourceCounters[sourceKey] = _sourceCounters.GetValueOrDefault(sourceKey) + 1;
        }
    }

    public TelemetrySnapshot CreateSnapshot()
    {
        lock (_lock)
        {
            var success = new Dictionary<string, int>(_actionSuccess, StringComparer.OrdinalIgnoreCase);
            var failure = new Dictionary<string, int>(_actionFailure, StringComparer.OrdinalIgnoreCase);
            var source = new Dictionary<string, int>(_sourceCounters, StringComparer.OrdinalIgnoreCase);

            var totalSuccess = success.Values.Sum();
            var totalFailure = failure.Values.Sum();
            var totalActions = totalSuccess + totalFailure;

            var fallbackCount = source.GetValueOrDefault(AddressSource.Fallback.ToString());
            var unresolvedCount = failure.Where(x => !success.ContainsKey(x.Key)).Sum(x => x.Value);

            var failureRate = totalActions == 0 ? 0d : (double)totalFailure / totalActions;
            var fallbackRate = totalActions == 0 ? 0d : (double)fallbackCount / totalActions;
            var unresolvedRate = totalActions == 0 ? 0d : (double)unresolvedCount / totalActions;

            return new TelemetrySnapshot(
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                ActionSuccessCounters: success,
                ActionFailureCounters: failure,
                AddressSourceCounters: source,
                TotalActions: totalActions,
                FailureRate: failureRate,
                FallbackRate: fallbackRate,
                UnresolvedRate: unresolvedRate);
        }
    }

    public async Task<string> ExportSnapshotAsync(string outputDirectory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new InvalidDataException("Output directory is required.");
        }

        Directory.CreateDirectory(outputDirectory);
        var snapshot = CreateSnapshot();
        var path = Path.Combine(outputDirectory, $"telemetry-snapshot-{snapshot.GeneratedAtUtc:yyyyMMddHHmmss}.json");
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken);
        return path;
    }

    public void Reset()
    {
        lock (_lock)
        {
            _actionSuccess.Clear();
            _actionFailure.Clear();
            _sourceCounters.Clear();
        }
    }
}
