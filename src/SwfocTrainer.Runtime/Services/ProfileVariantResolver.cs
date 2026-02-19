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
        ILogger<ProfileVariantResolver> logger,
        IProfileRepository? profileRepository = null,
        IProcessLocator? processLocator = null,
        IBinaryFingerprintService? fingerprintService = null,
        ICapabilityMapResolver? capabilityMapResolver = null)
    {
        _launchContextResolver = launchContextResolver;
        _logger = logger;
        _profileRepository = profileRepository;
        _processLocator = processLocator;
        _fingerprintService = fingerprintService;
        _capabilityMapResolver = capabilityMapResolver;
    }

    public async Task<ProfileVariantResolution> ResolveAsync(
        string requestedProfileId,
        IReadOnlyList<ProcessMetadata>? processes = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(requestedProfileId, UniversalProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return new ProfileVariantResolution(
                requestedProfileId,
                requestedProfileId,
                "explicit_profile_selection",
                1.0d);
        }

        var candidates = processes;
        if (candidates is null)
        {
            candidates = _processLocator is null
                ? Array.Empty<ProcessMetadata>()
                : await _processLocator.FindSupportedProcessesAsync(cancellationToken);
        }

        if (candidates.Count == 0)
        {
            return new ProfileVariantResolution(
                requestedProfileId,
                "base_swfoc",
                "no_process_detected",
                0.40d);
        }

        var launchProfiles = await LoadProfilesForLaunchContextAsync(cancellationToken);
        var best = candidates
            .Select(process =>
            {
                var context = process.LaunchContext ?? _launchContextResolver.Resolve(process, launchProfiles);
                return new { Process = process, Context = context };
            })
            .OrderByDescending(x => x.Context.Recommendation.Confidence)
            .ThenByDescending(x => x.Context.LaunchKind == LaunchKind.Workshop || x.Context.LaunchKind == LaunchKind.Mixed)
            .ThenByDescending(x => x.Process.ExeTarget == ExeTarget.Swfoc)
            .First();

        var recommended = best.Context.Recommendation.ProfileId;
        if (!string.IsNullOrWhiteSpace(recommended))
        {
            return new ProfileVariantResolution(
                requestedProfileId,
                recommended,
                best.Context.Recommendation.ReasonCode,
                best.Context.Recommendation.Confidence,
                ProcessId: best.Process.ProcessId,
                ProcessName: best.Process.ProcessName);
        }

        if (_fingerprintService is not null && _capabilityMapResolver is not null)
        {
            try
            {
                var fingerprint = await _fingerprintService.CaptureFromPathAsync(best.Process.ProcessPath, best.Process.ProcessId, cancellationToken);
                var profileId = await _capabilityMapResolver.ResolveDefaultProfileIdAsync(fingerprint, cancellationToken);
                if (!string.IsNullOrWhiteSpace(profileId))
                {
                    return new ProfileVariantResolution(
                        requestedProfileId,
                        profileId,
                        "fingerprint_default_profile",
                        0.70d,
                        FingerprintId: fingerprint.FingerprintId,
                        ProcessId: best.Process.ProcessId,
                        ProcessName: best.Process.ProcessName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Fingerprint-based profile resolution failed for PID {Pid}", best.Process.ProcessId);
            }
        }

        var fallback = best.Process.ExeTarget == ExeTarget.Sweaw ? "base_sweaw" : "base_swfoc";
        var fallbackReason = best.Process.ExeTarget == ExeTarget.Sweaw
            ? "exe_target_sweaw_fallback"
            : "exe_target_swfoc_fallback";

        return new ProfileVariantResolution(
            requestedProfileId,
            fallback,
            fallbackReason,
            0.60d,
            ProcessId: best.Process.ProcessId,
            ProcessName: best.Process.ProcessName);
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
