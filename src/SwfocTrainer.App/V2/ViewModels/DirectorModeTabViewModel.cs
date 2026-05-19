using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Director Mode tab) — INPC wrapper around DirectorModeState.
/// Content-creator camera-path tooling: stage waypoints, play through them at
/// per-waypoint duration, hide UI for clean shots, scrub time scale (slow-mo /
/// freeze-frame / fast-forward).
///
/// Playback ticking is driven from the App layer's existing dispatcher timer
/// — the VM exposes StepCommand for tests / manual stepping; the App may
/// install a DispatcherTimer that fires StepCommand.Execute on cadence.
/// </summary>
public sealed class DirectorModeTabViewModel : ObservableBase
{
    private readonly DirectorModeState _state;
    private readonly RecordingFeedbackSink _sink;
    private readonly FeatureToggleCoordinator _toggles;
    private readonly ObservableCollection<CameraWaypoint> _path = new();

    private string _waypointName = string.Empty;
    private float _waypointX;
    private float _waypointY;
    private float _waypointZ;
    private float _waypointRot;
    private float _waypointZoom = 1.0f;
    private int _waypointDurationMs = 2000;
    private int _selectedWaypointIndex = -1;
    private float _timeScale = 1.0f;
    private string _lastStatus = "(idle)";

    public DirectorModeTabViewModel(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _sink = new RecordingFeedbackSink();
        _toggles = new FeatureToggleCoordinator(_sink);
        var dispatcher = new BridgeDirectorDispatcher(bridge);
        _state = new DirectorModeState(dispatcher, _sink, _toggles);

        AddWaypointCommand = new RelayCommand(AddWaypoint);
        RemoveWaypointCommand = new RelayCommand(RemoveWaypoint);
        ClearPathCommand = new RelayCommand(ClearPath);
        ToggleHideUiCommand = new AsyncRelayCommand(ToggleHideUiCore, onError: HandleError);
        SetTimeScaleCommand = new AsyncRelayCommand(SetTimeScaleCore, () => false, HandleError);
        StartPlaybackCommand = new AsyncRelayCommand(StartPlaybackCore, onError: HandleError);
        StepPlaybackCommand = new AsyncRelayCommand(StepPlaybackCore, onError: HandleError);
        StopPlaybackCommand = new RelayCommand(StopPlayback);
        // 2026-04-27: persistence for camera paths. Operators were losing
        // waypoints on app restart; Save/Load lets them archive cinematics
        // for replay. JSON is human-readable so paths can be hand-edited /
        // shared via gist / version-controlled.
        SavePathCommand = new RelayCommand(SaveCameraPathToFile);
        LoadPathCommand = new RelayCommand(LoadCameraPathFromFile);

        // 2026-04-27 (iter 58): per-button capability metadata. Director
        // playback uses SetCameraPos (PHASE 2 PENDING — no IDA pin yet) +
        // SetGameSpeed (PHASE 2 PENDING) + DoString (LIVE — for HideUI /
        // SetCameraZoom escape hatches). Save/Load/Add/Remove waypoints
        // are pure VM-state ops with no bridge call.
        SetTimeScale = new CapabilityAwareAction("Set time scale", "SWFOC_SetGameSpeed");
        StartPlayback = new CapabilityAwareAction("Start playback", "SWFOC_SetCameraPos");
        StepPlayback = new CapabilityAwareAction("Step playback", "SWFOC_SetCameraPos");
        ToggleHideUi = new CapabilityAwareAction("Toggle hide UI", "SWFOC_DoString");
    }

    public CapabilityAwareAction SetTimeScale { get; }
    public CapabilityAwareAction StartPlayback { get; }
    public CapabilityAwareAction StepPlayback { get; }
    public CapabilityAwareAction ToggleHideUi { get; }

    public IReadOnlyList<CapabilityAwareAction> AllActions => new[]
    {
        SetTimeScale, StartPlayback, StepPlayback, ToggleHideUi,
    };

    public bool HasPhase2PendingAction => AllActions.Any(a => !a.IsAllLive);

    public string Phase2PendingWarning
    {
        get
        {
            var pending = AllActions.Where(a => !a.IsAllLive).ToList();
            if (pending.Count == 0) return string.Empty;
            var parts = pending.Select(a => $"{a.Name} ({a.Badge})");
            return "Some actions on this tab are PHASE 2 PENDING; their buttons are disabled "
                + "until a live engine hook exists. Affected: "
                + string.Join("; ", parts);
        }
    }

    public RelayCommand SavePathCommand { get; }
    public RelayCommand LoadPathCommand { get; }

    private void SaveCameraPathToFile()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save camera path as JSON",
            Filter = "Camera path (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"camera-path-{DateTime.Now:yyyyMMdd-HHmmss}.json",
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = System.Text.Json.JsonSerializer.Serialize(_path,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            System.IO.File.WriteAllText(dialog.FileName, json);
            LastStatus = $"Saved {_path.Count} waypoint(s) to {System.IO.Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            LastStatus = $"Save failed: {ex.Message}";
        }
    }

    private void LoadCameraPathFromFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Load camera path",
            Filter = "Camera path (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var json = System.IO.File.ReadAllText(dialog.FileName);
            var loaded = System.Text.Json.JsonSerializer
                .Deserialize<CameraWaypoint[]>(json) ?? Array.Empty<CameraWaypoint>();
            // Reset both the VM's ObservableCollection and the State's
            // internal path. Use the State's public Add/Clear methods so
            // playback sees the new entries — the State is the source of
            // truth, the VM collection is a view over it.
            _state.ClearPath();
            _path.Clear();
            foreach (var wp in loaded)
            {
                _state.AddWaypoint(wp);
                _path.Add(wp);
            }
            LastStatus = $"Loaded {loaded.Length} waypoint(s) from {System.IO.Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception ex)
        {
            LastStatus = $"Load failed: {ex.Message}";
        }
    }

    public string WaypointName
    {
        get => _waypointName;
        set => SetField(ref _waypointName, value ?? string.Empty);
    }

    public float WaypointX { get => _waypointX; set => SetField(ref _waypointX, value); }
    public float WaypointY { get => _waypointY; set => SetField(ref _waypointY, value); }
    public float WaypointZ { get => _waypointZ; set => SetField(ref _waypointZ, value); }
    public float WaypointRot { get => _waypointRot; set => SetField(ref _waypointRot, value); }
    public float WaypointZoom { get => _waypointZoom; set => SetField(ref _waypointZoom, value); }

    public int WaypointDurationMs
    {
        get => _waypointDurationMs;
        set => SetField(ref _waypointDurationMs, value);
    }

    public int SelectedWaypointIndex
    {
        get => _selectedWaypointIndex;
        set => SetField(ref _selectedWaypointIndex, value);
    }

    public float TimeScale
    {
        get => _timeScale;
        set => SetField(ref _timeScale, value);
    }

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge(
        "SWFOC_SetGameSpeed", "SWFOC_SetCameraPos", "SWFOC_DoString");

    public ObservableCollection<CameraWaypoint> Path => _path;

    public bool IsHideUiEnabled => _toggles.IsEnabled("director.hide_ui");
    public bool IsPlaybackRunning => _state.IsPlaybackRunning;
    public int CurrentWaypointIndex => _state.CurrentWaypointIndex;

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand AddWaypointCommand { get; }
    public ICommand RemoveWaypointCommand { get; }
    public ICommand ClearPathCommand { get; }
    public ICommand ToggleHideUiCommand { get; }
    public ICommand SetTimeScaleCommand { get; }
    public ICommand StartPlaybackCommand { get; }
    public ICommand StepPlaybackCommand { get; }
    public ICommand StopPlaybackCommand { get; }

    private void AddWaypoint()
    {
        var wp = new CameraWaypoint(
            string.IsNullOrWhiteSpace(_waypointName) ? $"WP{_path.Count + 1}" : _waypointName,
            _waypointX, _waypointY, _waypointZ,
            _waypointRot, _waypointZoom, _waypointDurationMs);
        ApplyFeedback(_state.AddWaypoint(wp));
        RefreshPath();
    }

    private void RemoveWaypoint()
    {
        ApplyFeedback(_state.RemoveWaypoint(_selectedWaypointIndex));
        RefreshPath();
    }

    private void ClearPath()
    {
        ApplyFeedback(_state.ClearPath());
        RefreshPath();
        OnPropertyChanged(nameof(IsPlaybackRunning));
        OnPropertyChanged(nameof(CurrentWaypointIndex));
    }

    private async Task ToggleHideUiCore()
    {
        var next = !IsHideUiEnabled;
        ApplyFeedback(await _state.ToggleHideUiAsync(next));
        OnPropertyChanged(nameof(IsHideUiEnabled));
    }

    private async Task SetTimeScaleCore() => ApplyFeedback(await _state.SetTimeScaleAsync(_timeScale));

    private async Task StartPlaybackCore()
    {
        ApplyFeedback(await _state.StartPlaybackAsync());
        OnPropertyChanged(nameof(IsPlaybackRunning));
        OnPropertyChanged(nameof(CurrentWaypointIndex));
    }

    private async Task StepPlaybackCore()
    {
        ApplyFeedback(await _state.StepPlaybackAsync());
        OnPropertyChanged(nameof(IsPlaybackRunning));
        OnPropertyChanged(nameof(CurrentWaypointIndex));
    }

    private void StopPlayback()
    {
        ApplyFeedback(_state.StopPlayback());
        OnPropertyChanged(nameof(IsPlaybackRunning));
        OnPropertyChanged(nameof(CurrentWaypointIndex));
    }

    private void RefreshPath()
    {
        _path.Clear();
        foreach (var wp in _state.Path) _path.Add(wp);
    }

    private void ApplyFeedback(UxFeedback fb)
    {
        LastStatus = string.Format(CultureInfo.InvariantCulture,
            "{0}: {1} — {2}", fb.Severity, fb.Title, fb.Message);
    }

    private void HandleError(Exception ex)
    {
        LastStatus = $"command failed: {ex.Message}";
    }
}
