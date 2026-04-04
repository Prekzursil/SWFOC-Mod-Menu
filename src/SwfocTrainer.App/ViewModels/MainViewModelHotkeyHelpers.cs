using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using SwfocTrainer.App.Models;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelHotkeyHelpers
{
    internal static string GetHotkeyFilePath()
    {
        return TrustedPathPolicy.CombineUnderRoot(
            TrustedPathPolicy.GetOrCreateAppDataRoot(),
            "hotkeys.json");
    }

    internal static JsonObject ParseHotkeyPayload(HotkeyBindingItem binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        try
        {
            return JsonNode.Parse(binding.PayloadJson ?? "{}") as JsonObject
                ?? BuildDefaultHotkeyPayload(binding.ActionId);
        }
        catch (JsonException)
        {
            return BuildDefaultHotkeyPayload(binding.ActionId);
        }
    }

    internal static string BuildDefaultHotkeyPayloadJson(string actionId)
    {
        ArgumentNullException.ThrowIfNull(actionId);
        return BuildDefaultHotkeyPayload(actionId).ToJsonString();
    }

    internal static async Task<string> LoadHotkeysAsync(ObservableCollection<HotkeyBindingItem> hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        hotkeys.Clear();
        var path = GetHotkeyFilePath();
        TrustedPathPolicy.EnsureSubPath(TrustedPathPolicy.GetOrCreateAppDataRoot(), path);
        if (!File.Exists(path))
        {
            hotkeys.Add(new HotkeyBindingItem
            {
                Gesture = "Ctrl+Shift+1",
                ActionId = MainViewModelDefaults.ActionSetCredits,
                PayloadJson = BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionSetCredits)
            });
            hotkeys.Add(new HotkeyBindingItem
            {
                Gesture = "Ctrl+Shift+2",
                ActionId = MainViewModelDefaults.ActionFreezeTimer,
                PayloadJson = BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionFreezeTimer)
            });
            hotkeys.Add(new HotkeyBindingItem
            {
                Gesture = "Ctrl+Shift+3",
                ActionId = MainViewModelDefaults.ActionToggleFogReveal,
                PayloadJson = BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionToggleFogReveal)
            });
            hotkeys.Add(new HotkeyBindingItem
            {
                Gesture = "Ctrl+Shift+4",
                ActionId = MainViewModelDefaults.ActionToggleInstantBuildPatch,
                PayloadJson = BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionToggleInstantBuildPatch)
            });
            hotkeys.Add(new HotkeyBindingItem
            {
                Gesture = "Ctrl+Shift+5",
                ActionId = MainViewModelDefaults.ActionFreezeSymbol,
                PayloadJson = BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionFreezeSymbol)
            });

            return "Created default hotkey bindings in memory";
        }

        var json = await File.ReadAllTextAsync(path);
        var items = JsonSerializer.Deserialize<List<HotkeyBindingItem>>(json) ?? new List<HotkeyBindingItem>();
        foreach (var item in items)
        {
            hotkeys.Add(item);
        }

        return $"Loaded {hotkeys.Count} hotkey bindings";
    }

    internal static async Task<string> SaveHotkeysAsync(IReadOnlyCollection<HotkeyBindingItem> hotkeys)
    {
        ArgumentNullException.ThrowIfNull(hotkeys);
        var hotkeyPath = GetHotkeyFilePath();
        TrustedPathPolicy.EnsureSubPath(TrustedPathPolicy.GetOrCreateAppDataRoot(), hotkeyPath);
        var hotkeyDirectory = Path.GetDirectoryName(hotkeyPath)
            ?? throw new InvalidOperationException("Hotkey file path has no parent directory.");
        Directory.CreateDirectory(hotkeyDirectory);
        var json = JsonSerializer.Serialize(hotkeys, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(hotkeyPath, json);
        return $"Saved {hotkeys.Count} hotkey bindings";
    }

    internal static string BuildHotkeyStatus(
        string gesture,
        string actionId,
        ActionExecutionResult result)
    {
        ArgumentNullException.ThrowIfNull(gesture);
        ArgumentNullException.ThrowIfNull(actionId);
        ArgumentNullException.ThrowIfNull(result);
        var diagnosticsSuffix = MainViewModelDiagnostics.BuildDiagnosticsStatusSuffix(result);
        return result.Succeeded
            ? $"Hotkey {gesture}: {actionId} succeeded{diagnosticsSuffix}"
            : $"Hotkey {gesture}: {actionId} failed ({result.Message}){diagnosticsSuffix}";
    }

    private static JsonObject BuildDefaultHotkeyPayload(string actionId)
    {
        return actionId switch
        {
            MainViewModelDefaults.ActionSetCredits => new JsonObject
            {
                [MainViewModelDefaults.PayloadSymbol] = MainViewModelDefaults.SymbolCredits,
                [MainViewModelDefaults.PayloadIntValue] = MainViewModelDefaults.DefaultCreditsValue,
                [MainViewModelDefaults.PayloadLockCredits] = false
            },
            MainViewModelDefaults.ActionFreezeTimer => new JsonObject
            {
                [MainViewModelDefaults.PayloadSymbol] = MainViewModelDefaults.SymbolGameTimerFreeze,
                [MainViewModelDefaults.PayloadBoolValue] = true
            },
            MainViewModelDefaults.ActionToggleFogReveal => new JsonObject
            {
                [MainViewModelDefaults.PayloadSymbol] = MainViewModelDefaults.SymbolFogReveal,
                [MainViewModelDefaults.PayloadBoolValue] = true
            },
            MainViewModelDefaults.ActionSetUnitCap => new JsonObject
            {
                [MainViewModelDefaults.PayloadSymbol] = MainViewModelDefaults.SymbolUnitCap,
                [MainViewModelDefaults.PayloadIntValue] = MainViewModelDefaults.DefaultUnitCapValue,
                [MainViewModelDefaults.PayloadEnable] = true
            },
            MainViewModelDefaults.ActionToggleInstantBuildPatch => new JsonObject
            {
                [MainViewModelDefaults.PayloadEnable] = true
            },
            MainViewModelDefaults.ActionSetGameSpeed => new JsonObject
            {
                [MainViewModelDefaults.PayloadSymbol] = MainViewModelDefaults.SymbolGameSpeed,
                [MainViewModelDefaults.PayloadFloatValue] = MainViewModelDefaults.DefaultGameSpeedValue
            },
            MainViewModelDefaults.ActionFreezeSymbol => new JsonObject
            {
                [MainViewModelDefaults.PayloadSymbol] = MainViewModelDefaults.SymbolCredits,
                [MainViewModelDefaults.PayloadFreeze] = true,
                [MainViewModelDefaults.PayloadIntValue] = MainViewModelDefaults.DefaultCreditsValue
            },
            MainViewModelDefaults.ActionUnfreezeSymbol => new JsonObject
            {
                [MainViewModelDefaults.PayloadSymbol] = MainViewModelDefaults.SymbolCredits,
                [MainViewModelDefaults.PayloadFreeze] = false
            },
            _ => new JsonObject()
        };
    }
}
