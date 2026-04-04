using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Core.Contracts;

public static class ModOnboardingServiceExtensions
{
    public static Task<ModOnboardingResult> ScaffoldDraftProfileAsync(
        this IModOnboardingService service,
        ModOnboardingRequest request)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(request);
        return service.ScaffoldDraftProfileAsync(request, CancellationToken.None);
    }

    public static Task<ModOnboardingBatchResult> ScaffoldDraftProfilesFromSeedsAsync(
        this IModOnboardingService service,
        ModOnboardingSeedBatchRequest request)
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(request);
        return service.ScaffoldDraftProfilesFromSeedsAsync(request, CancellationToken.None);
    }
}
