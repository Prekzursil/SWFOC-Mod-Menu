using System.Collections.ObjectModel;
using System.Globalization;
using SwfocTrainer.App.V2.Infrastructure;

namespace SwfocTrainer.App.V2.ViewModels;

// ============================================================================
// Tab 5 — Probes & Scripts
//
// Free-form Lua input box, canned-recipe dropdown, send button, append-only
// output log, save-recipe button. Everything routes through V2BridgeAdapter
// so the user sees the exact bridge bytes.
// ============================================================================

public sealed class ProbesTabViewModel : ObservableBase
{
    private const int MaxOutputLines = 40;

    // 2026-04-27 (iter 21): cap the in-memory recent-commands ring. 20 is
    // generous for an ad-hoc session and tiny enough to render in a
    // dropdown without the operator having to scroll.
    internal const int MaxHistoryEntries = 20;

    private readonly V2BridgeAdapter _bridge;
    private readonly ObservableCollection<V2Recipe> _recipes = new();
    private readonly ObservableCollection<string> _output = new();
    private readonly ObservableCollection<string> _history = new();
    private V2Recipe? _selectedRecipe;
    private string _luaInput = string.Empty;
    private string _saveRecipeNameInput = string.Empty;
    private string? _selectedHistory;

    public ProbesTabViewModel(V2BridgeAdapter bridge)
    {
        ArgumentNullException.ThrowIfNull(bridge);
        _bridge = bridge;

        foreach (var r in V2RecipeStore.LoadAll())
        {
            _recipes.Add(r);
        }

        SendCommand = new AsyncRelayCommand(
            SendAsync,
            canExecute: () => !string.IsNullOrWhiteSpace(_luaInput),
            onError: ex => Append($"[error] {ex.Message}"));

        LoadRecipeCommand = new RelayCommand(
            LoadRecipe,
            canExecute: () => _selectedRecipe is not null);

        SaveRecipeCommand = new RelayCommand(
            SaveRecipe,
            canExecute: () =>
                !string.IsNullOrWhiteSpace(_saveRecipeNameInput)
                && !string.IsNullOrWhiteSpace(_luaInput));

        // 2026-04-27: dump the entire output log to the clipboard. Useful
        // for "I sent these 5 probes and the bridge replied X for each" bug
        // reports — operator gets a single click instead of selecting +
        // copying lines manually.
        CopyOutputCommand = new RelayCommand(
            CopyOutputToClipboard,
            canExecute: () => _output.Count > 0);

        // 2026-04-27: clear the running log without losing the last query.
        ClearOutputCommand = new RelayCommand(
            () => _output.Clear(),
            canExecute: () => _output.Count > 0);

        // 2026-04-27 (iter 21): operator workflow is "send a probe, observe
        // the response, send a tweaked variant of the same probe". Without
        // history they have to re-paste from the output log (which gets
        // truncated to 40 lines) or re-type. RecallSelected pulls the
        // chosen entry back into LuaInput so they can edit + resend.
        RecallSelectedHistoryCommand = new RelayCommand(
            RecallSelectedHistory,
            canExecute: () => !string.IsNullOrWhiteSpace(_selectedHistory));
        ClearHistoryCommand = new RelayCommand(
            () => _history.Clear(),
            canExecute: () => _history.Count > 0);

        // 2026-04-27 (iter 59): per-button capability metadata. Send
        // routes raw Lua through SWFOC_DoString — escape hatch is LIVE.
        Send = new SwfocTrainer.Core.Diagnostics.CapabilityAwareAction(
            "Send Lua probe", "SWFOC_DoString");
    }

    public SwfocTrainer.Core.Diagnostics.CapabilityAwareAction Send { get; }
    public IReadOnlyList<SwfocTrainer.Core.Diagnostics.CapabilityAwareAction> AllActions =>
        new[] { Send };

    public RelayCommand CopyOutputCommand { get; }
    public RelayCommand ClearOutputCommand { get; }
    public RelayCommand RecallSelectedHistoryCommand { get; }
    public RelayCommand ClearHistoryCommand { get; }

    /// <summary>
    /// Most-recent-first ring of Lua snippets the operator has sent this
    /// session. Bounded at <see cref="MaxHistoryEntries"/>. Ephemeral —
    /// not persisted across editor restarts (use Save as recipe for that).
    /// </summary>
    public ObservableCollection<string> History => _history;

    public string? SelectedHistory
    {
        get => _selectedHistory;
        set => SetField(ref _selectedHistory, value);
    }

    private void RecallSelectedHistory()
    {
        if (string.IsNullOrWhiteSpace(_selectedHistory)) return;
        LuaInput = _selectedHistory;
    }

    /// <summary>
    /// Push a sent command onto the recent-history ring. Most-recent-first.
    /// De-duplicates by shifting an existing identical entry to the front
    /// instead of appending a duplicate (mirrors how shells handle history).
    /// Trims to <see cref="MaxHistoryEntries"/>.
    /// </summary>
    /// <remarks>
    /// Internal so test code can drive it without a live bridge round-trip.
    /// </remarks>
    internal void PushHistory(string lua)
    {
        if (string.IsNullOrWhiteSpace(lua)) return;
        var trimmed = lua.Trim();
        // Shell-style: if the user just resent the same line, don't double it.
        for (int i = 0; i < _history.Count; i++)
        {
            if (string.Equals(_history[i], trimmed, StringComparison.Ordinal))
            {
                if (i == 0) return; // already at the head, no work
                _history.Move(i, 0);
                return;
            }
        }
        _history.Insert(0, trimmed);
        while (_history.Count > MaxHistoryEntries)
        {
            _history.RemoveAt(_history.Count - 1);
        }
    }

    private void CopyOutputToClipboard()
    {
        if (_output.Count == 0) return;
        var blob = string.Join(Environment.NewLine, _output);
        try
        {
            System.Windows.Clipboard.SetText(blob);
            Append("[ok] Output copied to clipboard.");
        }
        catch (Exception ex)
        {
            // Clipboard occasionally locks behind another app; non-fatal.
            Append($"[err] Clipboard copy failed: {ex.Message}");
        }
    }

    public ObservableCollection<V2Recipe> Recipes => _recipes;

    public V2Recipe? SelectedRecipe
    {
        get => _selectedRecipe;
        set => SetField(ref _selectedRecipe, value);
    }

    public string LuaInput
    {
        get => _luaInput;
        set => SetField(ref _luaInput, value);
    }

    public string SaveRecipeNameInput
    {
        get => _saveRecipeNameInput;
        set => SetField(ref _saveRecipeNameInput, value);
    }

    public ObservableCollection<string> Output => _output;

    public AsyncRelayCommand SendCommand { get; }

    public RelayCommand LoadRecipeCommand { get; }

    public RelayCommand SaveRecipeCommand { get; }

    private async Task SendAsync()
    {
        var lua = _luaInput.Trim();
        if (string.IsNullOrEmpty(lua))
        {
            Append("[error] Empty Lua input.");
            return;
        }

        // 2026-04-27 (iter 21): push BEFORE the await so the history is
        // populated even if the bridge round-trip throws. The operator
        // typed it, they should be able to recall it.
        PushHistory(lua);

        Append($"> {lua}");
        var round = await _bridge.SendRawAsync(lua, CancellationToken.None).ConfigureAwait(true);
        Append(round.Succeeded ? $"[ok] {round.Response}" : $"[err] {round.ErrorMessage}");
    }

    private void LoadRecipe()
    {
        if (_selectedRecipe is null)
        {
            return;
        }

        LuaInput = _selectedRecipe.Lua;
    }

    private void SaveRecipe()
    {
        var name = _saveRecipeNameInput.Trim();
        var lua = _luaInput.Trim();
        if (name.Length == 0 || lua.Length == 0)
        {
            return;
        }

        var recipe = new V2Recipe(name, lua);
        if (V2RecipeStore.TryAppend(recipe, out var error))
        {
            _recipes.Add(recipe);
            Append($"[ok] Recipe '{name}' saved.");
            SaveRecipeNameInput = string.Empty;
        }
        else
        {
            Append($"[err] Recipe save failed: {error}");
        }
    }

    private void Append(string line)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        _output.Add($"{timestamp} {line}");
        while (_output.Count > MaxOutputLines)
        {
            _output.RemoveAt(0);
        }
    }
}
