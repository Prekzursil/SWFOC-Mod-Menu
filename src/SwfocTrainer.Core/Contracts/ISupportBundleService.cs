using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Produces a zipped support bundle with diagnostics, logs, and runtime snapshots.
/// </summary>
public interface ISupportBundleService
{
    Task<SupportBundleResult> ExportAsync(SupportBundleRequest request, CancellationToken cancellationToken = default);
}
