using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IRuntimeAdapter
{
    Task<AttachSession> AttachAsync(string profileId) =>
        AttachAsync(profileId, CancellationToken.None);

    Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken);

    Task<T> ReadAsync<T>(string symbol) where T : unmanaged =>
        ReadAsync<T>(symbol, CancellationToken.None);

    Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged;

    Task WriteAsync<T>(string symbol, T value) where T : unmanaged =>
        WriteAsync(symbol, value, CancellationToken.None);

    Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged;

    Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request) =>
        ExecuteAsync(request, CancellationToken.None);

    Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken);

    Task DetachAsync() => DetachAsync(CancellationToken.None);

    Task DetachAsync(CancellationToken cancellationToken);

    bool IsAttached { get; }

    AttachSession? CurrentSession { get; }

    Task<RuntimeCalibrationScanResult> ScanCalibrationCandidatesAsync(
        RuntimeCalibrationScanRequest request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new RuntimeCalibrationScanResult(
            Succeeded: false,
            ReasonCode: "not_supported",
            Message: "Calibration scan is not supported by this runtime adapter implementation.",
            Candidates: Array.Empty<RuntimeCalibrationCandidate>()));
    }

    Task<RuntimeCalibrationScanResult> ScanCalibrationCandidatesAsync(RuntimeCalibrationScanRequest request)
    {
        return ScanCalibrationCandidatesAsync(request, CancellationToken.None);
    }
}
