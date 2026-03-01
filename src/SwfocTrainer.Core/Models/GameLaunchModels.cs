namespace SwfocTrainer.Core.Models;

public enum GameLaunchTarget
{
    Sweaw = 0,
    Swfoc
}

public enum GameLaunchMode
{
    Vanilla = 0,
    SteamMod,
    ModPath
}

public sealed record GameLaunchRequest(
    GameLaunchTarget Target,
    GameLaunchMode Mode,
    string? WorkshopId = null,
    string? ModPath = null,
    string? ProfileIdHint = null,
    bool TerminateExistingTargets = false);

public sealed record GameLaunchResult(
    bool Succeeded,
    string Message,
    int ProcessId,
    string ExecutablePath,
    string Arguments,
    IReadOnlyDictionary<string, object?>? Diagnostics = null);
