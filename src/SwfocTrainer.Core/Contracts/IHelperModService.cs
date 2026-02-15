namespace SwfocTrainer.Core.Contracts;

public interface IHelperModService
{
    Task<string> DeployAsync(string profileId, CancellationToken cancellationToken = default);

    Task<bool> VerifyAsync(string profileId, CancellationToken cancellationToken = default);
}
