using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IGameLaunchService
{
    Task<GameLaunchResult> LaunchAsync(GameLaunchRequest request, CancellationToken cancellationToken);

    Task<GameLaunchResult> LaunchAsync(GameLaunchRequest request)
    {
        return LaunchAsync(request, CancellationToken.None);
    }
}
