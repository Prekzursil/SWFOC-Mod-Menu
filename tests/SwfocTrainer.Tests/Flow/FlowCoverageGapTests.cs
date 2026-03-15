using System.Reflection;
using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.Flow.Models;
using SwfocTrainer.Flow.Services;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

public sealed class FlowCoverageGapTests
{
    [Fact]
    public void Extract_ShouldReturnEmpty_WhenXmlContentMissing()
    {
        var extractor = new StoryPlotFlowExtractor();

        var report = extractor.Extract("   ", "Data/XML/Story/Empty.xml");

        report.Should().BeSameAs(FlowIndexReport.Empty);
    }

    [Fact]
    public void Extract_ShouldEmitDiagnostic_WhenXmlIsInvalid()
    {
        var extractor = new StoryPlotFlowExtractor();

        var report = extractor.Extract("<Story><Plot>", "Data/XML/Story/Broken.xml");

        report.Plots.Should().BeEmpty();
        report.Diagnostics.Should().ContainSingle(x => x.Contains("Invalid XML", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Extract_ShouldFallbackPlotId_AndIgnoreNonStoryEvents()
    {
        const string xml = """
<Story>
  <Plot>
    <Event Name="NOT_A_STORY_EVENT" LuaScript="Story/Ignored.lua" />
    <Event Name="STORY_CUSTOM_SEQUENCE" StoryScript="Story/Custom.lua" Reward="REWARD_X" />
  </Plot>
</Story>
""";

        var extractor = new StoryPlotFlowExtractor();

        var report = extractor.Extract(xml, "Data/XML/Story/UnnamedPlot.xml");

        report.Diagnostics.Should().BeEmpty();
        report.Plots.Should().ContainSingle();
        report.Plots[0].PlotId.Should().Be("UnnamedPlot");
        report.Plots[0].Events.Should().ContainSingle();
        report.Plots[0].Events[0].ModeHint.Should().Be(FlowModeHint.Unknown);
        report.Plots[0].Events[0].ScriptReference.Should().Be("Story/Custom.lua");
        report.Plots[0].Events[0].Attributes.Should().ContainKey("Reward").WhoseValue.Should().Be("REWARD_X");
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldFailClosed_WhenSymbolPackPayloadMissing()
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
                            "STORY_GALACTIC_TURN",
                            FlowModeHint.Galactic,
                            "Data/XML/Story/Campaign.xml",
                            "Story/Galactic.lua",
                            new Dictionary<string, string>())
                    })
            },
            Array.Empty<string>());
        var megaFiles = new MegaFilesIndex(Array.Empty<MegaFileEntry>(), Array.Empty<string>());

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, "");

        linkage.Links.Should().BeEmpty();
        linkage.Diagnostics.Should().ContainSingle("symbol-pack payload is empty");
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldUseUnknownMegSource_AndFallbackCapabilityState()
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
                            new Dictionary<string, string>())
                    })
            },
            Array.Empty<string>());
        const string symbolPack = """
{
  "capabilities": [
    { "featureId": "freeze_timer", "available": true, "state": "Verified", "reasonCode": "CAPABILITY_PROBE_PASS" }
  ]
}
""";

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(
            report,
            new MegaFilesIndex(Array.Empty<MegaFileEntry>(), Array.Empty<string>()),
            symbolPack);

        linkage.Diagnostics.Should().BeEmpty();
        linkage.Links.Should().Contain(x =>
            x.MegaFileSource == "unknown" &&
            x.FeatureId == "set_unit_cap" &&
            x.Available == false &&
            x.ReasonCode == "CAPABILITY_REQUIRED_MISSING");
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldMatchCapabilitiesCaseInsensitively_AndDefaultMissingStateMetadata()
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
                            "STORY_GALACTIC_TURN",
                            FlowModeHint.Galactic,
                            "Data/XML/Story/Campaign.xml",
                            "Story/Galactic.lua",
                            new Dictionary<string, string>())
                    })
            },
            Array.Empty<string>());
        const string symbolPack = """
{
  "capabilities": [
    { "featureId": "TOGGLE_AI", "available": true }
  ]
}
""";

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(
            report,
            new MegaFilesIndex(Array.Empty<MegaFileEntry>(), Array.Empty<string>()),
            symbolPack);

        linkage.Diagnostics.Should().BeEmpty();
        linkage.Links.Should().Contain(x =>
            x.FeatureId == "toggle_ai" &&
            x.Available &&
            x.State == "Unknown" &&
            x.ReasonCode == "CAPABILITY_UNKNOWN");
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldReturnEmpty_WhenFlowReportHasNoPlots()
    {
        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(
            FlowIndexReport.Empty,
            new MegaFilesIndex(Array.Empty<MegaFileEntry>(), Array.Empty<string>()),
            """{ "capabilities": [ { "featureId": "set_credits", "available": true, "state": "Verified", "reasonCode": "CAPABILITY_PROBE_PASS" } ] }""");

        linkage.Should().BeSameAs(FlowCapabilityLinkReport.Empty);
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldRejectInvalidOrMissingCapabilityPayloads()
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
                            "STORY_GALACTIC_TURN",
                            FlowModeHint.Galactic,
                            "Data/XML/Story/Campaign.xml",
                            "Story/Galactic.lua",
                            new Dictionary<string, string>())
                    })
            },
            Array.Empty<string>());
        var megaFiles = new MegaFilesIndex(Array.Empty<MegaFileEntry>(), Array.Empty<string>());

        var invalidJson = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, "{");
        var missingCapabilities = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, """{ "capabilities": [] }""");

        invalidJson.Links.Should().BeEmpty();
        invalidJson.Diagnostics.Should().ContainSingle(x => x.Contains("symbol-pack parse failed", StringComparison.OrdinalIgnoreCase));
        missingCapabilities.Links.Should().BeEmpty();
        missingCapabilities.Diagnostics.Should().ContainSingle("symbol-pack capabilities are missing");
    }

    [Fact]
    public void Build_ShouldEmitDiagnostic_WhenFlowReportHasNoPlots()
    {
        var exporter = new StoryFlowGraphExporter();

        var graph = exporter.Build(FlowIndexReport.Empty);

        graph.Nodes.Should().BeEmpty();
        graph.Edges.Should().BeEmpty();
        graph.Diagnostics.Should().ContainSingle("flow report has no plots.");
    }

    [Fact]
    public void Build_ShouldCarryForwardFlowDiagnostics_WhenPlotsExist()
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
                            "STORY_GALACTIC_TURN",
                            FlowModeHint.Galactic,
                            "Data/XML/Story/Campaign.xml",
                            "Story/Galactic.lua",
                            new Dictionary<string, string>())
                    })
            },
            new[] { "flow warning" });

        var graph = new StoryFlowGraphExporter().Build(report);

        graph.Nodes.Should().NotBeEmpty();
        graph.Diagnostics.Should().ContainSingle("flow warning");
    }

    [Fact]
    public void BuildMarkdownSummary_ShouldRenderDiagnosticsAndPlaceholders()
    {
        var graph = new StoryFlowGraphReport(
            Nodes:
            [
                new StoryFlowGraphNode(
                    "Campaign:STORY_UNKNOWN:000",
                    "Campaign",
                    "STORY_UNKNOWN",
                    FlowModeHint.Unknown,
                    "Data/XML/Story/Campaign.xml",
                    null,
                    Array.Empty<string>())
            ],
            Edges: Array.Empty<StoryFlowGraphEdge>(),
            Diagnostics: new[] { "graph warning" });

        var markdown = StoryFlowGraphExporter.BuildMarkdownSummary("base_swfoc", graph);

        markdown.Should().Contain("| Campaign | STORY_UNKNOWN | Unknown | - | - |");
        markdown.Should().Contain("## Diagnostics");
        markdown.Should().Contain("- graph warning");
    }

    [Fact]
    public void Build_ShouldCreateInferredSequenceEdges_ForMultipleEvents()
    {
        var exporter = new StoryFlowGraphExporter();
        var report = new FlowIndexReport(
            new[]
            {
                new FlowPlotRecord(
                    "Campaign",
                    "Data/XML/Story/Campaign.xml",
                    new[]
                    {
                        new FlowEventRecord("STORY_ZETA", FlowModeHint.Unknown, "Campaign.xml", "Story/Z.lua", new Dictionary<string, string>()),
                        new FlowEventRecord("STORY_ALPHA", FlowModeHint.Galactic, "Campaign.xml", "Story/A.lua", new Dictionary<string, string>()),
                        new FlowEventRecord("STORY_ALPHA", FlowModeHint.Galactic, "Campaign.xml", "Story/B.lua", new Dictionary<string, string>())
                    })
            },
            Array.Empty<string>());

        var graph = exporter.Build(report);

        graph.Edges.Count(x => x.EdgeType == "inferred_sequence").Should().Be(2);
        graph.Edges.Should().Contain(x =>
            x.EdgeType == "inferred_sequence" &&
            x.FromNodeId == "Campaign:STORY_ALPHA:000" &&
            x.ToNodeId == "Campaign:STORY_ALPHA:001");
    }

    [Fact]
    public void ResolveDefaultHarnessScriptPath_ShouldReturnExistingHarnessScript()
    {
        var method = typeof(LuaHarnessRunner).GetMethod(
            "ResolveDefaultHarnessScriptPath",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var resolved = method!.Invoke(null, Array.Empty<object?>()) as string;

        resolved.Should().NotBeNullOrWhiteSpace();
        File.Exists(resolved!).Should().BeTrue();
        resolved.Should().EndWith(Path.Combine("tools", "lua-harness", "run-lua-harness.ps1"));
    }

    [Fact]
    public void Extract_ShouldUseAlternatePlotIdAndScriptAttributeNames()
    {
        const string xml = """
<Story>
  <Plot ID="Campaign_Alt">
    <Event Name="STORY_GALACTIC_CUSTOM" ScriptName="Story/Alternate.lua" Reward="ALT_REWARD" />
  </Plot>
</Story>
""";

        var report = new StoryPlotFlowExtractor().Extract(xml, "Data/XML/Story/CampaignAlt.xml");

        report.Diagnostics.Should().BeEmpty();
        report.Plots.Should().ContainSingle();
        report.Plots[0].PlotId.Should().Be("Campaign_Alt");
        report.Plots[0].Events.Should().ContainSingle();
        report.Plots[0].Events[0].ScriptReference.Should().Be("Story/Alternate.lua");
        report.Plots[0].Events[0].Attributes.Should().ContainKey("Reward").WhoseValue.Should().Be("ALT_REWARD");
    }
}
