namespace SwfocTrainer.Flow.Models;

public sealed record FlowModeCount(
    FlowModeHint Mode,
    int Count);

public sealed record FlowLabSnapshot(
    IReadOnlyList<FlowModeCount> ModeCounts,
    IReadOnlyList<string> ScriptReferences,
    IReadOnlyList<string> MegaLoadOrder,
    IReadOnlyList<string> Diagnostics)
{
    public static readonly FlowLabSnapshot Empty = new(
        Array.Empty<FlowModeCount>(),
        Array.Empty<string>(),
        Array.Empty<string>(),
        Array.Empty<string>());
}
