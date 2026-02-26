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
    private IReadOnlyDictionary<string, object?> BuildActionContext(string actionId)
    {
        var reliability = ActionReliability.FirstOrDefault(x => x.ActionId.Equals(actionId, StringComparison.OrdinalIgnoreCase));
        return new Dictionary<string, object?>
        {
            ["reliabilityState"] = reliability?.State ?? MainViewModelDefaults.UnknownValue,
            ["reliabilityReasonCode"] = reliability?.ReasonCode ?? MainViewModelDefaults.UnknownValue,
            ["bundleGateResult"] = ResolveBundleGateResult(reliability)
        };
    }

    private static string ResolveBundleGateResult(ActionReliabilityViewItem? reliability)
    {
        if (reliability is null)
        {
            return MainViewModelDefaults.UnknownValue;
        }

        return reliability.State == "unavailable"
            ? MainViewModelDefaults.BundleBlockedValue
            : MainViewModelDefaults.BundlePassValue;
    }

    private static string BuildDiagnosticsStatusSuffix(ActionExecutionResult result)
    {
        if (result.Diagnostics is null)
        {
            return string.Empty;
        }

        var segments = new List<string>(capacity: 5);
        AppendDiagnosticSegment(segments, result.Diagnostics, "backend", "backend", "backendRoute");
        AppendDiagnosticSegment(segments, result.Diagnostics, "routeReasonCode", "routeReasonCode", "reasonCode");
        AppendDiagnosticSegment(segments, result.Diagnostics, "capabilityProbeReasonCode", "capabilityProbeReasonCode", "probeReasonCode");
        AppendDiagnosticSegment(segments, result.Diagnostics, "hookState", "hookState");
        AppendDiagnosticSegment(segments, result.Diagnostics, "hybridExecution", "hybridExecution");

        return segments.Count == 0 ? string.Empty : $" [{string.Join(", ", segments)}]";
    }

    private static void AppendDiagnosticSegment(
        ICollection<string> segments,
        IReadOnlyDictionary<string, object?> diagnostics,
        string segmentKey,
        params string[] candidateKeys)
    {
        foreach (var key in candidateKeys)
        {
            var value = TryGetDiagnosticString(diagnostics, key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                segments.Add($"{segmentKey}={value}");
                return;
            }
        }
    }

    private static string? TryGetDiagnosticString(IReadOnlyDictionary<string, object?> diagnostics, string key)
    {
        if (!diagnostics.TryGetValue(key, out var value) || value is null)
        {
            return null;
        }

        return value as string ?? value.ToString();
    }

    // ── Quick-Action Methods ──────────────────────────────────────────────

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
            Status = BuildQuickActionStatus(actionId, result);
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

    private string BuildQuickActionStatus(string actionId, ActionExecutionResult result)
    {
        var diagnosticsSuffix = BuildDiagnosticsStatusSuffix(result);
        return result.Succeeded
            ? $"✓ {actionId}: {result.Message}{diagnosticsSuffix}"
            : $"✗ {actionId}: {result.Message}{diagnosticsSuffix}";
    }

    private async Task QuickSetCreditsAsync()
    {
        if (!TryGetCreditsValue(out var value))
        {
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
        var payload = BuildCreditsPayload(value, CreditsFreeze);

        try
        {
            var result = await ExecuteSetCreditsAsync(payload);
            var diagnosticsSuffix = BuildDiagnosticsStatusSuffix(result);

            if (!result.Succeeded)
            {
                Status = $"✗ Credits: {result.Message}{diagnosticsSuffix}";
                return;
            }

            ApplyCreditsSuccessStatus(value, ResolveCreditsStateTag(result), diagnosticsSuffix);
        }
        catch (Exception ex)
        {
            Status = $"✗ Credits: {ex.Message}";
        }
    }

    private bool TryGetCreditsValue(out int value)
    {
        if (int.TryParse(CreditsValue, out value) && value >= 0)
        {
            return true;
        }

        Status = "✗ Invalid credits value. Enter a positive whole number.";
        value = 0;
        return false;
    }

    private async Task<bool> EnsureCreditsActionReadyAsync()
    {
        if (!_runtime.IsAttached || string.IsNullOrWhiteSpace(SelectedProfileId))
        {
            Status = "✗ Not attached to game.";
            return false;
        }

        return await EnsureActionAvailableForCurrentSessionAsync("set_credits", "Credits");
    }

    private void ResetCreditsFreeze()
    {
        if (_freezeService.IsFrozen("credits"))
        {
            _freezeService.Unfreeze("credits");
        }
    }

    private static JsonObject BuildCreditsPayload(int value, bool lockCredits)
    {
        return new JsonObject
        {
            ["symbol"] = "credits",
            ["intValue"] = value,
            ["lockCredits"] = lockCredits
        };
    }

    private async Task<ActionExecutionResult> ExecuteSetCreditsAsync(JsonObject payload)
    {
        return await _orchestrator.ExecuteAsync(
            SelectedProfileId!,
            "set_credits",
            payload,
            RuntimeMode,
            BuildActionContext("set_credits"));
    }

    private string ResolveCreditsStateTag(ActionExecutionResult result)
    {
        var stateTag = ReadDiagnosticString(result.Diagnostics, "creditsStateTag");
        if (!string.IsNullOrWhiteSpace(stateTag))
        {
            return stateTag;
        }

        return CreditsFreeze ? "HOOK_LOCK" : "HOOK_ONESHOT";
    }

    private void ApplyCreditsSuccessStatus(int value, string stateTag, string diagnosticsSuffix)
    {
        if (CreditsFreeze)
        {
            ApplyCreditsLockStatus(value, stateTag, diagnosticsSuffix);
            return;
        }

        ApplyCreditsOneShotStatus(value, stateTag, diagnosticsSuffix);
    }

    private void ApplyCreditsLockStatus(int value, string stateTag, string diagnosticsSuffix)
    {
        if (!stateTag.Equals("HOOK_LOCK", StringComparison.OrdinalIgnoreCase))
        {
            Status = $"✗ Credits: unexpected state '{stateTag}' for lock mode.{diagnosticsSuffix}";
            return;
        }

        // Hook lock is active — the cave code forces the float every frame.
        // Register with freeze service only for UI/diagnostics visibility.
        _freezeService.FreezeInt("credits", value);
        RefreshActiveFreezes();
        Status = $"✓ [HOOK_LOCK] Credits locked to {value:N0} (float+int hook active){diagnosticsSuffix}";
    }

    private void ApplyCreditsOneShotStatus(int value, string stateTag, string diagnosticsSuffix)
    {
        if (!stateTag.Equals("HOOK_ONESHOT", StringComparison.OrdinalIgnoreCase))
        {
            Status = $"✗ Credits: unexpected state '{stateTag}' for one-shot mode.{diagnosticsSuffix}";
            return;
        }

        Status = $"✓ [HOOK_ONESHOT] Credits set to {value:N0} (float+int sync){diagnosticsSuffix}";
    }

    private static string ReadDiagnosticString(IReadOnlyDictionary<string, object?>? diagnostics, string key)
    {
        if (diagnostics is null || !diagnostics.TryGetValue(key, out var raw) || raw is null)
        {
            return string.Empty;
        }

        if (raw is string s)
        {
            return s;
        }

        return raw.ToString() ?? string.Empty;
    }

    private Task QuickFreezeTimerAsync()
    {
        var currentValue = !_activeToggles.Contains("game_timer_freeze");
        return QuickRunActionAsync("freeze_timer",
            new JsonObject { ["symbol"] = "game_timer_freeze", ["boolValue"] = currentValue },
            "game_timer_freeze");
    }

    private Task QuickToggleFogAsync()
    {
        var currentValue = !_activeToggles.Contains("fog_reveal");
        return QuickRunActionAsync("toggle_fog_reveal",
            new JsonObject { ["symbol"] = "fog_reveal", ["boolValue"] = currentValue },
            "fog_reveal");
    }

    private Task QuickToggleAiAsync()
    {
        // ai_enabled: toggling to false disables AI, true re-enables
        var currentValue = _activeToggles.Contains("ai_enabled"); // flip: if active (=disabled), re-enable
        return QuickRunActionAsync("toggle_ai",
            new JsonObject { ["symbol"] = "ai_enabled", ["boolValue"] = currentValue },
            "ai_enabled");
    }

    private Task QuickInstantBuildAsync()
    {
        var enable = !_activeToggles.Contains("instant_build_nop");
        return QuickRunActionAsync("toggle_instant_build_patch",
            new JsonObject { ["enable"] = enable },
            "instant_build_nop");
    }

    private Task QuickUnitCapAsync()
        => QuickRunActionAsync("set_unit_cap",
            new JsonObject { ["symbol"] = "unit_cap", ["intValue"] = 99999, ["enable"] = true });

    private Task QuickGodModeAsync()
    {
        var currentValue = !_activeToggles.Contains("tactical_god_mode");
        return QuickRunActionAsync("toggle_tactical_god_mode",
            new JsonObject { ["symbol"] = "tactical_god_mode", ["boolValue"] = currentValue },
            "tactical_god_mode");
    }

    private Task QuickOneHitAsync()
    {
        var currentValue = !_activeToggles.Contains("tactical_one_hit_mode");
        return QuickRunActionAsync("toggle_tactical_one_hit_mode",
            new JsonObject { ["symbol"] = "tactical_one_hit_mode", ["boolValue"] = currentValue },
            "tactical_one_hit_mode");
    }

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
        ActiveFreezes.Clear();
        foreach (var symbol in _freezeService.GetFrozenSymbols())
        {
            ActiveFreezes.Add($"❄️ {symbol}");
        }
        foreach (var toggle in _activeToggles)
        {
            ActiveFreezes.Add($"🔒 {toggle}");
        }
        if (ActiveFreezes.Count == 0)
        {
            ActiveFreezes.Add("(none)");
        }
    }
}
