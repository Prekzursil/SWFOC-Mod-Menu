using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Models;

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

    private void ApplyDraftFromSnapshot(SelectedUnitSnapshot snapshot)
    {
        SelectedUnitHp = snapshot.Hp.ToString(DecimalPrecision3);
        SelectedUnitShield = snapshot.Shield.ToString(DecimalPrecision3);
        SelectedUnitSpeed = snapshot.Speed.ToString(DecimalPrecision3);
        SelectedUnitDamageMultiplier = snapshot.DamageMultiplier.ToString(DecimalPrecision3);
        SelectedUnitCooldownMultiplier = snapshot.CooldownMultiplier.ToString(DecimalPrecision3);
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

    private IReadOnlyDictionary<string, object?> BuildActionContext(string actionId)
    {
        var reliability = ActionReliability.FirstOrDefault(x => x.ActionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        return new Dictionary<string, object?>
        {
            ["reliabilityState"] = reliability?.State ?? UnknownValue,
            ["reliabilityReasonCode"] = reliability?.ReasonCode ?? UnknownValue,
            ["bundleGateResult"] = MainViewModelDiagnostics.ResolveBundleGateResult(reliability, UnknownValue)
        };
    }

    /// <summary>Toggle tracking to know which bool/toggle cheats are currently "on".</summary>
    private readonly HashSet<string> _activeToggles = new(StringComparer.OrdinalIgnoreCase);

    private async Task QuickRunActionAsync(string actionId, JsonObject payload, string? toggleKey = null)
    {
        if (!CanRunQuickAction())
        {
            return;
        }

        if (!await EnsureActionAvailableForCurrentSessionAsync(actionId, actionId))
        {
            return;
        }

        try
        {
            var result = await ExecuteQuickActionAsync(actionId, payload);
            ToggleQuickActionState(toggleKey, result.Succeeded);
            Status = MainViewModelDiagnostics.BuildQuickActionStatus(actionId, result);
        }
        catch (Exception ex)
        {
            Status = $"✗ {actionId}: {ex.Message}";
        }
    }

    private bool CanRunQuickAction() => _runtime.IsAttached && !string.IsNullOrWhiteSpace(SelectedProfileId);

    private async Task<ActionExecutionResult> ExecuteQuickActionAsync(string actionId, JsonObject payload)
    {
        return await _orchestrator.ExecuteAsync(
            SelectedProfileId!,
            actionId,
            payload,
            RuntimeMode,
            BuildActionContext(actionId));
    }

    private void ToggleQuickActionState(string? toggleKey, bool succeeded)
    {
        if (!succeeded || toggleKey is null)
        {
            return;
        }

        if (_activeToggles.Contains(toggleKey))
        {
            _activeToggles.Remove(toggleKey);
            return;
        }

        _activeToggles.Add(toggleKey);
    }

    private async Task QuickSetCreditsAsync()
    {
        if (!MainViewModelCreditsHelpers.TryParseCreditsValue(CreditsValue, out var value, out var parseError))
        {
            Status = parseError;
            return;
        }

        if (!await EnsureCreditsActionReadyAsync())
        {
            return;
        }

        // Clear any existing freeze / hook lock first.
        ResetCreditsFreeze();

        // Route through the full action pipeline which installs a trampoline hook
        // on the game's cvttss2si instruction to force the FLOAT source value.
        // This is the only reliable way — writing the int alone is useless because
        // the game overwrites it from the float every frame.
        var payload = MainViewModelPayloadHelpers.BuildCreditsPayload(value, CreditsFreeze);

        try
        {
            var result = await ExecuteSetCreditsAsync(payload);
            var diagnosticsSuffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);

            if (!result.Succeeded)
            {
                Status = $"✗ Credits: {result.Message}{diagnosticsSuffix}";
                return;
            }

            var stateTag = MainViewModelCreditsHelpers.ResolveCreditsStateTag(result, CreditsFreeze);
            var creditsStatus = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(
                CreditsFreeze,
                value,
                stateTag,
                diagnosticsSuffix);
            if (!creditsStatus.IsValid)
            {
                Status = creditsStatus.StatusMessage;
                return;
            }

            if (creditsStatus.ShouldFreeze)
            {
                // Hook lock is active. Register with freeze service for UI/diagnostics visibility.
                _freezeService.FreezeInt(SymbolCredits, value);
                RefreshActiveFreezes();
            }

            Status = creditsStatus.StatusMessage;
        }
        catch (Exception ex)
        {
            Status = $"✗ Credits: {ex.Message}";
        }
    }

    private async Task<bool> EnsureCreditsActionReadyAsync()
    {
        if (!_runtime.IsAttached || string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            Status = "✗ Not attached to game.";
            return false;
        }

        return await EnsureActionAvailableForCurrentSessionAsync(ActionSetCredits, "Credits");
    }

    private void ResetCreditsFreeze()
    {
        if (_freezeService.IsFrozen(SymbolCredits))
        {
            _freezeService.Unfreeze(SymbolCredits);
        }
    }

    private Task<ActionExecutionResult> ExecuteSetCreditsAsync(JsonObject payload) => _orchestrator.ExecuteAsync(
        SelectedProfileId!,
        ActionSetCredits,
        payload,
        RuntimeMode,
        BuildActionContext(ActionSetCredits));

    private Task QuickFreezeTimerAsync() => QuickRunActionAsync(
        ActionFreezeTimer,
        new JsonObject
        {
            [PayloadKeySymbol] = SymbolGameTimerFreeze,
            [PayloadKeyBoolValue] = !_activeToggles.Contains(SymbolGameTimerFreeze)
        },
        SymbolGameTimerFreeze);

    private Task QuickToggleFogAsync() => QuickRunActionAsync(
        ActionToggleFogReveal,
        new JsonObject
        {
            [PayloadKeySymbol] = SymbolFogReveal,
            [PayloadKeyBoolValue] = !_activeToggles.Contains(SymbolFogReveal)
        },
        SymbolFogReveal);

    private Task QuickToggleAiAsync() => QuickRunActionAsync(
        ActionToggleAi,
        new JsonObject
        {
            [PayloadKeySymbol] = SymbolAiEnabled,
            [PayloadKeyBoolValue] = _activeToggles.Contains(SymbolAiEnabled)
        },
        SymbolAiEnabled);

    private Task QuickInstantBuildAsync() => QuickRunActionAsync(
        ActionToggleInstantBuildPatch,
        new JsonObject
        {
            [PayloadKeyEnable] = !_activeToggles.Contains(SymbolInstantBuildNop)
        },
        SymbolInstantBuildNop);

    private Task QuickUnitCapAsync()
        => QuickRunActionAsync(ActionSetUnitCap,
            new JsonObject { [PayloadKeySymbol] = SymbolUnitCap, [PayloadKeyIntValue] = DefaultUnitCapValue, [PayloadKeyEnable] = true });

    private Task QuickGodModeAsync() => QuickRunActionAsync(
        ActionToggleTacticalGodMode,
        new JsonObject
        {
            [PayloadKeySymbol] = SymbolTacticalGodMode,
            [PayloadKeyBoolValue] = !_activeToggles.Contains(SymbolTacticalGodMode)
        },
        SymbolTacticalGodMode);

    private Task QuickOneHitAsync() => QuickRunActionAsync(
        ActionToggleTacticalOneHitMode,
        new JsonObject
        {
            [PayloadKeySymbol] = SymbolTacticalOneHitMode,
            [PayloadKeyBoolValue] = !_activeToggles.Contains(SymbolTacticalOneHitMode)
        },
        SymbolTacticalOneHitMode);

    private Task QuickUnfreezeAllAsync()
    {
        _freezeService.UnfreezeAll();
        _activeToggles.Clear();
        RefreshActiveFreezes();
        Status = "✓ All freezes and toggles cleared";
        return Task.CompletedTask;
    }

    private void RefreshActiveFreezes()
    {
        MainViewModelQuickActionHelpers.PopulateActiveFreezes(
            ActiveFreezes,
            _freezeService.GetFrozenSymbols(),
            _activeToggles);

        if (!_freezeUiTimer.IsEnabled)
        {
            _freezeUiTimer.Start();
        }
    }

    private async Task LoadHotkeysAsync()
    {
        Status = await MainViewModelHotkeyHelpers.LoadHotkeysAsync(Hotkeys);
    }

    private async Task SaveHotkeysAsync()
    {
        Status = await MainViewModelHotkeyHelpers.SaveHotkeysAsync(Hotkeys);
    }

    private Task AddHotkeyAsync()
    {
        Hotkeys.Add(new HotkeyBindingItem
        {
            Gesture = "Ctrl+Shift+0",
            ActionId = SelectedActionId,
            PayloadJson = "{}"
        });

        return Task.CompletedTask;
    }

    private Task RemoveHotkeyAsync()
    {
        if (SelectedHotkey is not null)
        {
            Hotkeys.Remove(SelectedHotkey);
        }

        return Task.CompletedTask;
    }

    public async Task<bool> ExecuteHotkeyAsync(string gesture)
    {
        if (!CanExecuteHotkey())
        {
            return false;
        }

        var binding = ResolveHotkeyBinding(gesture);
        if (binding is null)
        {
            return false;
        }

        if (!await EnsureActionAvailableForCurrentSessionAsync(binding.ActionId, $"Hotkey {gesture}"))
        {
            return true;
        }

        var payloadNode = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        var result = await _orchestrator.ExecuteAsync(
            SelectedProfileId!,
            binding.ActionId,
            payloadNode,
            RuntimeMode,
            BuildActionContext(binding.ActionId));
        Status = MainViewModelHotkeyHelpers.BuildHotkeyStatus(gesture, binding.ActionId, result);

        return true;
    }

    private bool CanExecuteHotkey() => _runtime.IsAttached && !string.IsNullOrWhiteSpace(SelectedProfileId);

    private HotkeyBindingItem? ResolveHotkeyBinding(string gesture) =>
        Hotkeys.FirstOrDefault(x =>
            string.Equals(x.Gesture, gesture, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(x.ActionId));
}
