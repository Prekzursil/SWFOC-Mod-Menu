using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-05-07 (iter 466, Savegame Rescue tab kickoff): operator-facing surface
/// for the <c>tools/savegame_rescue/</c> Python toolkit shipped 2026-05-07
/// after a real soft-lock incident in the user's AOTR_SUBMOD campaign.
/// Wraps four workflows (Snapshot all / Diagnose / Compare / Repair) as
/// async-shelled Python invocations so the WPF dispatcher stays responsive
/// while a 250+ MB save parses.
///
/// LIVE — drives the local Python toolkit at
/// <c>tools/savegame_rescue/</c>. No bridge call; works whether SWFOC is
/// running or not. Honest scope: requires Python 3.10+ on PATH and the
/// repo root resolvable (env <c>SWFOC_RESCUE_TOOLS</c> or fallback to
/// <c>%USERPROFILE%\Downloads\swfoc_memory\</c>).
/// </summary>
public sealed class SavegameRescueTabViewModel : ObservableBase
{
    private const string EnvVarToolsRoot = "SWFOC_RESCUE_TOOLS";
    private const string DefaultSaveSubdir =
        @"Petroglyph\Empire At War - Forces of Corruption\Save";

    private readonly ObservableCollection<string> _output = new();
    private readonly ObservableCollection<SaveFileEntry> _saves = new();
    private string _saveDirectory;
    private string _toolsRoot;
    private string? _selectedSavePath;
    private string? _baselineSavePath;
    private string _stripChunkIdHex = "0x4B5";
    private string _status = "(idle — point at a Save folder, click Refresh)";
    private bool _isBusy;

    public SavegameRescueTabViewModel(string? toolsRoot = null, string? saveDirectory = null)
    {
        _toolsRoot = ResolveToolsRoot(toolsRoot);
        _saveDirectory = ResolveSaveDirectory(saveDirectory);

        RefreshSavesCommand = new RelayCommand(RefreshSaves);
        BackupAllCommand = new AsyncRelayCommand(
            RunBackupAllAsync,
            canExecute: () => !_isBusy && Directory.Exists(_saveDirectory),
            onError: ex => AppendLine($"[error] {ex.Message}"));
        DiagnoseSelectedCommand = new AsyncRelayCommand(
            RunDiagnoseAsync,
            canExecute: () => !_isBusy && File.Exists(_selectedSavePath),
            onError: ex => AppendLine($"[error] {ex.Message}"));
        ScanRunawayCommand = new AsyncRelayCommand(
            RunScanRunawayAsync,
            canExecute: () => !_isBusy && _saves.Count >= 2,
            onError: ex => AppendLine($"[error] {ex.Message}"));
        CompareCommand = new AsyncRelayCommand(
            RunCompareAsync,
            canExecute: () => !_isBusy && File.Exists(_selectedSavePath) && File.Exists(_baselineSavePath),
            onError: ex => AppendLine($"[error] {ex.Message}"));
        RepairCommand = new AsyncRelayCommand(
            RunRepairAsync,
            canExecute: () =>
                !_isBusy
                && File.Exists(_selectedSavePath)
                && File.Exists(_baselineSavePath)
                && !string.IsNullOrWhiteSpace(_stripChunkIdHex),
            onError: ex => AppendLine($"[error] {ex.Message}"));
        ClearOutputCommand = new RelayCommand(() => _output.Clear());
    }

    public ObservableCollection<string> Output => _output;
    public ObservableCollection<SaveFileEntry> Saves => _saves;

    public string SaveDirectory
    {
        get => _saveDirectory;
        set
        {
            if (SetField(ref _saveDirectory, value ?? string.Empty))
            {
                Status = Directory.Exists(_saveDirectory)
                    ? $"(SaveDir = {_saveDirectory} — click Refresh)"
                    : $"(SaveDir does not exist: {_saveDirectory})";
            }
        }
    }

    public string ToolsRoot
    {
        get => _toolsRoot;
        set => SetField(ref _toolsRoot, value ?? string.Empty);
    }

    public string? SelectedSavePath
    {
        get => _selectedSavePath;
        set => SetField(ref _selectedSavePath, value);
    }

    public string? BaselineSavePath
    {
        get => _baselineSavePath;
        set => SetField(ref _baselineSavePath, value);
    }

    public string StripChunkIdHex
    {
        get => _stripChunkIdHex;
        set => SetField(ref _stripChunkIdHex, value ?? string.Empty);
    }

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public ICommand RefreshSavesCommand { get; }
    public ICommand BackupAllCommand { get; }
    public ICommand DiagnoseSelectedCommand { get; }
    public ICommand ScanRunawayCommand { get; }
    public ICommand CompareCommand { get; }
    public ICommand RepairCommand { get; }
    public ICommand ClearOutputCommand { get; }

    private void RefreshSaves()
    {
        _saves.Clear();
        if (!Directory.Exists(_saveDirectory))
        {
            Status = $"(SaveDir does not exist: {_saveDirectory})";
            return;
        }
        try
        {
            var files = Directory.EnumerateFiles(_saveDirectory, "*.PetroglyphFoC*Save")
                .Select(p => new FileInfo(p))
                .OrderByDescending(fi => fi.LastWriteTime)
                .ToArray();
            foreach (var fi in files)
            {
                _saves.Add(new SaveFileEntry(fi.Name, fi.FullName, fi.Length, fi.LastWriteTime));
            }
            Status = $"Listed {files.Length} save(s) in {_saveDirectory}.";
        }
        catch (Exception ex)
        {
            Status = $"Refresh failed: {ex.Message}";
        }
    }

    private async Task RunBackupAllAsync()
    {
        await RunPythonAsync(
            new[] { "-m", "tools.savegame_rescue.save_backup_snapshot" },
            label: "BACKUP ALL");
    }

    private async Task RunDiagnoseAsync()
    {
        if (!File.Exists(_selectedSavePath))
        {
            Status = "Select a save file in the list above first.";
            return;
        }
        await RunPythonAsync(
            new[] { "-m", "tools.savegame_rescue", "parse", _selectedSavePath! },
            label: $"DIAGNOSE {Path.GetFileName(_selectedSavePath)}");
    }

    private async Task RunScanRunawayAsync()
    {
        var paths = _saves
            .OrderBy(s => s.LastWriteTime)
            .Select(s => s.FullPath)
            .ToList();
        if (paths.Count < 2)
        {
            Status = "Need at least 2 saves to scan for runaway growth.";
            return;
        }
        var args = new[] { "-m", "tools.savegame_rescue.runaway_scan" }
            .Concat(paths)
            .ToArray();
        await RunPythonAsync(args, label: $"RUNAWAY SCAN ({paths.Count} saves)");
    }

    private async Task RunCompareAsync()
    {
        if (!File.Exists(_selectedSavePath) || !File.Exists(_baselineSavePath))
        {
            Status = "Pick both a baseline (good) save and a suspect (broken) save.";
            return;
        }
        await RunPythonAsync(
            new[] { "-m", "tools.savegame_rescue", "diff", _baselineSavePath!, _selectedSavePath! },
            label: $"DIFF {Path.GetFileName(_baselineSavePath)} vs {Path.GetFileName(_selectedSavePath)}");
    }

    private async Task RunRepairAsync()
    {
        if (!File.Exists(_selectedSavePath) || !File.Exists(_baselineSavePath))
        {
            Status = "Pick both a baseline (good) save and a suspect (broken) save.";
            return;
        }
        if (string.IsNullOrWhiteSpace(_stripChunkIdHex))
        {
            Status = "Specify a chunk_id (hex) to strip — default is 0x4B5 (AI TaskForce).";
            return;
        }
        var stem = Path.GetFileNameWithoutExtension(_selectedSavePath!);
        var dir = Path.GetDirectoryName(_selectedSavePath!) ?? _saveDirectory;
        var outputPath = Path.Combine(
            dir,
            $"{stem}.repaired_{DateTime.Now:yyyyMMdd-HHmmss}.PetroglyphFoC64Save");
        if (File.Exists(outputPath))
        {
            Status = $"Output already exists (won't overwrite): {outputPath}";
            return;
        }
        await RunPythonAsync(
            new[]
            {
                "-m", "tools.savegame_rescue.strip_chunks",
                _baselineSavePath!,
                _selectedSavePath!,
                _stripChunkIdHex,
                outputPath,
            },
            label: $"REPAIR {Path.GetFileName(_selectedSavePath)} -> {Path.GetFileName(outputPath)}");
    }

    private async Task RunPythonAsync(string[] args, string label)
    {
        if (!Directory.Exists(_toolsRoot))
        {
            Status = $"Tools root not found: {_toolsRoot}. Set env {EnvVarToolsRoot} or update ToolsRoot.";
            return;
        }
        IsBusy = true;
        Status = $"Running: {label}...";
        AppendLine($"--- {DateTime.Now:HH:mm:ss} {label} ---");
        AppendLine($"  cwd: {_toolsRoot}");
        AppendLine($"  cmd: python {string.Join(" ", args.Select(QuoteIfNeeded))}");

        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            WorkingDirectory = _toolsRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        foreach (var a in args)
        {
            startInfo.ArgumentList.Add(a);
        }

        try
        {
            using var proc = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to launch Python");
            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null) AppendLine(e.Data);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null) AppendLine($"[stderr] {e.Data}");
            };
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync().ConfigureAwait(true);
            AppendLine($"--- exit {proc.ExitCode} ---");
            Status = proc.ExitCode == 0
                ? $"OK: {label}"
                : $"FAILED ({proc.ExitCode}): {label}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void AppendLine(string line)
    {
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
        {
            d.BeginInvoke(new Action(() => AppendLine(line)));
            return;
        }
        _output.Add(line);
        const int hardCap = 5000;
        while (_output.Count > hardCap)
        {
            _output.RemoveAt(0);
        }
    }

    private static string QuoteIfNeeded(string s) =>
        s.Contains(' ') ? $"\"{s}\"" : s;

    private static string ResolveToolsRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot)) return explicitRoot!;
        var fromEnv = Environment.GetEnvironmentVariable(EnvVarToolsRoot);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv!;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Downloads", "swfoc_memory");
    }

    private static string ResolveSaveDirectory(string? explicitDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir!;
        var savedGames = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(savedGames, "Saved Games", DefaultSaveSubdir);
    }
}

public sealed record SaveFileEntry(
    string Name,
    string FullPath,
    long SizeBytes,
    DateTime LastWriteTime)
{
    public string SizeMb => $"{SizeBytes / 1_000_000.0:F1} MB";
    public string Modified => LastWriteTime.ToString("yyyy-MM-dd HH:mm");
}
