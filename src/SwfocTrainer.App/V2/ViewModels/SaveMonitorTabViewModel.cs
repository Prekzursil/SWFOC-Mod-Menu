using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-05-07 (iter 467, Save Monitor tab): live FileSystemWatcher over the
/// SWFOC Save folder. Logs every save creation/modification, computes file-size
/// deltas across consecutive saves, and surfaces a warning when the size growth
/// exceeds a heuristic threshold (default: 5 MB per save vs prior).
///
/// LIVE — runs locally; no SWFOC attach required. Catches the iter-466
/// soft-lock pattern (runaway AI TaskForce accumulation produces ballooning
/// save size) WHILE the campaign is in progress, not after the freeze.
/// </summary>
public sealed class SaveMonitorTabViewModel : ObservableBase, IDisposable
{
    private const long DefaultGrowthWarnBytes = 5_000_000L;

    private readonly ObservableCollection<SaveLogEntry> _entries = new();
    private readonly Dictionary<string, long> _lastSizeByName = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private string _saveDirectory;
    private string _status = "(idle — point at a Save folder, click Start)";
    private bool _isRunning;
    private long _growthWarnBytes = DefaultGrowthWarnBytes;
    private int _warningCount;

    public SaveMonitorTabViewModel(string? saveDirectory = null)
    {
        _saveDirectory = ResolveSaveDirectory(saveDirectory);
        StartCommand = new RelayCommand(Start, () => !_isRunning && Directory.Exists(_saveDirectory));
        StopCommand = new RelayCommand(Stop, () => _isRunning);
        ClearLogCommand = new RelayCommand(() =>
        {
            _entries.Clear();
            WarningCount = 0;
            _lastSizeByName.Clear();
        });
        SeedFromDirectoryCommand = new RelayCommand(SeedFromDirectory);
    }

    public ObservableCollection<SaveLogEntry> Entries => _entries;

    public string SaveDirectory
    {
        get => _saveDirectory;
        set
        {
            if (SetField(ref _saveDirectory, value ?? string.Empty))
            {
                Status = Directory.Exists(_saveDirectory)
                    ? $"(ready: {_saveDirectory})"
                    : $"(SaveDir does not exist: {_saveDirectory})";
            }
        }
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetField(ref _isRunning, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        private set => SetField(ref _warningCount, value);
    }

    public long GrowthWarnBytes
    {
        get => _growthWarnBytes;
        set => SetField(ref _growthWarnBytes, Math.Max(0, value));
    }

    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand SeedFromDirectoryCommand { get; }

    private void Start()
    {
        if (_isRunning) return;
        if (!Directory.Exists(_saveDirectory))
        {
            Status = $"Cannot start: SaveDir does not exist: {_saveDirectory}";
            return;
        }
        try
        {
            _watcher = new FileSystemWatcher(_saveDirectory, "*.PetroglyphFoC*Save")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnSaveEvent;
            _watcher.Changed += OnSaveEvent;
            _watcher.Renamed += OnSaveEvent;
            IsRunning = true;
            Status = $"Watching {_saveDirectory}...";
            AppendEntry(new SaveLogEntry(DateTime.Now, "(monitor started)", 0, 0, false, "Watching for save events"));
            SeedFromDirectory();
        }
        catch (Exception ex)
        {
            Status = $"Start failed: {ex.Message}";
        }
    }

    private void Stop()
    {
        if (!_isRunning) return;
        try
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnSaveEvent;
                _watcher.Changed -= OnSaveEvent;
                _watcher.Renamed -= OnSaveEvent;
                _watcher.Dispose();
                _watcher = null;
            }
            IsRunning = false;
            Status = "(stopped)";
            AppendEntry(new SaveLogEntry(DateTime.Now, "(monitor stopped)", 0, 0, false, ""));
        }
        catch (Exception ex)
        {
            Status = $"Stop failed: {ex.Message}";
        }
    }

    private void SeedFromDirectory()
    {
        if (!Directory.Exists(_saveDirectory)) return;
        try
        {
            foreach (var path in Directory.EnumerateFiles(_saveDirectory, "*.PetroglyphFoC*Save"))
            {
                var fi = new FileInfo(path);
                _lastSizeByName[fi.Name] = fi.Length;
            }
        }
        catch
        {
        }
    }

    private void OnSaveEvent(object sender, FileSystemEventArgs e)
    {
        try
        {
            var fi = new FileInfo(e.FullPath);
            if (!fi.Exists) return;
            long prevSize = _lastSizeByName.TryGetValue(fi.Name, out var p) ? p : 0;
            long delta = fi.Length - prevSize;
            _lastSizeByName[fi.Name] = fi.Length;
            bool warn = prevSize > 0 && delta > _growthWarnBytes;
            string note = warn
                ? $"+{delta / 1_000_000.0:F1} MB vs prior tick of THIS save — possible runaway accumulation. Run Diagnose."
                : (delta == 0 ? "(no size change — partial write?)" : $"{delta / 1_000_000.0:+0.0;-0.0} MB delta");
            AppendEntry(new SaveLogEntry(DateTime.Now, fi.Name, fi.Length, delta, warn, note));
            if (warn)
            {
                WarningCount++;
            }
        }
        catch
        {
        }
    }

    private void AppendEntry(SaveLogEntry entry)
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
        {
            d.BeginInvoke(new Action(() => AppendEntry(entry)));
            return;
        }
        _entries.Insert(0, entry);
        const int hardCap = 500;
        while (_entries.Count > hardCap)
        {
            _entries.RemoveAt(_entries.Count - 1);
        }
    }

    public void Dispose()
    {
        Stop();
    }

    private static string ResolveSaveDirectory(string? explicitDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir!;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Saved Games", "Petroglyph",
            "Empire At War - Forces of Corruption", "Save");
    }
}

public sealed record SaveLogEntry(
    DateTime Timestamp,
    string Filename,
    long SizeBytes,
    long DeltaBytes,
    bool IsWarning,
    string Note)
{
    public string Time => Timestamp.ToString("HH:mm:ss");
    public string SizeMb => SizeBytes > 0 ? $"{SizeBytes / 1_000_000.0:F1} MB" : "";
    public string DeltaMb => DeltaBytes != 0 ? $"{DeltaBytes / 1_000_000.0:+0.0;-0.0;0} MB" : "";
}
