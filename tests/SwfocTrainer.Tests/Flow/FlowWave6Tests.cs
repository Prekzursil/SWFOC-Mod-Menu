using System.Text.Json;
using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.Flow.Models;
using SwfocTrainer.Flow.Services;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

/// <summary>
/// Wave 6 — push Flow to 100% branch coverage.
/// Covers StoryPlotFlowExtractor remaining branches: BuildCapabilityLinkReport
/// (empty plots, empty symbol-pack, bad JSON, missing capabilities, galactic/tactical events,
/// unknown mode hint, null root element), ResolveModeHint (space/land/galactic/unknown),
/// ResolveCapability (missing feature fallback), TryParseCapabilities error paths.
/// </summary>
public sealed class FlowWave6Tests
{
    #region StoryPlotFlowExtractor — Extract

    [Fact]
    public void Extract_WhitespaceContent_ShouldReturnEmpty()
    {
        var extractor = new StoryPlotFlowExtractor();
        var result = extractor.Extract("   ", "test.xml");
        result.Should().Be(FlowIndexReport.Empty);
    }

    [Fact]
    public void Extract_InvalidXml_ShouldReturnDiagnostic()
    {
        var extractor = new StoryPlotFlowExtractor();
        var result = extractor.Extract("<<<not xml>>>", "bad.xml");
        result.Diagnostics.Should().Contain(d => d.Contains("Invalid XML"));
    }

    [Fact]
    public void Extract_NoPlotsElement_ShouldUseSyntheticPlot()
    {
        var extractor = new StoryPlotFlowExtractor();
        var xml = @"<Root><Event Name=""STORY_GALACTIC_EVENT"" Script=""test.lua"" /></Root>";
        var result = extractor.Extract(xml, "campaign.xml");
        result.Plots.Should().HaveCount(1);
        result.Plots[0].PlotId.Should().Be("campaign");
        result.Plots[0].Events.Should().HaveCount(1);
        result.Plots[0].Events[0].ModeHint.Should().Be(FlowModeHint.Galactic);
    }

    [Fact]
    public void Extract_WithPlotElement_ShouldExtractPlotId()
    {
        var extractor = new StoryPlotFlowExtractor();
        var xml = @"<Root><Plot Name=""MainPlot""><Event Name=""STORY_SPACE_TACTICAL_BATTLE"" LuaScript=""space.lua"" /></Plot></Root>";
        var result = extractor.Extract(xml, "story.xml");
        result.Plots.Should().HaveCount(1);
        result.Plots[0].PlotId.Should().Be("MainPlot");
        result.Plots[0].Events[0].ModeHint.Should().Be(FlowModeHint.TacticalSpace);
    }

    [Fact]
    public void Extract_PlotWithNoNameAttribute_ShouldFallbackToFileName()
    {
        var extractor = new StoryPlotFlowExtractor();
        var xml = @"<Root><Plot><Event Name=""STORY_LAND_TACTICAL_EVENT"" /></Plot></Root>";
        var result = extractor.Extract(xml, "fallback.xml");
        result.Plots[0].PlotId.Should().Be("fallback");
        result.Plots[0].Events[0].ModeHint.Should().Be(FlowModeHint.TacticalLand);
    }

    [Fact]
    public void Extract_EventWithNoStoryPrefix_ShouldBeExcluded()
    {
        var extractor = new StoryPlotFlowExtractor();
        var xml = @"<Root><Event Name=""NOT_A_STORY_EVENT"" /><Event Name=""STORY_REAL_EVENT"" /></Root>";
        var result = extractor.Extract(xml, "test.xml");
        result.Plots[0].Events.Should().HaveCount(1);
        result.Plots[0].Events[0].EventName.Should().Be("STORY_REAL_EVENT");
    }

    [Fact]
    public void Extract_EventWithUnknownModeHint_ShouldReturnUnknown()
    {
        var extractor = new StoryPlotFlowExtractor();
        var xml = @"<Root><Event Name=""STORY_SOMETHING_ELSE"" /></Root>";
        var result = extractor.Extract(xml, "test.xml");
        result.Plots[0].Events[0].ModeHint.Should().Be(FlowModeHint.Unknown);
    }

    [Fact]
    public void Extract_NullXmlContent_ShouldThrow()
    {
        var extractor = new StoryPlotFlowExtractor();
        var act = () => extractor.Extract(null!, "test.xml");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Extract_NullSourceFile_ShouldThrow()
    {
        var extractor = new StoryPlotFlowExtractor();
        var act = () => extractor.Extract("<Root/>", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Extract_EventWithScriptNameAttribute_ShouldResolveScript()
    {
        var extractor = new StoryPlotFlowExtractor();
        var xml = @"<Root><Event Name=""STORY_GALACTIC_CAMPAIGN"" ScriptName=""campaign.lua"" /></Root>";
        var result = extractor.Extract(xml, "test.xml");
        result.Plots[0].Events[0].ScriptReference.Should().Be("campaign.lua");
    }

    [Fact]
    public void Extract_EventWithEventScriptAttribute_ShouldResolveScript()
    {
        var extractor = new StoryPlotFlowExtractor();
        var xml = @"<Root><Event Name=""STORY_GALACTIC_EVENT"" EventScript=""event.lua"" /></Root>";
        var result = extractor.Extract(xml, "test.xml");
        result.Plots[0].Events[0].ScriptReference.Should().Be("event.lua");
    }

    [Fact]
    public void Extract_EventWithStoryScriptAttribute_ShouldResolveScript()
    {
        var extractor = new StoryPlotFlowExtractor();
        var xml = @"<Root><Event Name=""STORY_GALACTIC_EVENT"" StoryScript=""story.lua"" /></Root>";
        var result = extractor.Extract(xml, "test.xml");
        result.Plots[0].Events[0].ScriptReference.Should().Be("story.lua");
    }

    [Fact]
    public void Extract_PlotWithIdAttribute_ShouldUseIdAttribute()
    {
        var extractor = new StoryPlotFlowExtractor();
        var xml = @"<Root><Plot Id=""PlotById""><Event Name=""STORY_EVENT"" /></Plot></Root>";
        var result = extractor.Extract(xml, "test.xml");
        result.Plots[0].PlotId.Should().Be("PlotById");
    }

    [Fact]
    public void Extract_MultiplePlots_ShouldExtractAll()
    {
        var extractor = new StoryPlotFlowExtractor();
        var xml = @"<Root>
            <Plot Name=""Plot1""><Event Name=""STORY_EVENT_1"" /></Plot>
            <Plot Name=""Plot2""><Event Name=""STORY_EVENT_2"" /></Plot>
        </Root>";
        var result = extractor.Extract(xml, "test.xml");
        result.Plots.Should().HaveCount(2);
    }

    #endregion

    #region StoryPlotFlowExtractor — BuildCapabilityLinkReport

    [Fact]
    public void BuildCapabilityLinkReport_EmptyPlots_ShouldReturnEmpty()
    {
        var flowReport = new FlowIndexReport(Array.Empty<FlowPlotRecord>(), Array.Empty<string>());
        var megaIndex = MegaFilesIndex.Empty;
        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, megaIndex, "{}");
        result.Should().Be(FlowCapabilityLinkReport.Empty);
    }

    [Fact]
    public void BuildCapabilityLinkReport_EmptySymbolPack_ShouldReturnDiagnostic()
    {
        var events = new[]
        {
            new FlowEventRecord("STORY_GALACTIC_EVENT", FlowModeHint.Galactic, "test.xml", null, new Dictionary<string, string>())
        };
        var plots = new[] { new FlowPlotRecord("plot1", "test.xml", events) };
        var flowReport = new FlowIndexReport(plots, Array.Empty<string>());
        var megaIndex = MegaFilesIndex.Empty;

        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, megaIndex, "   ");
        result.Diagnostics.Should().Contain(d => d.Contains("empty"));
    }

    [Fact]
    public void BuildCapabilityLinkReport_BadJson_ShouldReturnDiagnostic()
    {
        var events = new[]
        {
            new FlowEventRecord("STORY_GALACTIC_EVENT", FlowModeHint.Galactic, "test.xml", null, new Dictionary<string, string>())
        };
        var plots = new[] { new FlowPlotRecord("plot1", "test.xml", events) };
        var flowReport = new FlowIndexReport(plots, Array.Empty<string>());
        var megaIndex = MegaFilesIndex.Empty;

        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, megaIndex, "<<<not json>>>");
        result.Diagnostics.Should().Contain(d => d.Contains("parse failed"));
    }

    [Fact]
    public void BuildCapabilityLinkReport_NullCapabilities_ShouldReturnDiagnostic()
    {
        var events = new[]
        {
            new FlowEventRecord("STORY_GALACTIC_EVENT", FlowModeHint.Galactic, "test.xml", null, new Dictionary<string, string>())
        };
        var plots = new[] { new FlowPlotRecord("plot1", "test.xml", events) };
        var flowReport = new FlowIndexReport(plots, Array.Empty<string>());
        var megaIndex = MegaFilesIndex.Empty;

        var symbolPack = JsonSerializer.Serialize(new { Capabilities = (object?)null });
        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, megaIndex, symbolPack);
        result.Diagnostics.Should().Contain(d => d.Contains("missing"));
    }

    [Fact]
    public void BuildCapabilityLinkReport_EmptyCapabilitiesList_ShouldReturnDiagnostic()
    {
        var events = new[]
        {
            new FlowEventRecord("STORY_GALACTIC_EVENT", FlowModeHint.Galactic, "test.xml", null, new Dictionary<string, string>())
        };
        var plots = new[] { new FlowPlotRecord("plot1", "test.xml", events) };
        var flowReport = new FlowIndexReport(plots, Array.Empty<string>());
        var megaIndex = MegaFilesIndex.Empty;

        var symbolPack = JsonSerializer.Serialize(new { Capabilities = Array.Empty<object>() });
        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, megaIndex, symbolPack);
        result.Diagnostics.Should().Contain(d => d.Contains("missing"));
    }

    [Fact]
    public void BuildCapabilityLinkReport_GalacticEvent_WithCapabilities_ShouldLinkFeatures()
    {
        var events = new[]
        {
            new FlowEventRecord("STORY_GALACTIC_EVENT", FlowModeHint.Galactic, "test.xml", null, new Dictionary<string, string>())
        };
        var plots = new[] { new FlowPlotRecord("plot1", "test.xml", events) };
        var flowReport = new FlowIndexReport(plots, Array.Empty<string>());

        var megaFiles = new[]
        {
            new MegaFileEntry("test.meg", 1, true, new Dictionary<string, string>())
        };
        var megaIndex = new MegaFilesIndex(megaFiles, Array.Empty<string>());

        var symbolPack = JsonSerializer.Serialize(new
        {
            Capabilities = new[]
            {
                new { FeatureId = "set_credits", Available = true, State = "Active", ReasonCode = "OK" },
                new { FeatureId = "toggle_ai", Available = false, State = "Inactive", ReasonCode = "BLOCKED" }
            }
        });

        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, megaIndex, symbolPack);
        result.Links.Should().HaveCount(2); // set_credits + toggle_ai for Galactic
        result.Links.Should().Contain(l => l.FeatureId == "set_credits" && l.Available);
        result.Links.Should().Contain(l => l.FeatureId == "toggle_ai" && !l.Available);
    }

    [Fact]
    public void BuildCapabilityLinkReport_TacticalEvent_ShouldLinkTacticalFeatures()
    {
        var events = new[]
        {
            new FlowEventRecord("STORY_LAND_TACTICAL_BATTLE", FlowModeHint.TacticalLand, "test.xml", null, new Dictionary<string, string>())
        };
        var plots = new[] { new FlowPlotRecord("plot1", "test.xml", events) };
        var flowReport = new FlowIndexReport(plots, Array.Empty<string>());
        var megaIndex = MegaFilesIndex.Empty;

        var symbolPack = JsonSerializer.Serialize(new
        {
            Capabilities = new[]
            {
                new { FeatureId = "freeze_timer", Available = true, State = "Active", ReasonCode = "OK" }
            }
        });

        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, megaIndex, symbolPack);
        // TacticalLand resolves 5 feature IDs
        result.Links.Should().HaveCount(5);
        result.Links.Should().Contain(l => l.FeatureId == "freeze_timer" && l.Available);
    }

    [Fact]
    public void BuildCapabilityLinkReport_TacticalSpaceEvent_ShouldLinkTacticalFeatures()
    {
        var events = new[]
        {
            new FlowEventRecord("STORY_SPACE_TACTICAL_BATTLE", FlowModeHint.TacticalSpace, "test.xml", null, new Dictionary<string, string>())
        };
        var plots = new[] { new FlowPlotRecord("plot1", "test.xml", events) };
        var flowReport = new FlowIndexReport(plots, Array.Empty<string>());
        var megaIndex = MegaFilesIndex.Empty;

        var symbolPack = JsonSerializer.Serialize(new
        {
            Capabilities = new[]
            {
                new { FeatureId = "toggle_fog_reveal", Available = true, State = "Active", ReasonCode = "OK" }
            }
        });

        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, megaIndex, symbolPack);
        result.Links.Should().HaveCount(5);
    }

    [Fact]
    public void BuildCapabilityLinkReport_UnknownModeHint_ShouldProduceNoLinks()
    {
        var events = new[]
        {
            new FlowEventRecord("STORY_UNKNOWN_TYPE", FlowModeHint.Unknown, "test.xml", null, new Dictionary<string, string>())
        };
        var plots = new[] { new FlowPlotRecord("plot1", "test.xml", events) };
        var flowReport = new FlowIndexReport(plots, Array.Empty<string>());
        var megaIndex = MegaFilesIndex.Empty;

        var symbolPack = JsonSerializer.Serialize(new
        {
            Capabilities = new[]
            {
                new { FeatureId = "set_credits", Available = true, State = "Active", ReasonCode = "OK" }
            }
        });

        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, megaIndex, symbolPack);
        result.Links.Should().BeEmpty();
    }

    [Fact]
    public void BuildCapabilityLinkReport_MissingCapability_ShouldUseFallback()
    {
        var events = new[]
        {
            new FlowEventRecord("STORY_GALACTIC_EVENT", FlowModeHint.Galactic, "test.xml", null, new Dictionary<string, string>())
        };
        var plots = new[] { new FlowPlotRecord("plot1", "test.xml", events) };
        var flowReport = new FlowIndexReport(plots, Array.Empty<string>());
        var megaIndex = MegaFilesIndex.Empty;

        // Only provide one capability, but Galactic needs set_credits and toggle_ai
        var symbolPack = JsonSerializer.Serialize(new
        {
            Capabilities = new[]
            {
                new { FeatureId = "set_credits", Available = true, State = "Active", ReasonCode = "OK" }
            }
        });

        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, megaIndex, symbolPack);
        var missingLink = result.Links.FirstOrDefault(l => l.FeatureId == "toggle_ai");
        missingLink.Should().NotBeNull();
        missingLink!.Available.Should().BeFalse();
        missingLink.ReasonCode.Should().Be("CAPABILITY_REQUIRED_MISSING");
    }

    [Fact]
    public void BuildCapabilityLinkReport_NullFlowReport_ShouldThrow()
    {
        var act = () => StoryPlotFlowExtractor.BuildCapabilityLinkReport(null!, MegaFilesIndex.Empty, "{}");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildCapabilityLinkReport_NullMegaIndex_ShouldThrow()
    {
        var flowReport = new FlowIndexReport(Array.Empty<FlowPlotRecord>(), Array.Empty<string>());
        var act = () => StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, null!, "{}");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildCapabilityLinkReport_NullSymbolPackJson_ShouldThrow()
    {
        var flowReport = new FlowIndexReport(Array.Empty<FlowPlotRecord>(), Array.Empty<string>());
        var act = () => StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, MegaFilesIndex.Empty, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildCapabilityLinkReport_CapabilityWithNullFeatureId_ShouldBeSkipped()
    {
        var events = new[]
        {
            new FlowEventRecord("STORY_GALACTIC_EVENT", FlowModeHint.Galactic, "test.xml", null, new Dictionary<string, string>())
        };
        var plots = new[] { new FlowPlotRecord("plot1", "test.xml", events) };
        var flowReport = new FlowIndexReport(plots, Array.Empty<string>());
        var megaIndex = MegaFilesIndex.Empty;

        // Include a capability with null FeatureId — should be skipped
        var symbolPack = JsonSerializer.Serialize(new
        {
            Capabilities = new object[]
            {
                new { FeatureId = (string?)null, Available = true, State = "Active", ReasonCode = "OK" },
                new { FeatureId = "set_credits", Available = true, State = "Active", ReasonCode = "OK" }
            }
        });

        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, megaIndex, symbolPack);
        result.Links.Should().Contain(l => l.FeatureId == "set_credits" && l.Available);
    }

    [Fact]
    public void BuildCapabilityLinkReport_CapabilityWithNullStateAndReasonCode_ShouldDefaultToUnknown()
    {
        var events = new[]
        {
            new FlowEventRecord("STORY_GALACTIC_EVENT", FlowModeHint.Galactic, "test.xml", null, new Dictionary<string, string>())
        };
        var plots = new[] { new FlowPlotRecord("plot1", "test.xml", events) };
        var flowReport = new FlowIndexReport(plots, Array.Empty<string>());
        var megaIndex = MegaFilesIndex.Empty;

        var symbolPack = JsonSerializer.Serialize(new
        {
            Capabilities = new object[]
            {
                new { FeatureId = "set_credits", Available = false, State = (string?)null, ReasonCode = (string?)null }
            }
        });

        var result = StoryPlotFlowExtractor.BuildCapabilityLinkReport(flowReport, megaIndex, symbolPack);
        var link = result.Links.First(l => l.FeatureId == "set_credits");
        link.State.Should().Be("Unknown");
        link.ReasonCode.Should().Be("CAPABILITY_UNKNOWN");
    }

    #endregion
}
