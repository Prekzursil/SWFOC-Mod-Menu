using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Input;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;

namespace SwfocTrainer.App.V2.ViewModels;

/// <summary>
/// 2026-04-26 (Unit D — Event Stream view) — INPC wrapper around
/// EventStreamViewState. Drain command pulls the bridge's #112 ring
/// buffer; FilteredEvents re-evaluates locally on filter changes.
///
/// The bridge buffer holds up to 256 events before older ones are
/// overwritten — the operator should drain frequently for
/// long-running observation. Visible buffer is capped at 5000 rows
/// in the State to avoid OOM.
/// </summary>
public sealed class EventStreamViewModel : ObservableBase, IDisposable
{
    // 2026-04-27 (iter 17): auto-drain delegated to the shared
    // PeriodicAutoRefreshDriver. 1-second cadence — bridge ring buffer
    // holds 256 events, 1Hz drains keep up with any plausible game-tick
    // event volume so we never lose data.
    private readonly EventStreamViewState _state;
    private readonly RecordingFeedbackSink _sink;
    private readonly ObservableCollection<DamageEventRow> _filtered = new();
    private readonly PeriodicAutoRefreshDriver _autoDrain;

    private int? _ownerSlotFilter;
    private long? _objAddrFilter;
    private bool _showGodModeClampsOnly;
    private string _ownerSlotFilterText = string.Empty;
    private string _objAddrFilterText = string.Empty;
    private string _lastStatus = "(idle)";
    private bool _isAutoDrainEnabled;

    public EventStreamViewModel(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _sink = new RecordingFeedbackSink();
        var dispatcher = new BridgeEventStreamDispatcher(bridge);
        _state = new EventStreamViewState(dispatcher, _sink);

        DrainCommand = new AsyncRelayCommand(DrainCore, onError: HandleError);
        ClearCommand = new RelayCommand(() =>
        {
            _state.Clear();
            RefreshFiltered();
        });
        // 2026-04-27 (iter 17): shared auto-refresh driver.
        _autoDrain = new PeriodicAutoRefreshDriver(
            interval: TimeSpan.FromSeconds(1),
            refreshAsync: async _ => await DrainCore().ConfigureAwait(false),
            onError: ex => LastStatus = $"auto-drain error: {ex.Message}");

        // 2026-04-27 (iter 59): per-button capability metadata. Drain
        // routes through SWFOC_EventStreamDrain (LIVE — SetHP detour
        // ring buffer reader).
        Drain = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Drain event stream", "SWFOC_EventStreamDrain");
    }

    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction Drain { get; }
    public IReadOnlyList<SwfocTrainer.Core.Diagnostics.CapabilityAwareAction> AllActions => new[] { Drain };

    /// <summary>
    /// 2026-04-27: live-drain toggle. Mirrors the Inspector's
    /// auto-refresh pattern — a PeriodicTimer fires DrainCore every 1 sec
    /// while checked. The bridge buffer holds 256 events; 1Hz drains keep
    /// up with any plausible event volume so we never miss data.
    /// </summary>
    public bool IsAutoDrainEnabled
    {
        get => _isAutoDrainEnabled;
        set
        {
            if (SetField(ref _isAutoDrainEnabled, value))
            {
                if (_isAutoDrainEnabled) _autoDrain.Start();
                else _autoDrain.Stop();
            }
        }
    }

    public void Dispose()
    {
        _autoDrain.Dispose();
    }

    public string OwnerSlotFilterText
    {
        get => _ownerSlotFilterText;
        set
        {
            if (SetField(ref _ownerSlotFilterText, value ?? string.Empty))
            {
                _ownerSlotFilter = int.TryParse(_ownerSlotFilterText, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var slot) ? slot : null;
                _state.OwnerSlotFilter = _ownerSlotFilter;
                RefreshFiltered();
            }
        }
    }

    public string ObjAddrFilterText
    {
        get => _objAddrFilterText;
        set
        {
            if (SetField(ref _objAddrFilterText, value ?? string.Empty))
            {
                _objAddrFilter = long.TryParse(_objAddrFilterText, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var addr) ? addr : null;
                _state.ObjAddrFilter = _objAddrFilter;
                RefreshFiltered();
            }
        }
    }

    public bool ShowGodModeClampsOnly
    {
        get => _showGodModeClampsOnly;
        set
        {
            if (SetField(ref _showGodModeClampsOnly, value))
            {
                _state.ShowGodModeClampsOnly = value;
                RefreshFiltered();
            }
        }
    }

    public string LastStatus
    {
        get => _lastStatus;
        private set => SetField(ref _lastStatus, value);
    }

    public string CapabilityBadge { get; } = CapabilityStatusCatalog.ComposeBadge("SWFOC_EventStreamDrain");

    public ObservableCollection<DamageEventRow> FilteredEvents => _filtered;

    public int TotalBufferedEvents => _state.Events.Count;

    public IReadOnlyList<UxFeedback> FeedbackHistory => _sink.Items;

    public ICommand DrainCommand { get; }
    public ICommand ClearCommand { get; }

    private async Task DrainCore()
    {
        ApplyFeedback(await _state.DrainAsync());
        RefreshFiltered();
    }

    private void RefreshFiltered()
    {
        _filtered.Clear();
        foreach (var row in _state.FilteredEvents()) _filtered.Add(row);
        OnPropertyChanged(nameof(TotalBufferedEvents));
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
