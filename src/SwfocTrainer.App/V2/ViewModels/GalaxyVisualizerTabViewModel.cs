using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SwfocTrainer.App.V2.Infrastructure;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-05-07 (iter 471, Galaxy Visualizer tab kickoff): visual dashboard for
/// the SWFOC save folder. Surfaces a per-save card grid with computed health
/// metrics, growth-rate sparkline, corruption signals, and a placeholder galaxy
/// mini-map (real planet rendering deferred until 0x3EA primary-state chunk
/// extraction lands).
///
/// LIVE — operates on file-system metadata only; no SWFOC attach required and
/// no Python invocation. Cheap enough to refresh on every tab activation.
///
/// Animation strategy: cards fade in via WPF Storyboard / DoubleAnimation on
/// `Opacity`. Health bars width-grow via DoubleAnimation on the inner Border
/// `Width`. No per-tick polling; everything runs from a single Refresh.
/// </summary>
public sealed class GalaxyVisualizerTabViewModel : ObservableBase
{
    private static readonly long AnomalyGrowthBytes = 5_000_000L;

    private readonly ObservableCollection<SaveCard> _cards = new();
    private readonly ObservableCollection<GrowthPoint> _growthCurve = new();
    private readonly ObservableCollection<ChunkSlice> _chunkSlices = new();
    private readonly ObservableCollection<PlanetEntry> _planets = new();
    private readonly Dictionary<string, int> _factionCounts = new(StringComparer.OrdinalIgnoreCase);
    private string _saveDirectory;
    private string _toolsRoot;
    private string? _selectedSavePath;
    private string _status = "(idle — click Refresh)";
    private string _inspectStatus = "(no save inspected — pick one above + click Inspect)";
    private string _planetStatus = "(no save inspected — click Inspect to extract real planet roster from save bytes)";
    private int _totalSaves;
    private int _anomalousSaves;
    private int _planetCount;
    private string _factionSummary = string.Empty;
    private double _averageHealthScore = 1.0;
    private string _growthCurvePath = string.Empty;
    private double _growthMaxMb = 100.0;
    private bool _isInspecting;

    public GalaxyVisualizerTabViewModel(string? saveDirectory = null, string? toolsRoot = null)
    {
        _saveDirectory = ResolveSaveDirectory(saveDirectory);
        _toolsRoot = ResolveToolsRoot(toolsRoot);
        RefreshCommand = new RelayCommand(Refresh);
        InspectSelectedCommand = new AsyncRelayCommand(
            InspectSelectedAsync,
            canExecute: () => !_isInspecting && File.Exists(_selectedSavePath),
            onError: ex => InspectStatus = $"[error] {ex.Message}");
    }

    public ObservableCollection<SaveCard> Cards => _cards;
    public ObservableCollection<GrowthPoint> GrowthCurve => _growthCurve;
    public ObservableCollection<ChunkSlice> ChunkSlices => _chunkSlices;
    public ObservableCollection<PlanetEntry> Planets => _planets;

    public int PlanetCount
    {
        get => _planetCount;
        private set => SetField(ref _planetCount, value);
    }

    public string FactionSummary
    {
        get => _factionSummary;
        private set => SetField(ref _factionSummary, value);
    }

    public string PlanetStatus
    {
        get => _planetStatus;
        private set => SetField(ref _planetStatus, value);
    }

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

    public string Status
    {
        get => _status;
        private set => SetField(ref _status, value);
    }

    public int TotalSaves
    {
        get => _totalSaves;
        private set => SetField(ref _totalSaves, value);
    }

    public int AnomalousSaves
    {
        get => _anomalousSaves;
        private set => SetField(ref _anomalousSaves, value);
    }

    public double AverageHealthScore
    {
        get => _averageHealthScore;
        private set => SetField(ref _averageHealthScore, value);
    }

    public string AverageHealthLabel => $"{_averageHealthScore * 100:F0}% healthy";

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

    public string InspectStatus
    {
        get => _inspectStatus;
        private set => SetField(ref _inspectStatus, value);
    }

    public string GrowthCurvePath
    {
        get => _growthCurvePath;
        private set => SetField(ref _growthCurvePath, value);
    }

    public double GrowthMaxMb
    {
        get => _growthMaxMb;
        private set => SetField(ref _growthMaxMb, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand InspectSelectedCommand { get; }

    private void Refresh()
    {
        _cards.Clear();
        _growthCurve.Clear();
        if (!Directory.Exists(_saveDirectory))
        {
            Status = $"(SaveDir does not exist: {_saveDirectory})";
            TotalSaves = 0;
            AnomalousSaves = 0;
            AverageHealthScore = 0;
            OnPropertyChanged(nameof(AverageHealthLabel));
            GrowthCurvePath = string.Empty;
            return;
        }
        try
        {
            var files = Directory.EnumerateFiles(_saveDirectory, "*.PetroglyphFoC*Save")
                .Select(p => new FileInfo(p))
                .OrderBy(f => f.LastWriteTime)
                .ToList();

            long? prevSize = null;
            DateTime? prevWrite = null;
            int anomalous = 0;
            double healthSum = 0;
            int total = files.Count;
            var orderedDeltas = new List<(FileInfo Fi, long Delta, double GrowthPerMin, bool Anom)>();
            foreach (var fi in files)
            {
                long delta = prevSize.HasValue ? fi.Length - prevSize.Value : 0;
                double minutes = prevWrite.HasValue
                    ? Math.Max(0.1, (fi.LastWriteTime - prevWrite.Value).TotalMinutes)
                    : 1.0;
                double growthPerMin = delta / minutes;
                bool anom = prevSize.HasValue && delta > AnomalyGrowthBytes;
                if (anom) anomalous++;
                orderedDeltas.Add((fi, delta, growthPerMin, anom));
                prevSize = fi.Length;
                prevWrite = fi.LastWriteTime;
            }

            foreach (var (fi, delta, growthPerMin, anom) in ((IEnumerable<(FileInfo, long, double, bool)>)orderedDeltas).Reverse())
            {
                double health = ComputeHealthScore(delta, growthPerMin, fi.Length);
                healthSum += health;
                _cards.Add(new SaveCard(
                    fi.Name,
                    fi.FullName,
                    fi.Length,
                    fi.LastWriteTime,
                    delta,
                    growthPerMin,
                    health,
                    anom));
            }

            TotalSaves = total;
            AnomalousSaves = anomalous;
            AverageHealthScore = total > 0 ? healthSum / total : 1.0;
            OnPropertyChanged(nameof(AverageHealthLabel));
            BuildGrowthCurve(orderedDeltas);
            Status = $"Loaded {total} saves; {anomalous} anomalous; avg health {AverageHealthScore * 100:F0}%.";
        }
        catch (Exception ex)
        {
            Status = $"Refresh failed: {ex.Message}";
        }
    }

    private const double GrowthCanvasWidth = 600.0;
    private const double GrowthCanvasHeight = 220.0;

    private void BuildGrowthCurve(List<(FileInfo Fi, long Delta, double GrowthPerMin, bool Anom)> ordered)
    {
        if (ordered.Count == 0)
        {
            GrowthCurvePath = string.Empty;
            return;
        }
        double maxMb = Math.Max(50.0, ordered.Max(o => o.Fi.Length) / 1_000_000.0);
        GrowthMaxMb = Math.Ceiling(maxMb / 50.0) * 50.0;
        double n = Math.Max(1, ordered.Count - 1);
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < ordered.Count; i++)
        {
            double sizeMb = ordered[i].Fi.Length / 1_000_000.0;
            double x = (i / n) * GrowthCanvasWidth;
            double y = GrowthCanvasHeight - (sizeMb / GrowthMaxMb) * GrowthCanvasHeight;
            sb.Append(i == 0 ? 'M' : 'L');
            sb.Append(' ');
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F1}", x);
            sb.Append(',');
            sb.AppendFormat(System.Globalization.CultureInfo.InvariantCulture, "{0:F1}", y);
            sb.Append(' ');
            _growthCurve.Add(new GrowthPoint(ordered[i].Fi.Name, sizeMb, x, y, ordered[i].Anom));
        }
        GrowthCurvePath = sb.ToString();
    }

    private async Task InspectSelectedAsync()
    {
        if (!File.Exists(_selectedSavePath))
        {
            InspectStatus = "Pick a save in the cards above first.";
            return;
        }
        if (!Directory.Exists(_toolsRoot))
        {
            InspectStatus = $"Tools root not found: {_toolsRoot}. Set env SWFOC_RESCUE_TOOLS or update ToolsRoot.";
            return;
        }
        _isInspecting = true;
        InspectStatus = $"Parsing {Path.GetFileName(_selectedSavePath)}...";
        try
        {
            string? stdout = await RunPythonParserAsync(_selectedSavePath!).ConfigureAwait(true);
            if (stdout is null)
            {
                InspectStatus = "Parse failed; see Save Rescue tab for details.";
                return;
            }
            ApplyHistogramToSlices(stdout);
            InspectStatus = $"Parsed {Path.GetFileName(_selectedSavePath)} — {_chunkSlices.Count} unique chunk IDs";

            PlanetStatus = $"Extracting galactic state from {Path.GetFileName(_selectedSavePath)}...";
            await ExtractGalaxyStateAsync(_selectedSavePath!).ConfigureAwait(true);
        }
        finally
        {
            _isInspecting = false;
        }
    }

    private async Task<string?> RunPythonParserAsync(string savePath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "python",
            WorkingDirectory = _toolsRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-m");
        startInfo.ArgumentList.Add("tools.savegame_rescue");
        startInfo.ArgumentList.Add("parse");
        startInfo.ArgumentList.Add(savePath);
        using var proc = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to launch Python");
        string stdout = await proc.StandardOutput.ReadToEndAsync().ConfigureAwait(true);
        await proc.WaitForExitAsync().ConfigureAwait(true);
        return proc.ExitCode == 0 ? stdout : null;
    }

    private async Task ExtractGalaxyStateAsync(string savePath)
    {
        string tempJson = Path.Combine(Path.GetTempPath(),
            $"swfoc_galaxy_{Guid.NewGuid():N}.json");
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "python",
                WorkingDirectory = _toolsRoot,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            startInfo.ArgumentList.Add("-m");
            startInfo.ArgumentList.Add("tools.savegame_rescue.extract_galaxy_state");
            startInfo.ArgumentList.Add(savePath);
            startInfo.ArgumentList.Add("--output");
            startInfo.ArgumentList.Add(tempJson);
            using var proc = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to launch extract_galaxy_state");
            string stderr = await proc.StandardError.ReadToEndAsync().ConfigureAwait(true);
            await proc.WaitForExitAsync().ConfigureAwait(true);
            if (proc.ExitCode != 0 || !File.Exists(tempJson))
            {
                PlanetStatus = $"Extract failed (exit {proc.ExitCode}). {stderr.Trim()}".Trim();
                return;
            }
            string json = await File.ReadAllTextAsync(tempJson).ConfigureAwait(true);
            ApplyGalaxyExtract(json, Path.GetFileName(savePath));
        }
        catch (Exception ex)
        {
            PlanetStatus = $"[error] {ex.Message}";
        }
        finally
        {
            try { if (File.Exists(tempJson)) File.Delete(tempJson); } catch { /* best-effort */ }
        }
    }

    private void ApplyGalaxyExtract(string json, string saveName)
    {
        List<PlanetEntry> entries;
        List<string> factions;
        int planetNamesTotal;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            entries = new List<PlanetEntry>();
            if (root.TryGetProperty("planets", out var planetsArr) && planetsArr.ValueKind == JsonValueKind.Array)
            {
                foreach (var p in planetsArr.EnumerateArray())
                {
                    string name = p.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                    long offset = p.TryGetProperty("chunk_offset", out var o) ? o.GetInt64() : 0;
                    int chunkId = p.TryGetProperty("chunk_id", out var ci) ? ci.GetInt32() : 0;
                    int chunkSize = p.TryGetProperty("chunk_size", out var cs) ? cs.GetInt32() : 0;
                    var factionList = new List<string>();
                    if (p.TryGetProperty("candidate_factions", out var fa) && fa.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var f in fa.EnumerateArray())
                        {
                            var s = f.GetString();
                            if (!string.IsNullOrEmpty(s)) factionList.Add(s!);
                        }
                    }
                    entries.Add(new PlanetEntry(name, chunkId, offset, chunkSize,
                        string.Join(", ", factionList),
                        FactionToBrush(factionList.Count > 0 ? factionList[0] : string.Empty)));
                }
            }
            factions = new List<string>();
            if (root.TryGetProperty("factions_seen", out var fSeen) && fSeen.ValueKind == JsonValueKind.Array)
            {
                foreach (var f in fSeen.EnumerateArray())
                {
                    var s = f.GetString();
                    if (!string.IsNullOrEmpty(s)) factions.Add(s!);
                }
            }
            planetNamesTotal = root.TryGetProperty("planet_names_total", out var pt) ? pt.GetInt32() : entries.Count;
        }
        catch (Exception ex)
        {
            PlanetStatus = $"[json parse failed] {ex.Message}";
            return;
        }

        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
        {
            d.Invoke(() => PopulatePlanets(entries, factions, planetNamesTotal, saveName));
        }
        else
        {
            PopulatePlanets(entries, factions, planetNamesTotal, saveName);
        }
    }

    private void PopulatePlanets(List<PlanetEntry> entries, List<string> factions, int planetNamesTotal, string saveName)
    {
        _planets.Clear();
        _factionCounts.Clear();
        foreach (var entry in entries.OrderBy(e => e.PlanetName, StringComparer.OrdinalIgnoreCase))
        {
            _planets.Add(entry);
            if (!string.IsNullOrWhiteSpace(entry.PrimaryFaction))
            {
                _factionCounts[entry.PrimaryFaction] = _factionCounts.GetValueOrDefault(entry.PrimaryFaction) + 1;
            }
        }
        PlanetCount = entries.Count;
        FactionSummary = factions.Count == 0
            ? "(no faction tokens co-located)"
            : string.Join(" · ", factions);
        PlanetStatus = entries.Count == 0
            ? $"Extracted from {saveName} — 0 known-planet hits. Try a save further into a campaign or extend KNOWN_PLANETS."
            : $"Extracted from {saveName} — {entries.Count} planet entries ({planetNamesTotal} name strings); {factions.Count} factions seen.";
    }

    private static Brush FactionToBrush(string faction) => faction switch
    {
        "Empire" => new SolidColorBrush(Color.FromRgb(0x55, 0x88, 0xC0)),
        "Rebels" => new SolidColorBrush(Color.FromRgb(0xC0, 0x55, 0x55)),
        "Underworld" => new SolidColorBrush(Color.FromRgb(0xC0, 0xA8, 0x55)),
        "Pirates" => new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88)),
        "Hutts" => new SolidColorBrush(Color.FromRgb(0xA0, 0x70, 0x55)),
        "Republic" => new SolidColorBrush(Color.FromRgb(0x55, 0xC0, 0xA0)),
        "Neutral" => new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x90)),
        "Mandalorians" => new SolidColorBrush(Color.FromRgb(0x90, 0x70, 0xC0)),
        "BlackSun" => new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x50)),
        "CIS" => new SolidColorBrush(Color.FromRgb(0xA0, 0x55, 0x88)),
        _ => new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x60)),
    };

    private static readonly Regex HistRegex =
        new(@"^\s*0x([0-9A-Fa-f]+)\s+(\d+)\s+(\d+)\s*$", RegexOptions.Compiled);

    private void ApplyHistogramToSlices(string parserOutput)
    {
        var hits = new List<(uint Cid, int Count, long Bytes)>();
        foreach (var line in parserOutput.Split('\n'))
        {
            var m = HistRegex.Match(line);
            if (!m.Success) continue;
            uint cid = Convert.ToUInt32(m.Groups[1].Value, 16);
            int count = int.Parse(m.Groups[2].Value);
            long bytes = long.Parse(m.Groups[3].Value);
            hits.Add((cid, count, bytes));
        }
        if (hits.Count == 0) return;
        long totalBytes = hits.Sum(h => h.Bytes);
        var top = hits.OrderByDescending(h => h.Bytes).Take(15).ToList();
        if (Application.Current?.Dispatcher is { } d && !d.CheckAccess())
        {
            d.Invoke(() => PopulateSlices(top, totalBytes));
        }
        else
        {
            PopulateSlices(top, totalBytes);
        }
    }

    private void PopulateSlices(List<(uint Cid, int Count, long Bytes)> top, long totalBytes)
    {
        _chunkSlices.Clear();
        var palette = new[]
        {
            Color.FromRgb(0x67, 0x90, 0xC0),
            Color.FromRgb(0xC8, 0x52, 0x4F),
            Color.FromRgb(0x4A, 0xA8, 0x6B),
            Color.FromRgb(0xD8, 0x95, 0x35),
            Color.FromRgb(0x8E, 0x6F, 0xC0),
            Color.FromRgb(0x4F, 0xA0, 0xC8),
            Color.FromRgb(0xC8, 0x90, 0x4F),
            Color.FromRgb(0xA8, 0x4A, 0x8E),
            Color.FromRgb(0x6B, 0xC8, 0xA0),
            Color.FromRgb(0xC8, 0x6B, 0x6B),
        };
        for (int i = 0; i < top.Count; i++)
        {
            var (cid, count, bytes) = top[i];
            double pct = totalBytes > 0 ? (double)bytes / totalBytes : 0;
            var color = palette[i % palette.Length];
            string label = $"0x{cid:X8} ({cid})";
            if (cid == 0x4B5) label += " ★ AI TaskForce";
            else if (cid == 0x3E8) label += " ★ Save metadata";
            else if (cid == 0x3E9) label += " ★ AI/scripting";
            else if (cid == 0x3EA) label += " ★ Primary state";
            else if (cid == 0x3EB) label += " ★ Auxiliary state";
            else if (cid == 0x3EC) label += " ★ Footer";
            _chunkSlices.Add(new ChunkSlice(label, count, bytes, pct, new SolidColorBrush(color)));
        }
    }

    private static double ComputeHealthScore(long delta, double growthPerMin, long size)
    {
        if (delta <= 0) return 1.0;
        double absoluteScore = 1.0 - Math.Min(1.0, (double)delta / (AnomalyGrowthBytes * 4));
        double rateScore = 1.0 - Math.Min(1.0, growthPerMin / 200_000.0);
        double sizeScore = size < 350_000_000L ? 1.0 : 0.5;
        return Math.Max(0.0, Math.Min(1.0, (absoluteScore * 0.5) + (rateScore * 0.4) + (sizeScore * 0.1)));
    }

    private static string ResolveSaveDirectory(string? explicitDir)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir!;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Saved Games", "Petroglyph",
            "Empire At War - Forces of Corruption", "Save");
    }

    private static string ResolveToolsRoot(string? explicitRoot)
    {
        if (!string.IsNullOrWhiteSpace(explicitRoot)) return explicitRoot!;
        var fromEnv = Environment.GetEnvironmentVariable("SWFOC_RESCUE_TOOLS");
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv!;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Downloads", "swfoc_memory");
    }
}

public sealed record SaveCard(
    string Name,
    string FullPath,
    long SizeBytes,
    DateTime LastWriteTime,
    long DeltaVsPrior,
    double GrowthPerMinute,
    double HealthScore,
    bool IsAnomalous)
{
    public string DisplayName =>
        Name.Length > 38 ? Name.Substring(0, 35) + "..." : Name;

    public string SizeMb => $"{SizeBytes / 1_000_000.0:F1} MB";
    public string DeltaMb =>
        DeltaVsPrior == 0 ? "(baseline)" : $"{DeltaVsPrior / 1_000_000.0:+0.0;-0.0;0} MB";
    public string GrowthLabel =>
        GrowthPerMinute < 1000 ? "stable" : $"{GrowthPerMinute / 1_000_000.0:F2} MB/min";
    public string Modified => LastWriteTime.ToString("yyyy-MM-dd HH:mm");
    public string HealthLabel => $"{HealthScore * 100:F0}%";
    public double HealthBarWidth => Math.Max(2.0, 240.0 * HealthScore);

    public Brush HealthBrush =>
        IsAnomalous
            ? new SolidColorBrush(Color.FromRgb(0xC8, 0x42, 0x42))
            : HealthScore < 0.5
                ? new SolidColorBrush(Color.FromRgb(0xD8, 0x95, 0x35))
                : new SolidColorBrush(Color.FromRgb(0x4A, 0xA8, 0x6B));

    public Brush BorderBrush =>
        IsAnomalous
            ? new SolidColorBrush(Color.FromRgb(0xA0, 0x35, 0x35))
            : new SolidColorBrush(Color.FromRgb(0x40, 0x40, 0x40));

    public string AnomalyBadge => IsAnomalous ? "ANOMALY" : "OK";
}

public sealed record GrowthPoint(string Filename, double SizeMb, double X, double Y, bool IsAnomalous)
{
    public string Tooltip => $"{Filename}\n{SizeMb:F1} MB" + (IsAnomalous ? " (anomaly)" : string.Empty);
}

public sealed record ChunkSlice(string Label, int Count, long Bytes, double FractionOfTotal, Brush Fill)
{
    public string SizeMb => $"{Bytes / 1_000_000.0:F1} MB";
    public string PercentLabel => $"{FractionOfTotal * 100:F1}%";
    public double BarWidth => Math.Max(2.0, 240.0 * FractionOfTotal);
    public string CountLabel => $"× {Count:N0}";
}

public sealed record PlanetEntry(
    string PlanetName,
    int ChunkId,
    long ChunkOffset,
    int ChunkSize,
    string FactionLabel,
    Brush FactionBrush)
{
    public string DisplayName => PlanetName.Replace('_', ' ');
    public string PrimaryFaction => FactionLabel.Split(',') is { Length: > 0 } parts
        ? parts[0].Trim()
        : string.Empty;
    public string ChunkIdHex => $"0x{ChunkId:X4}";
    public string OffsetHex => $"0x{ChunkOffset:X8}";
    public string SizeLabel => ChunkSize < 1024
        ? $"{ChunkSize} B"
        : $"{ChunkSize / 1024.0:F1} KB";
}
