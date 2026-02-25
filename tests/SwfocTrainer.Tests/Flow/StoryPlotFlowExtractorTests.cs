using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.Flow.Models;
using SwfocTrainer.Flow.Services;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

public sealed class StoryPlotFlowExtractorTests
{
    [Fact]
    public void Extract_ShouldMapTacticalEventsAndScripts()
    {
        const string xml = """
<Story>
  <Plot Name="Galactic_Campaign">
    <Event Name="STORY_SPACE_TACTICAL" LuaScript="Story/SpaceBattle.lua" Reward="SPACE_REWARD" />
    <Event Name="STORY_LAND_TACTICAL" Script="Story/LandBattle.lua" />
    <Event Name="STORY_GALACTIC_TURN" EventScript="Story/Galactic.lua" />
  </Plot>
</Story>
""";

        var extractor = new StoryPlotFlowExtractor();

        var report = extractor.Extract(xml, "Data/XML/Story/Campaign.xml");

        report.Diagnostics.Should().BeEmpty();
        report.Plots.Should().ContainSingle();
        report.Plots[0].PlotId.Should().Be("Galactic_Campaign");
        report.Plots[0].Events.Should().HaveCount(3);

        report.Plots[0].Events.Should().ContainSingle(x =>
            x.EventName == "STORY_SPACE_TACTICAL" &&
            x.ModeHint == FlowModeHint.TacticalSpace &&
            x.ScriptReference == "Story/SpaceBattle.lua");

        report.Plots[0].Events.Should().ContainSingle(x =>
            x.EventName == "STORY_LAND_TACTICAL" &&
            x.ModeHint == FlowModeHint.TacticalLand &&
            x.ScriptReference == "Story/LandBattle.lua");

        report.Plots[0].Events.Should().ContainSingle(x =>
            x.EventName == "STORY_GALACTIC_TURN" &&
            x.ModeHint == FlowModeHint.Galactic &&
            x.ScriptReference == "Story/Galactic.lua");
    }

    [Fact]
    public void Extract_ShouldCreateSyntheticPlotWhenPlotNodesAreMissing()
    {
        const string xml = """
<Story>
  <Event Name="STORY_SPACE_TACTICAL" LuaScript="Story/SpaceBattle.lua" />
</Story>
""";

        var extractor = new StoryPlotFlowExtractor();

        var report = extractor.Extract(xml, "Data/XML/Story/SkirmishFlow.xml");

        report.Plots.Should().ContainSingle();
        report.Plots[0].PlotId.Should().Be("SkirmishFlow");
        report.GetAllEvents().Should().ContainSingle(x => x.ModeHint == FlowModeHint.TacticalSpace);
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldProduceDeterministicModeLinkedRows()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_SPACE_TACTICAL" LuaScript="Story/SpaceBattle.lua" />
    <Event Name="STORY_GALACTIC_TURN" LuaScript="Story/Galactic.lua" />
  </Plot>
</Story>
""";
        const string symbolPack = """
{
  "capabilities": [
    { "featureId": "freeze_timer", "available": true, "state": "Verified", "reasonCode": "CAPABILITY_PROBE_PASS" },
    { "featureId": "toggle_fog_reveal", "available": true, "state": "Verified", "reasonCode": "CAPABILITY_PROBE_PASS" },
    { "featureId": "toggle_ai", "available": true, "state": "Verified", "reasonCode": "CAPABILITY_PROBE_PASS" },
    { "featureId": "set_unit_cap", "available": false, "state": "Unavailable", "reasonCode": "CAPABILITY_REQUIRED_MISSING" },
    { "featureId": "toggle_instant_build_patch", "available": false, "state": "Unavailable", "reasonCode": "CAPABILITY_REQUIRED_MISSING" },
    { "featureId": "set_credits", "available": true, "state": "Verified", "reasonCode": "CAPABILITY_PROBE_PASS" }
  ]
}
""";

        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Data/XML/Story/Campaign.xml");
        var megaFiles = new MegaFilesIndex(
            Files: new[]
            {
                new MegaFileEntry("Base.meg", 0, true, new Dictionary<string, string>())
            },
            Diagnostics: Array.Empty<string>());

        var linkage = extractor.BuildCapabilityLinkReport(report, megaFiles, symbolPack);

        linkage.Diagnostics.Should().BeEmpty();
        linkage.Links.Should().NotBeEmpty();
        linkage.Links.Select(x => x.MegaFileSource).Distinct().Should().ContainSingle().Which.Should().Be("Base.meg");
        linkage.Links.Should().Contain(x =>
            x.EventName == "STORY_SPACE_TACTICAL" &&
            x.FeatureId == "freeze_timer" &&
            x.Available);
        linkage.Links.Should().Contain(x =>
            x.EventName == "STORY_GALACTIC_TURN" &&
            x.FeatureId == "set_credits" &&
            x.Available);
    }
}
