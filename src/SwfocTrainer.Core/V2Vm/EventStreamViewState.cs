using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// One drained event from the bridge's #112 ring buffer. Mirrors the
/// CSV row format the bridge emits via SWFOC_EventStreamDrain:
///   <c>timestamp_ms;obj_addr;owner_slot;requested_hp;current_hp</c>
/// </summary>
public sealed record DamageEventRow(
    long TimestampMs,
    long ObjAddr,
    int OwnerSlot,
    float RequestedHp,
    float CurrentHp)
{
    public string ObjAddrHex => $"0x{ObjAddr:X}";
    public float Delta => CurrentHp - RequestedHp;
    public bool ClampedByGodMode => Math.Abs(Delta) > 0.01f && CurrentHp > RequestedHp;
}

/// <summary>
/// V2 Event Stream view (Task #164). Tails the #112 damage-event ring
/// buffer through the bridge, parses each row, and exposes a filtered
/// + paginated view the WPF DataGrid binds to. The actual timer that
/// calls DrainAsync lives in the App project.
/// </summary>
public sealed class EventStreamViewState
{
    private readonly IEventStreamDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;
    private readonly List<DamageEventRow> _events = new();
    private const int MaxBufferRows = 5000;

    public EventStreamViewState(IEventStreamDispatcher dispatcher, IUxFeedbackSink feedback)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        _dispatcher = dispatcher;
        _feedback = feedback;
    }

    public IReadOnlyList<DamageEventRow> Events => _events;
    public int? OwnerSlotFilter { get; set; }
    public long? ObjAddrFilter { get; set; }
    public bool ShowGodModeClampsOnly { get; set; }

    public async Task<UxFeedback> DrainAsync(CancellationToken ct = default)
    {
        var rows = await _dispatcher.DrainEventStreamAsync(ct);
        if (rows.Count == 0)
        {
            return Emit(UxFeedback.Info("event_drain", "no new events", "event_drain"));
        }
        _events.AddRange(rows);
        // Cap the tail to MaxBufferRows so a long session doesn't OOM.
        if (_events.Count > MaxBufferRows)
        {
            _events.RemoveRange(0, _events.Count - MaxBufferRows);
        }
        return Emit(UxFeedback.Info("event_drain",
            $"appended {rows.Count} events (buffer={_events.Count})", "event_drain"));
    }

    public IReadOnlyList<DamageEventRow> FilteredEvents()
    {
        IEnumerable<DamageEventRow> q = _events;
        if (OwnerSlotFilter is int slot)
        {
            q = q.Where(e => e.OwnerSlot == slot);
        }
        if (ObjAddrFilter is long addr)
        {
            q = q.Where(e => e.ObjAddr == addr);
        }
        if (ShowGodModeClampsOnly)
        {
            q = q.Where(e => e.ClampedByGodMode);
        }
        return q.ToList();
    }

    public void Clear() => _events.Clear();

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

public interface IEventStreamDispatcher
{
    Task<IReadOnlyList<DamageEventRow>> DrainEventStreamAsync(CancellationToken ct);
}
