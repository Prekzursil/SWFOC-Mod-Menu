using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Captures deterministic binary fingerprints for runtime capability resolution.
/// </summary>
public interface IBinaryFingerprintService
{
    Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int? processId = null, CancellationToken cancellationToken = default);
}
