using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Story Events tab) — INPC wrapper around StoryEventsTabState.
/// Two surfaces: dispatch a story event by id (Story_Event), or set a story
/// flag (Set_Game_Flag). Both route through SWFOC_DoString today (no
/// dedicated bridge helpers); errors surface via "bridge rejected" status.
/// </summary>
public sealed class StoryEventsTabViewModel : ObservableBase
{
    private readonly StoryEventsTabState _state;
    private readonly RecordingFeedbackSink _sink;
    private readonly ObservableCollection<string> _filteredEvents = new();

    private string _selectedEventId = string.Empty;
    private string _flagName = string.Empty;
    private string _flagValue = string.Empty;
    private string _searchQuery = string.Empty;
    private string _lastStatus = "(idle)";

    public StoryEventsTabViewModel(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _sink = new RecordingFeedbackSink();
        var dispatcher = new BridgeStoryEventsDispatcher(bridge);
        _state = new StoryEventsTabState(dispatcher, _sink);

        FireEventCommand = new AsyncRelayCommand(FireEventCore, onError: HandleError);
        SetFlagCommand = new AsyncRelayCommand(SetFlagCore, onError: HandleError);

        // 2026-04-27 (iter 59): per-button capability metadata. Both Fire
        // and Set Flag route through SWFOC_DoString → engine globals
        // (Story_Event, Set_Game_Flag) — escape hatch is LIVE.
        FireEvent = new CapabilityAwareAction("Fire story event", "SWFOC_DoString");
        SetFlag = new CapabilityAwareAction("Set story flag", "SWFOC_DoString");
    }

    public CapabilityAwareAction FireEvent { get; }
    public CapabilityAwareAction SetFlag { get; }
    public IReadOnlyList<CapabilityAwareAction> AllActions => new[] { FireEvent, SetFlag };

    public string SelectedEventId
    {
        get => _selectedEventId;
        set { if (SetField(ref _selectedEventId, value ?? string.Empty)) _state.SelectedEventId = _selectedEventId; }
    }

    public string FlagName
    {
        get => _flagName;
        set { if (SetField(ref _flagName, value ?? string.Empty)) _state.FlagName = _flagName; }
    }

    public string FlagValue
    {
        get => _flagValue;
        set { if (SetField(ref _flagValue, value ?? string.Empty)) _state.FlagValue = _flagValue; }
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetField(ref _searchQuery, value ?? string.Empty))
            {
                _state.SearchQuery = _searchQuery;
                RefreshFilteredEvents();
            }
        }
    }

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge("SWFOC_DoString");

    public ObservableCollection<string> FilteredEvents => _filteredEvents;

    /// <summary>
    /// 2026-04-27: curated common-flag suggestions for the Set_Game_Flag
    /// surface. Every entry below is a Petroglyph-convention name that
    /// shows up in vanilla campaign scripts. The bound ComboBox is
    /// IsEditable=True so mod-defined flags still work — these are
    /// autocomplete shortcuts, not a constraint.
    /// </summary>
    public IReadOnlyList<string> FlagNameSuggestions { get; } = new[]
    {
        "game_won",
        "game_lost",
        "tutorial_complete",
        "research_complete",
        "credits_unlimited",
        "tech_unlocked_all",
        "objective_complete",
        "scenario_advanced",
    };

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand FireEventCommand { get; }
    public ICommand SetFlagCommand { get; }

    public void SetAvailableEvents(IEnumerable<string> events)
    {
        _state.SetAvailableEvents(events);
        RefreshFilteredEvents();
    }

    private void RefreshFilteredEvents()
    {
        _filteredEvents.Clear();
        foreach (var e in _state.FilteredEvents())
        {
            _filteredEvents.Add(e);
        }
    }

    private async Task FireEventCore() => ApplyFeedback(await _state.FireEventAsync());
    private async Task SetFlagCore() => ApplyFeedback(await _state.SetFlagAsync());

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
