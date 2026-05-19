using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// One waypoint along a saved camera path.
/// </summary>
public sealed record CameraWaypoint(
    string Name,
    float X, float Y, float Z,
    float Rot, float Zoom,
    int DurationMs);

/// <summary>
/// V2 Director Mode (Task #168). Content-creator features: camera
/// paths (sequenced waypoints), hide-UI toggle, slow-motion, freeze-
/// frame. Phase 1 owns the path-data model + playback state machine;
/// Phase 2 wires it into the camera/game-speed bridge helpers.
/// </summary>
public sealed class DirectorModeState
{
    private readonly IDirectorDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;
    private readonly FeatureToggleCoordinator _toggles;
    private readonly List<CameraWaypoint> _path = new();

    public DirectorModeState(IDirectorDispatcher dispatcher, IUxFeedbackSink feedback,
                              FeatureToggleCoordinator toggles)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        ArgumentNullException.ThrowIfNull(toggles);
        _dispatcher = dispatcher;
        _feedback = feedback;
        _toggles = toggles;
    }

    public IReadOnlyList<CameraWaypoint> Path => _path;
    public bool IsPlaybackRunning { get; private set; }
    public int CurrentWaypointIndex { get; private set; }

    public UxFeedback AddWaypoint(CameraWaypoint waypoint)
    {
        ArgumentNullException.ThrowIfNull(waypoint);
        if (string.IsNullOrWhiteSpace(waypoint.Name))
        {
            return Emit(UxFeedback.Error("director.add",
                "waypoint name required", "director"));
        }
        if (waypoint.DurationMs < 0)
        {
            return Emit(UxFeedback.Error("director.add",
                $"duration must be >= 0, got {waypoint.DurationMs}", "director"));
        }
        _path.Add(waypoint);
        return Emit(UxFeedback.Success("director.add",
            $"added '{waypoint.Name}' at ({waypoint.X:0.0},{waypoint.Y:0.0},{waypoint.Z:0.0})",
            "director"));
    }

    public UxFeedback RemoveWaypoint(int index)
    {
        if (index < 0 || index >= _path.Count)
        {
            return Emit(UxFeedback.Error("director.remove",
                $"index {index} out of range [0,{_path.Count})", "director"));
        }
        var name = _path[index].Name;
        _path.RemoveAt(index);
        return Emit(UxFeedback.Info("director.remove",
            $"removed '{name}' (now {_path.Count} waypoints)", "director"));
    }

    public UxFeedback ClearPath()
    {
        var count = _path.Count;
        _path.Clear();
        IsPlaybackRunning = false;
        CurrentWaypointIndex = 0;
        return Emit(UxFeedback.Info("director.clear",
            $"cleared {count} waypoints", "director"));
    }

    public Task<UxFeedback> ToggleHideUiAsync(bool hide, CancellationToken ct = default)
    {
        return _toggles.ToggleAsync("director.hide_ui", hide,
            action: async cancel =>
            {
                var ok = await _dispatcher.SetUiVisibleAsync(!hide, cancel);
                return ok
                    ? UxFeedback.Success("director.hide_ui",
                        hide ? "UI hidden" : "UI shown", "director")
                    : UxFeedback.Error("director.hide_ui", "bridge rejected", "director");
            },
            disableAction: hide
                ? async cancel =>
                {
                    var ok = await _dispatcher.SetUiVisibleAsync(true, cancel);
                    return ok
                        ? UxFeedback.Info("director.hide_ui", "UI shown (cleanup)", "director")
                        : UxFeedback.Warning("director.hide_ui", "cleanup failed", "director");
                }
        : null,
            cancellationToken: ct);
    }

    /// <summary>
    /// Set the game-speed scale (slow-motion is &lt; 1; freeze-frame is 0;
    /// fast-forward is &gt; 1). Routes through the same SetGameSpeed helper
    /// the Speed tab uses, but the director context emits Director-tagged
    /// feedback so the operator can distinguish "I slow-mo'd from
    /// director" vs "I changed speed elsewhere".
    /// </summary>
    public async Task<UxFeedback> SetTimeScaleAsync(float scale, CancellationToken ct = default)
    {
        if (scale < 0)
        {
            return Emit(UxFeedback.Error("director.time_scale",
                $"scale must be >= 0, got {scale}", "director"));
        }
        var ok = await _dispatcher.SetGameSpeedAsync(scale, ct);
        var label = scale switch
        {
            0 => "freeze-frame",
            < 1 => $"slow-mo ({scale:0.00}×)",
            1 => "real-time",
            _ => $"fast-forward ({scale:0.00}×)"
        };
        return Emit(ok
            ? UxFeedback.Success("director.time_scale", label, "director")
            : UxFeedback.Error("director.time_scale", "bridge rejected", "director"));
    }

    /// <summary>
    /// Begin advancing through the saved path. Returns Warning when the
    /// path is empty (nothing to play). The actual frame-by-frame
    /// playback is App-driven — the App layer ticks <see cref="StepPlaybackAsync"/>
    /// according to its dispatcher timer and the per-waypoint duration.
    /// </summary>
    public Task<UxFeedback> StartPlaybackAsync(CancellationToken ct = default)
    {
        if (_path.Count == 0)
        {
            return Task.FromResult(Emit(UxFeedback.Warning("director.play",
                "no waypoints saved — playback skipped", "director")));
        }
        IsPlaybackRunning = true;
        CurrentWaypointIndex = 0;
        return StepPlaybackAsync(ct);
    }

    public async Task<UxFeedback> StepPlaybackAsync(CancellationToken ct = default)
    {
        if (!IsPlaybackRunning)
        {
            return Emit(UxFeedback.Info("director.step",
                "playback not running", "director"));
        }
        if (CurrentWaypointIndex >= _path.Count)
        {
            IsPlaybackRunning = false;
            return Emit(UxFeedback.Success("director.step",
                "playback complete", "director"));
        }
        var wp = _path[CurrentWaypointIndex];
        var ok = await _dispatcher.SetCameraPosAsync(wp.X, wp.Y, wp.Z, ct);
        if (ok)
        {
            await _dispatcher.SetCameraZoomAsync(wp.Zoom, ct);
        }
        var fb = ok
            ? UxFeedback.Info("director.step",
                $"waypoint {CurrentWaypointIndex + 1}/{_path.Count}: {wp.Name}", "director")
            : UxFeedback.Error("director.step",
                $"camera move failed at waypoint {CurrentWaypointIndex + 1}", "director");
        Emit(fb);
        CurrentWaypointIndex++;
        return fb;
    }

    public UxFeedback StopPlayback()
    {
        IsPlaybackRunning = false;
        var idx = CurrentWaypointIndex;
        CurrentWaypointIndex = 0;
        return Emit(UxFeedback.Info("director.stop",
            $"stopped at waypoint {idx} of {_path.Count}", "director"));
    }

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

public interface IDirectorDispatcher
{
    Task<bool> SetUiVisibleAsync(bool visible, CancellationToken ct);
    Task<bool> SetGameSpeedAsync(float scale, CancellationToken ct);
    Task<bool> SetCameraPosAsync(float x, float y, float z, CancellationToken ct);
    Task<bool> SetCameraZoomAsync(float zoom, CancellationToken ct);
}
