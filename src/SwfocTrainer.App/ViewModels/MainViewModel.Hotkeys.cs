using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.IO;

namespace SwfocTrainer.App.ViewModels;

public sealed partial class MainViewModel
{
    private static string HotkeyFilePath => TrustedPathPolicy.CombineUnderRoot(
        TrustedPathPolicy.GetOrCreateAppDataRoot(),
        "hotkeys.json");

    private async Task LoadHotkeysAsync()
    {
        Hotkeys.Clear();
        var path = HotkeyFilePath;
        TrustedPathPolicy.EnsureSubPath(TrustedPathPolicy.GetOrCreateAppDataRoot(), path);
        if (!File.Exists(path))
        {
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+1", ActionId = ActionSetCredits, PayloadJson = BuildDefaultHotkeyPayloadJson(ActionSetCredits) });
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+2", ActionId = ActionFreezeTimer, PayloadJson = BuildDefaultHotkeyPayloadJson(ActionFreezeTimer) });
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+3", ActionId = ActionToggleFogReveal, PayloadJson = BuildDefaultHotkeyPayloadJson(ActionToggleFogReveal) });
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+4", ActionId = ActionToggleInstantBuildPatch, PayloadJson = BuildDefaultHotkeyPayloadJson(ActionToggleInstantBuildPatch) });
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+5", ActionId = ActionFreezeSymbol, PayloadJson = BuildDefaultHotkeyPayloadJson(ActionFreezeSymbol) });
            Status = "Created default hotkey bindings in memory";
            return;
        }

        var json = await File.ReadAllTextAsync(path);
        var items = JsonSerializer.Deserialize<List<HotkeyBindingItem>>(json) ?? new List<HotkeyBindingItem>();
        foreach (var item in items)
        {
            Hotkeys.Add(item);
        }

        Status = $"Loaded {Hotkeys.Count} hotkey bindings";
    }

    private async Task SaveHotkeysAsync()
    {
        var hotkeyPath = HotkeyFilePath;
        TrustedPathPolicy.EnsureSubPath(TrustedPathPolicy.GetOrCreateAppDataRoot(), hotkeyPath);
        Directory.CreateDirectory(Path.GetDirectoryName(hotkeyPath)!);
        var json = JsonSerializer.Serialize(Hotkeys, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(hotkeyPath, json);
        Status = $"Saved {Hotkeys.Count} hotkey bindings";
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

        var payloadNode = ParseHotkeyPayload(binding);
        var result = await _orchestrator.ExecuteAsync(
            SelectedProfileId!,
            binding.ActionId,
            payloadNode,
            RuntimeMode,
            BuildActionContext(binding.ActionId));
        Status = BuildHotkeyStatus(gesture, binding.ActionId, result);

        return true;
    }

    private bool CanExecuteHotkey() => _runtime.IsAttached && !string.IsNullOrWhiteSpace(SelectedProfileId);

    private HotkeyBindingItem? ResolveHotkeyBinding(string gesture)
    {
        var binding = Hotkeys.FirstOrDefault(x => string.Equals(x.Gesture, gesture, StringComparison.OrdinalIgnoreCase));
        return binding is not null && !string.IsNullOrWhiteSpace(binding.ActionId) ? binding : null;
    }

    private static JsonObject ParseHotkeyPayload(HotkeyBindingItem binding)
    {
        try
        {
            return JsonNode.Parse(binding.PayloadJson ?? "{}") as JsonObject
                ?? BuildDefaultHotkeyPayload(binding.ActionId);
        }
        catch
        {
            return BuildDefaultHotkeyPayload(binding.ActionId);
        }
    }

    private static string BuildDefaultHotkeyPayloadJson(string actionId)
    {
        return BuildDefaultHotkeyPayload(actionId).ToJsonString();
    }

    private static string BuildHotkeyStatus(string gesture, string actionId, ActionExecutionResult result)
    {
        var diagnosticsSuffix = BuildDiagnosticsStatusSuffix(result);
        return result.Succeeded
            ? $"Hotkey {gesture}: {actionId} succeeded{diagnosticsSuffix}"
            : $"Hotkey {gesture}: {actionId} failed ({result.Message}){diagnosticsSuffix}";
    }

    private static JsonObject BuildDefaultHotkeyPayload(string actionId)
    {
        return actionId switch
        {
            ActionSetCredits => new JsonObject { [PayloadKeySymbol] = SymbolCredits, [PayloadKeyIntValue] = DefaultCreditsValue, [PayloadKeyLockCredits] = false },
            ActionFreezeTimer => new JsonObject { [PayloadKeySymbol] = SymbolGameTimerFreeze, [PayloadKeyBoolValue] = true },
            ActionToggleFogReveal => new JsonObject { [PayloadKeySymbol] = SymbolFogReveal, [PayloadKeyBoolValue] = true },
            ActionSetUnitCap => new JsonObject { [PayloadKeySymbol] = SymbolUnitCap, [PayloadKeyIntValue] = DefaultUnitCapValue, [PayloadKeyEnable] = true },
            ActionToggleInstantBuildPatch => new JsonObject { [PayloadKeyEnable] = true },
            ActionSetGameSpeed => new JsonObject { [PayloadKeySymbol] = SymbolGameSpeed, [PayloadKeyFloatValue] = DefaultGameSpeedValue },
            ActionFreezeSymbol => new JsonObject { [PayloadKeySymbol] = SymbolCredits, [PayloadKeyFreeze] = true, [PayloadKeyIntValue] = DefaultCreditsValue },
            ActionUnfreezeSymbol => new JsonObject { [PayloadKeySymbol] = SymbolCredits, [PayloadKeyFreeze] = false },
            _ => new JsonObject()
        };
    }

}
