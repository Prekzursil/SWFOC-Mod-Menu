using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;

namespace SwfocTrainer.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task LoadProfilesAsync()
    {
        Profiles.Clear();
        var ids = await _profiles.ListAvailableProfilesAsync();
        foreach (var id in ids)
        {
            Profiles.Add(id);
        }

        var recommended = await RecommendProfileIdAsync();
        if (string.IsNullOrWhiteSpace(SelectedProfileId) || !Profiles.Contains(SelectedProfileId))
        {
            var resolvedProfileId = Profiles.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(recommended) && Profiles.Contains(recommended))
            {
                resolvedProfileId = recommended;
            }

            if (Profiles.Contains(UniversalProfileId))
            {
                resolvedProfileId = UniversalProfileId;
            }

            SelectedProfileId = resolvedProfileId;
        }

        Status = !string.IsNullOrWhiteSpace(recommended)
            ? $"Loaded {Profiles.Count} profiles (recommended: {recommended})"
            : $"Loaded {Profiles.Count} profiles";

        // Reduce friction: show actions for the selected profile immediately.
        if (!string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            await LoadActionsAsync();
            await LoadSpawnPresetsAsync();
        }
    }

    private async Task<string?> RecommendProfileIdAsync()
    {
        // Prefer attaching to whatever is actually running, and select a profile accordingly.
        // This avoids a common mistake: defaulting to base_sweaw when only swfoc.exe is running.
        try
        {
            var processes = await _processLocator.FindSupportedProcessesAsync();
            if (processes.Count == 0)
            {
                return null;
            }

            var bestRecommendation = await TryResolveLaunchContextRecommendationAsync(processes);
            if (!string.IsNullOrWhiteSpace(bestRecommendation))
            {
                return bestRecommendation;
            }

            return ResolveFallbackProfileRecommendation(processes);
        }
        catch
        {
            // If process enumeration fails (permissions/WMI), don't block the UI.
        }

        return null;
    }

    private async Task<IReadOnlyList<TrainerProfile>> LoadResolvedProfilesForLaunchContextAsync()
    {
        var ids = await _profiles.ListAvailableProfilesAsync();
        var profiles = new List<TrainerProfile>(ids.Count);
        foreach (var id in ids)
        {
            profiles.Add(await _profiles.ResolveInheritedProfileAsync(id));
        }

        return profiles;
    }

    private async Task AttachAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        try
        {
            var requestedProfileId = SelectedProfileId;
            var resolution = await ResolveAttachProfileAsync(requestedProfileId);

            Status = BuildAttachStartStatus(resolution.EffectiveProfileId, resolution.Variant);
            var session = await _runtime.AttachAsync(resolution.EffectiveProfileId);
            if (resolution.Variant is not null)
            {
                SelectedProfileId = resolution.EffectiveProfileId;
            }

            ApplyAttachSessionStatus(session);

            // Most people expect the Action dropdown to be usable immediately after attach.
            // Loading actions is profile-driven and doesn't require a process attach, but
            // doing it here avoids a common "Action is empty" confusion.
            await LoadActionsAsync();
            await LoadSpawnPresetsAsync();
            RefreshLiveOpsDiagnostics();
            await RefreshActionReliabilityAsync();
        }
        catch (Exception ex)
        {
            await HandleAttachFailureAsync(ex);
        }
    }

    private async Task<string> BuildAttachProcessHintAsync()
    {
        try
        {
            var all = await _processLocator.FindSupportedProcessesAsync();
            if (all.Count == 0)
            {
                return "Detected game processes: none. Ensure the game is running and try launching trainer as Administrator.";
            }

            return BuildAttachProcessHintSummary(all);
        }
        catch
        {
            return "Could not enumerate process diagnostics.";
        }
    }

    private static string BuildAttachProcessHintSummary(IReadOnlyList<ProcessMetadata> processes)
    {
        var summary = string.Join(", ", processes
            .Take(3)
            .Select(BuildAttachProcessHintSegment));
        var more = processes.Count > 3 ? $", +{processes.Count - 3} more" : string.Empty;
        return $"Detected game processes: {summary}{more}";
    }

    private static string BuildAttachProcessHintSegment(ProcessMetadata process)
    {
        var launchContext = process.LaunchContext;
        var cmd = ReadProcessMetadata(process, "commandLineAvailable", "False");
        var mods = ReadProcessMetadata(process, "steamModIdsDetected", string.Empty);
        var via = ReadProcessMetadata(process, "detectedVia", UnknownValue);
        var launch = launchContext?.LaunchKind.ToString() ?? "n/a";
        var recommended = launchContext?.Recommendation.ProfileId ?? string.Empty;
        var reason = launchContext?.Recommendation.ReasonCode ?? UnknownValue;
        var confidence = launchContext is null
            ? "0.00"
            : launchContext.Recommendation.Confidence.ToString("0.00");
        return $"{process.ProcessName}:{process.ProcessId}:{process.ExeTarget}:cmd={cmd}:mods={mods}:launch={launch}:rec={recommended}:{reason}:{confidence}:via={via}";
    }

    private static bool HasSteamModId(IEnumerable<ProcessMetadata> processes, string workshopId)
    {
        return processes.Any(process => ProcessHasSteamModId(process, workshopId));
    }

    private static bool ProcessHasSteamModId(ProcessMetadata process, string workshopId)
    {
        return HasLaunchContextModId(process, workshopId) ||
               HasCommandLineModId(process, workshopId) ||
               HasMetadataModId(process, workshopId);
    }

    private static bool HasLaunchContextModId(ProcessMetadata process, string workshopId)
    {
        return process.LaunchContext is not null &&
               process.LaunchContext.SteamModIds.Any(id => id.Equals(workshopId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasCommandLineModId(ProcessMetadata process, string workshopId)
    {
        return process.CommandLine?.Contains(workshopId, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool HasMetadataModId(ProcessMetadata process, string workshopId)
    {
        var ids = ReadProcessMetadata(process, "steamModIdsDetected", string.Empty);
        if (string.IsNullOrWhiteSpace(ids))
        {
            return false;
        }

        var split = ids.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return split.Any(id => id.Equals(workshopId, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsStarWarsGProcess(ProcessMetadata process)
    {
        if (process.ProcessName.Equals("StarWarsG", StringComparison.OrdinalIgnoreCase) ||
            process.ProcessName.Equals("StarWarsG.exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (process.Metadata is not null &&
            process.Metadata.TryGetValue("isStarWarsG", out var raw) &&
            bool.TryParse(raw, out var parsed))
        {
            return parsed;
        }

        return process.ProcessPath.Contains("StarWarsG.exe", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildProcessDiagnosticSummary(ProcessMetadata process)
    {
        var dependencySegment = BuildProcessDependencySegment(
            ReadProcessMetadata(process, "dependencyValidation", "Pass"),
            ReadProcessMetadata(process, "dependencyValidationMessage", string.Empty));

        return $"target={process.ExeTarget} | launch={BuildLaunchKindSegment(process)} | hostRole={BuildHostRoleSegment(process)} | score={BuildSelectionScoreSegment(process)} | module={BuildModuleSizeSegment(process)} | workshopMatches={BuildWorkshopMatchesSegment(process)} | cmdLine={ReadProcessMetadata(process, "commandLineAvailable", "False")} | mods={ReadProcessMods(process)} | {BuildModPathSegment(process)} | {BuildRecommendationSegment(process)} | {BuildResolvedVariantSegment(process)} | via={ReadProcessMetadata(process, "detectedVia", UnknownValue)} | {dependencySegment} | fallbackRate={ReadProcessMetadata(process, "fallbackHitRate", "n/a")} | unresolvedRate={ReadProcessMetadata(process, "unresolvedSymbolRate", "n/a")}";
    }

    private static string BuildLaunchKindSegment(ProcessMetadata process)
    {
        return process.LaunchContext?.LaunchKind.ToString() ?? "Unknown";
    }

    private static string BuildHostRoleSegment(ProcessMetadata process)
    {
        return process.HostRole.ToString().ToLowerInvariant();
    }

    private static string BuildSelectionScoreSegment(ProcessMetadata process)
    {
        return process.SelectionScore.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static string BuildModuleSizeSegment(ProcessMetadata process)
    {
        return process.MainModuleSize > 0
            ? process.MainModuleSize.ToString(CultureInfo.InvariantCulture)
            : "n/a";
    }

    private static string BuildWorkshopMatchesSegment(ProcessMetadata process)
    {
        return process.WorkshopMatchCount.ToString(CultureInfo.InvariantCulture);
    }

    private static string BuildModPathSegment(ProcessMetadata process)
    {
        var modPath = process.LaunchContext?.ModPathNormalized;
        return string.IsNullOrWhiteSpace(modPath) ? "modPath=none" : $"modPath={modPath}";
    }

    private static string BuildRecommendationSegment(ProcessMetadata process)
    {
        var recommendation = process.LaunchContext?.Recommendation;
        var profile = recommendation?.ProfileId ?? "none";
        var reason = recommendation?.ReasonCode ?? UnknownValue;
        var confidence = recommendation is null ? "0.00" : recommendation.Confidence.ToString("0.00");
        return $"rec={profile}:{reason}:{confidence}";
    }

    private static string BuildResolvedVariantSegment(ProcessMetadata process)
    {
        var resolvedVariant = ReadProcessMetadata(process, "resolvedVariant", "n/a");
        var reason = ReadProcessMetadata(process, "resolvedVariantReasonCode", "n/a");
        var confidence = ReadProcessMetadata(process, "resolvedVariantConfidence", "0.00");
        return $"variant={resolvedVariant}:{reason}:{confidence}";
    }

    private async Task<string?> TryResolveLaunchContextRecommendationAsync(IReadOnlyList<ProcessMetadata> processes)
    {
        var resolvedProfiles = await LoadResolvedProfilesForLaunchContextAsync();
        var contexts = processes
            .Select(process => process.LaunchContext ?? _launchContextResolver.Resolve(process, resolvedProfiles))
            .ToArray();

        return contexts
            .Where(context => !string.IsNullOrWhiteSpace(context.Recommendation.ProfileId))
            .OrderByDescending(context => context.Recommendation.Confidence)
            .ThenByDescending(context => context.LaunchKind == LaunchKind.Workshop || context.LaunchKind == LaunchKind.Mixed)
            .Select(context => context.Recommendation.ProfileId)
            .FirstOrDefault();
    }

    private static string? ResolveFallbackProfileRecommendation(IReadOnlyList<ProcessMetadata> processes)
    {
        // First priority: explicit mod IDs in command line or parsed metadata.
        if (HasSteamModId(processes, "3447786229"))
        {
            return "roe_3447786229_swfoc";
        }

        if (HasSteamModId(processes, "1397421866"))
        {
            return "aotr_1397421866_swfoc";
        }

        if (processes.Any(x => x.ExeTarget == ExeTarget.Swfoc) || processes.Any(IsStarWarsGProcess))
        {
            // FoC-safe default when StarWarsG is running but command-line hints are unavailable.
            return BaseSwfocProfileId;
        }

        return processes.Any(x => x.ExeTarget == ExeTarget.Sweaw)
            ? "base_sweaw"
            : null;
    }

    private async Task<(string EffectiveProfileId, ProfileVariantResolution? Variant)> ResolveAttachProfileAsync(string requestedProfileId)
    {
        if (!string.Equals(requestedProfileId, UniversalProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return (requestedProfileId, null);
        }

        var processes = await _processLocator.FindSupportedProcessesAsync();
        var variant = await _profileVariantResolver.ResolveAsync(requestedProfileId, processes, CancellationToken.None);
        return (variant.ResolvedProfileId, variant);
    }

    private static string BuildAttachStartStatus(string effectiveProfileId, ProfileVariantResolution? variant)
    {
        return variant is null
            ? $"Attaching using profile '{effectiveProfileId}'..."
            : $"Attaching using universal profile -> '{effectiveProfileId}' ({variant.ReasonCode}, conf={variant.Confidence:0.00})...";
    }

    private void ApplyAttachSessionStatus(AttachSession session)
    {
        RuntimeMode = session.Process.Mode;
        ResolvedSymbolsCount = session.Symbols.Symbols.Count;
        var signatureCount = session.Symbols.Symbols.Values.Count(x => x.Source == AddressSource.Signature);
        var fallbackCount = session.Symbols.Symbols.Values.Count(x => x.Source == AddressSource.Fallback);
        var healthyCount = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Healthy);
        var degradedCount = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Degraded);
        var unresolvedCount = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Unresolved || x.Address == nint.Zero);
        Status = $"Attached to PID {session.Process.ProcessId} ({session.Process.ProcessName}) | " +
                 $"{BuildProcessDiagnosticSummary(session.Process)} | symbols: sig={signatureCount}, fallback={fallbackCount}, healthy={healthyCount}, degraded={degradedCount}, unresolved={unresolvedCount}";
    }

    private async Task HandleAttachFailureAsync(Exception ex)
    {
        RuntimeMode = RuntimeMode.Unknown;
        ResolvedSymbolsCount = 0;
        var processHint = await BuildAttachProcessHintAsync();
        Status = $"Attach failed: {ex.Message}. {processHint}";
    }

    private static string ReadProcessMetadata(ProcessMetadata process, string key, string fallback)
    {
        if (process.Metadata is null || !process.Metadata.TryGetValue(key, out var value))
        {
            return fallback;
        }

        return value;
    }

    private static string ReadProcessMods(ProcessMetadata process)
    {
        var mods = ReadProcessMetadata(process, "steamModIdsDetected", string.Empty);
        return string.IsNullOrWhiteSpace(mods) ? "none" : mods;
    }

    private static string BuildProcessDependencySegment(string dependencyState, string dependencyMessage)
    {
        return dependencyState.Equals("Pass", StringComparison.OrdinalIgnoreCase) ||
               string.IsNullOrWhiteSpace(dependencyMessage)
            ? $"dependency={dependencyState}"
            : $"dependency={dependencyState} ({dependencyMessage})";
    }

    private static bool IsActionAvailableForCurrentSession(string actionId, ActionSpec spec, AttachSession session)
    {
        return IsActionAvailableForCurrentSession(actionId, spec, session, out _);
    }

    private static bool IsActionAvailableForCurrentSession(
        string actionId,
        ActionSpec spec,
        AttachSession session,
        out string? unavailableReason)
    {
        unavailableReason = ResolveActionUnavailableReason(actionId, spec, session);
        return string.IsNullOrWhiteSpace(unavailableReason);
    }

    private static string? ResolveActionUnavailableReason(
        string actionId,
        ActionSpec spec,
        AttachSession session)
    {
        if (IsDependencyDisabledAction(actionId, session))
        {
            return "action is disabled by dependency validation for this attachment.";
        }

        var requiredSymbol = ResolveRequiredSymbolForSessionGate(actionId, spec);
        if (string.IsNullOrWhiteSpace(requiredSymbol))
        {
            return null;
        }

        if (!session.Symbols.TryGetValue(requiredSymbol, out var symbolInfo) ||
            symbolInfo is null ||
            symbolInfo.Address == nint.Zero ||
            symbolInfo.HealthStatus == SymbolHealthStatus.Unresolved)
        {
            return $"required symbol '{requiredSymbol}' is unresolved for this attachment.";
        }

        return null;
    }

    private static bool IsDependencyDisabledAction(string actionId, AttachSession session)
    {
        if (session.Process.Metadata is null ||
            !session.Process.Metadata.TryGetValue("dependencyDisabledActions", out var disabledIdsRaw) ||
            string.IsNullOrWhiteSpace(disabledIdsRaw))
        {
            return false;
        }

        var disabledIds = disabledIdsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return disabledIds.Any(x => x.Equals(actionId, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveRequiredSymbolForSessionGate(string actionId, ActionSpec spec)
    {
        if (spec.ExecutionKind is not (ExecutionKind.Memory or ExecutionKind.CodePatch or ExecutionKind.Freeze or ExecutionKind.Sdk))
        {
            return null;
        }

        if (!spec.PayloadSchema.TryGetPropertyValue("required", out var requiredNode) || requiredNode is not JsonArray required)
        {
            return null;
        }

        var requiresSymbol = required.Any(x => string.Equals(x?.GetValue<string>(), PayloadKeySymbol, StringComparison.OrdinalIgnoreCase));
        if (!requiresSymbol)
        {
            return null;
        }

        return DefaultSymbolByActionId.TryGetValue(actionId, out var symbol) && !string.IsNullOrWhiteSpace(symbol)
            ? symbol
            : null;
    }

    private async Task<ActionSpec?> ResolveActionSpecAsync(string actionId)
    {
        if (_loadedActionSpecs.TryGetValue(actionId, out var actionSpec))
        {
            return actionSpec;
        }

        if (string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            return null;
        }

        try
        {
            var profile = await _profiles.ResolveInheritedProfileAsync(SelectedProfileId);
            _loadedActionSpecs = profile.Actions;
            return _loadedActionSpecs.TryGetValue(actionId, out actionSpec) ? actionSpec : null;
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> EnsureActionAvailableForCurrentSessionAsync(string actionId, string statusPrefix)
    {
        var session = _runtime.CurrentSession;
        if (session is null)
        {
            return true;
        }

        var actionSpec = await ResolveActionSpecAsync(actionId);
        if (actionSpec is null)
        {
            return true;
        }

        if (IsActionAvailableForCurrentSession(actionId, actionSpec, session, out var unavailableReason))
        {
            return true;
        }

        var reason = string.IsNullOrWhiteSpace(unavailableReason)
            ? "action is unavailable for this attachment."
            : unavailableReason;
        Status = $"✗ {statusPrefix}: {reason}";
        return false;
    }

    private async Task DetachAsync()
    {
        _orchestrator.UnfreezeAll();
        await _runtime.DetachAsync();
        ActionReliability.Clear();
        SelectedUnitTransactions.Clear();
        LiveOpsDiagnostics.Clear();
        Status = "Detached";
    }
}
