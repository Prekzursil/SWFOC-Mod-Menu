namespace SwfocTrainer.Core.Contracts;

public interface IProfileUpdateService
{
    Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    Task<string> InstallProfileAsync(string profileId, CancellationToken cancellationToken = default);
}
