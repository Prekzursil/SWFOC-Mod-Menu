using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;

namespace SwfocTrainer.App.ViewModels;

public sealed partial class MainViewModel
{
    private async Task RefreshActionReliabilityAsync()
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

    private void RefreshLiveOpsDiagnostics()
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
            var reason = GetMetadataValueOrDefault(metadata, "resolvedVariantReasonCode", "unknown");
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
        LiveOpsDiagnostics.Add(BuildDependencyDiagnostic(dependency, dependencyMessage));
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

    private static string BuildDependencyDiagnostic(string dependency, string dependencyMessage)
    {
        return string.IsNullOrWhiteSpace(dependencyMessage)
            ? $"dependency: {dependency}"
            : $"dependency: {dependency} ({dependencyMessage})";
    }

    private async Task CaptureSelectedUnitBaselineAsync()
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

    private async Task ApplySelectedUnitDraftAsync()
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

    private async Task RevertSelectedUnitTransactionAsync()
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

    private async Task RestoreSelectedUnitBaselineAsync()
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

    private async Task LoadSpawnPresetsAsync()
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

    private async Task RunSpawnBatchAsync()
    {
        if (!TryResolveSpawnBatchSelection(out var profileId, out var selectedPreset))
        {
            return;
        }

        if (!TryValidateSpawnRuntimeMode() ||
            !TryParseSpawnQuantity(out var quantity) ||
            !TryParseSpawnDelayMs(out var delayMs))
        {
            return;
        }

        var preset = selectedPreset.ToCorePreset();
        var plan = _spawnPresets.BuildBatchPlan(
            profileId,
            preset,
            quantity,
            delayMs,
            SelectedFaction,
            SelectedEntryMarker,
            SpawnStopOnFailure);

        var result = await _spawnPresets.ExecuteBatchAsync(profileId, plan, RuntimeMode);
        Status = result.Succeeded
            ? $"✓ {result.Message}"
            : $"✗ {result.Message}";
    }

    private bool TryResolveSpawnBatchSelection(out string profileId, out SpawnPresetViewItem selectedPreset)
    {
        profileId = SelectedProfileId ?? string.Empty;
        selectedPreset = SelectedSpawnPreset!;
        return SelectedProfileId is not null && SelectedSpawnPreset is not null;
    }

    private bool TryValidateSpawnRuntimeMode()
    {
        if (RuntimeMode != RuntimeMode.Unknown)
        {
            return true;
        }

        Status = "✗ Spawn batch blocked: runtime mode is unknown.";
        return false;
    }

    private bool TryParseSpawnQuantity(out int quantity)
    {
        if (int.TryParse(SpawnQuantity, out quantity) && quantity > 0)
        {
            return true;
        }

        Status = "✗ Invalid spawn quantity.";
        quantity = 0;
        return false;
    }

    private bool TryParseSpawnDelayMs(out int delayMs)
    {
        if (int.TryParse(SpawnDelayMs, out delayMs) && delayMs >= 0)
        {
            return true;
        }

        Status = "✗ Invalid spawn delay (ms).";
        delayMs = 0;
        return false;
    }

    private void ApplyDraftFromSnapshot(SelectedUnitSnapshot snapshot)
    {
        SelectedUnitHp = snapshot.Hp.ToString("0.###");
        SelectedUnitShield = snapshot.Shield.ToString("0.###");
        SelectedUnitSpeed = snapshot.Speed.ToString("0.###");
        SelectedUnitDamageMultiplier = snapshot.DamageMultiplier.ToString("0.###");
        SelectedUnitCooldownMultiplier = snapshot.CooldownMultiplier.ToString("0.###");
        SelectedUnitVeterancy = snapshot.Veterancy.ToString();
        SelectedUnitOwnerFaction = snapshot.OwnerFaction.ToString();
    }

    private void RefreshSelectedUnitTransactions()
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
        if (!TryParseSelectedUnitDraftValues(
                out var hp,
                out var shield,
                out var speed,
                out var damage,
                out var cooldown,
                out var veterancy,
                out var ownerFaction,
                out var error))
        {
            return DraftBuildResult.Failed(error);
        }

        var draft = new SelectedUnitDraft(
            Hp: hp,
            Shield: shield,
            Speed: speed,
            DamageMultiplier: damage,
            CooldownMultiplier: cooldown,
            Veterancy: veterancy,
            OwnerFaction: ownerFaction);

        return draft.IsEmpty
            ? DraftBuildResult.Failed("No selected-unit values entered.")
            : DraftBuildResult.FromDraft(draft);
    }

    private bool TryParseSelectedUnitDraftValues(
        out float? hp,
        out float? shield,
        out float? speed,
        out float? damage,
        out float? cooldown,
        out int? veterancy,
        out int? ownerFaction,
        out string error)
    {
        if (!TryParseSelectedUnitFloatValues(out hp, out shield, out speed, out damage, out cooldown, out error))
        {
            veterancy = null;
            ownerFaction = null;
            return false;
        }

        return TryParseSelectedUnitIntValues(out veterancy, out ownerFaction, out error);
    }

    private bool TryParseSelectedUnitFloatValues(
        out float? hp,
        out float? shield,
        out float? speed,
        out float? damage,
        out float? cooldown,
        out string error)
    {
        hp = null;
        shield = null;
        speed = null;
        damage = null;
        cooldown = null;

        if (!TryParseSelectedUnitFloat(SelectedUnitHp, "HP must be a number.", out hp, out error))
        {
            return false;
        }

        if (!TryParseSelectedUnitFloat(SelectedUnitShield, "Shield must be a number.", out shield, out error))
        {
            return false;
        }

        if (!TryParseSelectedUnitFloat(SelectedUnitSpeed, "Speed must be a number.", out speed, out error))
        {
            return false;
        }

        if (!TryParseSelectedUnitFloat(SelectedUnitDamageMultiplier, "Damage multiplier must be a number.", out damage, out error))
        {
            return false;
        }

        return TryParseSelectedUnitFloat(SelectedUnitCooldownMultiplier, "Cooldown multiplier must be a number.", out cooldown, out error);
    }

    private bool TryParseSelectedUnitIntValues(out int? veterancy, out int? ownerFaction, out string error)
    {
        veterancy = null;
        ownerFaction = null;
        if (!TryParseSelectedUnitInt(SelectedUnitVeterancy, "Veterancy must be an integer.", out veterancy, out error))
        {
            return false;
        }

        return TryParseSelectedUnitInt(SelectedUnitOwnerFaction, "Owner faction must be an integer.", out ownerFaction, out error);
    }

    private static bool TryParseSelectedUnitFloat(string input, string errorMessage, out float? value, out string error)
    {
        if (TryParseOptionalFloat(input, out value))
        {
            error = string.Empty;
            return true;
        }

        error = errorMessage;
        return false;
    }

    private static bool TryParseSelectedUnitInt(string input, string errorMessage, out int? value, out string error)
    {
        if (TryParseOptionalInt(input, out value))
        {
            error = string.Empty;
            return true;
        }

        error = errorMessage;
        return false;
    }

    private static bool TryParseOptionalFloat(string input, out float? value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = null;
            return true;
        }

        if (float.TryParse(input, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static bool TryParseOptionalInt(string input, out int? value)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            value = null;
            return true;
        }

        if (int.TryParse(input, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private sealed record DraftBuildResult(bool Succeeded, string Message, SelectedUnitDraft? Draft)
    {
        public static DraftBuildResult Failed(string message) => new(false, message, null);

        public static DraftBuildResult FromDraft(SelectedUnitDraft draft) => new(true, "ok", draft);
    }

}
