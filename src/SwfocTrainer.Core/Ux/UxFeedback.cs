namespace SwfocTrainer.Core.Ux;

/// <summary>
/// Severity tier for a UI feedback notification. Mapped to colours +
/// icons in the V2 status pane (red/yellow/green/grey). Task #155
/// introduces this taxonomy so every V2 tab uses the same severity
/// convention instead of ad-hoc string literals like "OK"/"warn".
/// </summary>
public enum UxSeverity
{
    /// <summary>Routine info — "Refreshed roster (3 players)".</summary>
    Info = 0,

    /// <summary>Operation succeeded — typically green-tick UI.</summary>
    Success,

    /// <summary>
    /// Operation completed but with caveats — bridge returned OK but
    /// the live hook is in Phase-1 stub mode, or the action partially
    /// applied (e.g. multi-select with 1-of-3 enemy units skipped).
    /// </summary>
    Warning,

    /// <summary>
    /// Operation failed loudly. The Message is shown verbatim to the
    /// user; the CorrelationId helps the operator find the matching
    /// audit log entry.
    /// </summary>
    Error,
}

/// <summary>
/// One feedback notification — a structured replacement for the
/// scattered "_lastInspectOrHardpoint = OK: ..." string concatenation
/// patterns the V2 tabs were using before #155.
///
/// Records are immutable; the UI layer subscribes to an
/// <see cref="IUxFeedbackSink"/> stream and renders each one as a
/// toast / status-bar update / scrolling output line according to
/// per-tab UX rules.
/// </summary>
public sealed record UxFeedback(
    UxSeverity Severity,
    string Title,
    string Message,
    string? CorrelationId = null,
    string? FeatureId = null,
    DateTimeOffset? OccurredAt = null)
{
    /// <summary>Convenience constructor with Info severity.</summary>
    public static UxFeedback Info(string title, string message,
                                   string? featureId = null,
                                   string? correlationId = null)
        => new(UxSeverity.Info, title, message, correlationId, featureId, DateTimeOffset.UtcNow);

    public static UxFeedback Success(string title, string message,
                                      string? featureId = null,
                                      string? correlationId = null)
        => new(UxSeverity.Success, title, message, correlationId, featureId, DateTimeOffset.UtcNow);

    public static UxFeedback Warning(string title, string message,
                                      string? featureId = null,
                                      string? correlationId = null)
        => new(UxSeverity.Warning, title, message, correlationId, featureId, DateTimeOffset.UtcNow);

    public static UxFeedback Error(string title, string message,
                                    string? featureId = null,
                                    string? correlationId = null)
        => new(UxSeverity.Error, title, message, correlationId, featureId, DateTimeOffset.UtcNow);
}

/// <summary>
/// Sink interface that V2 tab ViewModels emit feedback to. The UI
/// layer (App project) subscribes a concrete sink that updates a
/// status bar / toast queue / output log; tests use a recording sink
/// that captures every emission for inspection.
/// </summary>
public interface IUxFeedbackSink
{
    void Emit(UxFeedback feedback);
}

/// <summary>
/// Recording sink — for tests + the V2 output-log scroller. Holds
/// every emitted feedback in chronological order.
/// </summary>
public sealed class RecordingFeedbackSink : IUxFeedbackSink
{
    private readonly List<UxFeedback> _items = new();
    private readonly object _lock = new();

    public IReadOnlyList<UxFeedback> Items
    {
        get
        {
            lock (_lock) { return _items.ToList(); }
        }
    }

    public int Count
    {
        get { lock (_lock) { return _items.Count; } }
    }

    public void Emit(UxFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);
        lock (_lock) { _items.Add(feedback); }
    }

    /// <summary>Clear the buffer (e.g. when the operator clicks "Clear log" in the UI).</summary>
    public void Clear()
    {
        lock (_lock) { _items.Clear(); }
    }

    /// <summary>Last emitted feedback, or null if none.</summary>
    public UxFeedback? Last
    {
        get
        {
            lock (_lock) { return _items.Count > 0 ? _items[^1] : null; }
        }
    }

    /// <summary>Filter by severity (case-insensitive).</summary>
    public IReadOnlyList<UxFeedback> BySeverity(UxSeverity severity)
    {
        lock (_lock)
        {
            return _items.Where(f => f.Severity == severity).ToList();
        }
    }
}

/// <summary>
/// No-op sink — for headless test fixtures that don't care about
/// feedback emissions but need to satisfy the constructor's
/// IUxFeedbackSink dependency.
/// </summary>
public sealed class NullFeedbackSink : IUxFeedbackSink
{
    public static readonly NullFeedbackSink Instance = new();
    public void Emit(UxFeedback feedback) { }
}
