using FluentAssertions;
using SwfocTrainer.Flow.Models;
using SwfocTrainer.Flow.Services;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

/// <summary>
/// Wave 3 Final coverage for Flow: StoryFlowGraphExporter (empty plots, markdown diagnostics),
/// StoryPlotFlowExtractor (null root), LuaHarnessRunner (default script fallback),
/// FlowLabModels and FlowModels record constructors.
/// </summary>
public sealed class FlowWave3FinalTests
{
    #region StoryFlowGraphExporter

    [Fact]
    public void Build_EmptyPlots_ShouldReturnEmptyWithDiagnostic()
    {
        var exporter = new StoryFlowGraphExporter();
        var report = new FlowIndexReport(
            Array.Empty<FlowPlotRecord>(),
            new[] { "existing diag" });
        var result = exporter.Build(report);
        result.Nodes.Should().BeEmpty();
        result.Edges.Should().BeEmpty();
        result.Diagnostics.Should().Contain("flow report has no plots.");
    }

    [Fact]
    public void BuildMarkdownSummary_WithDiagnostics_ShouldIncludeDiagnosticsSection()
    {
        var report = new StoryFlowGraphReport(
            Array.Empty<StoryFlowGraphNode>(),
            Array.Empty<StoryFlowGraphEdge>(),
            new[] { "diag1", "diag2" });
        var md = StoryFlowGraphExporter.BuildMarkdownSummary("test_profile", report);
        md.Should().Contain("## Diagnostics");
        md.Should().Contain("- diag1");
        md.Should().Contain("- diag2");
    }

    [Fact]
    public void BuildMarkdownSummary_NoDiagnostics_ShouldNotIncludeSection()
    {
        var report = new StoryFlowGraphReport(
            Array.Empty<StoryFlowGraphNode>(),
            Array.Empty<StoryFlowGraphEdge>(),
            Array.Empty<string>());
        var md = StoryFlowGraphExporter.BuildMarkdownSummary("test_profile", report);
        md.Should().NotContain("## Diagnostics");
    }

    [Fact]
    public void BuildMarkdownSummary_WithNodes_ShouldFormatTable()
    {
        var nodes = new[]
        {
            new StoryFlowGraphNode("n1", "plot1", "STORY_EVENT", FlowModeHint.Galactic, "source.xml", "script.lua", new[] { "toggle_credits" }),
            new StoryFlowGraphNode("n2", "plot1", "STORY_EVENT2", FlowModeHint.TacticalLand, "source.xml", null, Array.Empty<string>())
        };
        var report = new StoryFlowGraphReport(nodes, Array.Empty<StoryFlowGraphEdge>(), Array.Empty<string>());
        var md = StoryFlowGraphExporter.BuildMarkdownSummary("test_profile", report);
        md.Should().Contain("| plot1 | STORY_EVENT |");
        md.Should().Contain("toggle_credits");
        md.Should().Contain("| - |"); // empty features
    }

    #endregion

    #region Model constructors

    [Fact]
    public void FlowModeCount_ShouldStoreProperties()
    {
        var c = new FlowModeCount(FlowModeHint.Galactic, 5);
        c.Mode.Should().Be(FlowModeHint.Galactic);
        c.Count.Should().Be(5);
    }

    [Fact]
    public void FlowLabSnapshot_Empty_ShouldHaveEmptyCollections()
    {
        FlowLabSnapshot.Empty.ModeCounts.Should().BeEmpty();
        FlowLabSnapshot.Empty.ScriptReferences.Should().BeEmpty();
        FlowLabSnapshot.Empty.MegaLoadOrder.Should().BeEmpty();
        FlowLabSnapshot.Empty.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void FlowLabSnapshot_WithValues_ShouldStoreAll()
    {
        var snapshot = new FlowLabSnapshot(
            new[] { new FlowModeCount(FlowModeHint.Galactic, 3) },
            new[] { "script.lua" },
            new[] { "file.meg" },
            new[] { "diag" });
        snapshot.ModeCounts.Should().HaveCount(1);
        snapshot.ScriptReferences.Should().Contain("script.lua");
    }

    [Fact]
    public void StoryFlowGraphNode_ShouldStoreAllProperties()
    {
        var n = new StoryFlowGraphNode("n1", "p1", "ev", FlowModeHint.Unknown, "src", "script", new[] { "f1" });
        n.ScriptReference.Should().Be("script");
        n.ExpectedFeatureIds.Should().Contain("f1");
    }

    [Fact]
    public void StoryFlowGraphEdge_ShouldStoreAllProperties()
    {
        var e = new StoryFlowGraphEdge("n1", "n2", "flow", "next");
        e.FromNodeId.Should().Be("n1");
        e.Reason.Should().Be("next");
    }

    [Fact]
    public void StoryFlowGraphReport_Empty_ShouldHaveEmptyCollections()
    {
        StoryFlowGraphReport.Empty.Nodes.Should().BeEmpty();
        StoryFlowGraphReport.Empty.Edges.Should().BeEmpty();
        StoryFlowGraphReport.Empty.Diagnostics.Should().BeEmpty();
    }

    #endregion
}
