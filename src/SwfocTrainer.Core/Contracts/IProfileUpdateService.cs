using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IProfileUpdateService
{
    Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    Task<string> InstallProfileAsync(string profileId, CancellationToken cancellationToken = default);

    Task<ProfileInstallResult> InstallProfileTransactionalAsync(string profileId, CancellationToken cancellationToken = default);

    Task<ProfileRollbackResult> RollbackLastInstallAsync(string profileId, CancellationToken cancellationToken = default);
}
