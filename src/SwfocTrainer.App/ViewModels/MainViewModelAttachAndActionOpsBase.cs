using System.Text.Json.Nodes;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

public abstract class MainViewModelAttachAndActionOpsBase : MainViewModelQuickActionsBase
{
    protected MainViewModelAttachAndActionOpsBase(MainViewModelDependencies dependencies)
        : base(dependencies)
    {
    }

    protected async Task LoadProfilesAsync()
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
    protected async Task<string?> RecommendProfileIdAsync()
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

            return MainViewModelAttachHelpers.ResolveFallbackProfileRecommendation(processes, BaseSwfocProfileId);
        }
        catch
        {
            // If process enumeration fails (permissions/WMI), don't block the UI.
        }

        return null;
    }
    protected async Task<IReadOnlyList<TrainerProfile>> LoadResolvedProfilesForLaunchContextAsync()
    {
        var ids = await _profiles.ListAvailableProfilesAsync();
        var profiles = new List<TrainerProfile>(ids.Count);
        foreach (var id in ids)
        {
            profiles.Add(await _profiles.ResolveInheritedProfileAsync(id));
        }

        return profiles;
    }
    protected async Task AttachAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        try
        {
            var requestedProfileId = SelectedProfileId;
            var resolution = await ResolveAttachProfileAsync(requestedProfileId);

            Status = MainViewModelAttachHelpers.BuildAttachStartStatus(resolution.EffectiveProfileId, resolution.Variant);
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
    protected async Task<string> BuildAttachProcessHintAsync()
    {
        try
        {
            var all = await _processLocator.FindSupportedProcessesAsync();
            if (all.Count == 0)
            {
                return "Detected game processes: none. Ensure the game is running and try launching trainer as Administrator.";
            }

            return MainViewModelAttachHelpers.BuildAttachProcessHintSummary(all, UnknownValue);
        }
        catch
        {
            return "Could not enumerate process diagnostics.";
        }
    }
    protected async Task<string?> TryResolveLaunchContextRecommendationAsync(IReadOnlyList<ProcessMetadata> processes)
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
    protected async Task<(string EffectiveProfileId, ProfileVariantResolution? Variant)> ResolveAttachProfileAsync(string requestedProfileId)
    {
        if (!string.Equals(requestedProfileId, UniversalProfileId, StringComparison.OrdinalIgnoreCase))
        {
            return (requestedProfileId, null);
        }

        var processes = await _processLocator.FindSupportedProcessesAsync();
        var variant = await _profileVariantResolver.ResolveAsync(requestedProfileId, processes, CancellationToken.None);
        return (variant.ResolvedProfileId, variant);
    }
    protected void ApplyAttachSessionStatus(AttachSession session)
    {
        RuntimeMode = session.Process.Mode;
        ResolvedSymbolsCount = session.Symbols.Symbols.Count;
        var signatureCount = session.Symbols.Symbols.Values.Count(x => x.Source == AddressSource.Signature);
        var fallbackCount = session.Symbols.Symbols.Values.Count(x => x.Source == AddressSource.Fallback);
        var healthyCount = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Healthy);
        var degradedCount = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Degraded);
        var unresolvedCount = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Unresolved || x.Address == nint.Zero);
        Status = $"Attached to PID {session.Process.ProcessId} ({session.Process.ProcessName}) | " +
                 $"{MainViewModelDiagnostics.BuildProcessDiagnosticSummary(session.Process, UnknownValue)} | symbols: sig={signatureCount}, fallback={fallbackCount}, healthy={healthyCount}, degraded={degradedCount}, unresolved={unresolvedCount}";
    }
    protected async Task HandleAttachFailureAsync(Exception ex)
    {
        RuntimeMode = RuntimeMode.Unknown;
        ResolvedSymbolsCount = 0;
        var processHint = await BuildAttachProcessHintAsync();
        Status = $"Attach failed: {ex.Message}. {processHint}";
    }
    protected async Task<ActionSpec?> ResolveActionSpecAsync(string actionId)
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
    protected override async Task<bool> EnsureActionAvailableForCurrentSessionAsync(string actionId, string statusPrefix)
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

        if (MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
                actionId,
                actionSpec,
                session,
                DefaultSymbolByActionId,
                out var unavailableReason))
        {
            return true;
        }

        var reason = string.IsNullOrWhiteSpace(unavailableReason)
            ? "action is unavailable for this attachment."
            : unavailableReason;
        Status = $"✗ {statusPrefix}: {reason}";
        return false;
    }

    // Kept as a compatibility shim for reflection-based tests that assert gating semantics.
    private static string? ResolveActionUnavailableReason(string actionId, ActionSpec spec, AttachSession session)
    {
        MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
            actionId,
            spec,
            session,
            DefaultSymbolByActionId,
            out var unavailableReason);
        return unavailableReason;
    }
    protected async Task DetachAsync()
    {
        _orchestrator.UnfreezeAll();
        await _runtime.DetachAsync();
        ActionReliability.Clear();
        SelectedUnitTransactions.Clear();
        LiveOpsDiagnostics.Clear();
        Status = "Detached";
    }
    protected async Task LoadActionsAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        Actions.Clear();
        var profile = await _profiles.ResolveInheritedProfileAsync(SelectedProfileId);
        _loadedActionSpecs = profile.Actions;
        var filteredOut = 0;
        foreach (var (actionId, actionSpec) in profile.Actions.OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (_runtime.CurrentSession is not null &&
                !MainViewModelAttachHelpers.IsActionAvailableForCurrentSession(
                    actionId,
                    actionSpec,
                    _runtime.CurrentSession,
                    DefaultSymbolByActionId,
                    out _))
            {
                filteredOut++;
                continue;
            }

            Actions.Add(actionId);
        }

        SelectedActionId = Actions.FirstOrDefault() ?? string.Empty;
        Status = filteredOut > 0
            ? $"Loaded {Actions.Count} actions ({filteredOut} hidden: unresolved symbols)"
            : $"Loaded {Actions.Count} actions";

        if (_runtime.IsAttached)
        {
            await RefreshActionReliabilityAsync();
        }
    }
    protected async Task ExecuteActionAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        try
        {
            JsonObject payloadNode;
            try
            {
                payloadNode = JsonNode.Parse(PayloadJson) as JsonObject ?? new JsonObject();
            }
            catch (Exception ex)
            {
                Status = $"Invalid payload JSON: {ex.Message}";
                return;
            }

            var result = await _orchestrator.ExecuteAsync(
                SelectedProfileId,
                SelectedActionId,
                payloadNode,
                RuntimeMode,
                BuildActionContext(SelectedActionId));
            Status = result.Succeeded
                ? $"Action succeeded: {result.Message}{MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result)}"
                : $"Action failed: {result.Message}{MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result)}";
        }
        catch (Exception ex)
        {
            Status = $"Action failed: {ex.Message}";
        }
    }
    protected override void ApplyPayloadTemplateForSelectedAction()
    {
        if (!TryGetRequiredPayloadKeysForSelectedAction(out var required))
        {
            return;
        }

        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            SelectedActionId,
            required,
            DefaultSymbolByActionId,
            DefaultHelperHookByActionId);
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults(SelectedActionId, payload);

        // Only apply a template when it would actually help. Don't clobber the user's JSON with "{}".
        if (payload.Count == 0)
        {
            return;
        }

        PayloadJson = payload.ToJsonString(PrettyJson);
    }
    protected bool TryGetRequiredPayloadKeysForSelectedAction(out JsonArray required)
    {
        required = new JsonArray();
        if (string.IsNullOrWhiteSpace(SelectedActionId))
        {
            return false;
        }

        if (!_loadedActionSpecs.TryGetValue(SelectedActionId, out var action))
        {
            return false;
        }

        if (!action.PayloadSchema.TryGetPropertyValue("required", out var requiredNode) || requiredNode is not JsonArray requiredKeys)
        {
            return false;
        }

        required = requiredKeys;
        return true;
    }
}
