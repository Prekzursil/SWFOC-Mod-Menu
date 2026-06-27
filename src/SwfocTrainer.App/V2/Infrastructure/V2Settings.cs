using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SwfocTrainer.App.V2.Infrastructure;

// ============================================================================
// V2 settings. Persisted to %APPDATA%\SwfocTrainer\v2_settings.json. One small
// record serialized as JSON. No migration, no versioning — if the file gets
// corrupted the user loads defaults.
// ============================================================================

public sealed class V2Settings
{
    /// <summary>Game install root (default: Steam-standard SWFOC path).</summary>
    [JsonPropertyName("gamePath")]
    public string GamePath { get; set; } =
        "D:\\SteamLibrary\\steamapps\\common\\Star Wars Empire at War";

    /// <summary>Named pipe the bridge is listening on.</summary>
    [JsonPropertyName("bridgePipeName")]
    public string BridgePipeName { get; set; } = "swfoc_bridge";

    /// <summary>
    /// Path to <c>swfoc_bridge.log</c> (tail target for the diagnostics tab).
    /// Default matches the path where the deployed <c>powrprof.dll</c> writes.
    /// </summary>
    [JsonPropertyName("logPath")]
    public string LogPath { get; set; } =
        "D:\\SteamLibrary\\steamapps\\common\\Star Wars Empire at War\\corruption\\swfoc_bridge.log";

    /// <summary>Probe the bridge automatically when the window opens.</summary>
    [JsonPropertyName("autoConnect")]
    public bool AutoConnectOnStartup { get; set; } = true;

    /// <summary>
    /// Unlocks the UI entries for helpers that may require a fresh bridge
    /// build (post-2026-04-10 added the diagnostic helpers; post-2026-04-27
    /// added <c>SWFOC_BatchTypeExists</c>). When false, helpers that have
    /// not yet been verified against the deployed <c>powrprof.dll</c> are
    /// disabled with a tooltip explaining the dependency.
    /// </summary>
    [JsonPropertyName("showAdvanced")]
    public bool ShowAdvancedHelpers { get; set; }

    /// <summary>
    /// Profile id forwarded to ILuaBridgeExecutor. The adapter does not care
    /// but feature services sometimes embed it in diagnostics.
    /// </summary>
    [JsonPropertyName("profileId")]
    public string ProfileId { get; set; } = "base_swfoc";

    /// <summary>
    /// 2026-04-25: persisted theme preference. <c>"system"</c>, <c>"light"</c>,
    /// or <c>"dark"</c>. Default <c>"system"</c> means "match the current
    /// Windows AppsUseLightTheme registry setting on each launch". Users
    /// can override via the Settings tab; the change is live (no restart).
    /// </summary>
    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "system";

    /// <summary>
    /// 2026-05-07 (iter 309, Thread D arc post-finale): root directory holding
    /// extracted SWFOC DDS textures (operator runs Python
    /// <c>tools/asset_extractor/meg_parser.py</c> + <c>thumbnail_cache.py</c>
    /// once per game install to populate it). Consumed by
    /// <c>UnitIconResolver</c> to render unit-type icons in the Spawning tab
    /// ListBox. Null = no icons shown (graceful — null IconPath hides the
    /// Image control). The constructor at MainViewModelV2 falls back to the
    /// <c>SWFOC_EXTRACTED_DDS_ROOT</c> env var when this is null/empty.
    /// </summary>
    [JsonPropertyName("iconsRoot")]
    public string? IconsRoot { get; set; }

    /// <summary>Directory under %APPDATA% where V2 settings and recipes live.</summary>
    public static string AppDataDirectory
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, "SwfocTrainer");
        }
    }

    public static string SettingsFilePath => Path.Combine(AppDataDirectory, "v2_settings.json");

    public static string RecipesFilePath => Path.Combine(AppDataDirectory, "recipes.json");

    public static V2Settings Load()
    {
        try
        {
            if (!File.Exists(SettingsFilePath))
            {
                return new V2Settings();
            }

            var json = File.ReadAllText(SettingsFilePath);
            var parsed = JsonSerializer.Deserialize<V2Settings>(json);
            return parsed ?? new V2Settings();
        }
        catch (IOException)
        {
            return new V2Settings();
        }
        catch (UnauthorizedAccessException)
        {
            return new V2Settings();
        }
        catch (JsonException)
        {
            return new V2Settings();
        }
    }

    public bool TrySave(out string? error)
    {
        try
        {
            Directory.CreateDirectory(AppDataDirectory);
            var json = JsonSerializer.Serialize(
                this,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFilePath, json);
            error = null;
            return true;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
