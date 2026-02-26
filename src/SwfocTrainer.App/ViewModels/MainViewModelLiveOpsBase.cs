using System.Collections.Generic;
using System.Linq;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

public abstract class MainViewModelLiveOpsBase : MainViewModelBindableMembersBase
{
    protected MainViewModelLiveOpsBase(MainViewModelDependencies dependencies)
        : base(dependencies)
    {
    }

    protected abstract Task<bool> EnsureActionAvailableForCurrentSessionAsync(string actionId, string statusPrefix);

    protected async Task RefreshActionReliabilityAsync()
    {
        ActionReliability.Clear();
        if (SelectedProfileId is null || _runtime.CurrentSession is null)
        {
            return;
        }

        RefreshLiveOpsDiagnostics();

        var profile = await _profiles.ResolveInheritedProfileAsync(SelectedProfileId);
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog = null;
        try
        {
            catalog = await _catalog.LoadCatalogAsync(SelectedProfileId);
        }
        catch
        {
            // Catalog is optional for reliability scoring.
        }

        var reliability = _actionReliability.Evaluate(profile, _runtime.CurrentSession, catalog);
        foreach (var item in reliability)
        {
            ActionReliability.Add(new ActionReliabilityViewItem(
                item.ActionId,
                item.State.ToString().ToLowerInvariant(),
                item.ReasonCode,
                item.Confidence,
                item.Detail ?? string.Empty));
        }
    }

    protected void RefreshLiveOpsDiagnostics()
    {
        LiveOpsDiagnostics.Clear();
        var session = _runtime.CurrentSession;
        if (session is null)
        {
            return;
        }

        var metadata = session.Process.Metadata;
        AddLiveOpsModeDiagnostics(session, metadata);
        AddLiveOpsLaunchDiagnostics(session, metadata);
        AddLiveOpsDependencyDiagnostics(metadata);
        AddLiveOpsSymbolDiagnostics(session);
    }

    private void AddLiveOpsModeDiagnostics(AttachSession session, IReadOnlyDictionary<string, string>? metadata)
    {
        LiveOpsDiagnostics.Add($"mode: {session.Process.Mode}");
        if (metadata is not null && metadata.TryGetValue("runtimeModeReasonCode", out var modeReason))
        {
            LiveOpsDiagnostics.Add($"mode_reason: {modeReason}");
        }
    }

    private void AddLiveOpsLaunchDiagnostics(AttachSession session, IReadOnlyDictionary<string, string>? metadata)
    {
        LiveOpsDiagnostics.Add($"launch: {session.Process.LaunchContext?.LaunchKind ?? LaunchKind.Unknown}");
        LiveOpsDiagnostics.Add($"recommendation: {session.Process.LaunchContext?.Recommendation.ProfileId ?? "none"}");
        if (metadata is not null && metadata.TryGetValue("resolvedVariant", out var resolvedVariant))
        {
            var reason = GetMetadataValueOrDefault(metadata, "resolvedVariantReasonCode", UnknownValue);
            var confidence = GetMetadataValueOrDefault(metadata, "resolvedVariantConfidence", "0.00");
            LiveOpsDiagnostics.Add($"variant: {resolvedVariant} ({reason}, conf={confidence})");
        }
    }

    private void AddLiveOpsDependencyDiagnostics(IReadOnlyDictionary<string, string>? metadata)
    {
        if (metadata is null || !metadata.TryGetValue("dependencyValidation", out var dependency))
        {
            return;
        }

        var dependencyMessage = GetMetadataValueOrDefault(metadata, "dependencyValidationMessage", string.Empty);
        LiveOpsDiagnostics.Add(MainViewModelDiagnostics.BuildDependencyDiagnostic(dependency, dependencyMessage));
    }

    private void AddLiveOpsSymbolDiagnostics(AttachSession session)
    {
        var healthy = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Healthy);
        var degraded = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Degraded);
        var unresolved = session.Symbols.Symbols.Values.Count(x => x.HealthStatus == SymbolHealthStatus.Unresolved || x.Address == nint.Zero);
        LiveOpsDiagnostics.Add($"symbols: healthy={healthy}, degraded={degraded}, unresolved={unresolved}");
    }

    private static string GetMetadataValueOrDefault(
        IReadOnlyDictionary<string, string> metadata,
        string key,
        string fallback)
    {
        return metadata.TryGetValue(key, out var value) ? value : fallback;
    }

    protected async Task CaptureSelectedUnitBaselineAsync()
    {
        if (!_runtime.IsAttached)
        {
            Status = "✗ Not attached to game.";
            return;
        }

        try
        {
            var snapshot = await _selectedUnitTransactions.CaptureAsync();
            ApplyDraftFromSnapshot(snapshot);
            RefreshSelectedUnitTransactions();
            Status = $"Selected-unit baseline captured at {snapshot.CapturedAt:HH:mm:ss} UTC.";
        }
        catch (Exception ex)
        {
            Status = $"✗ Capture selected-unit baseline failed: {ex.Message}";
        }
    }

    protected async Task ApplySelectedUnitDraftAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var draftResult = BuildSelectedUnitDraft();
        if (!draftResult.Succeeded)
        {
            Status = $"✗ {draftResult.Message}";
            return;
        }

        var result = await _selectedUnitTransactions.ApplyAsync(SelectedProfileId, draftResult.Draft!, RuntimeMode);
        RefreshSelectedUnitTransactions();
        if (result.Succeeded)
        {
            var latest = await _selectedUnitTransactions.CaptureAsync();
            ApplyDraftFromSnapshot(latest);
        }

        Status = result.Succeeded
            ? $"✓ Selected-unit transaction applied ({result.TransactionId})."
            : $"✗ Selected-unit apply failed: {result.Message}";
    }

    protected async Task RevertSelectedUnitTransactionAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var result = await _selectedUnitTransactions.RevertLastAsync(SelectedProfileId, RuntimeMode);
        RefreshSelectedUnitTransactions();
        if (result.Succeeded)
        {
            var latest = await _selectedUnitTransactions.CaptureAsync();
            ApplyDraftFromSnapshot(latest);
        }

        Status = result.Succeeded
            ? $"✓ Reverted selected-unit transaction ({result.TransactionId})."
            : $"✗ Revert failed: {result.Message}";
    }

    protected async Task RestoreSelectedUnitBaselineAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        var result = await _selectedUnitTransactions.RestoreBaselineAsync(SelectedProfileId, RuntimeMode);
        RefreshSelectedUnitTransactions();
        if (result.Succeeded)
        {
            var latest = await _selectedUnitTransactions.CaptureAsync();
            ApplyDraftFromSnapshot(latest);
        }

        Status = result.Succeeded
            ? $"✓ Selected-unit baseline restored ({result.TransactionId})."
            : $"✗ Baseline restore failed: {result.Message}";
    }

    protected async Task LoadSpawnPresetsAsync()
    {
        if (SelectedProfileId is null)
        {
            return;
        }

        SpawnPresets.Clear();
        var presets = await _spawnPresets.LoadPresetsAsync(SelectedProfileId);
        foreach (var preset in presets)
        {
            SpawnPresets.Add(new SpawnPresetViewItem(
                preset.Id,
                preset.Name,
                preset.UnitId,
                preset.Faction,
                preset.EntryMarker,
                preset.DefaultQuantity,
                preset.DefaultDelayMs,
                preset.Description ?? string.Empty));
        }

        SelectedSpawnPreset = SpawnPresets.FirstOrDefault();
        Status = $"Loaded {SpawnPresets.Count} spawn preset(s).";
    }

    protected async Task RunSpawnBatchAsync()
    {
        var batchInputs = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                SelectedProfileId,
                SelectedSpawnPreset,
                RuntimeMode,
                SpawnQuantity,
                SpawnDelayMs));
        if (!batchInputs.Succeeded)
        {
            Status = batchInputs.FailureStatus;
            return;
        }

        var preset = batchInputs.SelectedPreset!.ToCorePreset();
        var plan = _spawnPresets.BuildBatchPlan(
            batchInputs.ProfileId,
            preset,
            batchInputs.Quantity,
            batchInputs.DelayMs,
            SelectedFaction,
            SelectedEntryMarker,
            SpawnStopOnFailure);

        var result = await _spawnPresets.ExecuteBatchAsync(batchInputs.ProfileId, plan, RuntimeMode);
        Status = result.Succeeded
            ? $"✓ {result.Message}"
            : $"✗ {result.Message}";
    }

    protected void ApplyDraftFromSnapshot(SelectedUnitSnapshot snapshot)
    {
        SelectedUnitHp = snapshot.Hp.ToString(DecimalPrecision3);
        SelectedUnitShield = snapshot.Shield.ToString(DecimalPrecision3);
        SelectedUnitSpeed = snapshot.Speed.ToString(DecimalPrecision3);
        SelectedUnitDamageMultiplier = snapshot.DamageMultiplier.ToString(DecimalPrecision3);
        SelectedUnitCooldownMultiplier = snapshot.CooldownMultiplier.ToString(DecimalPrecision3);
        SelectedUnitVeterancy = snapshot.Veterancy.ToString();
        SelectedUnitOwnerFaction = snapshot.OwnerFaction.ToString();
    }

    protected void RefreshSelectedUnitTransactions()
    {
        SelectedUnitTransactions.Clear();
        foreach (var item in _selectedUnitTransactions.History.OrderByDescending(x => x.Timestamp))
        {
            SelectedUnitTransactions.Add(new SelectedUnitTransactionViewItem(
                item.TransactionId,
                item.Timestamp,
                item.IsRollback,
                item.Message,
                string.Join(",", item.AppliedActions)));
        }
    }

    private DraftBuildResult BuildSelectedUnitDraft()
    {
        var floatInputs = new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs(
            SelectedUnitHp,
            SelectedUnitShield,
            SelectedUnitSpeed,
            SelectedUnitDamageMultiplier,
            SelectedUnitCooldownMultiplier);

        if (!MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(
                floatInputs,
                out var floatValues,
                out var error))
        {
            return DraftBuildResult.Failed(error);
        }

        if (!MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitIntValues(
                SelectedUnitVeterancy,
                SelectedUnitOwnerFaction,
                out var veterancy,
                out var ownerFaction,
                out error))
        {
            return DraftBuildResult.Failed(error);
        }

        var draft = new SelectedUnitDraft(
            Hp: floatValues.Hp,
            Shield: floatValues.Shield,
            Speed: floatValues.Speed,
            DamageMultiplier: floatValues.Damage,
            CooldownMultiplier: floatValues.Cooldown,
            Veterancy: veterancy,
            OwnerFaction: ownerFaction);

        return draft.IsEmpty
            ? DraftBuildResult.Failed("No selected-unit values entered.")
            : DraftBuildResult.FromDraft(draft);
    }

    protected IReadOnlyDictionary<string, object?> BuildActionContext(string actionId)
    {
        var reliability = ActionReliability.FirstOrDefault(x => x.ActionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        return new Dictionary<string, object?>
        {
            ["reliabilityState"] = reliability?.State ?? UnknownValue,
            ["reliabilityReasonCode"] = reliability?.ReasonCode ?? UnknownValue,
            ["bundleGateResult"] = MainViewModelDiagnostics.ResolveBundleGateResult(reliability, UnknownValue)
        };
    }
}
