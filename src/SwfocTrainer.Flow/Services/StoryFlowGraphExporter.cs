using System.Text;
using SwfocTrainer.Flow.Models;

namespace SwfocTrainer.Flow.Services;

public sealed class StoryFlowGraphExporter
{
    private static readonly string[] TacticalFeatureIds =
    [
        "freeze_timer",
        "toggle_fog_reveal",
        "toggle_ai",
        "set_unit_cap",
        "toggle_instant_build_patch"
    ];

    private static readonly string[] GalacticFeatureIds =
    [
        "set_credits",
        "toggle_ai"
    ];

    public StoryFlowGraphReport Build(FlowIndexReport flowReport)
    {
        if (flowReport.Plots.Count == 0)
        {
            return StoryFlowGraphReport.Empty with
            {
                Diagnostics = new[] { "flow report has no plots." }
            };
        }

        var nodes = new List<StoryFlowGraphNode>();
        var edges = new List<StoryFlowGraphEdge>();
        var diagnostics = new List<string>(flowReport.Diagnostics);

        foreach (var plot in flowReport.Plots
                     .OrderBy(x => x.SourceFile, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(x => x.PlotId, StringComparer.OrdinalIgnoreCase))
        {
            var rootId = $"plot:{plot.PlotId}";
            nodes.Add(new StoryFlowGraphNode(
                NodeId: rootId,
                PlotId: plot.PlotId,
                EventName: "__plot__",
                ModeHint: FlowModeHint.Unknown,
                SourceFile: plot.SourceFile,
                ScriptReference: null,
                ExpectedFeatureIds: Array.Empty<string>()));

            var orderedEvents = plot.Events
                .OrderBy(x => x.EventName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.ScriptReference, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            StoryFlowGraphNode? previous = null;
            for (var i = 0; i < orderedEvents.Length; i++)
            {
                var flowEvent = orderedEvents[i];
                var node = new StoryFlowGraphNode(
                    NodeId: $"{plot.PlotId}:{flowEvent.EventName}:{i:000}",
                    PlotId: plot.PlotId,
                    EventName: flowEvent.EventName,
                    ModeHint: flowEvent.ModeHint,
                    SourceFile: flowEvent.SourceFile,
                    ScriptReference: flowEvent.ScriptReference,
                    ExpectedFeatureIds: ResolveExpectedFeatures(flowEvent.ModeHint));
                nodes.Add(node);
                edges.Add(new StoryFlowGraphEdge(
                    FromNodeId: rootId,
                    ToNodeId: node.NodeId,
                    EdgeType: "plot_contains_event",
                    Reason: flowEvent.EventName));

                if (previous is not null)
                {
                    edges.Add(new StoryFlowGraphEdge(
                        FromNodeId: previous.NodeId,
                        ToNodeId: node.NodeId,
                        EdgeType: "inferred_sequence",
                        Reason: "ordered-by-event-name"));
                }

                previous = node;
            }
        }

        return new StoryFlowGraphReport(nodes, edges, diagnostics);
    }

    public static string BuildMarkdownSummary(string profileId, StoryFlowGraphReport graph)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"# Story Flow Graph ({profileId})");
        builder.AppendLine();
        builder.AppendLine($"- nodes: {graph.Nodes.Count}");
        builder.AppendLine($"- edges: {graph.Edges.Count}");
        builder.AppendLine();
        builder.AppendLine("| Plot | Event | Mode | Script | Expected Features |");
        builder.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var node in graph.Nodes.Where(x => x.EventName != "__plot__"))
        {
            var expected = node.ExpectedFeatureIds.Count == 0
                ? "-"
                : string.Join(", ", node.ExpectedFeatureIds);
            var script = string.IsNullOrWhiteSpace(node.ScriptReference) ? "-" : node.ScriptReference;
            builder.AppendLine($"| {node.PlotId} | {node.EventName} | {node.ModeHint} | {script} | {expected} |");
        }

        if (graph.Diagnostics.Count > 0)
        {
            builder.AppendLine();
            builder.AppendLine("## Diagnostics");
            foreach (var diagnostic in graph.Diagnostics)
            {
                builder.AppendLine($"- {diagnostic}");
            }
        }

        return builder.ToString();
    }

    private static IReadOnlyList<string> ResolveExpectedFeatures(FlowModeHint modeHint)
    {
        return modeHint switch
        {
            FlowModeHint.TacticalLand => TacticalFeatureIds,
            FlowModeHint.TacticalSpace => TacticalFeatureIds,
            FlowModeHint.Galactic => GalacticFeatureIds,
            _ => Array.Empty<string>()
        };
    }
}
