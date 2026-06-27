using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using SwfocTrainer.App.V2.Infrastructure;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-05-07 (iter 301): row binding for the Settings tab's mod-picker
/// DataGrid. Populated from <c>SWFOC_ListMods</c> (iter-300); the
/// IsCurrentlyLoaded flag is cross-referenced with <c>SWFOC_GetCurrentMod</c>
/// (iter-299) so the operator can spot the active mod at a glance.
/// </summary>
public sealed record ModRow(string Name, string Path, bool IsCurrentlyLoaded);

// ============================================================================
// Tab 6 — Settings
//
// Edits V2Settings in-place. Save button persists to v2_settings.json. The
// pipe name is surfaced read-only so the user can visually confirm which pipe
// the diagnostic probes are hitting.
// ============================================================================

public sealed class SettingsTabViewModel : ObservableBase
{
    private readonly V2Settings _settings;
    private readonly V2BridgeAdapter? _bridge;
    private string _statusMessage = string.Empty;
    private string _activeMod = "(unknown)";
    private string _modPickerStatus = "Click 'Refresh mods' to discover available mods.";

    /// <summary>
    /// 2026-05-07 (iter 301): mod-picker DataGrid binding. Populated by
    /// <see cref="RefreshModsCommand"/> calling SWFOC_ListMods (iter-300).
    /// IsCurrentlyLoaded is computed by cross-referencing SWFOC_GetCurrentMod
    /// (iter-299) — the operator can spot the active mod without opening
    /// File Explorer.
    /// </summary>
    public ObservableCollection<ModRow> Mods { get; } = new();

    /// <summary>
    /// 2026-05-07 (iter 301): the currently-loaded mod name (from iter-299
    /// SWFOC_GetCurrentMod). Surfaced in the Settings tab's "Currently
    /// loaded:" badge.
    /// </summary>
    public string ActiveMod
    {
        get => _activeMod;
        private set => SetField(ref _activeMod, value);
    }

    public string ModPickerStatus
    {
        get => _modPickerStatus;
        private set => SetField(ref _modPickerStatus, value);
    }

    public SettingsTabViewModel(V2Settings settings, V2BridgeAdapter? bridge = null)
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
        _bridge = bridge;

        SaveCommand = new RelayCommand(Save);
        ReloadCommand = new RelayCommand(Reload);
        // 2026-04-27 (iter 18): operator escape hatch when settings get
        // wedged. Confirmation-gated to prevent accidental wipes.
        ResetToDefaultsCommand = new RelayCommand(ResetToDefaultsWithConfirm);
        // 2026-04-27: file-dialog Browse buttons next to the path inputs.
        // Operator no longer has to type / paste long Steam install paths.
        BrowseGamePathCommand = new RelayCommand(BrowseGamePath);
        BrowseLogPathCommand = new RelayCommand(BrowseLogPath);
        // 2026-05-07 (iter 310, Thread D arc post-finale): folder-picker for
        // the iter-309 IconsRoot setting. OpenFolderDialog ships in .NET 8 —
        // no need for the WindowsAPICodePack hack older docs reference.
        BrowseIconsRootCommand = new RelayCommand(BrowseIconsRoot);
        // 2026-04-27: launch the v2_settings.json file in the OS default
        // text editor (notepad / VSCode / etc.) so power-users can hand-
        // edit fields the GUI doesn't surface yet (e.g. plugin overrides).
        OpenSettingsFileCommand = new RelayCommand(OpenSettingsFile);
        OpenLogFileCommand = new RelayCommand(OpenLogFile);
        // 2026-05-07 (iter 301): mod-picker commands. Consume iter-299
        // GetCurrentMod + iter-300 ListMods bridge wires.
        RefreshModsCommand = new AsyncRelayCommand(RefreshModsCore, onError: HandleError);
        OpenModsFolderCommand = new RelayCommand(OpenModsFolder);
    }

    public ICommand RefreshModsCommand { get; }
    public RelayCommand OpenModsFolderCommand { get; }

    /// <summary>
    /// 2026-05-07 (iter 301): query the bridge for SWFOC_ListMods + the
    /// active mod, then rebuild the bound <see cref="Mods"/> collection
    /// with cross-referenced IsCurrentlyLoaded flags.
    /// </summary>
    internal async Task RefreshModsCore()
    {
        if (_bridge is null)
        {
            ModPickerStatus = "Bridge not connected. Mod picker requires a live bridge connection.";
            Mods.Clear();
            return;
        }

        // First: fetch the active mod (iter-299) so we can cross-reference
        // against the listing. Vanilla returns "vanilla" sentinel.
        var currentRt = await _bridge.SendRawAsync(
            "return SWFOC_GetCurrentMod", CancellationToken.None).ConfigureAwait(true);
        var currentName = "(unknown)";
        if (currentRt.Succeeded && !string.IsNullOrEmpty(currentRt.Response))
        {
            var currentResp = currentRt.Response;
            if (currentResp == "vanilla")
            {
                currentName = "vanilla";
            }
            else
            {
                // First line is "<name>;<version>"; we only need the name.
                var firstLine = currentResp.Split('\n')[0];
                var nameVer = firstLine.Split(';');
                if (nameVer.Length > 0 && !string.IsNullOrEmpty(nameVer[0]))
                    currentName = nameVer[0];
            }
        }
        ActiveMod = currentName;

        // Second: enumerate all mods (iter-300).
        var listRt = await _bridge.SendRawAsync(
            "return SWFOC_ListMods", CancellationToken.None).ConfigureAwait(true);
        Mods.Clear();
        if (!listRt.Succeeded)
        {
            ModPickerStatus = $"Bridge call failed: {listRt.Response ?? "(no response)"}";
            return;
        }
        var listResp = listRt.Response ?? string.Empty;
        if (listResp == "(no_mods)")
        {
            ModPickerStatus = "No mods found in ./Mods/ folder. Click 'Open Mods folder' to add some.";
            return;
        }
        if (listResp.StartsWith("ERR:", StringComparison.Ordinal))
        {
            ModPickerStatus = $"Bridge error: {listResp}";
            return;
        }

        var rows = new List<ModRow>();
        foreach (var line in listResp.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(';');
            if (parts.Length < 2) continue;
            var name = parts[0].Trim();
            var path = parts[1].Trim();
            if (string.IsNullOrEmpty(name)) continue;
            var isLoaded = string.Equals(name, currentName, StringComparison.OrdinalIgnoreCase);
            rows.Add(new ModRow(name, path, isLoaded));
        }
        foreach (var row in rows) Mods.Add(row);
        ModPickerStatus = string.Format(CultureInfo.InvariantCulture,
            "Found {0} mod(s). Currently loaded: {1}", rows.Count, ActiveMod);
    }

    /// <summary>
    /// 2026-05-07 (iter 301): launch the OS file explorer at the game's
    /// Mods/ folder so the operator can drop a sidecar mod (iter-297
    /// stub-XML repair) into it, copy a folder, etc. Falls back to the
    /// game-path's parent if Mods/ doesn't exist yet.
    /// </summary>
    private void OpenModsFolder()
    {
        var gamePath = _settings.GamePath ?? string.Empty;
        var parent = string.IsNullOrEmpty(gamePath)
            ? string.Empty
            : System.IO.Path.GetDirectoryName(gamePath) ?? string.Empty;
        var modsDir = string.IsNullOrEmpty(parent)
            ? string.Empty
            : System.IO.Path.Combine(parent, "Mods");

        if (string.IsNullOrEmpty(modsDir) || !System.IO.Directory.Exists(modsDir))
        {
            // Fall back to the parent directory; operator can navigate manually.
            ModPickerStatus = string.IsNullOrEmpty(parent)
                ? "Configure GamePath first (Browse... above)."
                : $"Mods folder not found at {modsDir}; opening parent instead.";
            if (!string.IsNullOrEmpty(parent))
                TryStartShellCommand(parent);
            return;
        }
        TryStartShellCommand(modsDir);
        ModPickerStatus = $"Opened: {modsDir}";
    }

    public RelayCommand OpenSettingsFileCommand { get; }
    public RelayCommand OpenLogFileCommand { get; }

    private void OpenSettingsFile()
    {
        var path = SettingsFilePath;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            StatusMessage = "Settings file not found — click Save first to create it.";
            return;
        }
        TryStartShellCommand(path);
    }

    private void OpenLogFile()
    {
        var path = _settings.LogPath;
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
        {
            StatusMessage = "Log file not found — start the bridge first.";
            return;
        }
        TryStartShellCommand(path);
    }

    private void TryStartShellCommand(string path)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true, // routes through the OS file association
            };
            System.Diagnostics.Process.Start(psi);
            StatusMessage = $"Opened: {path}";
        }
        catch (Exception ex)
        {
            // ProcessStart can throw on missing file association etc.
            StatusMessage = $"Open failed: {ex.Message}";
        }
    }

    public RelayCommand BrowseGamePathCommand { get; }
    public RelayCommand BrowseLogPathCommand { get; }
    /// <summary>
    /// 2026-05-07 (iter 310, Thread D arc post-finale): folder-picker for
    /// the iter-309 IconsRoot setting. Opens OpenFolderDialog (.NET 8+).
    /// </summary>
    public RelayCommand BrowseIconsRootCommand { get; }

    private void BrowseIconsRoot()
    {
        // OpenFolderDialog ships in .NET 8 (System.Windows.Forms-free). The
        // operator picks the directory containing extracted DDS textures
        // (typically `<extract-root>` from `python meg_parser.py --extract-all`).
        var dialog = new OpenFolderDialog
        {
            Title = "Pick the extracted-DDS root directory",
        };
        try
        {
            var current = _settings.IconsRoot;
            if (!string.IsNullOrWhiteSpace(current) && System.IO.Directory.Exists(current))
            {
                dialog.InitialDirectory = current;
            }
        }
        catch (Exception)
        {
            // Best-effort; falls back to OS default.
        }

        if (dialog.ShowDialog() == true)
        {
            IconsRoot = dialog.FolderName;
        }
    }

    private void BrowseGamePath()
    {
        // Game path expects the StarWarsG.exe binary, not a directory.
        var dialog = new OpenFileDialog
        {
            Title = "Locate StarWarsG.exe",
            Filter = "Star Wars: FoC executable (StarWarsG.exe)|StarWarsG.exe|Executables (*.exe)|*.exe|All files (*.*)|*.*",
            FileName = string.IsNullOrWhiteSpace(_settings.GamePath) ? "StarWarsG.exe" : _settings.GamePath,
            CheckFileExists = true,
        };
        // Pre-seed initial directory if the current path's parent exists.
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_settings.GamePath);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
            {
                dialog.InitialDirectory = dir;
            }
        }
        catch (Exception)
        {
            // Best-effort; falls back to OpenFileDialog default.
        }

        if (dialog.ShowDialog() == true)
        {
            GamePath = dialog.FileName;
        }
    }

    private void BrowseLogPath()
    {
        // Log path is a FILE the bridge writes to (often doesn't exist
        // yet on first launch). Use OpenFileDialog with CheckFileExists=
        // false so the operator can pick a spot for a future file.
        var dialog = new OpenFileDialog
        {
            Title = "Pick the bridge log file location",
            Filter = "Log files (*.log)|*.log|All files (*.*)|*.*",
            FileName = string.IsNullOrWhiteSpace(_settings.LogPath) ? "swfoc_bridge.log" : _settings.LogPath,
            CheckFileExists = false,
            CheckPathExists = true,
        };
        try
        {
            var dir = System.IO.Path.GetDirectoryName(_settings.LogPath);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
            {
                dialog.InitialDirectory = dir;
            }
        }
        catch (Exception)
        {
            // Best-effort.
        }

        if (dialog.ShowDialog() == true)
        {
            LogPath = dialog.FileName;
        }
    }

    public string GamePath
    {
        get => _settings.GamePath;
        set
        {
            if (_settings.GamePath == value)
            {
                return;
            }
            _settings.GamePath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(GamePathStatus));
        }
    }

    /// <summary>
    /// 2026-04-27: live validation of <see cref="GamePath"/>. The Settings
    /// tab binds a TextBlock to this property so the operator sees
    /// "(file not found)" or "(directory missing)" before they hit Save.
    /// Returns empty when the path is valid (so the badge collapses).
    /// </summary>
    public string GamePathStatus => ValidatePath(_settings.GamePath, requireFile: true);

    public string BridgePipeName
    {
        get => _settings.BridgePipeName;
        set
        {
            if (_settings.BridgePipeName == value)
            {
                return;
            }
            _settings.BridgePipeName = value;
            OnPropertyChanged();
        }
    }

    public string LogPath
    {
        get => _settings.LogPath;
        set
        {
            if (_settings.LogPath == value)
            {
                return;
            }
            _settings.LogPath = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LogPathStatus));
        }
    }

    /// <summary>
    /// 2026-04-27: live validation for <see cref="LogPath"/>. We only
    /// check that the parent directory exists — the log file itself may
    /// not exist yet (the bridge writes it on first connect).
    /// </summary>
    public string LogPathStatus => ValidatePath(_settings.LogPath, requireFile: false);

    /// <summary>
    /// 2026-05-07 (iter 310, Thread D arc post-finale): operator-facing input
    /// for the iter-309 <see cref="V2Settings.IconsRoot"/> setting. Two-way
    /// bound to a TextBox in the Settings tab's "Unit icons" GroupBox; null
    /// in the underlying settings is surfaced as empty string in the UI so
    /// the TextBox doesn't show "null".
    /// </summary>
    public string IconsRoot
    {
        get => _settings.IconsRoot ?? string.Empty;
        set
        {
            // Empty string is normalized to null in storage so the JSON writer
            // emits `"iconsRoot": null` (not `"iconsRoot": ""`) — matches the
            // iter-309 ResolveIconsRoot precedence (whitespace = unset = null).
            var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
            if (_settings.IconsRoot == normalized)
            {
                return;
            }
            _settings.IconsRoot = normalized;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IconsRootStatus));
        }
    }

    /// <summary>
    /// 2026-05-07 (iter 310): live status badge for IconsRoot. Three states:
    ///   "(unset — set this OR SWFOC_EXTRACTED_DDS_ROOT env var to see icons)"
    ///   "(directory not found)"
    ///   "Found N icons" — counts <c>i_button_*.dds</c> files via the same
    ///     5-candidate-relpath walk the iter-308 UnitIconResolver uses.
    /// Empty / no-icons surfaces a useful next-step message instead of hiding.
    /// Operator restart still required for the iter-309 resolver to pick up
    /// changes (deferred to iter-311+); this badge confirms the path itself.
    /// </summary>
    public string IconsRootStatus
    {
        get
        {
            var path = _settings.IconsRoot;
            if (string.IsNullOrWhiteSpace(path))
            {
                return "(unset — set this or SWFOC_EXTRACTED_DDS_ROOT env var to see unit icons)";
            }
            try
            {
                if (!System.IO.Directory.Exists(path))
                {
                    return "(directory not found)";
                }
                var iconCount = CountIconsAtRoot(path);
                if (iconCount == 0)
                {
                    return "(no i_button_*.dds files found — run python tools/asset_extractor/meg_parser.py to extract MasterTextures.meg first)";
                }
                // 2026-05-07 (iter 312): hot-swap shipped — no restart needed.
                // The composition root subscribes to IconsRoot PropertyChanged
                // and rebuilds the Spawning tab's resolver immediately.
                return string.Format(CultureInfo.InvariantCulture,
                    "Found {0} icons (Spawning tab updates live on edit)",
                    iconCount);
            }
            catch (Exception)
            {
                return "(invalid path syntax)";
            }
        }
    }

    private static int CountIconsAtRoot(string root)
    {
        // Mirror the iter-308 UnitIconResolver candidate-relpath walk so the
        // operator's count badge matches what the resolver would actually see.
        // Sum counts across all 5 candidate dirs (operator may have flat OR
        // SWFOC-conventional layout depending on how they extracted).
        var candidates = new[]
        {
            System.IO.Path.Combine("Data", "Art", "Textures", "Units"),
            System.IO.Path.Combine("Data", "Art", "Textures"),
            System.IO.Path.Combine("Art", "Textures", "Units"),
            System.IO.Path.Combine("Art", "Textures"),
            string.Empty,
        };
        var total = 0;
        foreach (var rel in candidates)
        {
            var full = string.IsNullOrEmpty(rel) ? root : System.IO.Path.Combine(root, rel);
            if (System.IO.Directory.Exists(full))
            {
                try
                {
                    total += System.IO.Directory.GetFiles(full, "i_button_*.dds").Length;
                }
                catch (Exception)
                {
                    // Unreadable subdir — skip silently rather than fail the badge.
                }
            }
        }
        return total;
    }

    private static string ValidatePath(string? path, bool requireFile)
    {
        if (string.IsNullOrWhiteSpace(path)) return "(empty)";
        try
        {
            if (requireFile)
            {
                if (System.IO.File.Exists(path)) return string.Empty;
                if (System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(path) ?? string.Empty))
                {
                    return "(file not found in expected directory)";
                }
                return "(directory missing)";
            }
            // Directory-mode (LogPath): only need the parent dir to exist.
            var dir = System.IO.Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(dir)) return "(invalid path)";
            return System.IO.Directory.Exists(dir) ? string.Empty : "(parent directory missing)";
        }
        catch (Exception)
        {
            // Path syntax errors (illegal chars on Windows etc.).
            return "(invalid path syntax)";
        }
    }

    public bool AutoConnectOnStartup
    {
        get => _settings.AutoConnectOnStartup;
        set
        {
            if (_settings.AutoConnectOnStartup == value)
            {
                return;
            }
            _settings.AutoConnectOnStartup = value;
            OnPropertyChanged();
        }
    }

    public bool ShowAdvancedHelpers
    {
        get => _settings.ShowAdvancedHelpers;
        set
        {
            if (_settings.ShowAdvancedHelpers == value)
            {
                return;
            }
            _settings.ShowAdvancedHelpers = value;
            OnPropertyChanged();
        }
    }

    public string ProfileId
    {
        get => _settings.ProfileId;
        set
        {
            if (_settings.ProfileId == value)
            {
                return;
            }
            _settings.ProfileId = value;
            OnPropertyChanged();
        }
    }

    /// <summary>Theme preference options for the picker. Stable strings.</summary>
    public ObservableCollection<string> ThemeOptions { get; } =
        new() { "system", "dark", "light" };

    /// <summary>
    /// 2026-04-25: theme preference. Setting this fires
    /// <see cref="ThemeService.ApplyPreference"/> immediately so the
    /// operator sees the change live, then auto-saves to v2_settings.json
    /// so the choice survives a relaunch.
    /// </summary>
    public string Theme
    {
        get => _settings.Theme;
        set
        {
            var normalised = (value ?? "system").Trim().ToLowerInvariant();
            if (_settings.Theme == normalised)
            {
                return;
            }
            _settings.Theme = normalised;
            ThemeService.ApplyPreference(ThemeService.ParsePreference(normalised));
            // Auto-persist on theme change — separate from the explicit Save
            // button. Prevents the user picking a theme, closing the app,
            // and finding it reverted on next launch.
            _settings.TrySave(out _);
            OnPropertyChanged();
        }
    }

    public string SettingsFilePath => V2Settings.SettingsFilePath;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public RelayCommand SaveCommand { get; }

    public RelayCommand ReloadCommand { get; }

    /// <summary>
    /// 2026-04-27 (iter 18): operator escape hatch for "I broke my
    /// settings" — overwrites every field to factory defaults (the
    /// values the no-arg V2Settings ctor produces) and persists. Gated
    /// by a Yes/No confirmation prompt to prevent accidental wipes.
    /// </summary>
    public RelayCommand ResetToDefaultsCommand { get; }

    private void ResetToDefaultsWithConfirm()
    {
        var result = System.Windows.MessageBox.Show(
            "Overwrite every Settings field with factory defaults? " +
            "Game path, log path, profile id, theme preference, etc. will all reset. " +
            "This cannot be undone (the previous v2_settings.json on disk will be replaced on next Save).",
            "Reset settings to defaults?",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Warning,
            System.Windows.MessageBoxResult.No);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            StatusMessage = "Reset cancelled.";
            return;
        }

        var defaults = new V2Settings();
        _settings.GamePath = defaults.GamePath;
        _settings.BridgePipeName = defaults.BridgePipeName;
        _settings.LogPath = defaults.LogPath;
        _settings.AutoConnectOnStartup = defaults.AutoConnectOnStartup;
        _settings.ShowAdvancedHelpers = defaults.ShowAdvancedHelpers;
        _settings.ProfileId = defaults.ProfileId;
        _settings.Theme = defaults.Theme;

        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(BridgePipeName));
        OnPropertyChanged(nameof(LogPath));
        OnPropertyChanged(nameof(AutoConnectOnStartup));
        OnPropertyChanged(nameof(ShowAdvancedHelpers));
        OnPropertyChanged(nameof(ProfileId));
        // Theme follows the same ThemePreference setter binding.
        OnPropertyChanged(nameof(GamePathStatus));
        OnPropertyChanged(nameof(LogPathStatus));

        var ok = _settings.TrySave(out var error);
        StatusMessage = ok
            ? "Settings reset to defaults and saved."
            : $"Reset to defaults but save failed: {error}";
    }

    private void Save()
    {
        var ok = _settings.TrySave(out var error);
        StatusMessage = ok
            ? $"Saved {DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture)}"
            : $"Save failed: {error}";
    }

    private void Reload()
    {
        var fresh = V2Settings.Load();
        _settings.GamePath = fresh.GamePath;
        _settings.BridgePipeName = fresh.BridgePipeName;
        _settings.LogPath = fresh.LogPath;
        _settings.AutoConnectOnStartup = fresh.AutoConnectOnStartup;
        _settings.ShowAdvancedHelpers = fresh.ShowAdvancedHelpers;
        _settings.ProfileId = fresh.ProfileId;

        OnPropertyChanged(nameof(GamePath));
        OnPropertyChanged(nameof(BridgePipeName));
        OnPropertyChanged(nameof(LogPath));
        OnPropertyChanged(nameof(AutoConnectOnStartup));
        OnPropertyChanged(nameof(ShowAdvancedHelpers));
        OnPropertyChanged(nameof(ProfileId));
        StatusMessage = "Reloaded from disk.";
    }

    /// <summary>
    /// 2026-05-07 (iter 301): error handler for AsyncRelayCommand mod-picker
    /// commands. Routes to the visible <see cref="ModPickerStatus"/> field
    /// so operators see what failed without diving into Output panes.
    /// </summary>
    private void HandleError(Exception ex)
    {
        ModPickerStatus = $"Mod picker failed: {ex.Message}";
    }
}
