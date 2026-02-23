using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Resolves a user-facing profile selection (including universal_auto) into an internal concrete profile.
/// </summary>
public interface IProfileVariantResolver
{
    Task<ProfileVariantResolution> ResolveAsync(
        string requestedProfileId,
        CancellationToken cancellationToken);

    Task<ProfileVariantResolution> ResolveAsync(
        string requestedProfileId,
        IReadOnlyList<ProcessMetadata>? processes,
        CancellationToken cancellationToken);
}
