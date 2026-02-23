using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class ProfileVariantResolver : IProfileVariantResolver
{
    public const string UniversalProfileId = "universal_auto";

    private readonly ILaunchContextResolver _launchContextResolver;
    private readonly IProfileRepository? _profileRepository;
    private readonly IProcessLocator? _processLocator;
    private readonly IBinaryFingerprintService? _fingerprintService;
    private readonly ICapabilityMapResolver? _capabilityMapResolver;
    private readonly ILogger<ProfileVariantResolver> _logger;

    public ProfileVariantResolver(
        ILaunchContextResolver launchContextResolver,
        ILogger<ProfileVariantResolver> logger)
        : this(launchContextResolver, logger, null, null, null, null)
    {
    }

    public ProfileVariantResolver(
        ILaunchContextResolver launchContextResolver,
        ILogger<ProfileVariantResolver> logger,
        IProfileRepository? profileRepository,
        IProcessLocator? processLocator,
        IBinaryFingerprintService? fingerprintService,
        ICapabilityMapResolver? capabilityMapResolver)
    {
        _launchContextResolver = launchContextResolver;
        _logger = logger;
        _profileRepository = profileRepository;
        _processLocator = processLocator;
        _fingerprintService = fingerprintService;
        _capabilityMapResolver = capabilityMapResolver;
    }

    public Task<ProfileVariantResolution> ResolveAsync(
        string requestedProfileId,
        CancellationToken cancellationToken)
    {
        return ResolveAsync(requestedProfileId, null, cancellationToken);
    }

    public async Task<ProfileVariantResolution> ResolveAsync(
        string requestedProfileId,
        IReadOnlyList<ProcessMetadata>? processes,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(requestedProfileId, UniversalProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileVariantResolution(
                requestedProfileId,
                requestedProfileId,
                "explicit_profile_selection",
                1.0d);
        }

        var candidates = await ResolveCandidatesAsync(processes, cancellationToken);
        if (candidates.Count == 0)
        {
            return new ProfileVariantResolution(
                requestedProfileId,
                "base_swfoc",
                "no_process_detected",
                0.40d);
        }

        var launchProfiles = await LoadProfilesForLaunchContextAsync(cancellationToken);
        var bestCandidate = SelectBestCandidate(candidates, launchProfiles);

        var launchRecommendation = TryBuildLaunchRecommendation(
            requestedProfileId,
            bestCandidate.Process,
            bestCandidate.Context);
        if (launchRecommendation is not null)
        {
            return launchRecommendation;
        }

        var fingerprintRecommendation = await TryResolveFingerprintDefaultProfileAsync(
            requestedProfileId,
            bestCandidate.Process,
            cancellationToken);
        if (fingerprintRecommendation is not null)
        {
            return fingerprintRecommendation;
        }

        return BuildExeTargetFallbackResolution(requestedProfileId, bestCandidate.Process);
    }

    private async Task<IReadOnlyList<ProcessMetadata>> ResolveCandidatesAsync(
        IReadOnlyList<ProcessMetadata>? processes,
        CancellationToken cancellationToken)
    {
        if (processes is not null)
        {
            return processes;
        }

        if (_processLocator is null)
        {
            return Array.Empty<ProcessMetadata>();
        }

        return await _processLocator.FindSupportedProcessesAsync(cancellationToken);
    }

    private (ProcessMetadata Process, LaunchContext Context) SelectBestCandidate(
        IReadOnlyList<ProcessMetadata> candidates,
        IReadOnlyList<TrainerProfile> launchProfiles)
    {
        return candidates
            .Select(process =>
            {
                var context = process.LaunchContext ?? _launchContextResolver.Resolve(process, launchProfiles);
                return (Process: process, Context: context);
            })
            .OrderByDescending(candidate => candidate.Context.Recommendation.Confidence)
            .ThenByDescending(candidate => candidate.Context.LaunchKind == LaunchKind.Workshop || candidate.Context.LaunchKind == LaunchKind.Mixed)
            .ThenByDescending(candidate => candidate.Process.ExeTarget == ExeTarget.Swfoc)
            .First();
    }

    private static ProfileVariantResolution? TryBuildLaunchRecommendation(
        string requestedProfileId,
        ProcessMetadata process,
        LaunchContext launchContext)
    {
        var recommendedProfileId = launchContext.Recommendation.ProfileId;
        if (string.IsNullOrWhiteSpace(recommendedProfileId))
        {
            return null;
        }

        return new ProfileVariantResolution(
            requestedProfileId,
            recommendedProfileId,
            launchContext.Recommendation.ReasonCode,
            launchContext.Recommendation.Confidence,
            ProcessId: process.ProcessId,
            ProcessName: process.ProcessName);
    }

    private async Task<ProfileVariantResolution?> TryResolveFingerprintDefaultProfileAsync(
        string requestedProfileId,
        ProcessMetadata process,
        CancellationToken cancellationToken)
    {
        if (_fingerprintService is null || _capabilityMapResolver is null)
        {
            return null;
        }

        try
        {
            var fingerprint = await _fingerprintService.CaptureFromPathAsync(process.ProcessPath, process.ProcessId, cancellationToken);
            var profileId = await _capabilityMapResolver.ResolveDefaultProfileIdAsync(fingerprint, cancellationToken);
            if (string.IsNullOrWhiteSpace(profileId))
            {
                return null;
            }

            return new ProfileVariantResolution(
                requestedProfileId,
                profileId,
                "fingerprint_default_profile",
                0.70d,
                FingerprintId: fingerprint.FingerprintId,
                ProcessId: process.ProcessId,
                ProcessName: process.ProcessName);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Fingerprint-based profile resolution failed for PID {Pid}", process.ProcessId);
            return null;
        }
    }

    private static ProfileVariantResolution BuildExeTargetFallbackResolution(
        string requestedProfileId,
        ProcessMetadata process)
    {
        var fallback = process.ExeTarget == ExeTarget.Sweaw ? "base_sweaw" : "base_swfoc";
        var fallbackReason = process.ExeTarget == ExeTarget.Sweaw
            ? "exe_target_sweaw_fallback"
            : "exe_target_swfoc_fallback";
        return new ProfileVariantResolution(
            requestedProfileId,
            fallback,
            fallbackReason,
            0.60d,
            ProcessId: process.ProcessId,
            ProcessName: process.ProcessName);
    }

    private async Task<IReadOnlyList<TrainerProfile>> LoadProfilesForLaunchContextAsync(CancellationToken cancellationToken)
    {
        if (_profileRepository is null)
        {
            return Array.Empty<TrainerProfile>();
        }

        try
        {
            var ids = await _profileRepository.ListAvailableProfilesAsync(cancellationToken);
            var profiles = new List<TrainerProfile>(ids.Count);
            foreach (var id in ids)
            {
                profiles.Add(await _profileRepository.ResolveInheritedProfileAsync(id, cancellationToken));
            }

            return profiles;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to load profiles for launch-context hint resolution.");
            return Array.Empty<TrainerProfile>();
        }
    }
}
