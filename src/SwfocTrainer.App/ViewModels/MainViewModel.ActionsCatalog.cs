using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

public partial class MainViewModel
{
    private async Task LoadActionsAsync()
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
                !IsActionAvailableForCurrentSession(actionId, actionSpec, _runtime.CurrentSession))
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

    private async Task ExecuteActionAsync()
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
                ? $"Action succeeded: {result.Message}{BuildDiagnosticsStatusSuffix(result)}"
                : $"Action failed: {result.Message}{BuildDiagnosticsStatusSuffix(result)}";
        }
        catch (Exception ex)
        {
            Status = $"Action failed: {ex.Message}";
        }
    }

    private void ApplyPayloadTemplateForSelectedAction()
    {
        if (!TryGetRequiredPayloadKeysForSelectedAction(out var required))
        {
            return;
        }

        var payload = BuildRequiredPayloadTemplate(SelectedActionId, required);
        ApplyActionSpecificPayloadDefaults(SelectedActionId, payload);

        // Only apply a template when it would actually help. Don't clobber the user's JSON with "{}".
        if (payload.Count == 0)
        {
            return;
        }

        PayloadJson = payload.ToJsonString(PrettyJson);
    }

    private bool TryGetRequiredPayloadKeysForSelectedAction(out JsonArray required)
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

    private static JsonObject BuildRequiredPayloadTemplate(string actionId, JsonArray required)
    {
        var payload = new JsonObject();

        foreach (var node in required)
        {
            var key = node?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            payload[key] = BuildRequiredPayloadValue(actionId, key);
        }

        return payload;
    }

    private static JsonNode? BuildRequiredPayloadValue(string actionId, string key)
    {
        return key switch
        {
            PayloadKeySymbol => JsonValue.Create(DefaultSymbolByActionId.TryGetValue(actionId, out var sym) ? sym : string.Empty),
            PayloadKeyIntValue => JsonValue.Create(actionId switch
            {
                ActionSetCredits => DefaultCreditsValue,
                ActionSetUnitCap => DefaultUnitCapValue,
                _ => 0
            }),
            PayloadKeyFloatValue => JsonValue.Create(1.0f),
            PayloadKeyBoolValue => JsonValue.Create(true),
            PayloadKeyEnable => JsonValue.Create(true),
            PayloadKeyFreeze => JsonValue.Create(!actionId.Equals(ActionUnfreezeSymbol, StringComparison.OrdinalIgnoreCase)),
            "patchBytes" => JsonValue.Create("90 90 90 90 90"),
            "originalBytes" => JsonValue.Create("48 8B 74 24 68"),
            "helperHookId" => JsonValue.Create(DefaultHelperHookByActionId.TryGetValue(actionId, out var hook) ? hook : actionId),
            "unitId" => JsonValue.Create(string.Empty),
            "entryMarker" => JsonValue.Create(string.Empty),
            "faction" => JsonValue.Create(string.Empty),
            "globalKey" => JsonValue.Create(string.Empty),
            "nodePath" => JsonValue.Create(string.Empty),
            "value" => JsonValue.Create(string.Empty),
            _ => JsonValue.Create(string.Empty)
        };
    }

    private static void ApplyActionSpecificPayloadDefaults(string actionId, JsonObject payload)
    {
        if (actionId.Equals(ActionSetCredits, StringComparison.OrdinalIgnoreCase))
        {
            payload[PayloadKeyLockCredits] = false;
        }

        // For freeze_symbol, include a default intValue so the user has a working template.
        if (actionId.Equals(ActionFreezeSymbol, StringComparison.OrdinalIgnoreCase) && !payload.ContainsKey(PayloadKeyIntValue))
        {
            payload[PayloadKeyIntValue] = DefaultCreditsValue;
        }
    }

    private async Task LoadCatalogAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        CatalogSummary.Clear();
        var catalog = await _catalog.LoadCatalogAsync(SelectedProfileId);
        foreach (var kv in catalog)
        {
            CatalogSummary.Add($"{kv.Key}: {kv.Value.Count}");
        }

        Status = $"Catalog loaded for {SelectedProfileId}";
    }

    private async Task DeployHelperAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var path = await _helper.DeployAsync(SelectedProfileId);
        Status = $"Helper deployed to: {path}";
    }

    private async Task VerifyHelperAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var ok = await _helper.VerifyAsync(SelectedProfileId);
        Status = ok ? "Helper verification passed" : "Helper verification failed";
    }

    private async Task CheckUpdatesAsync()
    {
        Updates.Clear();
        var updates = await _updates.CheckForUpdatesAsync();
        foreach (var profile in updates)
        {
            Updates.Add(profile);
        }

        Status = updates.Count > 0 ? $"Updates available for {updates.Count} profile(s)" : "No profile updates";
    }

    private async Task InstallUpdateAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var result = await _updates.InstallProfileTransactionalAsync(SelectedProfileId);
        if (!result.Succeeded)
        {
            Status = $"Profile update failed: {result.Message}";
            OpsArtifactSummary = $"install failed ({result.ReasonCode ?? UnknownValue})";
            return;
        }

        Status = $"Installed profile update: {result.InstalledPath}";
        var receiptPart = string.IsNullOrWhiteSpace(result.ReceiptPath) ? "no receipt" : result.ReceiptPath;
        var backupPart = string.IsNullOrWhiteSpace(result.BackupPath) ? "no backup" : result.BackupPath;
        OpsArtifactSummary = $"install receipt: {receiptPart} | backup: {backupPart}";
    }

    private async Task RollbackProfileUpdateAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var rollback = await _updates.RollbackLastInstallAsync(SelectedProfileId);
        if (!rollback.Restored)
        {
            Status = $"Rollback failed: {rollback.Message}";
            OpsArtifactSummary = $"rollback failed ({rollback.ReasonCode ?? UnknownValue})";
            return;
        }

        Status = rollback.Message;
        OpsArtifactSummary = $"rollback source: {rollback.BackupPath ?? "n/a"}";
    }

    private async Task ScaffoldModProfileAsync()
    {
        var launchLines = OnboardingLaunchSample
            .Split(Environment.NewLine, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
        var launchSamples = launchLines
            .Select(line => new ModLaunchSample(ProcessName: null, ProcessPath: null, CommandLine: line))
            .ToArray();

        var request = new ModOnboardingRequest(
            DraftProfileId: OnboardingDraftProfileId,
            DisplayName: OnboardingDisplayName,
            BaseProfileId: string.IsNullOrWhiteSpace(OnboardingBaseProfileId) ? BaseSwfocProfileId : OnboardingBaseProfileId,
            LaunchSamples: launchSamples,
            ProfileAliases: new[] { OnboardingDraftProfileId, OnboardingDisplayName },
            NamespaceRoot: OnboardingNamespaceRoot,
            Notes: "Generated by Mod Compatibility Studio");

        var result = await _modOnboarding.ScaffoldDraftProfileAsync(request, CancellationToken.None);
        var warnings = result.Warnings.Count == 0
            ? "none"
            : string.Join("; ", result.Warnings);

        OnboardingSummary = $"draft={result.ProfileId} output={result.OutputPath} workshop=[{string.Join(',', result.InferredWorkshopIds)}] hints=[{string.Join(',', result.InferredPathHints)}] warnings={warnings}";
        Status = $"Draft profile scaffolded: {result.ProfileId}";
    }

    private async Task ExportCalibrationArtifactAsync()
    {
        var profileId = SelectedProfileId ?? OnboardingDraftProfileId;
        var outputDir = Path.Combine(SupportBundleOutputDirectory, "calibration");
        Directory.CreateDirectory(outputDir);

        var request = new ModCalibrationArtifactRequest(
            ProfileId: profileId,
            OutputDirectory: outputDir,
            Session: _runtime.CurrentSession,
            OperatorNotes: CalibrationNotes);

        var result = await _modCalibration.ExportCalibrationArtifactAsync(request);
        OpsArtifactSummary = result.ArtifactPath;
        Status = result.Succeeded
            ? $"Calibration artifact exported: {result.ArtifactPath}"
            : "Calibration artifact export failed.";
    }

    private async Task BuildCompatibilityReportAsync()
    {
        var profileId = SelectedProfileId ?? OnboardingDraftProfileId;
        var profile = await _profiles.ResolveInheritedProfileAsync(profileId);
        var report = await _modCalibration.BuildCompatibilityReportAsync(
            profile,
            _runtime.CurrentSession);

        ModCompatibilityRows.Clear();
        foreach (var action in report.Actions)
        {
            ModCompatibilityRows.Add($"{action.ActionId} | {action.State} | {action.ReasonCode} | {action.Confidence:0.00}");
        }

        ModCompatibilitySummary = $"promotionReady={report.PromotionReady} dependency={report.DependencyStatus} unresolvedCritical={report.UnresolvedCriticalSymbols}";
        Status = $"Compatibility report generated for {profileId}";
    }

    private async Task ExportSupportBundleAsync()
    {
        var result = await _supportBundles.ExportAsync(new SupportBundleRequest(
            OutputDirectory: SupportBundleOutputDirectory,
            ProfileId: SelectedProfileId,
            Notes: "Exported from Profiles & Updates tab"));

        OpsArtifactSummary = result.BundlePath;
        Status = result.Succeeded
            ? $"Support bundle exported: {result.BundlePath}"
            : "Support bundle export failed.";
    }

    private async Task ExportTelemetrySnapshotAsync()
    {
        var telemetryDir = Path.Combine(SupportBundleOutputDirectory, "telemetry");
        Directory.CreateDirectory(telemetryDir);
        var path = await _telemetry.ExportSnapshotAsync(telemetryDir);
        OpsArtifactSummary = path;
        Status = $"Telemetry snapshot exported: {path}";
    }
}
