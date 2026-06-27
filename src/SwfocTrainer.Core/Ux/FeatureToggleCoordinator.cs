namespace SwfocTrainer.Core.Ux;

/// <summary>
/// Records the lifecycle state of a single feature toggle (e.g.
/// "GodMode = ON"). Used by <see cref="FeatureToggleCoordinator"/> to
/// drive cleanup-on-disable: when the editor detaches from the game,
/// the coordinator can iterate every Enabled toggle and run its
/// disable callback so the engine doesn't get left with leftover
/// hooks installed.
/// </summary>
public sealed record FeatureToggleState(
    string FeatureId,
    bool Enabled,
    DateTimeOffset LastChanged,
    string? LastReason = null);

/// <summary>
/// Async lifecycle hook for a feature toggle. Both Enable and Disable
/// return a <see cref="UxFeedback"/> that the coordinator emits on the
/// shared sink — so the operator sees every state transition.
/// </summary>
public delegate Task<UxFeedback> FeatureToggleAction(CancellationToken cancellationToken);

/// <summary>
/// Cross-cutting feature-toggle bookkeeper for V2 tabs. Solves Task
/// #155's "cleanup-on-disable" requirement: every feature that gets
/// enabled is registered here with its disable callback, and the
/// coordinator runs every disable on detach (or panic-shutdown) so
/// the bridge never leaves an installed hook hanging.
///
/// Uses async/await so disable callbacks can chain network I/O
/// (named-pipe round-trips). Concurrent calls to ToggleAsync for the
/// same feature are serialised per-feature via a lock dictionary.
/// </summary>
public sealed class FeatureToggleCoordinator
{
    private readonly IUxFeedbackSink _sink;
    private readonly Dictionary<string, FeatureToggleState> _state = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FeatureToggleAction> _disableActions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateLock = new();

    public FeatureToggleCoordinator(IUxFeedbackSink sink)
    {
        ArgumentNullException.ThrowIfNull(sink);
        _sink = sink;
    }

    /// <summary>Snapshot of the current toggle state map.</summary>
    public IReadOnlyDictionary<string, FeatureToggleState> States
    {
        get
        {
            lock (_stateLock)
            {
                return new Dictionary<string, FeatureToggleState>(_state, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    /// <summary>
    /// Toggle a feature on or off. Atomically acquires a per-feature
    /// semaphore so two concurrent toggle calls for the same feature
    /// queue up rather than racing. Records the new state + emits a
    /// feedback notification on the shared sink.
    ///
    /// Idempotent: ToggleAsync(id, true) when the feature is already
    /// enabled is a no-op (no callback runs, no feedback emitted).
    /// This matches what the OHK / GodMode bridge stubs already do.
    /// </summary>
    public async Task<UxFeedback> ToggleAsync(
        string featureId,
        bool enable,
        FeatureToggleAction action,
        FeatureToggleAction? disableAction = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(featureId);
        ArgumentNullException.ThrowIfNull(action);

        var sem = GetOrCreateSemaphore(featureId);
        await sem.WaitAsync(cancellationToken);
        try
        {
            // Idempotency check.
            FeatureToggleState? current;
            lock (_stateLock) { _state.TryGetValue(featureId, out current); }
            var alreadyAtTarget = current is not null && current.Enabled == enable;
            if (alreadyAtTarget)
            {
                // Even a no-op gets emitted on the sink — the operator
                // sees "GodMode already on" instead of silence after a
                // double-click. Better UX than swallowing the event.
                var noopFeedback = UxFeedback.Info(
                    title: $"{featureId}: already {(enable ? "enabled" : "disabled")}",
                    message: "no-op (idempotent toggle)",
                    featureId: featureId);
                _sink.Emit(noopFeedback);
                return noopFeedback;
            }

            // Run the caller-provided action.
            var feedback = await action(cancellationToken);
            _sink.Emit(feedback);

            // Stash the disable callback when enabling so detach can call it.
            lock (_stateLock)
            {
                if (enable && disableAction is not null)
                {
                    _disableActions[featureId] = disableAction;
                }
                else if (!enable)
                {
                    _disableActions.Remove(featureId);
                }
                _state[featureId] = new FeatureToggleState(
                    FeatureId: featureId,
                    Enabled: enable,
                    LastChanged: DateTimeOffset.UtcNow,
                    LastReason: feedback.Message);
            }
            return feedback;
        }
        finally
        {
            sem.Release();
        }
    }

    /// <summary>
    /// Run every registered disable callback for currently-enabled
    /// features. Called on detach + on app shutdown. Errors in any
    /// individual disable callback are caught + emitted as
    /// Warning-severity feedback so a single misbehaving feature
    /// doesn't block cleanup of the others.
    /// </summary>
    public async Task<int> CleanupAllAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, FeatureToggleAction> snapshot;
        lock (_stateLock)
        {
            snapshot = new Dictionary<string, FeatureToggleAction>(
                _disableActions, StringComparer.OrdinalIgnoreCase);
        }

        var cleaned = 0;
        foreach (var (featureId, disableAction) in snapshot)
        {
            try
            {
                var feedback = await disableAction(cancellationToken);
                _sink.Emit(feedback);
                cleaned++;
                lock (_stateLock)
                {
                    _state[featureId] = new FeatureToggleState(
                        FeatureId: featureId,
                        Enabled: false,
                        LastChanged: DateTimeOffset.UtcNow,
                        LastReason: $"cleanup: {feedback.Message}");
                    _disableActions.Remove(featureId);
                }
            }
            catch (Exception ex)
            {
                // Don't let one bad disable kill the others.
                _sink.Emit(UxFeedback.Warning(
                    title: $"{featureId}: cleanup-on-disable failed",
                    message: ex.Message,
                    featureId: featureId));
            }
        }
        return cleaned;
    }

    /// <summary>Returns true when the feature is currently flagged enabled.</summary>
    public bool IsEnabled(string featureId)
    {
        ArgumentNullException.ThrowIfNull(featureId);
        lock (_stateLock)
        {
            return _state.TryGetValue(featureId, out var s) && s.Enabled;
        }
    }

    /// <summary>Ids of features that are currently enabled.</summary>
    public IReadOnlyList<string> EnabledFeatures()
    {
        lock (_stateLock)
        {
            return _state.Where(kv => kv.Value.Enabled).Select(kv => kv.Key).ToList();
        }
    }

    private SemaphoreSlim GetOrCreateSemaphore(string featureId)
    {
        lock (_stateLock)
        {
            if (!_locks.TryGetValue(featureId, out var sem))
            {
                sem = new SemaphoreSlim(1, 1);
                _locks[featureId] = sem;
            }
            return sem;
        }
    }
}
