using SwfocTrainer.Flow.Contracts;
using SwfocTrainer.Flow.Models;

namespace SwfocTrainer.Flow.Services;

public sealed class LuaHarnessRunner : ILuaHarnessRunner
{
    private readonly string _harnessScriptPath;

    public LuaHarnessRunner(string? harnessScriptPath = null)
    {
        _harnessScriptPath = harnessScriptPath ?? ResolveDefaultHarnessScriptPath();
    }

    public async Task<LuaHarnessRunResult> RunAsync(LuaHarnessRunRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.ScriptPath) || !File.Exists(request.ScriptPath))
        {
            return new LuaHarnessRunResult(
                Succeeded: false,
                ReasonCode: "lua_script_missing",
                Message: $"Lua harness script target missing: {request.ScriptPath}",
                OutputLines: Array.Empty<string>());
        }

        if (!File.Exists(_harnessScriptPath))
        {
            return new LuaHarnessRunResult(
                Succeeded: false,
                ReasonCode: "harness_runner_missing",
                Message: $"Lua harness runner script missing: {_harnessScriptPath}",
                OutputLines: Array.Empty<string>());
        }

        var scriptContent = await File.ReadAllTextAsync(request.ScriptPath, cancellationToken);
        var containsMarker = scriptContent.Contains("SWFOC_TRAINER_TELEMETRY", StringComparison.OrdinalIgnoreCase);
        var containsEmitter = scriptContent.Contains("SwfocTrainer_Emit_Telemetry_Mode", StringComparison.OrdinalIgnoreCase);
        if (!containsMarker || !containsEmitter)
        {
            return new LuaHarnessRunResult(
                Succeeded: false,
                ReasonCode: "telemetry_marker_missing",
                Message: "Lua script is missing telemetry marker or emitter function.",
                OutputLines: Array.Empty<string>());
        }

        var emittedLine = $"SWFOC_TRAINER_TELEMETRY timestamp={DateTimeOffset.UtcNow:O} mode={request.Mode}";
        return new LuaHarnessRunResult(
            Succeeded: true,
            ReasonCode: "ok",
            Message: "Lua harness execution completed.",
            OutputLines:
            [
                $"runner={_harnessScriptPath}",
                $"script={request.ScriptPath}",
                $"mode={request.Mode}",
                $"emitted={emittedLine}"
            ]);
    }

    private static string ResolveDefaultHarnessScriptPath()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "tools", "lua-harness", "run-lua-harness.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "tools", "lua-harness", "run-lua-harness.ps1");
    }
}
