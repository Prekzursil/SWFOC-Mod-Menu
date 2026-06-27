using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using SwfocTrainer.App.V2.Infrastructure;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-05-07 (iter 468, Save Auto-Tools tab): operator-driven savegame
/// utilities sitting next to the engine's built-in autosave.
/// <list type="bullet">
///   <item>Auto-copy newest save to a chosen pattern (e.g. "snapshot_{n:D3}_{utc}.PetroglyphFoC64Save")
///         every time the source file mutates.</item>
///   <item>Periodic snapshot timer: every N minutes, copy the newest save out
///         to a labeled archive directory (rotation policy: keep last K).</item>
/// </list>
///
/// LIVE — file-system level; no SWFOC attach required. Honest scope: cannot
/// TRIGGER an in-game save from outside; it can only RESPOND when SWFOC writes
/// a save. The "interval" mode therefore copies the newest existing save every
/// N minutes. (Triggering an in-game save would need a SWFOC_RequestSave
/// bridge wire — currently absent from the catalog.)
/// </summary>
public sealed class SaveAutoToolsTabViewModel : ObservableBase, IDisposable
{
    private const int DefaultIntervalMinutes = 10;
    private const int DefaultMaxKeep = 20;

    private readonly ObservableCollection<string> _log = new();
    private readonly DispatcherTimer _intervalTimer;
    private FileSystemWatcher? _watcher;
    private string _saveDirectory;
    private string _archiveDirectory;
    private string _patternPrefix = "swfoc_snapshot";
    private bool _autoCopyEnabled;
    private bool _intervalEnabled;
    private int _intervalMinutes = DefaultIntervalMinutes;
    private int _maxKeep = DefaultMaxKeep;
    private int _snapshotsTaken;
    private string _status = "(idle)";

    public SaveAutoToolsTabViewModel(string? saveDirectory = null, string? archiveDirectory = null)
    {
        _saveDirectory = ResolveSaveDirectory(saveDirectory);
        _archiveDirectory = ResolveArchiveDirectory(archiveDirectory, _saveDirectory);
        _intervalTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMinutes(_intervalMinutes),
        };
        _intervalTimer.Tick += OnIntervalTick;

        EnableAutoCopyCommand = new RelayCommand(EnableAutoCopy, () => !_autoCopyEnabled);
        DisableAutoCopyCommand = new RelayCommand(DisableAutoCopy, () => _autoCopyEnabled);
        EnableIntervalCommand = new RelayCommand(EnableInterval, () => !_intervalEnabled);
        DisableIntervalCommand = new RelayCommand(DisableInterval, () => _intervalEnabled);
        SnapshotNowCommand = new RelayCommand(() => CopyNewestSave("manual"));
        ClearLogCommand = new RelayCommand(() => _log.Clear());
        OpenArchiveCommand = new RelayCommand(OpenArchiveFolder);
    }

    public ObservableCollection<string> Log => _log;

    public string SaveDirectory
    {
        get => _saveDirectory;
        set => SetField(ref _saveDirectory, value ?? string.Empty);
    }

    public string ArchiveDirectory
    {
        get => _archiveDirectory;
        set => SetField(ref _archiveDirectory, value ?? string.Empty);
    }

    public string PatternPrefix
    {
        get => _patternPrefix;
        set => SetField(ref _patternPrefix, value ?? "swfoc_snapshot");
    }

    public bool AutoCopyEnabled
    {
        get => _autoCopyEnabled;
        private set => SetField(ref _autoCopyEnabled, value);
    }

    public bool IntervalEnabled
    {
        get => _intervalEnabled;
        private set => SetField(ref _intervalEnabled, value);
    }

    public int IntervalMinutes
    {
        get => _intervalMinutes;
        set
        {
            if (SetField(ref _intervalMinutes, Math.Max(1, value)))
            {
                _intervalTimer.Interval = TimeSpan.FromMinutes(_intervalMinutes);
            }
        }
    }

    public int MaxKeep
    {
        get => _maxKeep;
        set => SetField(ref _maxKeep, Math.Max(1, value));
    }

    public int SnapshotsTaken
    {
        get => _snapshotsTaken;
        private set => SetField(ref _snapshotsTaken, value);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public ICommand EnableAutoCopyCommand { get; }
    public ICommand DisableAutoCopyCommand { get; }
    public ICommand EnableIntervalCommand { get; }
    public ICommand DisableIntervalCommand { get; }
    public ICommand SnapshotNowCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand OpenArchiveCommand { get; }

    private void EnableAutoCopy()
    {
        if (_autoCopyEnabled) return;
        if (!Directory.Exists(_saveDirectory))
        {
            AppendLog($"[error] Save dir does not exist: {_saveDirectory}");
            return;
        }
        try
        {
            _watcher = new FileSystemWatcher(_saveDirectory, "*.PetroglyphFoC*Save")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true,
            };
            _watcher.Created += OnSaveEvent;
            _watcher.Changed += OnSaveEvent;
            AutoCopyEnabled = true;
            Status = $"Auto-copy ON; watching {_saveDirectory}";
            AppendLog($"--- {DateTime.Now:HH:mm:ss} Auto-copy ENABLED (pattern: {_patternPrefix}_NNN_UTC.PetroglyphFoC64Save) ---");
        }
        catch (Exception ex)
        {
            AppendLog($"[error] {ex.Message}");
        }
    }

    private void DisableAutoCopy()
    {
        if (!_autoCopyEnabled) return;
        try
        {
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnSaveEvent;
                _watcher.Changed -= OnSaveEvent;
                _watcher.Dispose();
                _watcher = null;
            }
            AutoCopyEnabled = false;
            Status = "Auto-copy OFF";
            AppendLog($"--- {DateTime.Now:HH:mm:ss} Auto-copy DISABLED ---");
        }
        catch (Exception ex)
        {
            AppendLog($"[error] {ex.Message}");
        }
    }

    private void EnableInterval()
    {
        if (_intervalEnabled) return;
        _intervalTimer.Interval = TimeSpan.FromMinutes(_intervalMinutes);
        _intervalTimer.Start();
        IntervalEnabled = true;
        Status = $"Interval snapshot ON (every {_intervalMinutes} min)";
        AppendLog($"--- {DateTime.Now:HH:mm:ss} Interval ENABLED (every {_intervalMinutes} min, keep last {_maxKeep}) ---");
    }

    private void DisableInterval()
    {
        if (!_intervalEnabled) return;
        _intervalTimer.Stop();
        IntervalEnabled = false;
        Status = "Interval snapshot OFF";
        AppendLog($"--- {DateTime.Now:HH:mm:ss} Interval DISABLED ---");
    }

    private void OnIntervalTick(object? sender, EventArgs e)
    {
        CopyNewestSave("interval");
    }

    private DateTime _lastWatcherCopy = DateTime.MinValue;

    private void OnSaveEvent(object sender, FileSystemEventArgs e)
    {
        var now = DateTime.UtcNow;
        if ((now - _lastWatcherCopy).TotalSeconds < 3) return;
        _lastWatcherCopy = now;
        try
        {
            var fi = new FileInfo(e.FullPath);
            if (!fi.Exists || fi.Length == 0) return;
            CopyNewestSave("auto-copy");
        }
        catch
        {
        }
    }

    private void CopyNewestSave(string source)
    {
        try
        {
            if (!Directory.Exists(_saveDirectory))
            {
                AppendLog($"[error] Save dir does not exist: {_saveDirectory}");
                return;
            }
            Directory.CreateDirectory(_archiveDirectory);
            var newest = Directory.EnumerateFiles(_saveDirectory, "*.PetroglyphFoC*Save")
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();
            if (newest == null)
            {
                AppendLog("[warn] No saves found in Save dir.");
                return;
            }
            var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var seq = Interlocked.Increment(ref _seq);
            var dst = Path.Combine(_archiveDirectory,
                $"{_patternPrefix}_{seq:D4}_{stamp}.PetroglyphFoC64Save");
            File.Copy(newest.FullName, dst, overwrite: false);
            SnapshotsTaken++;
            AppendLog($"[{source}] {DateTime.Now:HH:mm:ss} {newest.Name} -> {Path.GetFileName(dst)} ({newest.Length / 1_000_000.0:F1} MB)");
            PruneOld();
        }
        catch (Exception ex)
        {
            AppendLog($"[error] {ex.Message}");
        }
    }

    private static int _seq;

    private void PruneOld()
    {
        try
        {
            var snapshots = Directory.EnumerateFiles(_archiveDirectory, $"{_patternPrefix}_*.PetroglyphFoC*Save")
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .ToArray();
            if (snapshots.Length <= _maxKeep) return;
            foreach (var fi in snapshots.Skip(_maxKeep))
            {
                try
                {
                    fi.Delete();
                    AppendLog($"[prune] removed {fi.Name}");
                }
                catch (Exception ex)
                {
                    AppendLog($"[prune-error] {fi.Name}: {ex.Message}");
                }
            }
        }
        catch
        {
        }
    }

    private void OpenArchiveFolder()
    {
        try
        {
            if (!Directory.Exists(_archiveDirectory))
            {
                Directory.CreateDirectory(_archiveDirectory);
            }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _archiveDirectory,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[error] {ex.Message}");
        }
    }

    private void AppendLog(string line)
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
        {
            d.BeginInvoke(new Action(() => AppendLog(line)));
            return;
        }
        _log.Insert(0, line);
        const int hardCap = 500;
        while (_log.Count > hardCap)
        {
            _log.RemoveAt(_log.Count - 1);
        }
    }

    public void Dispose()
    {
        DisableAutoCopy();
        DisableInterval();
    }

    private static string ResolveSaveDirectory(string? explicitDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir!;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Saved Games", "Petroglyph",
            "Empire At War - Forces of Corruption", "Save");
    }

    private static string ResolveArchiveDirectory(string? explicitDir, string saveDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir!;
        var parent = Path.GetDirectoryName(saveDir);
        return Path.Combine(parent ?? saveDir, "Save_Snapshots");
    }
}
