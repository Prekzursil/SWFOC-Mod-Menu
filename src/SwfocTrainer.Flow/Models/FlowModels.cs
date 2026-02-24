using System.Collections.ObjectModel;

namespace SwfocTrainer.Flow.Models;

public enum FlowModeHint
{
    Unknown,
    Galactic,
    TacticalLand,
    TacticalSpace
}

public sealed record FlowEventRecord(
    string EventName,
    FlowModeHint ModeHint,
    string SourceFile,
    string? ScriptReference,
    IReadOnlyDictionary<string, string> Attributes);

public sealed record FlowPlotRecord(
    string PlotId,
    string SourceFile,
    IReadOnlyList<FlowEventRecord> Events);

public sealed record FlowIndexReport(
    IReadOnlyList<FlowPlotRecord> Plots,
    IReadOnlyList<string> Diagnostics)
{
    public static readonly FlowIndexReport Empty = new(
        Array.Empty<FlowPlotRecord>(),
        Array.Empty<string>());

    public IReadOnlyList<FlowEventRecord> AllEvents =>
        new ReadOnlyCollection<FlowEventRecord>(
            Plots.SelectMany(plot => plot.Events).ToArray());
}
