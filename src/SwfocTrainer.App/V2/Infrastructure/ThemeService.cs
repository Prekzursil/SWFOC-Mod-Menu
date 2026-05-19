using System.Windows;
using Microsoft.Win32;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-25: light/dark theme switcher. Swaps the active
/// ResourceDictionary at <c>Application.Current.Resources.MergedDictionaries</c>
/// so every <c>DynamicResource</c> binding in the running window updates
/// live — no relaunch required.
///
/// Three preference values: <c>System</c> (follow Windows AppsUseLightTheme),
/// <c>Light</c>, <c>Dark</c>. The setting is persisted in V2Settings; on
/// startup, <c>ApplyPreference</c> resolves "system" against the current
/// Windows registry value and applies the corresponding dictionary.
/// </summary>
public enum ThemeMode
{
    Dark = 0,
    Light = 1,
}

public enum ThemePreference
{
    System = 0,
    Dark = 1,
    Light = 2,
}

public static class ThemeService
{
    private const string DarkUri = "pack://application:,,,/V2/Themes/Dark.xaml";
    private const string LightUri = "pack://application:,,,/V2/Themes/Light.xaml";

    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const string AppsUseLightThemeValue = "AppsUseLightTheme";

    /// <summary>The actual mode currently rendered (Light / Dark only — never System).</summary>
    public static ThemeMode CurrentMode { get; private set; } = ThemeMode.Dark;

    /// <summary>The preference the user picked (System / Light / Dark).</summary>
    public static ThemePreference CurrentPreference { get; private set; } = ThemePreference.System;

    /// <summary>
    /// Read Windows' "use light theme for apps" registry value. Returns
    /// <see cref="ThemeMode.Light"/> when the registry says 1 or the key is
    /// missing on older OS builds (light is the historical Windows default);
    /// <see cref="ThemeMode.Dark"/> when explicitly set to 0.
    /// </summary>
    public static ThemeMode ReadSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            var value = key?.GetValue(AppsUseLightThemeValue);
            if (value is int i) return i == 0 ? ThemeMode.Dark : ThemeMode.Light;
        }
        catch (System.Security.SecurityException) { /* sandboxed — fall through */ }
        catch (System.IO.IOException) { /* registry read failed */ }
        return ThemeMode.Light;
    }

    /// <summary>Resolve a preference into the concrete mode that should be rendered.</summary>
    public static ThemeMode Resolve(ThemePreference preference) =>
        preference switch
        {
            ThemePreference.Light => ThemeMode.Light,
            ThemePreference.Dark => ThemeMode.Dark,
            ThemePreference.System => ReadSystemTheme(),
            _ => ThemeMode.Dark,
        };

    /// <summary>
    /// Apply the resolved mode to the running app. Removes any previously
    /// merged theme dictionary before adding the new one so repeated calls
    /// don't accumulate dead entries.
    /// </summary>
    public static void ApplyMode(ThemeMode mode)
    {
        var app = Application.Current;
        if (app is null) return;
        var newDict = new ResourceDictionary
        {
            Source = new Uri(mode == ThemeMode.Light ? LightUri : DarkUri),
        };

        var merged = app.Resources.MergedDictionaries;
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var src = merged[i].Source?.ToString() ?? string.Empty;
            if (src.Contains("/V2/Themes/Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                src.Contains("/V2/Themes/Light.xaml", StringComparison.OrdinalIgnoreCase))
            {
                merged.RemoveAt(i);
            }
        }
        merged.Add(newDict);
        CurrentMode = mode;
    }

    /// <summary>Apply a preference (resolves to a mode first, then applies).</summary>
    public static void ApplyPreference(ThemePreference preference)
    {
        CurrentPreference = preference;
        ApplyMode(Resolve(preference));
    }

    /// <summary>Parse the persisted string into a preference.</summary>
    public static ThemePreference ParsePreference(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return ThemePreference.System;
        return raw.Trim().ToLowerInvariant() switch
        {
            "light" => ThemePreference.Light,
            "dark" => ThemePreference.Dark,
            "system" => ThemePreference.System,
            _ => ThemePreference.System,
        };
    }

    /// <summary>Stable string form for persistence.</summary>
    public static string ToPersistString(ThemePreference preference) =>
        preference switch
        {
            ThemePreference.Light => "light",
            ThemePreference.Dark => "dark",
            ThemePreference.System => "system",
            _ => "system",
        };
}
