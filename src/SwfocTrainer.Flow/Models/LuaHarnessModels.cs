namespace SwfocTrainer.Flow.Models;

public sealed record LuaHarnessRunRequest(
    string ScriptPath,
    string Mode = "TacticalLand");

public sealed record LuaHarnessRunResult(
    bool Succeeded,
    string ReasonCode,
    string Message,
    IReadOnlyList<string> OutputLines,
    string? ArtifactPath = null);
