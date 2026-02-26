using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

public abstract class MainViewModelQuickActionsBase : MainViewModelLiveOpsBase
{
    protected MainViewModelQuickActionsBase(MainViewModelDependencies dependencies)
        : base(dependencies)
    {
    }

    /// <summary>Toggle tracking to know which bool/toggle cheats are currently "on".</summary>
    private readonly HashSet<string> _activeToggles = new(StringComparer.OrdinalIgnoreCase);

    protected async Task QuickRunActionAsync(string actionId, JsonObject payload, string? toggleKey = null)
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

    protected async Task QuickSetCreditsAsync()
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

    protected Task QuickFreezeTimerAsync() => QuickRunActionAsync(
        ActionFreezeTimer,
        new JsonObject
        {
            [PayloadKeySymbol] = SymbolGameTimerFreeze,
            [PayloadKeyBoolValue] = !_activeToggles.Contains(SymbolGameTimerFreeze)
        },
        SymbolGameTimerFreeze);

    protected Task QuickToggleFogAsync() => QuickRunActionAsync(
        ActionToggleFogReveal,
        new JsonObject
        {
            [PayloadKeySymbol] = SymbolFogReveal,
            [PayloadKeyBoolValue] = !_activeToggles.Contains(SymbolFogReveal)
        },
        SymbolFogReveal);

    protected Task QuickToggleAiAsync() => QuickRunActionAsync(
        ActionToggleAi,
        new JsonObject
        {
            [PayloadKeySymbol] = SymbolAiEnabled,
            [PayloadKeyBoolValue] = _activeToggles.Contains(SymbolAiEnabled)
        },
        SymbolAiEnabled);

    protected Task QuickInstantBuildAsync() => QuickRunActionAsync(
        ActionToggleInstantBuildPatch,
        new JsonObject
        {
            [PayloadKeyEnable] = !_activeToggles.Contains(SymbolInstantBuildNop)
        },
        SymbolInstantBuildNop);

    protected Task QuickUnitCapAsync()
        => QuickRunActionAsync(ActionSetUnitCap,
            new JsonObject { [PayloadKeySymbol] = SymbolUnitCap, [PayloadKeyIntValue] = DefaultUnitCapValue, [PayloadKeyEnable] = true });

    protected Task QuickGodModeAsync() => QuickRunActionAsync(
        ActionToggleTacticalGodMode,
        new JsonObject
        {
            [PayloadKeySymbol] = SymbolTacticalGodMode,
            [PayloadKeyBoolValue] = !_activeToggles.Contains(SymbolTacticalGodMode)
        },
        SymbolTacticalGodMode);

    protected Task QuickOneHitAsync() => QuickRunActionAsync(
        ActionToggleTacticalOneHitMode,
        new JsonObject
        {
            [PayloadKeySymbol] = SymbolTacticalOneHitMode,
            [PayloadKeyBoolValue] = !_activeToggles.Contains(SymbolTacticalOneHitMode)
        },
        SymbolTacticalOneHitMode);

    protected Task QuickUnfreezeAllAsync()
    {
        _freezeService.UnfreezeAll();
        _activeToggles.Clear();
        RefreshActiveFreezes();
        Status = "✓ All freezes and toggles cleared";
        return Task.CompletedTask;
    }

    protected void RefreshActiveFreezes()
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

    protected async Task LoadHotkeysAsync()
    {
        Status = await MainViewModelHotkeyHelpers.LoadHotkeysAsync(Hotkeys);
    }

    protected async Task SaveHotkeysAsync()
    {
        Status = await MainViewModelHotkeyHelpers.SaveHotkeysAsync(Hotkeys);
    }

    protected Task AddHotkeyAsync()
    {
        Hotkeys.Add(new HotkeyBindingItem
        {
            Gesture = "Ctrl+Shift+0",
            ActionId = SelectedActionId,
            PayloadJson = "{}"
        });

        return Task.CompletedTask;
    }

    protected Task RemoveHotkeyAsync()
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
