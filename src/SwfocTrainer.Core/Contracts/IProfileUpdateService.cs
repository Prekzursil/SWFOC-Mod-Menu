using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IProfileUpdateService
{
    Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<string>> CheckForUpdatesAsync()
    {
        return CheckForUpdatesAsync(CancellationToken.None);
    }

    Task<string> InstallProfileAsync(string profileId, CancellationToken cancellationToken);

    Task<string> InstallProfileAsync(string profileId)
    {
        return InstallProfileAsync(profileId, CancellationToken.None);
    }

    Task<ProfileInstallResult> InstallProfileTransactionalAsync(string profileId, CancellationToken cancellationToken);

    Task<ProfileInstallResult> InstallProfileTransactionalAsync(string profileId)
    {
        return InstallProfileTransactionalAsync(profileId, CancellationToken.None);
    }

    Task<ProfileRollbackResult> RollbackLastInstallAsync(string profileId, CancellationToken cancellationToken);

    Task<ProfileRollbackResult> RollbackLastInstallAsync(string profileId)
    {
        return RollbackLastInstallAsync(profileId, CancellationToken.None);
    }
}
