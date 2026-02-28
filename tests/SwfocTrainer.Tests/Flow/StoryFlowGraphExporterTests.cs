using FluentAssertions;
using SwfocTrainer.Flow.Models;
using SwfocTrainer.Flow.Services;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

public sealed class StoryFlowGraphExporterTests
{
    [Fact]
    public void Build_ShouldEmitDeterministicNodesAndEdges_WithModeLinkedFeatures()
    {
        var report = new FlowIndexReport(
            new[]
            {
                new FlowPlotRecord(
                    "Campaign",
                    "Data/XML/Story/Campaign.xml",
                    new[]
                    {
                        new FlowEventRecord(
                            "STORY_SPACE_TACTICAL",
                            FlowModeHint.TacticalSpace,
                            "Data/XML/Story/Campaign.xml",
                            "Story/Space.lua",
                            new Dictionary<string, string>()),
                        new FlowEventRecord(
                            "STORY_GALACTIC_TURN",
                            FlowModeHint.Galactic,
                            "Data/XML/Story/Campaign.xml",
                            "Story/Galactic.lua",
                            new Dictionary<string, string>())
                    })
            },
            Array.Empty<string>());
        var exporter = new StoryFlowGraphExporter();

        var graph = exporter.Build(report);

        graph.Diagnostics.Should().BeEmpty();
        graph.Nodes.Should().Contain(x => x.NodeId == "plot:Campaign");
        graph.Nodes.Should().Contain(x =>
            x.EventName == "STORY_SPACE_TACTICAL" &&
            x.ExpectedFeatureIds.Contains("toggle_instant_build_patch"));
        graph.Nodes.Should().Contain(x =>
            x.EventName == "STORY_GALACTIC_TURN" &&
            x.ExpectedFeatureIds.Contains("set_credits"));
        graph.Edges.Should().Contain(x =>
            x.EdgeType == "plot_contains_event" &&
            x.ToNodeId.Contains("STORY_SPACE_TACTICAL", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildMarkdownSummary_ShouldContainExpectedFeatureHints()
    {
        var graph = new StoryFlowGraphReport(
            Nodes: new[]
            {
                new StoryFlowGraphNode(
                    "plot:Campaign",
                    "Campaign",
                    "__plot__",
                    FlowModeHint.Unknown,
                    "Data/XML/Story/Campaign.xml",
                    null,
                    Array.Empty<string>()),
                new StoryFlowGraphNode(
                    "Campaign:STORY_LAND_TACTICAL:000",
                    "Campaign",
                    "STORY_LAND_TACTICAL",
                    FlowModeHint.TacticalLand,
                    "Data/XML/Story/Campaign.xml",
                    "Story/Land.lua",
                    new[] { "freeze_timer", "set_unit_cap" })
            },
            Edges: Array.Empty<StoryFlowGraphEdge>(),
            Diagnostics: Array.Empty<string>());
        var markdown = StoryFlowGraphExporter.BuildMarkdownSummary("roe_3447786229_swfoc", graph);

        markdown.Should().Contain("STORY_LAND_TACTICAL");
        markdown.Should().Contain("freeze_timer");
        markdown.Should().Contain("set_unit_cap");
    }
}
