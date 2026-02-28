using System.IO;
using System.Text.Json;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.App.ViewModels;

internal static class MainViewModelRuntimeModeOverrideHelpers
{
    internal const string ModeOverrideAuto = "Auto";
    internal const string ModeOverrideGalactic = "Galactic";
    internal const string ModeOverrideTactical = "Tactical";

    private const string SettingsFileName = "runtime-mode-settings.json";
    private const string SettingsKeyModeOverride = "modeOverride";

    internal static readonly IReadOnlyList<string> ModeOverrideOptions =
    [
        ModeOverrideAuto,
        ModeOverrideGalactic,
        ModeOverrideTactical
    ];

    internal static string Normalize(string? raw)
    {
        if (string.Equals(raw, ModeOverrideGalactic, StringComparison.OrdinalIgnoreCase))
        {
            return ModeOverrideGalactic;
        }

        if (string.Equals(raw, ModeOverrideTactical, StringComparison.OrdinalIgnoreCase))
        {
            return ModeOverrideTactical;
        }

        return ModeOverrideAuto;
    }

    internal static RuntimeMode ResolveEffectiveRuntimeMode(RuntimeMode runtimeMode, string? modeOverride)
    {
        return Normalize(modeOverride) switch
        {
            ModeOverrideGalactic => RuntimeMode.Galactic,
            ModeOverrideTactical => RuntimeMode.Tactical,
            _ => runtimeMode
        };
    }

    internal static string Load()
    {
        var path = GetSettingsPath();
        if (!File.Exists(path))
        {
            return ModeOverrideAuto;
        }

        try
        {
            var json = File.ReadAllText(path);
            var root = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            return Normalize(root is not null && root.TryGetValue(SettingsKeyModeOverride, out var value) ? value : null);
        }
        catch
        {
            return ModeOverrideAuto;
        }
    }

    internal static void Save(string? modeOverride)
    {
        var normalized = Normalize(modeOverride);
        var path = GetSettingsPath();
        TrustedPathPolicy.EnsureSubPath(TrustedPathPolicy.GetOrCreateAppDataRoot(), path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var data = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SettingsKeyModeOverride] = normalized
        };

        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }

    private static string GetSettingsPath()
    {
        return Path.Combine(TrustedPathPolicy.GetOrCreateAppDataRoot(), SettingsFileName);
    }
}
