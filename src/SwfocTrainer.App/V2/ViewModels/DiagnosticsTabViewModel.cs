using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Threading;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Services;

namespace SwfocTrainer.App.V2.ViewModels;

// ============================================================================
// Tab 1 — Connection & Diagnostics
//
// Auto-runs on load:
//   SWFOC_GetVersion          (live in every bridge build)
//   SWFOC_GetBuildInfo        (live in every bridge build)
//   SWFOC_DiagListRegisteredFunctions  (added 2026-04-10; deploys older than
//                                       that will report it missing)
//   SWFOC_DiagSelfTest                 (added 2026-04-10; same caveat)
//
// Displays an at-a-glance status banner (green/red), the raw probe outputs,
// and a tail of the bridge log file via FileSystemWatcher.
// ============================================================================

public sealed class DiagnosticsTabViewModel : ObservableBase, IDisposable
{
    // Minimum number of helpers we expect a current bridge to register.
    // Bumped over time as new helpers land. As of 2026-04-27 the bridge
    // registers 70+ helpers; we set the floor at 60 so a slightly older
    // build still passes (and the operator gets a softer warning instead
    // of a hard error).
    private const int MinimumExpectedHelperCount = 60;

    // How many log tail lines to show in the diagnostics panel.
    private const int MaxLogTailLines = 100;

    private readonly V2BridgeAdapter _bridge;
    private readonly V2Settings _settings;
    private readonly ObservableCollection<string> _logTail = new();
    private FileSystemWatcher? _logWatcher;
    private DispatcherTimer? _logPollTimer;
    private long _lastLogLength;

    private string _versionText = "(not probed yet)";
    private string _buildInfoText = "(not probed yet)";
    private string _registeredHelpersText = "(not probed yet)";
    private int _registeredHelperCount;
    private string _selfTestText = "(not probed yet)";
    private bool _bridgeReady;
    private string _statusBanner = "Bridge not yet probed. Click Refresh.";
    private bool _pipeConnected;
    private bool _isBusy;
    // 2026-04-27 (iter 17): auto-refresh delegated to the shared
    // PeriodicAutoRefreshDriver. 5s cadence — slow enough to avoid
    // hammering the pipe, fast enough that the operator notices a
    // bridge drop within seconds.
    private readonly PeriodicAutoRefreshDriver _autoRefresh;
    private bool _isAutoRefreshEnabled;

    public DiagnosticsTabViewModel(V2BridgeAdapter bridge, V2Settings settings)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        ArgumentNullException.ThrowIfNull(settings);
        _bridge = bridge;
        _settings = settings;

        RefreshCommand = new AsyncRelayCommand(
            RefreshAsync,
            canExecute: () => !_isBusy,
            onError: ex => AppendLog($"[refresh error] {ex.Message}"));

        // 2026-05-05 (iter 190): wire iter-178 global no-arg getter buttons.
        ReadGameModeCommand = new AsyncRelayCommand(
            () => ReadGlobalStateAsync("SWFOC_GetGameModeLua", "Game mode"),
            onError: ex => AppendLog($"[read_game_mode error] {ex.Message}"));
        ReadLocalPlayerCommand = new AsyncRelayCommand(
            () => ReadGlobalStateAsync("SWFOC_GetLocalPlayerLua", "Local player handle"),
            onError: ex => AppendLog($"[read_local_player error] {ex.Message}"));
        ReadTimeScaleCommand = new AsyncRelayCommand(
            () => ReadGlobalStateAsync("SWFOC_GetSecondsPerGameMinuteLua", "Time scale (sec/game-min)"),
            onError: ex => AppendLog($"[read_time_scale error] {ex.Message}"));
        // 2026-05-05 (iter 205): iter-181 Thread.Get_Current_Stage button.
        // Returns the current cinematic-thread stage int (or "nil" outside
        // a cinematic). Validates iter-181 namespace-agnostic finding —
        // helper handles dotted Thread.* names transparently. Useful for
        // mid-cinematic debugging: pair with iter-178 Get_Game_Mode to see
        // both the running mode AND the stage within it.
        ReadThreadStageCommand = new AsyncRelayCommand(
            () => ReadGlobalStateAsync("SWFOC_ThreadGetCurrentStageLua", "Thread current stage"),
            onError: ex => AppendLog($"[read_thread_stage error] {ex.Message}"));

        // 2026-05-05 (iter 207): iter-158 Hide_GUI_Object + iter-166
        // Show_GUI_Object symmetric pair. Operator workflow: hide an HUD
        // element during cinematic recording, click Show to re-expose
        // afterward. Same shape as iter-190 ReadGlobalState pattern but
        // with a string arg (the element name).
        HideGuiObjectCommand = new AsyncRelayCommand(
            () => InvokeGuiToggleAsync("SWFOC_HideGuiObjectLua", "Hide GUI element"),
            onError: ex => AppendLog($"[hide_gui_object error] {ex.Message}"));
        ShowGuiObjectCommand = new AsyncRelayCommand(
            () => InvokeGuiToggleAsync("SWFOC_ShowGuiObjectLua", "Show GUI element"),
            onError: ex => AppendLog($"[show_gui_object error] {ex.Message}"));

        ReadGameModeAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Read game mode (Lua)", "SWFOC_GetGameModeLua");
        ReadLocalPlayerAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Read local player (Lua)", "SWFOC_GetLocalPlayerLua");
        ReadTimeScaleAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Read time scale (Lua)", "SWFOC_GetSecondsPerGameMinuteLua");
        // 2026-05-05 (iter 205): iter-181 Thread.Get_Current_Stage capability action.
        ReadThreadStageAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Read thread stage (Lua)", "SWFOC_ThreadGetCurrentStageLua");
        // 2026-05-05 (iter 207): iter-158 Hide / iter-166 Show GUI capability actions.
        HideGuiObjectAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Hide GUI element (Lua)", "SWFOC_HideGuiObjectLua");
        ShowGuiObjectAction = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Show GUI element (Lua)", "SWFOC_ShowGuiObjectLua");

        CopyDiagnosticsCommand = new RelayCommand(CopyDiagnosticsToClipboard);
        // 2026-04-27 (iter 41): operator-discoverability for the iter 39
        // checked-in markdown capability report. Resolves the file relative
        // to the editor's running location (publish/ → ../knowledge-base/...
        // works for both dev tree and the side-by-side install layout) and
        // hands it off to ShellExecute so the OS default markdown viewer
        // opens it. Surfaces the same data as the in-app banners but in a
        // sortable, copy-friendly form.
        OpenCapabilityReportCommand = new RelayCommand(OpenCapabilityReport);
        // 2026-04-27 (iter 62): sibling button for the iter-61 surface
        // report (per-tab actions keyed by button name). Same shell-out
        // pattern as OpenCapabilityReport but a different file glob.
        OpenCapabilitySurfaceReportCommand = new RelayCommand(OpenCapabilitySurfaceReport);
        // 2026-04-27 (iter 46): activity-log clipboard export. Wired here
        // (not at the property-init line) so the implementation method
        // sits next to the command without forward-reference clutter.
        CopyActivityLogCommand = new RelayCommand(CopyActivityLogToClipboard);
        // 2026-04-28 (iter 84): JSON sibling for operators pasting into
        // automation pipelines / API requests / log aggregators.
        CopyActivityLogJsonCommand = new RelayCommand(CopyActivityLogJsonToClipboard);
        // 2026-04-27 (iter 50): file-export sibling to the clipboard one.
        SaveActivityLogCommand = new RelayCommand(SaveActivityLogToFile);
        // 2026-04-28 (iter 85): JSON file-export sibling — completes the
        // pair (clipboard TSV / clipboard JSON / file TSV / file JSON).
        SaveActivityLogJsonCommand = new RelayCommand(SaveActivityLogJsonToFile);
        // 2026-04-28 (iter 87): single-click reset for all three filters
        // (errors-only / time-window / substring). Operator routinely
        // narrows the log during bug-repro and forgets to re-widen
        // before the next attempt; this button is the "I'm done with
        // that filter set" exit.
        ResetActivityLogFiltersCommand = new RelayCommand(() =>
        {
            ActivityLogErrorsOnly = false;
            ActivityLogCommandFilter = string.Empty;
            ActivityLogTimeWindowMinutes = null;
            AppendLog("[activity_log] filters reset (errors-only / window / substring all cleared)");
        });
        // 2026-04-27 (iter 66): operator workflow — clear the activity
        // ring before reproducing a bug, then click the suspect button
        // to see only that interaction's bridge calls.
        ClearActivityLogCommand = new RelayCommand(() =>
        {
            _bridge.ClearActivityLog();
            NotifyRecentBridgeCallsChanged();
            // Update the iter 48 stats line + iter 51 last-failure
            // callout so they reflect the empty buffer.
            OnPropertyChanged(nameof(ActivityStatsLine));
            OnPropertyChanged(nameof(LastFailureSummary));
            OnPropertyChanged(nameof(HasRecentFailure));
            AppendLog("[activity_log] cleared");
        });
        // 2026-04-28 (iter 75): pin / unpin / clear-pinned. Pin uses
        // RelayCommand<T> so the XAML can pass the selected DataGrid
        // row's bound entry. Operator workflow: right-click any row →
        // Pin to bookmark; pinned entries survive ring rotation.
        PinActivityCommand = new RelayCommand<BridgeActivityEntry>(entry =>
        {
            if (entry is null) return;
            var added = _bridge.PinActivity(entry);
            OnPropertyChanged(nameof(PinnedBridgeCalls));
            // iter 83: keep header count in sync with the list.
            OnPropertyChanged(nameof(PinnedBridgeCallsHeader));
            AppendLog(added
                ? $"[activity_log] pinned: {entry.LuaCommand}"
                : "[activity_log] pin cap reached (50) — unpin something first");
        });
        UnpinActivityCommand = new RelayCommand<BridgeActivityEntry>(entry =>
        {
            if (entry is null) return;
            _bridge.UnpinActivity(entry);
            OnPropertyChanged(nameof(PinnedBridgeCalls));
            OnPropertyChanged(nameof(PinnedBridgeCallsHeader));
            AppendLog($"[activity_log] unpinned: {entry.LuaCommand}");
        });
        ClearPinnedCommand = new RelayCommand(() =>
        {
            _bridge.ClearPinnedActivity();
            OnPropertyChanged(nameof(PinnedBridgeCalls));
            OnPropertyChanged(nameof(PinnedBridgeCallsHeader));
            AppendLog("[activity_log] cleared pinned entries");
        });
        // 2026-04-28 (iter 79): operator can acknowledge the iter-51 last-
        // failure callout. Records a dismissal timestamp; LastFailureSummary
        // returns null for failures whose Timestamp <= _failureDismissedAtUtc,
        // so already-seen failures hide. Any new failure (Timestamp greater)
        // re-shows the callout automatically — operators never miss a fresh
        // problem just because they dismissed the prior one.
        DismissLastFailureCommand = new RelayCommand(() =>
        {
            _failureDismissedAtUtc = DateTimeOffset.UtcNow;
            OnPropertyChanged(nameof(LastFailureSummary));
            OnPropertyChanged(nameof(HasRecentFailure));
            AppendLog("[diagnostics] last-failure dismissed");
        });
        // 2026-04-27 (iter 47): subscribe to the adapter's per-call event so
        // the activity DataGrid live-updates regardless of which tab fired
        // the call. Marshal to the WPF dispatcher because the event fires
        // on the worker thread that completed the round-trip.
        _bridge.ActivityRecorded += OnBridgeActivityRecorded;
        _autoRefresh = new PeriodicAutoRefreshDriver(
            interval: TimeSpan.FromSeconds(5),
            refreshAsync: async _ => await RefreshAsync().ConfigureAwait(false),
            canRefresh: () => !_isBusy,
            onError: ex => AppendLog($"[auto-refresh error] {ex.Message}"));
    }

    public AsyncRelayCommand RefreshCommand { get; }

    // 2026-05-05 (iter 190): native UX for iter-178 global no-arg getters.
    // Three 0-arg engine state queries — operator clicks, result lands in the
    // diagnostic log. No input field needed (these are global getters with
    // no arg). Continues iter-188/189 native-UX surfacing arc, third tab.
    public AsyncRelayCommand ReadGameModeCommand { get; }
    /// <summary>
    /// 2026-05-05 (iter 205): iter-181 Thread.Get_Current_Stage probe.
    /// Returns the current cinematic-thread stage int (engine returns nil
    /// outside an active cinematic; bridge wrapper coerces to "nil" string).
    /// Sibling to iter-190 ReadGameMode/ReadLocalPlayer/ReadTimeScale.
    /// </summary>
    public AsyncRelayCommand ReadThreadStageCommand { get; }

    /// <summary>
    /// 2026-05-05 (iter 207): iter-158 Hide_GUI_Object + iter-166 Show_GUI_Object
    /// symmetric pair. Both take a single string element name (e.g. "Tactical_HUD",
    /// "Galactic_Top_Bar"). Operator workflow: hide HUD elements during
    /// cinematic recording, click Show after to restore. Shared input field
    /// <see cref="GuiObjectElementName"/> means operators type the name once
    /// for both buttons.
    /// </summary>
    public AsyncRelayCommand HideGuiObjectCommand { get; }
    /// <summary>2026-05-05 (iter 207): iter-166 Show_GUI_Object probe.</summary>
    public AsyncRelayCommand ShowGuiObjectCommand { get; }

    private string _guiObjectElementName = "Tactical_HUD";

    /// <summary>
    /// 2026-05-05 (iter 207): operator-supplied GUI element name for the
    /// iter-158 Hide / iter-166 Show buttons. Default is a likely-real
    /// element name so the operator can click without typing on first use,
    /// but they're free to overwrite it.
    /// </summary>
    public string GuiObjectElementName
    {
        get => _guiObjectElementName;
        set => SetField(ref _guiObjectElementName, value ?? string.Empty);
    }
    public AsyncRelayCommand ReadLocalPlayerCommand { get; }
    public AsyncRelayCommand ReadTimeScaleCommand { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ReadGameModeAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ReadLocalPlayerAction { get; }
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ReadTimeScaleAction { get; }
    /// <summary>2026-05-05 (iter 205): iter-181 Thread.Get_Current_Stage capability action.</summary>
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ReadThreadStageAction { get; }
    /// <summary>2026-05-05 (iter 207): iter-158 Hide_GUI_Object capability action.</summary>
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction HideGuiObjectAction { get; }
    /// <summary>2026-05-05 (iter 207): iter-166 Show_GUI_Object capability action.</summary>
    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction ShowGuiObjectAction { get; }

    /// <summary>
    /// 2026-04-27: aggregate version + build info + registered helpers +
    /// self-test + last log tail into a single multi-line string and copy
    /// to the clipboard. Operators raising bug reports get this in one
    /// click instead of screen-grabbing four panels.
    /// </summary>
    public RelayCommand CopyDiagnosticsCommand { get; }

    /// <summary>
    /// 2026-04-27 (iter 41): opens the auto-generated capability matrix
    /// markdown in the OS default viewer. The file is regenerated by the
    /// `CapabilityCatalogReportTests.ReportFile_MatchesCatalogSnapshot`
    /// test; this just shells out so the operator never has to grep the
    /// filesystem to find it.
    /// </summary>
    public RelayCommand OpenCapabilityReportCommand { get; }

    /// <summary>
    /// 2026-04-27 (iter 62): sibling to <see cref="OpenCapabilityReportCommand"/>
    /// — opens the editor-wide capability surface report
    /// (<c>capability_surface_2026-04-27.md</c>) which is keyed by
    /// tab + button name. The capability report is keyed by helper
    /// name; this report shows where each helper is wired in the UI.
    /// </summary>
    public RelayCommand OpenCapabilitySurfaceReportCommand { get; }

    internal void OpenCapabilitySurfaceReport()
    {
        var path = ResolveCapabilitySurfaceReportPath();
        if (path is null)
        {
            AppendLog("[capability_surface_report] not found — expected at " +
                "<editor>/../swfoc_memory/knowledge-base/capability_surface_*.md");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppendLog($"[capability_surface_report] open failed: {ex.Message}");
        }
    }

    internal static string? ResolveCapabilitySurfaceReportPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var sibling = Path.Combine(
                Path.GetDirectoryName(dir) ?? string.Empty,
                "swfoc_memory", "knowledge-base");
            if (Directory.Exists(sibling))
            {
                var matches = Directory.GetFiles(sibling, "capability_surface_*.md");
                if (matches.Length > 0)
                {
                    Array.Sort(matches);
                    return matches[^1]; // newest by sort order (yyyy-MM-dd)
                }
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>
    /// Probes plausible install layouts for the report and shells it open.
    /// Returns silently with a status-line message when not found, rather
    /// than throwing — operators expect the button to be a no-op when the
    /// editor is shipped without the report.
    /// </summary>
    internal void OpenCapabilityReport()
    {
        var path = ResolveCapabilityReportPath();
        if (path is null)
        {
            AppendLog("[capability_report] not found — expected at " +
                "<editor>/../swfoc_memory/knowledge-base/capability_status_*.md");
            return;
        }
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
            AppendLog($"[capability_report] opened {path}");
        }
        catch (Exception ex)
        {
            AppendLog($"[capability_report] open failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 2026-04-27 (iter 45) — operator-visible audit of recent bridge calls.
    /// Read directly from the <see cref="V2BridgeAdapter"/>'s ring buffer
    /// and filtered by <see cref="ActivityLogErrorsOnly"/>.
    /// The Diagnostics tab renders this in a DataGrid expander so the
    /// operator can see at-a-glance whether a click is reaching the bridge,
    /// whether responses are coming back, and how long round-trips take.
    /// </summary>
    public IReadOnlyList<BridgeActivityEntry> RecentBridgeCalls
    {
        get
        {
            IEnumerable<BridgeActivityEntry> rows = _bridge.RecentCalls;
            if (_activityLogErrorsOnly)
            {
                rows = rows.Where(e => !e.Succeeded);
            }
            // 2026-04-28 (iter 86): time-window filter. When the operator
            // selects "Last 5 minutes" or "Last 1 minute", drop entries
            // older than now - N minutes. Useful for tight bug-repro
            // windows where the operator wants to focus on a recent
            // interaction without manually clearing the log first.
            if (_activityLogTimeWindowMinutes is int windowMins && windowMins > 0)
            {
                var cutoff = DateTimeOffset.UtcNow.AddMinutes(-windowMins);
                rows = rows.Where(e => e.Timestamp >= cutoff);
            }
            // 2026-04-27 (iter 66): substring filter on the Lua command
            // text. Lets the operator type "GodMode" and see only the
            // calls that target that helper (or any substring match
            // against the raw Lua: arg values, response markers, etc.).
            // Empty / whitespace filter = no narrowing.
            if (!string.IsNullOrWhiteSpace(_activityLogCommandFilter))
            {
                var needle = _activityLogCommandFilter;
                rows = rows.Where(e =>
                    e.LuaCommand.Contains(needle, StringComparison.OrdinalIgnoreCase));
            }
            return rows.ToList();
        }
    }

    /// <summary>
    /// 2026-04-27 (iter 46) — checkbox toggle: filter the activity DataGrid
    /// to only show failed (Succeeded=false) entries.
    /// </summary>
    private bool _activityLogErrorsOnly;
    public bool ActivityLogErrorsOnly
    {
        get => _activityLogErrorsOnly;
        set
        {
            if (SetField(ref _activityLogErrorsOnly, value))
            {
                OnPropertyChanged(nameof(RecentBridgeCalls));
                OnPropertyChanged(nameof(ActiveFiltersDescription));
                OnPropertyChanged(nameof(HasActiveFilters));
            }
        }
    }

    /// <summary>
    /// 2026-04-28 (iter 86) — time-window filter on the activity log.
    /// Null = no filter (full list). 1 = "last 1 minute". 5 = "last 5
    /// minutes". Backed by a ComboBox in XAML (the bound enum-ish int
    /// makes it trivial to add more presets without changing the VM).
    /// </summary>
    private int? _activityLogTimeWindowMinutes;
    public int? ActivityLogTimeWindowMinutes
    {
        get => _activityLogTimeWindowMinutes;
        set
        {
            if (SetField(ref _activityLogTimeWindowMinutes, value))
            {
                OnPropertyChanged(nameof(RecentBridgeCalls));
                OnPropertyChanged(nameof(ActiveFiltersDescription));
                OnPropertyChanged(nameof(HasActiveFilters));
            }
        }
    }

    /// <summary>
    /// 2026-04-27 (iter 66) — substring filter on the Lua command column.
    /// Case-insensitive. Empty / whitespace = no filter (full list).
    /// Operators reproducing "X button does Y" can type "X" and see only
    /// X's calls rather than scrolling 50 entries.
    /// </summary>
    private string _activityLogCommandFilter = string.Empty;
    public string ActivityLogCommandFilter
    {
        get => _activityLogCommandFilter;
        set
        {
            if (SetField(ref _activityLogCommandFilter, value ?? string.Empty))
            {
                OnPropertyChanged(nameof(RecentBridgeCalls));
                OnPropertyChanged(nameof(ActiveFiltersDescription));
                OnPropertyChanged(nameof(HasActiveFilters));
            }
        }
    }

    /// <summary>
    /// 2026-04-28 (iter 89) — true when at least one of the three composable
    /// filters is active. Bound to a small TextBlock's Visibility via the
    /// existing BoolToVisibility converter so the active-filters status line
    /// hides when nothing is narrowing the view.
    /// </summary>
    public bool HasActiveFilters =>
        _activityLogErrorsOnly
        || _activityLogTimeWindowMinutes is not null
        || !string.IsNullOrWhiteSpace(_activityLogCommandFilter);

    /// <summary>
    /// 2026-04-28 (iter 89) — operator-readable summary of which filters
    /// are currently active. Empty when none. Format example:
    /// "Active filters: errors-only · window 5 min · 'GodMode'".
    /// Pairs with iter-87 Reset filters button so operators see what's
    /// narrowing the view AND have a clear path to widen it again.
    /// </summary>
    public string ActiveFiltersDescription
    {
        get
        {
            var parts = new System.Collections.Generic.List<string>(3);
            if (_activityLogErrorsOnly) parts.Add("errors-only");
            if (_activityLogTimeWindowMinutes is int mins)
            {
                parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "window {0} min", mins));
            }
            if (!string.IsNullOrWhiteSpace(_activityLogCommandFilter))
            {
                parts.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "'{0}'", _activityLogCommandFilter));
            }
            return parts.Count == 0
                ? string.Empty
                : "Active filters: " + string.Join(" · ", parts);
        }
    }

    /// <summary>
    /// 2026-04-27 (iter 66) — drop every entry in the activity ring buffer.
    /// Operators reproducing a bug click this before the suspect button
    /// to see only that interaction's calls. Calls back through the
    /// adapter so any other VM bound to <see cref="V2BridgeAdapter.RecentCalls"/>
    /// also re-reads on the next change-notification.
    /// </summary>
    public RelayCommand ClearActivityLogCommand { get; private set; } = default!;

    /// <summary>
    /// 2026-04-27 (iter 46) — copy current (filtered) activity log to the
    /// clipboard as a tab-separated TSV: timestamp / OK / ms / command /
    /// response. Operator-friendly for pasting into bug reports.
    /// </summary>
    public RelayCommand CopyActivityLogCommand { get; private set; } = default!;

    /// <summary>
    /// 2026-04-28 (iter 84) — JSON sibling to <see cref="CopyActivityLogCommand"/>.
    /// Operators paste activity logs into automation pipelines (or LLM
    /// review tooling that expects JSON) without having to TSV→JSON
    /// convert manually.
    /// </summary>
    public RelayCommand CopyActivityLogJsonCommand { get; private set; } = default!;

    /// <summary>
    /// 2026-04-27 (iter 50) — save the same TSV the clipboard export emits to
    /// a file picked via SaveFileDialog. For cross-session bug reports or
    /// captures larger than the clipboard can carry.
    /// </summary>
    public RelayCommand SaveActivityLogCommand { get; private set; } = default!;

    /// <summary>
    /// 2026-04-28 (iter 85) — JSON file-export sibling to <see cref="SaveActivityLogCommand"/>.
    /// Pairs with the iter-84 <see cref="CopyActivityLogJsonCommand"/> so
    /// the editor offers all four exit points (clipboard TSV / clipboard
    /// JSON / file TSV / file JSON).
    /// </summary>
    public RelayCommand SaveActivityLogJsonCommand { get; private set; } = default!;

    /// <summary>
    /// 2026-04-28 (iter 87) — single-click reset for all three activity
    /// log filters (errors-only / time-window / substring). Operator's
    /// "exit my filter set" button after a bug-repro session.
    /// </summary>
    public RelayCommand ResetActivityLogFiltersCommand { get; private set; } = default!;

    /// <summary>
    /// Build the TSV the clipboard + file exports share. Internal so tests
    /// can drive it without a clipboard or file dialog.
    /// </summary>
    internal string BuildActivityLogTsv()
    {
        var rows = RecentBridgeCalls;
        if (rows.Count == 0) return string.Empty;
        var sb = new System.Text.StringBuilder(8 * 1024);
        sb.AppendLine("timestamp\tok\tms\tcommand\tresponse_or_error");
        foreach (var e in rows)
        {
            sb.Append(e.Timestamp.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture)).Append('\t');
            sb.Append(e.Succeeded ? "1" : "0").Append('\t');
            sb.Append(e.DurationMs).Append('\t');
            sb.Append(e.LuaCommand?.Replace('\t', ' ').Replace("\r", "").Replace("\n", " ") ?? "").Append('\t');
            sb.AppendLine(e.ResponseOrError?.Replace('\t', ' ').Replace("\r", "").Replace("\n", " ") ?? "");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 2026-04-28 (iter 84) — JSON sibling to <see cref="BuildActivityLogTsv"/>.
    /// Emits a JSON array of objects with stable field names so operators
    /// pasting into bug-report automation, log-aggregator pipelines, or
    /// API request bodies don't have to parse TSV. Empty buffer → "[]".
    /// </summary>
    internal string BuildActivityLogJson()
    {
        var rows = RecentBridgeCalls;
        if (rows.Count == 0) return "[]";
        var entries = rows.Select(e => new
        {
            timestamp = e.Timestamp.ToString(
                "yyyy-MM-ddTHH:mm:ss.fffzzz",
                System.Globalization.CultureInfo.InvariantCulture),
            succeeded = e.Succeeded,
            durationMs = e.DurationMs,
            luaCommand = e.LuaCommand ?? string.Empty,
            responseOrError = e.ResponseOrError ?? string.Empty,
        });
        return System.Text.Json.JsonSerializer.Serialize(entries,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    }

    private void CopyActivityLogToClipboard()
    {
        var tsv = BuildActivityLogTsv();
        if (string.IsNullOrEmpty(tsv))
        {
            AppendLog("[activity_log] empty — nothing to copy");
            return;
        }
        try
        {
            System.Windows.Clipboard.SetText(tsv);
            AppendLog($"[activity_log] copied {RecentBridgeCalls.Count} row(s) to clipboard");
        }
        catch (Exception ex)
        {
            AppendLog($"[activity_log] clipboard copy failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 2026-04-28 (iter 84) — JSON variant. Empty buffer is allowed (emits
    /// "[]" so downstream parsers don't choke). Same exception path as
    /// the TSV copy because System.Windows.Clipboard.SetText is the
    /// failure surface, not the JSON serializer.
    /// </summary>
    private void CopyActivityLogJsonToClipboard()
    {
        var json = BuildActivityLogJson();
        try
        {
            System.Windows.Clipboard.SetText(json);
            AppendLog($"[activity_log] copied {RecentBridgeCalls.Count} row(s) as JSON");
        }
        catch (Exception ex)
        {
            AppendLog($"[activity_log] JSON clipboard copy failed: {ex.Message}");
        }
    }

    private void SaveActivityLogToFile()
    {
        var tsv = BuildActivityLogTsv();
        if (string.IsNullOrEmpty(tsv))
        {
            AppendLog("[activity_log] empty — nothing to save");
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save bridge activity log",
            FileName = $"swfoc_activity_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.tsv",
            DefaultExt = ".tsv",
            Filter = "Tab-separated values (*.tsv)|*.tsv|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true)
        {
            // Operator cancelled — quiet no-op.
            return;
        }
        try
        {
            File.WriteAllText(dialog.FileName, tsv, System.Text.Encoding.UTF8);
            AppendLog($"[activity_log] saved {RecentBridgeCalls.Count} row(s) to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            AppendLog($"[activity_log] save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 2026-04-28 (iter 85) — JSON file-export sibling of <see cref="SaveActivityLogToFile"/>.
    /// Operator picks a path via SaveFileDialog (same UX as the TSV save);
    /// the actual file-write is delegated to <see cref="WriteActivityLogJsonToFile"/>
    /// so unit tests can drive the file-emit path without a dialog.
    /// </summary>
    private void SaveActivityLogJsonToFile()
    {
        if (RecentBridgeCalls.Count == 0)
        {
            // Mirror TSV behavior: don't pop a dialog when the buffer is
            // empty. The clipboard JSON variant emits "[]" because
            // pasted-into-a-script empty arrays are valid input; for
            // file save the operator's expecting actual content.
            AppendLog("[activity_log] empty — nothing to save");
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save bridge activity log (JSON)",
            FileName = $"swfoc_activity_{DateTimeOffset.Now:yyyyMMdd_HHmmss}.json",
            DefaultExt = ".json",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            AddExtension = true,
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }
        try
        {
            WriteActivityLogJsonToFile(dialog.FileName);
            AppendLog($"[activity_log] saved {RecentBridgeCalls.Count} row(s) as JSON to {dialog.FileName}");
        }
        catch (Exception ex)
        {
            AppendLog($"[activity_log] JSON save failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 2026-04-28 (iter 85) — testable file-write seam. Writes the current
    /// JSON buffer to <paramref name="path"/> as UTF-8 (no BOM). Tests
    /// drive this directly to verify the on-disk artifact without
    /// involving SaveFileDialog.
    /// </summary>
    internal void WriteActivityLogJsonToFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var json = BuildActivityLogJson();
        File.WriteAllText(path, json, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// 2026-04-27 (iter 51) — most-recent-failure summary. Returns null when
    /// the ring buffer has no failures; the bound XAML callout uses null →
    /// hidden via a NullToVisibility converter (or an explicit binding to
    /// HasRecentFailure). Operator sees the failure at a glance instead of
    /// scrolling through 50 entries.
    /// </summary>
    public string? LastFailureSummary
    {
        get
        {
            // RecentBridgeCalls already respects the Errors-only filter; we
            // want the unfiltered list here so the callout shows even when
            // the operator is browsing the success path.
            var raw = _bridge.RecentCalls;
            var failure = raw.FirstOrDefault(e => !e.Succeeded);
            if (failure is null) return null;
            // 2026-04-28 (iter 79): respect operator dismissal. A failure
            // whose Timestamp is at-or-before the dismissal point is hidden;
            // any newer failure overrides dismissal automatically.
            if (failure.Timestamp <= _failureDismissedAtUtc) return null;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Last failure {0}: {1} → {2}",
                failure.Timestamp.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture),
                failure.LuaCommand,
                failure.ResponseOrError);
        }
    }

    /// <summary>
    /// True when the ring buffer contains at least one failed entry that
    /// the operator has not yet dismissed (iter 79). Bound to the callout's
    /// Visibility via a BooleanToVisibilityConverter (built into WPF) so the
    /// UI can hide the row when everything is OK or has been acknowledged.
    /// </summary>
    public bool HasRecentFailure
    {
        get
        {
            var failure = _bridge.RecentCalls.FirstOrDefault(e => !e.Succeeded);
            return failure is not null && failure.Timestamp > _failureDismissedAtUtc;
        }
    }

    /// <summary>
    /// 2026-04-28 (iter 79) — last time the operator dismissed the failure
    /// callout. Failures with Timestamp ≤ this point are hidden. Newer
    /// failures override dismissal so operators never miss a fresh problem
    /// just because they acknowledged a prior one.
    /// </summary>
    private DateTimeOffset _failureDismissedAtUtc = DateTimeOffset.MinValue;

    /// <summary>
    /// 2026-04-28 (iter 79) — sets <see cref="_failureDismissedAtUtc"/> to
    /// "now" so the iter-51 last-failure callout hides until a new failure
    /// appears.
    /// </summary>
    public RelayCommand DismissLastFailureCommand { get; private set; } = default!;

    /// <summary>
    /// 2026-04-27 (iter 48) — single-line bridge-health summary. Computed
    /// from the same ring buffer the DataGrid reads, formatted for an
    /// at-a-glance status display: "47 calls · 96% OK · avg 1.2 ms · top:
    /// SWFOC_GetVersion ×8".
    /// </summary>
    public string ActivityStatsLine
    {
        get
        {
            var s = _bridge.ComputeStats();
            if (s.TotalCalls == 0) return "(no recent calls)";
            // 2026-04-28 (iter 82): when at least one call has failed, append
            // an explicit "(N failed)" so operators don't have to mentally
            // compute total × (1 − OK%). When everything's clean, hide the
            // suffix to keep the line uncluttered.
            var failedCount = s.TotalCalls - s.SuccessCount;
            var failedSuffix = failedCount > 0
                ? string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    " ({0} failed)", failedCount)
                : string.Empty;
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} calls · {1:P0} OK{5} · avg {2:0.0} ms · top: {3} ×{4}",
                s.TotalCalls,
                s.SuccessRate,
                s.AverageDurationMs,
                s.TopCommand ?? "—",
                s.TopCommandCount,
                failedSuffix);
        }
    }

    /// <summary>
    /// 2026-04-28 (iter 70): operator-facing bridge-health bucket
    /// rendered as a colored dot on the bottom status bar. Reads the
    /// iter-48 ring-buffer stats and surfaces the iter-70
    /// <see cref="BridgeActivityStats.HealthCategory"/>. Updates in
    /// lockstep with <see cref="ActivityStatsLine"/> via the iter-47
    /// <c>ActivityRecorded</c> event subscription.
    /// </summary>
    public string BridgeHealthCategory => _bridge.ComputeStats().HealthCategory;

    /// <summary>
    /// 2026-04-28 (iter 74): group-by-command aggregation of the
    /// activity ring. Operators see which helper dominated recent
    /// traffic without scrolling the per-call DataGrid. Sorted by
    /// CallCount descending so dominant helpers float to the top.
    /// </summary>
    public IReadOnlyList<BridgeCommandSummary> RecentCallCommandSummaries =>
        _bridge.ComputeCommandSummaries();

    /// <summary>
    /// 2026-04-28 (iter 75): pinned activity entries — bookmarks that
    /// survive ring rotation. Separate from <see cref="RecentBridgeCalls"/>
    /// so operators chasing long bugs can keep specific entries handy
    /// even as new traffic pushes them out of the rolling 50-entry buffer.
    /// </summary>
    public IReadOnlyList<BridgeActivityEntry> PinnedBridgeCalls => _bridge.PinnedCalls;

    /// <summary>
    /// 2026-04-28 (iter 83) — operator-facing header text for the
    /// Pinned-calls expander. Reads "Pinned calls (N bookmarks)" when
    /// N &gt; 0, "Pinned calls" otherwise. Lets operators see how many
    /// entries they've stashed without expanding the section. Updates
    /// in lockstep with <see cref="PinnedBridgeCalls"/> via the existing
    /// pin/unpin/clear OnPropertyChanged calls.
    /// </summary>
    public string PinnedBridgeCallsHeader
    {
        get
        {
            var count = _bridge.PinnedCalls.Count;
            return count == 0
                ? "Pinned calls"
                : string.Format(
                    System.Globalization.CultureInfo.InvariantCulture,
                    "Pinned calls ({0} bookmark{1})",
                    count,
                    count == 1 ? string.Empty : "s");
        }
    }

    /// <summary>
    /// Pin the currently-selected DataGrid row (bound from XAML to the
    /// command parameter). Operator-fired via the right-click context
    /// menu on the recent-calls DataGrid.
    /// </summary>
    public RelayCommand<BridgeActivityEntry> PinActivityCommand { get; private set; } = default!;

    /// <summary>Unpin: remove the entry from the pinned list.</summary>
    public RelayCommand<BridgeActivityEntry> UnpinActivityCommand { get; private set; } = default!;

    /// <summary>Clear all pinned entries in one click.</summary>
    public RelayCommand ClearPinnedCommand { get; private set; } = default!;

    /// <summary>
    /// Tooltip for the bottom-bar health dot. Combines the category with
    /// the underlying stats so operators hover and see "Healthy · 47
    /// calls · 96% OK".
    /// </summary>
    public string BridgeHealthTooltip
    {
        get
        {
            var s = _bridge.ComputeStats();
            if (s.TotalCalls == 0)
                return "Bridge health: Healthy (no calls yet — green by default)";
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Bridge health: {0} · {1} calls · {2:P0} OK · {3} fail" +
                "\n\nThresholds: <5% fail = Healthy (green), 5-15% = Degraded (amber), >=15% = Failing (red). Floor of 5 calls before going non-green.",
                s.HealthCategory,
                s.TotalCalls,
                s.SuccessRate,
                s.FailureCount);
        }
    }

    /// <summary>
    /// Fired by the same RefreshCommand path that updates VersionText
    /// et al — re-reads the adapter's ring buffer so the bound DataGrid
    /// shows the latest snapshot.
    /// </summary>
    public void NotifyRecentBridgeCallsChanged()
    {
        OnPropertyChanged(nameof(RecentBridgeCalls));
        // Stats line + last-failure callout share the same source data;
        // refresh in lockstep.
        OnPropertyChanged(nameof(ActivityStatsLine));
        OnPropertyChanged(nameof(LastFailureSummary));
        OnPropertyChanged(nameof(HasRecentFailure));
        // iter 70: bottom-bar health dot tracks the same stats source.
        OnPropertyChanged(nameof(BridgeHealthCategory));
        OnPropertyChanged(nameof(BridgeHealthTooltip));
        // iter 74: group-by-command aggregation tracks the same source.
        OnPropertyChanged(nameof(RecentCallCommandSummaries));
    }

    /// <summary>
    /// 2026-04-27 (iter 47) — handler for `V2BridgeAdapter.ActivityRecorded`.
    /// Marshals to the WPF dispatcher so INPC fires on the UI thread (the
    /// adapter's event fires on the worker thread that completed the
    /// round-trip).
    /// </summary>
    private void OnBridgeActivityRecorded(BridgeActivityEntry _)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            // No WPF dispatcher (test mode) or already on UI thread — fire directly.
            NotifyRecentBridgeCallsChanged();
            return;
        }
        dispatcher.BeginInvoke(NotifyRecentBridgeCallsChanged);
    }

    /// <summary>
    /// Walks parent directories from the running exe looking for a sibling
    /// <c>swfoc_memory/knowledge-base/capability_status_*.md</c> file.
    /// Returns the first match or null. Internal so tests can drive it
    /// without launching the editor.
    /// </summary>
    internal static string? ResolveCapabilityReportPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var sibling = Path.Combine(
                Path.GetDirectoryName(dir) ?? string.Empty,
                "swfoc_memory", "knowledge-base");
            if (Directory.Exists(sibling))
            {
                var matches = Directory.GetFiles(sibling, "capability_status_*.md");
                if (matches.Length > 0)
                {
                    Array.Sort(matches);
                    return matches[^1]; // newest by sort order (yyyy-MM-dd)
                }
            }
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }

    /// <summary>
    /// 2026-04-27 (iter 16): live-monitoring toggle. 5-second
    /// PeriodicTimer fires the same RefreshCommand the operator
    /// would manually click. Stops cleanly on uncheck / app dispose.
    /// </summary>
    public bool IsAutoRefreshEnabled
    {
        get => _isAutoRefreshEnabled;
        set
        {
            if (SetField(ref _isAutoRefreshEnabled, value))
            {
                if (_isAutoRefreshEnabled) _autoRefresh.Start();
                else _autoRefresh.Stop();
            }
        }
    }

    private void CopyDiagnosticsToClipboard()
    {
        var sb = new System.Text.StringBuilder(2048);
        sb.AppendLine("=== SWFOC Trainer Editor — Diagnostics Snapshot ===");
        sb.AppendLine($"Captured: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Bridge ready: {_bridgeReady}");
        sb.AppendLine($"Pipe connected: {_pipeConnected}");
        sb.AppendLine();
        sb.AppendLine($"Version: {_versionText}");
        sb.AppendLine($"Build info: {_buildInfoText}");
        sb.AppendLine($"Registered helpers ({_registeredHelperCount}): {_registeredHelpersText}");
        sb.AppendLine($"Self-test: {_selfTestText}");
        sb.AppendLine();
        sb.AppendLine("=== Bridge log tail (newest last) ===");
        foreach (var line in _logTail)
        {
            sb.AppendLine(line);
        }
        try
        {
            System.Windows.Clipboard.SetText(sb.ToString());
            AppendLog("[ok] Diagnostics copied to clipboard.");
        }
        catch (Exception ex)
        {
            // Clipboard can throw if another app holds the lock; non-fatal.
            AppendLog($"[err] Clipboard copy failed: {ex.Message}");
        }
    }

    public string VersionText
    {
        get => _versionText;
        private set => SetField(ref _versionText, value);
    }

    public string BuildInfoText
    {
        get => _buildInfoText;
        private set => SetField(ref _buildInfoText, value);
    }

    public string RegisteredHelpersText
    {
        get => _registeredHelpersText;
        private set => SetField(ref _registeredHelpersText, value);
    }

    public int RegisteredHelperCount
    {
        get => _registeredHelperCount;
        private set => SetField(ref _registeredHelperCount, value);
    }

    public string SelfTestText
    {
        get => _selfTestText;
        private set => SetField(ref _selfTestText, value);
    }

    public bool BridgeReady
    {
        get => _bridgeReady;
        private set
        {
            if (SetField(ref _bridgeReady, value))
            {
                OnPropertyChanged(nameof(StatusBrush));
            }
        }
    }

    public string StatusBanner
    {
        get => _statusBanner;
        private set => SetField(ref _statusBanner, value);
    }

    public bool PipeConnected
    {
        get => _pipeConnected;
        private set => SetField(ref _pipeConnected, value);
    }

    public string PipeName => _bridge.PipeName;

    public ObservableCollection<string> LogTail => _logTail;

    public string LogFilePath => _settings.LogPath;

    /// <summary>WPF brush key the banner binds to via a style trigger.</summary>
    public string StatusBrush => _bridgeReady ? "#2E7D32" : "#C62828";

    /// <summary>
    /// Invoked by the main view-model when the tab is first shown. Probes
    /// the bridge, rebinds the log watcher, and populates the tail.
    /// </summary>
    public Task InitializeAsync()
    {
        StartLogWatcher();
        return RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_isBusy)
        {
            return;
        }

        _isBusy = true;
        try
        {
            // 2026-04-23 fix: skip the IsBridgeAvailable() pre-flight. The bridge
            // pipe is single-instance (max_instances=1 in lua_bridge.cpp) so a
            // separate connect-then-disconnect pre-check races with any other
            // pipe client (our terminal probes, a previous slow drain, the
            // previous probe's cleanup) and returns false even when the bridge
            // is perfectly healthy. Instead, just send each probe directly and
            // let SafeProbeAsync surface the pipe error. PipeConnected is then
            // derived from whether probes actually succeeded, not from a
            // flaky pre-check.

            var version = await SafeProbeAsync("return SWFOC_GetVersion()").ConfigureAwait(true);
            VersionText = MapProbeDisplay(version);

            var buildInfo = await SafeProbeAsync("return SWFOC_GetBuildInfo()").ConfigureAwait(true);
            BuildInfoText = MapProbeDisplay(buildInfo);

            var registered = await SafeProbeAsync("return SWFOC_DiagListRegisteredFunctions()").ConfigureAwait(true);
            if (registered.IsError && registered.Display.Contains("SWFOC_DiagListRegisteredFunctions", StringComparison.OrdinalIgnoreCase))
            {
                RegisteredHelpersText = "(probe not available on this bridge build — update powrprof.dll to the current build)";
                RegisteredHelperCount = 0;
            }
            else if (registered.IsError)
            {
                RegisteredHelpersText = MapProbeDisplay(registered);
                RegisteredHelperCount = 0;
            }
            else
            {
                RegisteredHelpersText = registered.Display;
                RegisteredHelperCount = CountRegistered(registered.Display);
            }

            var selfTest = await SafeProbeAsync("return SWFOC_DiagSelfTest()").ConfigureAwait(true);
            if (selfTest.IsError && selfTest.Display.Contains("SWFOC_DiagSelfTest", StringComparison.OrdinalIgnoreCase))
            {
                SelfTestText = "(probe not available on this bridge build — update powrprof.dll to the current build)";
            }
            else
            {
                SelfTestText = MapProbeDisplay(selfTest);
            }

            // PipeConnected is true iff at least one probe got a non-error
            // response back. "Got a Lua error back" (e.g. ERR: timeout) still
            // counts as pipe-connected because the pipe round-trip completed.
            // Only true pipe failures (connect refused, write/read IO error)
            // produce !Succeeded + a pipe-level error message.
            PipeConnected = !version.IsError
                            || !buildInfo.IsError
                            || !registered.IsError
                            || !selfTest.IsError;

            StatusBanner = BuildStatusBanner(version, buildInfo, registered, selfTest);
            BridgeReady = PipeConnected
                          && !version.IsError
                          && !buildInfo.IsError
                          && !StatusBanner.StartsWith("Bridge issue", StringComparison.Ordinal);

            // 2026-04-27 (iter 45): the 4 SafeProbeAsync calls above each
            // landed an entry in V2BridgeAdapter's ring buffer. Tell the
            // bound DataGrid the snapshot moved.
            NotifyRecentBridgeCallsChanged();
        }
        finally
        {
            _isBusy = false;
        }
    }

    // Translate pipe-level errors into a friendlier UI string without
    // losing the underlying diagnostic. Lua-level errors (ERR: ...) are
    // surfaced as-is so the user sees the actual bridge response.
    private static string MapProbeDisplay(ProbeResult result)
    {
        if (!result.IsError)
        {
            return result.Display;
        }

        if (result.Display.Contains("did not accept a connection", StringComparison.OrdinalIgnoreCase)
            || result.Display.Contains("connect", StringComparison.OrdinalIgnoreCase) && result.Display.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "(pipe not connected — is SWFOC running with the bridge DLL loaded?)";
        }

        if (result.Display.Contains("timed out", StringComparison.OrdinalIgnoreCase)
            || result.Display.Contains("ERR: timeout", StringComparison.OrdinalIgnoreCase))
        {
            return "(bridge timed out — game may be paused or in a menu. Resume the game and click Refresh.)";
        }

        return result.Display;
    }

    private async Task<ProbeResult> SafeProbeAsync(string lua)
    {
        try
        {
            var round = await _bridge.SendRawAsync(lua, CancellationToken.None).ConfigureAwait(true);
            return round.Succeeded
                ? new ProbeResult(round.Response, false)
                : new ProbeResult(round.ErrorMessage, true);
        }
        catch (IOException ex)
        {
            return new ProbeResult(ex.Message, true);
        }
        catch (UnauthorizedAccessException ex)
        {
            return new ProbeResult(ex.Message, true);
        }
        catch (SecurityException ex)
        {
            return new ProbeResult(ex.Message, true);
        }
        catch (TimeoutException ex)
        {
            return new ProbeResult(ex.Message, true);
        }
        catch (InvalidOperationException ex)
        {
            return new ProbeResult(ex.Message, true);
        }
        catch (ArgumentException ex)
        {
            return new ProbeResult(ex.Message, true);
        }
    }

    // 2026-05-05 (iter 190): unified handler for iter-178 global no-arg
    // getter buttons (Game_Mode / Local_Player / Seconds_Per_Game_Minute).
    // Wraps the bridge call in `return` so DoString's return-stack capture
    // path activates, then appends a labeled line to the diagnostic log.
    private async Task ReadGlobalStateAsync(string swfocFnName, string operatorLabel)
    {
        var lua = "return " + swfocFnName + "()";
        var probe = await SafeProbeAsync(lua).ConfigureAwait(true);
        if (probe.IsError)
        {
            AppendLog($"[engine_state] {operatorLabel} -> ERROR: {probe.Display}");
        }
        else
        {
            AppendLog($"[engine_state] {operatorLabel} -> {probe.Display}");
        }
    }

    /// <summary>
    /// 2026-05-05 (iter 207): sibling to <see cref="ReadGlobalStateAsync"/>
    /// for iter-158/iter-166 1-string-arg globals (Hide/Show GUI Object).
    /// Builds <c>return [bridgeFnName]('elementName')</c> with the operator's
    /// shared <see cref="GuiObjectElementName"/> field. Single-quote wrap
    /// matches the iter-117/119 wire format; embedded single-quotes in the
    /// element name are escaped (rare, but the engine accepts them).
    /// </summary>
    private async Task InvokeGuiToggleAsync(string swfocFnName, string operatorLabel)
    {
        var elementName = _guiObjectElementName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(elementName))
        {
            AppendLog($"[gui_toggle] {operatorLabel} skipped: element name is required.");
            return;
        }
        var safe = elementName.Replace("'", "\\'", StringComparison.Ordinal);
        var lua = "return " + swfocFnName + "('" + safe + "')";
        var probe = await SafeProbeAsync(lua).ConfigureAwait(true);
        if (probe.IsError)
        {
            AppendLog($"[gui_toggle] {operatorLabel}('{elementName}') -> ERROR: {probe.Display}");
        }
        else
        {
            AppendLog($"[gui_toggle] {operatorLabel}('{elementName}') -> {probe.Display}");
        }
    }

    private string BuildStatusBanner(
        ProbeResult version,
        ProbeResult buildInfo,
        ProbeResult registered,
        ProbeResult selfTest)
    {
        // All probes failing with a pipe-connect error means the bridge
        // genuinely is unreachable (game not running, DLL not loaded, or
        // another client has the single pipe slot held indefinitely).
        bool allPipeFailures =
            IsPipeConnectError(version)
            && IsPipeConnectError(buildInfo)
            && IsPipeConnectError(registered)
            && IsPipeConnectError(selfTest);

        if (allPipeFailures)
        {
            return "Bridge issue: Pipe not connected. Confirm SWFOC is running and powrprof.dll is loaded; then click Refresh.";
        }

        // All probes failing with timeouts means the bridge is loaded but
        // the game's Lua loop isn't pumping (paused, menu, loading screen).
        // Distinct from a true pipe failure — the user just needs to resume.
        bool allTimeouts =
            IsTimeoutError(version)
            && IsTimeoutError(buildInfo)
            && IsTimeoutError(registered)
            && IsTimeoutError(selfTest);

        if (allTimeouts)
        {
            return "Bridge issue: Pipe connected but bridge timed out. Game may be paused or in a menu — resume and click Refresh.";
        }

        if (version.IsError)
        {
            return $"Bridge issue: {MapProbeDisplay(version)}";
        }

        if (buildInfo.IsError)
        {
            return $"Bridge issue: {MapProbeDisplay(buildInfo)}";
        }

        // Version-mismatch check against the known-minimum build.
        if (!version.Display.Contains("1.4", StringComparison.OrdinalIgnoreCase)
            && !version.Display.Contains("1.5", StringComparison.OrdinalIgnoreCase)
            && !version.Display.Contains("dev", StringComparison.OrdinalIgnoreCase))
        {
            return $"Bridge issue: Old DLL loaded — expected v1.4-dev+, got {version.Display}.";
        }

        if (!registered.IsError && _registeredHelperCount > 0 && _registeredHelperCount < MinimumExpectedHelperCount)
        {
            return $"Bridge issue: Registered helper count below minimum (got {_registeredHelperCount}, expected >= {MinimumExpectedHelperCount}).";
        }

        if (!selfTest.IsError && selfTest.Display.Contains("failed=", StringComparison.OrdinalIgnoreCase)
            && !selfTest.Display.Contains("failed=0", StringComparison.OrdinalIgnoreCase))
        {
            return $"Bridge issue: SelfTest reports failures — {selfTest.Display}";
        }

        return $"Bridge ready. {version.Display} — {buildInfo.Display}";
    }

    private static bool IsPipeConnectError(ProbeResult r) =>
        r.IsError
        && (r.Display.Contains("did not accept a connection", StringComparison.OrdinalIgnoreCase)
            || r.Display.Contains("pipe", StringComparison.OrdinalIgnoreCase) && r.Display.Contains("IO error", StringComparison.OrdinalIgnoreCase));

    private static bool IsTimeoutError(ProbeResult r) =>
        r.IsError
        && (r.Display.Contains("ERR: timeout", StringComparison.OrdinalIgnoreCase)
            || r.Display.Contains("response read timed out", StringComparison.OrdinalIgnoreCase));

    private static int CountRegistered(string listPayload)
    {
        if (string.IsNullOrWhiteSpace(listPayload))
        {
            return 0;
        }

        return listPayload.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
    }

    private void StartLogWatcher()
    {
        DisposeLogWatcher();

        _logTail.Clear();
        _lastLogLength = 0;

        var path = _settings.LogPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            AppendLog("(log path not configured)");
            return;
        }

        var dir = Path.GetDirectoryName(path);
        var file = Path.GetFileName(path);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file))
        {
            AppendLog("(log path invalid)");
            return;
        }

        if (!Directory.Exists(dir))
        {
            AppendLog($"(log directory missing: {dir})");
            return;
        }

        try
        {
            _logWatcher = new FileSystemWatcher(dir, file)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _logWatcher.Changed += OnLogFileChanged;
            _logWatcher.Created += OnLogFileChanged;

            // Periodic poll — FileSystemWatcher notifications alone can be flaky
            // on locked log files. 1-second cadence keeps the tail alive.
            _logPollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _logPollTimer.Tick += (_, _) => ReadLogTail(path);
            _logPollTimer.Start();

            ReadLogTail(path);
        }
        catch (IOException ex)
        {
            AppendLog($"(log watcher failed: {ex.Message})");
        }
        catch (UnauthorizedAccessException ex)
        {
            AppendLog($"(log watcher denied: {ex.Message})");
        }
    }

    private void OnLogFileChanged(object sender, FileSystemEventArgs e)
    {
        // FileSystemWatcher fires on the worker thread — marshal to UI.
        Application.Current?.Dispatcher.BeginInvoke(() => ReadLogTail(e.FullPath));
    }

    private void ReadLogTail(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length < _lastLogLength)
            {
                // File rotated.
                _lastLogLength = 0;
                _logTail.Clear();
            }

            if (fs.Length == _lastLogLength)
            {
                return;
            }

            fs.Seek(_lastLogLength, SeekOrigin.Begin);
            using var reader = new StreamReader(fs);
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (line is null)
                {
                    break;
                }

                AppendLog(line);
            }

            _lastLogLength = fs.Length;
        }
        catch (IOException)
        {
            // Skip — next poll will retry.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private void AppendLog(string line)
    {
        _logTail.Add(line);
        while (_logTail.Count > MaxLogTailLines)
        {
            _logTail.RemoveAt(0);
        }
    }

    private void DisposeLogWatcher()
    {
        if (_logWatcher is not null)
        {
            _logWatcher.EnableRaisingEvents = false;
            _logWatcher.Changed -= OnLogFileChanged;
            _logWatcher.Created -= OnLogFileChanged;
            _logWatcher.Dispose();
            _logWatcher = null;
        }

        if (_logPollTimer is not null)
        {
            _logPollTimer.Stop();
            _logPollTimer = null;
        }
    }

    public void Dispose()
    {
        // 2026-04-27 (iter 47): unsubscribe from the adapter event so the
        // GC can collect this VM (the adapter outlives the VM via DI).
        _bridge.ActivityRecorded -= OnBridgeActivityRecorded;
        _autoRefresh.Dispose();
        DisposeLogWatcher();
    }

    private readonly record struct ProbeResult(string Display, bool IsError);
}
