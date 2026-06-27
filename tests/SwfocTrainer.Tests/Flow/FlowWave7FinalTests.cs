using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using SwfocTrainer.Flow.Models;
using SwfocTrainer.Flow.Services;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

/// <summary>
/// Wave 7 final coverage — fills remaining gaps:
/// FlowModels record (StoryFlowGraphEdge), LuaHarnessRunner fallback default path,
/// StoryFlowGraphExporter switch arms (TacticalLand, TacticalSpace, default),
/// StoryPlotFlowExtractor null root early return via reflection.
/// </summary>
public sealed class FlowWave7FinalTests
{
    #region FlowModels record coverage (line 62 — StoryFlowGraphEdge constructor)

    [Fact]
    public void StoryFlowGraphEdge_Constructor_ShouldStoreProperties()
    {
        var edge = new StoryFlowGraphEdge("from1", "to2", "sequential", "plot1");
        edge.FromNodeId.Should().Be("from1");
        edge.ToNodeId.Should().Be("to2");
        edge.EdgeType.Should().Be("sequential");
        edge.Reason.Should().Be("plot1");
    }

    #endregion

    #region LuaHarnessRunner — line 89 fallback default path

    [Fact]
    public void ResolveDefaultHarnessScriptPath_ShouldReturnPathEndingWithPs1()
    {
        var method = typeof(LuaHarnessRunner).GetMethod(
            "ResolveDefaultHarnessScriptPath",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull("ResolveDefaultHarnessScriptPath must exist");

        var result = method!.Invoke(null, Array.Empty<object>()) as string ?? throw new InvalidOperationException("test setup: expected non-null result.");
        result.Should().NotBeNullOrWhiteSpace();
        result!.Should().EndWith("run-lua-harness.ps1");
    }

    #endregion

    #region StoryFlowGraphExporter — switch arms (lines 143, 146)

    [Fact]
    public void Build_WithTacticalLandModeHint_ShouldProduceTacticalFeatureIds()
    {
        var exporter = new StoryFlowGraphExporter();
        var evt = new FlowEventRecord("STORY_LAND_EVENT", FlowModeHint.TacticalLand, "source.xml", null, new Dictionary<string, string>());
        var plot = new FlowPlotRecord("tacticalPlot", "source.xml", new[] { evt });
        var report = new FlowIndexReport(new[] { plot }, Array.Empty<string>());
        var result = exporter.Build(report);
        // The exporter creates nodes; find the one for our event
        var landNode = result.Nodes.FirstOrDefault(n => n.EventName == "STORY_LAND_EVENT");
        landNode.Should().NotBeNull();
        landNode!.ExpectedFeatureIds.Should().NotBeEmpty();
    }

    [Fact]
    public void Build_WithTacticalSpaceModeHint_ShouldProduceTacticalFeatureIds()
    {
        var exporter = new StoryFlowGraphExporter();
        var evt = new FlowEventRecord("STORY_SPACE_EVENT", FlowModeHint.TacticalSpace, "source.xml", null, new Dictionary<string, string>());
        var plot = new FlowPlotRecord("spacePlot", "source.xml", new[] { evt });
        var report = new FlowIndexReport(new[] { plot }, Array.Empty<string>());
        var result = exporter.Build(report);
        var spaceNode = result.Nodes.FirstOrDefault(n => n.EventName == "STORY_SPACE_EVENT");
        spaceNode.Should().NotBeNull();
        spaceNode!.ExpectedFeatureIds.Should().NotBeEmpty();
    }

    [Fact]
    public void Build_WithUnknownModeHint_ShouldProduceEmptyFeatureIds()
    {
        var exporter = new StoryFlowGraphExporter();
        var evt = new FlowEventRecord("STORY_UNK_EVENT", (FlowModeHint)999, "source.xml", null, new Dictionary<string, string>());
        var plot = new FlowPlotRecord("unknownPlot", "source.xml", new[] { evt });
        var report = new FlowIndexReport(new[] { plot }, Array.Empty<string>());
        var result = exporter.Build(report);
        var unkNode = result.Nodes.FirstOrDefault(n => n.EventName == "STORY_UNK_EVENT");
        unkNode.Should().NotBeNull();
        unkNode!.ExpectedFeatureIds.Should().BeEmpty();
    }

    #endregion

    #region StoryPlotFlowExtractor — null root early return (lines 136-137) via reflection

    [Fact]
    public void ExtractEvents_NullRoot_ShouldReturnEmpty()
    {
        var extractor = new StoryPlotFlowExtractor();
        var method = typeof(StoryPlotFlowExtractor).GetMethod(
            "ExtractEvents",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull("ExtractEvents must exist");

        var result = method!.Invoke(extractor, new object?[] { null, "test.xml" });
        result.Should().NotBeNull();
        var events = result as IReadOnlyList<FlowEventRecord> ?? throw new InvalidOperationException("test setup: expected non-null result.");
        events.Should().NotBeNull();
        events!.Should().BeEmpty();
    }

    #endregion
}
