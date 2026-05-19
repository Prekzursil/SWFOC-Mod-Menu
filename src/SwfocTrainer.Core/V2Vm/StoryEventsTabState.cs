using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// V2 Tab 9 (Story Events). Task #153 — flag browser, set flag,
/// fire event dropdown, reward buttons.
/// </summary>
public sealed class StoryEventsTabState
{
    private readonly IStoryEventsDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;
    private List<string> _availableEvents = new();

    public StoryEventsTabState(IStoryEventsDispatcher dispatcher, IUxFeedbackSink feedback)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        _dispatcher = dispatcher;
        _feedback = feedback;
    }

    public string SelectedEventId { get; set; } = string.Empty;
    public string FlagName { get; set; } = string.Empty;
    public string FlagValue { get; set; } = string.Empty;
    public string SearchQuery { get; set; } = string.Empty;

    public void SetAvailableEvents(IEnumerable<string> events)
    {
        ArgumentNullException.ThrowIfNull(events);
        _availableEvents = events.Where(e => !string.IsNullOrWhiteSpace(e)).ToList();
    }

    public IReadOnlyList<string> FilteredEvents()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery)) return _availableEvents;
        var q = SearchQuery.Trim();
        return _availableEvents
            .Where(e => e.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public async Task<UxFeedback> FireEventAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(SelectedEventId))
        {
            return Emit(UxFeedback.Error("fire_event", "no event selected", "fire_event"));
        }
        var ok = await _dispatcher.FireStoryEventAsync(SelectedEventId, ct);
        return Emit(ok
            ? UxFeedback.Success("fire_event",
                $"dispatched Story_Event(\"{SelectedEventId}\")", "fire_event")
            : UxFeedback.Error("fire_event", "bridge rejected", "fire_event"));
    }

    public async Task<UxFeedback> SetFlagAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(FlagName))
        {
            return Emit(UxFeedback.Error("set_flag", "no flag name", "set_flag"));
        }
        var ok = await _dispatcher.SetStoryFlagAsync(FlagName, FlagValue, ct);
        return Emit(ok
            ? UxFeedback.Success("set_flag",
                $"flag '{FlagName}' = '{FlagValue}'", "set_flag")
            : UxFeedback.Error("set_flag", "bridge rejected", "set_flag"));
    }

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

public interface IStoryEventsDispatcher
{
    Task<bool> FireStoryEventAsync(string eventId, CancellationToken ct);
    Task<bool> SetStoryFlagAsync(string flag, string value, CancellationToken ct);
}
