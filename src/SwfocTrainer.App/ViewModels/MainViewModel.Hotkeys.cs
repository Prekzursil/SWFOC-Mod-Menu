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
    private string HotkeyFilePath => TrustedPathPolicy.CombineUnderRoot(
        TrustedPathPolicy.GetOrCreateAppDataRoot(),
        "hotkeys.json");

    private async Task LoadHotkeysAsync()
    {
        Hotkeys.Clear();
        var path = HotkeyFilePath;
        TrustedPathPolicy.EnsureSubPath(TrustedPathPolicy.GetOrCreateAppDataRoot(), path);
        if (!File.Exists(path))
        {
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+1", ActionId = "set_credits", PayloadJson = "{\"symbol\":\"credits\",\"intValue\":1000000,\"lockCredits\":false}" });
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+2", ActionId = "freeze_timer", PayloadJson = "{\"symbol\":\"game_timer_freeze\",\"boolValue\":true}" });
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+3", ActionId = "toggle_fog_reveal", PayloadJson = "{\"symbol\":\"fog_reveal\",\"boolValue\":true}" });
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+4", ActionId = "toggle_instant_build_patch", PayloadJson = "{\"enable\":true}" });
            Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+5", ActionId = "freeze_symbol", PayloadJson = "{\"symbol\":\"credits\",\"freeze\":true,\"intValue\":1000000}" });
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

    private string BuildHotkeyStatus(string gesture, string actionId, ActionExecutionResult result)
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
            "set_credits" => new JsonObject { ["symbol"] = "credits", ["intValue"] = 1000000, ["lockCredits"] = false },
            "freeze_timer" => new JsonObject { ["symbol"] = "game_timer_freeze", ["boolValue"] = true },
            "toggle_fog_reveal" => new JsonObject { ["symbol"] = "fog_reveal", ["boolValue"] = true },
            "set_unit_cap" => new JsonObject { ["symbol"] = "unit_cap", ["intValue"] = 99999, ["enable"] = true },
            "toggle_instant_build_patch" => new JsonObject { ["enable"] = true },
            "set_game_speed" => new JsonObject { ["symbol"] = "game_speed", ["floatValue"] = (float)2.0 },
            "freeze_symbol" => new JsonObject { ["symbol"] = "credits", ["freeze"] = true, ["intValue"] = 1000000 },
            "unfreeze_symbol" => new JsonObject { ["symbol"] = "credits", ["freeze"] = false },
            _ => new JsonObject()
        };
    }
}
