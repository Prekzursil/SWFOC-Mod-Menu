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
    public void Build_ShouldEmitDiagnostic_WhenFlowReportHasNoPlots()
    {
        var exporter = new StoryFlowGraphExporter();

        var graph = exporter.Build(FlowIndexReport.Empty);

        graph.Nodes.Should().BeEmpty();
        graph.Edges.Should().BeEmpty();
        graph.Diagnostics.Should().ContainSingle("flow report has no plots.");
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
}
