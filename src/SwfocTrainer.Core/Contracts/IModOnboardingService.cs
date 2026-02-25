using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

/// <summary>
/// Creates draft custom-mod profiles from launch samples and a base profile seed.
/// </summary>
public interface IModOnboardingService
{
    Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest request, CancellationToken cancellationToken);
    Task<ModOnboardingBatchResult> ScaffoldDraftProfilesFromSeedsAsync(
        ModOnboardingSeedBatchRequest request,
        CancellationToken cancellationToken);
}
