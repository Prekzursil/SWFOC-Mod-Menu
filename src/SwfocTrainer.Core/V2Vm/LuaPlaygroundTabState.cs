using SwfocTrainer.Core.Ux;

namespace SwfocTrainer.Core.V2Vm;

/// <summary>
/// V2 Tab — Lua Playground. Task #110 — free-form
/// <c>SWFOC_DoString</c> editor + a curated recipe library so the
/// modder can paste / save / run Lua snippets directly against the
/// bridge's embedded VM without going through the typed action surface.
///
/// Recipes are stored in-memory; the App layer is responsible for
/// persistence (SQLite or JSON file). The state model here is purely
/// for the "type, save, recall, run" loop.
/// </summary>
public sealed class LuaPlaygroundTabState
{
    private readonly ILuaPlaygroundDispatcher _dispatcher;
    private readonly IUxFeedbackSink _feedback;
    private readonly Dictionary<string, string> _recipes =
        new(StringComparer.OrdinalIgnoreCase);

    public LuaPlaygroundTabState(ILuaPlaygroundDispatcher dispatcher, IUxFeedbackSink feedback)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(feedback);
        _dispatcher = dispatcher;
        _feedback = feedback;
    }

    public string ScriptText { get; set; } = string.Empty;
    public string LastResponse { get; private set; } = string.Empty;
    public IReadOnlyDictionary<string, string> Recipes => _recipes;

    public UxFeedback SaveRecipe(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Emit(UxFeedback.Error("save_recipe", "name required", "save_recipe"));
        }
        if (string.IsNullOrEmpty(ScriptText))
        {
            return Emit(UxFeedback.Error("save_recipe", "script empty", "save_recipe"));
        }
        _recipes[name] = ScriptText;
        return Emit(UxFeedback.Success("save_recipe",
            $"saved '{name}' ({ScriptText.Length} chars)", "save_recipe"));
    }

    public UxFeedback LoadRecipe(string name)
    {
        if (!_recipes.TryGetValue(name, out var script))
        {
            return Emit(UxFeedback.Error("load_recipe",
                $"recipe '{name}' not found", "load_recipe"));
        }
        ScriptText = script;
        return Emit(UxFeedback.Info("load_recipe",
            $"loaded '{name}' ({script.Length} chars)", "load_recipe"));
    }

    public UxFeedback DeleteRecipe(string name)
    {
        if (!_recipes.Remove(name))
        {
            return Emit(UxFeedback.Warning("delete_recipe",
                $"recipe '{name}' was not present", "delete_recipe"));
        }
        return Emit(UxFeedback.Success("delete_recipe", $"removed '{name}'", "delete_recipe"));
    }

    public async Task<UxFeedback> RunAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(ScriptText))
        {
            return Emit(UxFeedback.Error("run_lua", "script empty", "run_lua"));
        }
        var response = await _dispatcher.ExecuteLuaAsync(ScriptText, ct);
        LastResponse = response ?? string.Empty;
        var failed = response is null
            || response.StartsWith("ERR", StringComparison.OrdinalIgnoreCase);
        return Emit(failed
            ? UxFeedback.Error("run_lua",
                $"script failed: {response ?? "<null response>"}", "run_lua")
            : UxFeedback.Warning("run_lua",
                // Warning rather than Success because the playground bypasses
                // every typed validation surface — the operator should treat
                // even "OK" results as "did what I just type actually do?".
                $"OK | response: {response}", "run_lua"));
    }

    private UxFeedback Emit(UxFeedback fb) { _feedback.Emit(fb); return fb; }
}

public interface ILuaPlaygroundDispatcher
{
    Task<string?> ExecuteLuaAsync(string script, CancellationToken ct);
}
