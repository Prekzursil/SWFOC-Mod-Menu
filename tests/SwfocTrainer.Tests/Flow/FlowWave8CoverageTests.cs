using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.Flow.Models;
using SwfocTrainer.Flow.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

/// <summary>
/// Wave 8 coverage: remaining branches in StoryPlotFlowExtractor, StoryFlowGraphExporter,
/// FlowLabSnapshotBuilder, LuaHarnessRunner — null guards, whitespace/invalid XML,
/// mode hint detection, capability link report, graph export, markdown summary,
/// Lua harness missing script/harness/telemetry marker, successful execution.
/// </summary>
public sealed class FlowWave8CoverageTests
{
    #region StoryPlotFlowExtractor — null guards

    [Fact]
    public void Extract_ShouldThrow_WhenXmlContentIsNull()
    {
        var extractor = new StoryPlotFlowExtractor();
        var act = () => extractor.Extract(null!, "test.xml");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Extract_ShouldThrow_WhenSourceFileIsNull()
    {
        var extractor = new StoryPlotFlowExtractor();
        var act = () => extractor.Extract("<root/>", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region StoryPlotFlowExtractor — whitespace/invalid XML

    [Fact]
    public void Extract_ShouldReturnEmpty_WhenXmlContentIsWhitespace()
    {
        var extractor = new StoryPlotFlowExtractor();
        var result = extractor.Extract("   ", "test.xml");
        result.Plots.Should().BeEmpty();
    }

    [Fact]
    public void Extract_ShouldReturnDiagnostic_WhenXmlIsInvalid()
    {
        var extractor = new StoryPlotFlowExtractor();
        var result = extractor.Extract("<<<invalid>>>", "bad.xml");
        result.Diagnostics.Should().Contain(d => d.Contains("Invalid XML"));
    }

    #endregion

    #region StoryPlotFlowExtractor — mode hint detection

    [Fact]
    public void Extract_ShouldDetectTacticalSpaceMode()
    {
        var xml = """
            <Campaign>
                <Plot Name="test_plot">
                    <Event Name="STORY_SPACE_TACTICAL_START" />
                </Plot>
            </Campaign>
            """;
        var extractor = new StoryPlotFlowExtractor();
        var result = extractor.Extract(xml, "space.xml");

        result.Plots.Should().ContainSingle();
        result.Plots[0].Events.Should().ContainSingle();
        result.Plots[0].Events[0].ModeHint.Should().Be(FlowModeHint.TacticalSpace);
    }

    [Fact]
    public void Extract_ShouldDetectTacticalLandMode()
    {
        var xml = """
            <Campaign>
                <Plot Name="test_plot">
                    <Event Name="STORY_LAND_TACTICAL_BATTLE" />
                </Plot>
            </Campaign>
            """;
        var extractor = new StoryPlotFlowExtractor();
        var result = extractor.Extract(xml, "land.xml");

        result.Plots[0].Events[0].ModeHint.Should().Be(FlowModeHint.TacticalLand);
    }

    [Fact]
    public void Extract_ShouldDetectGalacticMode()
    {
        var xml = """
            <Campaign>
                <Plot Name="test_plot">
                    <Event Name="STORY_GALACTIC_CONQUEST" />
                </Plot>
            </Campaign>
            """;
        var extractor = new StoryPlotFlowExtractor();
        var result = extractor.Extract(xml, "galactic.xml");

        result.Plots[0].Events[0].ModeHint.Should().Be(FlowModeHint.Galactic);
    }

    [Fact]
    public void Extract_ShouldReturnUnknownMode_WhenNoModeKeyword()
    {
        var xml = """
            <Campaign>
                <Plot Name="test_plot">
                    <Event Name="STORY_GENERIC_EVENT" />
                </Plot>
            </Campaign>
            """;
        var extractor = new StoryPlotFlowExtractor();
        var result = extractor.Extract(xml, "generic.xml");

        result.Plots[0].Events[0].ModeHint.Should().Be(FlowModeHint.Unknown);
    }

    [Fact]
    public void Extract_ShouldCreateSyntheticPlot_WhenNoPlotElements()
    {
        var xml = """
            <Campaign>
                <Event Name="STORY_GALACTIC_TEST" />
            </Campaign>
            """;
        var extractor = new StoryPlotFlowExtractor();
        var result = extractor.Extract(xml, "no_plot.xml");

        result.Plots.Should().ContainSingle();
        result.Plots[0].PlotId.Should().Be("no_plot");
    }

    [Fact]
    public void Extract_ShouldCaptureScriptReference()
    {
        var xml = """
            <Campaign>
                <Plot Name="test_plot">
                    <Event Name="STORY_GALACTIC_EVENT" Script="galactic_event.lua" />
                </Plot>
            </Campaign>
            """;
        var extractor = new StoryPlotFlowExtractor();
        var result = extractor.Extract(xml, "scripted.xml");

        result.Plots[0].Events[0].ScriptReference.Should().Be("galactic_event.lua");
    }

    #endregion

    #region StoryPlotFlowExtractor — BuildCapabilityLinkReport

    [Fact]
    public void BuildCapabilityLinkReport_ShouldThrow_WhenFlowReportIsNull()
    {
        var act = () => StoryPlotFlowExtractor.BuildCapabilityLinkReport(null!, MegaFilesIndex.Empty, "{}");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldThrow_WhenMegaFilesIndexIsNull()
    {
        var act = () => StoryPlotFlowExtractor.BuildCapabilityLinkReport(FlowIndexReport.Empty, null!, "{}");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldThrow_WhenSymbolPackJsonIsNull()
    {
        var act = () => StoryPlotFlowExtractor.BuildCapabilityLinkReport(FlowIndexReport.Empty, MegaFilesIndex.Empty, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldReturnEmpty_WhenNoPlots()
    {
        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(FlowIndexReport.Empty, MegaFilesIndex.Empty, "{}");
        result.Links.Should().BeEmpty();
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldReturnDiagnostic_WhenSymbolPackIsWhitespace()
    {
        var report = new FlowIndexReport(
            new[] { new FlowPlotRecord("plot1", "test.xml", new[]
            {
                new FlowEventRecord("STORY_GALACTIC_TEST", FlowModeHint.Galactic, "test.xml", null,
                    new Dictionary<string, string>())
            }) },
            Array.Empty<string>());

        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, MegaFilesIndex.Empty, "   ");
        result.Diagnostics.Should().Contain(d => d.Contains("empty"));
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldReturnLinks_WhenCapabilitiesPresent()
    {
        var report = new FlowIndexReport(
            new[] { new FlowPlotRecord("plot1", "test.xml", new[]
            {
                new FlowEventRecord("STORY_GALACTIC_TEST", FlowModeHint.Galactic, "test.xml", null,
                    new Dictionary<string, string>())
            }) },
            Array.Empty<string>());

        var symbolPack = """
            {
                "capabilities": [
                    { "featureId": "set_credits", "available": true, "state": "Stable", "reasonCode": "OK" }
                ]
            }
            """;

        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, MegaFilesIndex.Empty, symbolPack);
        result.Links.Should().NotBeEmpty();
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldHandleInvalidJson()
    {
        var report = new FlowIndexReport(
            new[] { new FlowPlotRecord("plot1", "test.xml", new[]
            {
                new FlowEventRecord("STORY_GALACTIC_TEST", FlowModeHint.Galactic, "test.xml", null,
                    new Dictionary<string, string>())
            }) },
            Array.Empty<string>());

        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, MegaFilesIndex.Empty, "not-json{{{");
        result.Diagnostics.Should().Contain(d => d.Contains("parse failed"));
    }

    #endregion

    #region StoryFlowGraphExporter — Build

    [Fact]
    public void GraphBuild_ShouldThrow_WhenFlowReportIsNull()
    {
        var exporter = new StoryFlowGraphExporter();
        var act = () => exporter.Build(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GraphBuild_ShouldReturnEmpty_WhenNoPlots()
    {
        var exporter = new StoryFlowGraphExporter();
        var result = exporter.Build(FlowIndexReport.Empty);
        result.Nodes.Should().BeEmpty();
        result.Edges.Should().BeEmpty();
        result.Diagnostics.Should().Contain(d => d.Contains("no plots"));
    }

    [Fact]
    public void GraphBuild_ShouldCreateNodesAndEdges()
    {
        var report = new FlowIndexReport(
            new[] { new FlowPlotRecord("plot1", "test.xml", new[]
            {
                new FlowEventRecord("STORY_GALACTIC_A", FlowModeHint.Galactic, "test.xml", null,
                    new Dictionary<string, string>()),
                new FlowEventRecord("STORY_GALACTIC_B", FlowModeHint.Galactic, "test.xml", "script.lua",
                    new Dictionary<string, string>())
            }) },
            Array.Empty<string>());

        var exporter = new StoryFlowGraphExporter();
        var result = exporter.Build(report);

        // 1 root node + 2 event nodes
        result.Nodes.Should().HaveCount(3);
        // 2 plot_contains_event edges + 1 inferred_sequence edge
        result.Edges.Should().HaveCount(3);
        result.Edges.Should().Contain(e => e.EdgeType == "plot_contains_event");
        result.Edges.Should().Contain(e => e.EdgeType == "inferred_sequence");
    }

    #endregion

    #region StoryFlowGraphExporter — BuildMarkdownSummary

    [Fact]
    public void BuildMarkdownSummary_ShouldThrow_WhenProfileIdIsNull()
    {
        var act = () => StoryFlowGraphExporter.BuildMarkdownSummary(null!, StoryFlowGraphReport.Empty);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildMarkdownSummary_ShouldThrow_WhenGraphIsNull()
    {
        var act = () => StoryFlowGraphExporter.BuildMarkdownSummary("test", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildMarkdownSummary_ShouldContainProfileId()
    {
        var md = StoryFlowGraphExporter.BuildMarkdownSummary("base_swfoc", StoryFlowGraphReport.Empty);
        md.Should().Contain("base_swfoc");
        md.Should().Contain("nodes: 0");
    }

    [Fact]
    public void BuildMarkdownSummary_ShouldIncludeDiagnostics()
    {
        var report = new StoryFlowGraphReport(
            Array.Empty<StoryFlowGraphNode>(),
            Array.Empty<StoryFlowGraphEdge>(),
            new[] { "diagnostic message" });
        var md = StoryFlowGraphExporter.BuildMarkdownSummary("test", report);
        md.Should().Contain("Diagnostics");
        md.Should().Contain("diagnostic message");
    }

    #endregion

    #region FlowLabSnapshotBuilder

    [Fact]
    public void SnapshotBuild_ShouldThrow_WhenFlowReportIsNull()
    {
        var builder = new FlowLabSnapshotBuilder();
        var act = () => builder.Build(null!, MegaFilesIndex.Empty);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SnapshotBuild_ShouldThrow_WhenMegaFilesIndexIsNull()
    {
        var builder = new FlowLabSnapshotBuilder();
        var act = () => builder.Build(FlowIndexReport.Empty, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SnapshotBuild_ShouldReturnModeCounts()
    {
        var report = new FlowIndexReport(
            new[] { new FlowPlotRecord("plot1", "test.xml", new[]
            {
                new FlowEventRecord("STORY_GALACTIC_A", FlowModeHint.Galactic, "test.xml", "script.lua",
                    new Dictionary<string, string>()),
                new FlowEventRecord("STORY_LAND_TACTICAL_B", FlowModeHint.TacticalLand, "test.xml", null,
                    new Dictionary<string, string>())
            }) },
            Array.Empty<string>());

        var megaIndex = new MegaFilesIndex(
            new[] { new MegaFileEntry("test.meg", 0, true, new Dictionary<string, string>()) },
            Array.Empty<string>());

        var builder = new FlowLabSnapshotBuilder();
        var result = builder.Build(report, megaIndex);

        result.ModeCounts.Should().HaveCount(2);
        result.ScriptReferences.Should().ContainSingle().Which.Should().Be("script.lua");
        result.MegaLoadOrder.Should().ContainSingle().Which.Should().Be("test.meg");
    }

    #endregion

    #region LuaHarnessRunner — null guards

    [Fact]
    public void LuaRunner_ShouldThrow_WhenHarnessScriptPathIsNull()
    {
        var act = () => new LuaHarnessRunner(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_ShouldThrow_WhenRequestIsNull()
    {
        var runner = new LuaHarnessRunner("harness.ps1");
        var act = async () => await runner.RunAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_OneParam_ShouldThrow_WhenRequestIsNull()
    {
        var runner = new LuaHarnessRunner("harness.ps1");
        var act = async () => await runner.RunAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    #endregion

    #region LuaHarnessRunner — missing script

    [Fact]
    public async Task RunAsync_ShouldFail_WhenScriptPathIsMissing()
    {
        var runner = new LuaHarnessRunner("harness.ps1");
        var request = new LuaHarnessRunRequest(ScriptPath: "nonexistent.lua");

        var result = await runner.RunAsync(request, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("lua_script_missing");
    }

    [Fact]
    public async Task RunAsync_ShouldFail_WhenScriptPathIsWhitespace()
    {
        var runner = new LuaHarnessRunner("harness.ps1");
        var request = new LuaHarnessRunRequest(ScriptPath: "   ");

        var result = await runner.RunAsync(request, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("lua_script_missing");
    }

    #endregion

    #region LuaHarnessRunner — missing harness

    [Fact]
    public async Task RunAsync_ShouldFail_WhenHarnessScriptIsMissing()
    {
        using var temp = new TempDirectory("swfoc-flow-w8-harness");
        var scriptPath = Path.Join(temp.Path, "test_script.lua");
        File.WriteAllText(scriptPath, "-- SWFOC_TRAINER_TELEMETRY\n-- SwfocTrainer_Emit_Telemetry_Mode()");

        var runner = new LuaHarnessRunner(Path.Join(temp.Path, "nonexistent_harness.ps1"));
        var request = new LuaHarnessRunRequest(ScriptPath: scriptPath);

        var result = await runner.RunAsync(request, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("harness_runner_missing");
    }

    #endregion

    #region LuaHarnessRunner — missing telemetry marker

    [Fact]
    public async Task RunAsync_ShouldFail_WhenTelemetryMarkerIsMissing()
    {
        using var temp = new TempDirectory("swfoc-flow-w8-nomarker");
        var scriptPath = Path.Join(temp.Path, "no_marker.lua");
        var harnessPath = Path.Join(temp.Path, "harness.ps1");
        File.WriteAllText(scriptPath, "-- no telemetry here");
        File.WriteAllText(harnessPath, "# harness");

        var runner = new LuaHarnessRunner(harnessPath);
        var request = new LuaHarnessRunRequest(ScriptPath: scriptPath);

        var result = await runner.RunAsync(request, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("telemetry_marker_missing");
    }

    #endregion

    #region LuaHarnessRunner — successful execution

    [Fact]
    public async Task RunAsync_ShouldSucceed_WhenAllConditionsMet()
    {
        using var temp = new TempDirectory("swfoc-flow-w8-success");
        var scriptPath = Path.Join(temp.Path, "good_script.lua");
        var harnessPath = Path.Join(temp.Path, "harness.ps1");
        File.WriteAllText(scriptPath, "-- SWFOC_TRAINER_TELEMETRY\nSwfocTrainer_Emit_Telemetry_Mode()");
        File.WriteAllText(harnessPath, "# harness runner");

        var runner = new LuaHarnessRunner(harnessPath);
        var request = new LuaHarnessRunRequest(ScriptPath: scriptPath, Mode: "TacticalLand");

        var result = await runner.RunAsync(request, CancellationToken.None);
        result.Succeeded.Should().BeTrue();
        result.ReasonCode.Should().Be("ok");
        result.OutputLines.Should().Contain(l => l.Contains("mode=TacticalLand"));
        result.OutputLines.Should().Contain(l => l.Contains("SWFOC_TRAINER_TELEMETRY"));
    }

    #endregion
}
