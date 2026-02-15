using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public interface IProfileRepository
{
    Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken = default);

    Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken = default);

    Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken = default);

    Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken = default);
}
