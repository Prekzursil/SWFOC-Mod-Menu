using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface ILaunchContextResolver
{
    LaunchContext Resolve(ProcessMetadata process, IReadOnlyList<TrainerProfile> profiles);
}
