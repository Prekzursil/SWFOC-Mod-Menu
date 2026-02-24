namespace SwfocTrainer.Core.Contracts;

public interface IHelperModService
{
    Task<string> DeployAsync(string profileId, CancellationToken cancellationToken);

    Task<string> DeployAsync(string profileId)
    {
        return DeployAsync(profileId, CancellationToken.None);
    }

    Task<bool> VerifyAsync(string profileId, CancellationToken cancellationToken);

    Task<bool> VerifyAsync(string profileId)
    {
        return VerifyAsync(profileId, CancellationToken.None);
    }
}
