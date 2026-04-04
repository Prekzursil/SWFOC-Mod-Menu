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

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, symbolPack);

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

    [Fact]
    public void Extract_ShouldReturnEmpty_WhenXmlContentIsWhitespace()
    {
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract("   ", "empty.xml");
        report.Should().Be(FlowIndexReport.Empty);
    }

    [Fact]
    public void Extract_ShouldReturnDiagnostic_WhenXmlIsInvalid()
    {
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract("<not valid xml><<>", "bad.xml");
        report.Diagnostics.Should().ContainSingle(x => x.Contains("Invalid XML", StringComparison.OrdinalIgnoreCase));
        report.Plots.Should().BeEmpty();
    }

    [Fact]
    public void Extract_ShouldThrow_WhenXmlContentIsNull()
    {
        var extractor = new StoryPlotFlowExtractor();
        var act = () => extractor.Extract(null!, "file.xml");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Extract_ShouldThrow_WhenSourceFileIsNull()
    {
        var extractor = new StoryPlotFlowExtractor();
        var act = () => extractor.Extract("<root/>", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Extract_ShouldIgnoreNonStoryEvents()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="NON_STORY_EVENT" LuaScript="Script.lua" />
    <Event Name="STORY_SPACE_TACTICAL" LuaScript="Battle.lua" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Data/XML/Story/Campaign.xml");
        report.Plots[0].Events.Should().ContainSingle(x => x.EventName == "STORY_SPACE_TACTICAL");
    }

    [Fact]
    public void Extract_ShouldIgnoreEventsWithBlankName()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="   " LuaScript="Script.lua" />
    <Event LuaScript="Other.lua" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Data/XML/Story/Campaign.xml");
        report.Plots[0].Events.Should().BeEmpty();
    }

    [Fact]
    public void Extract_ShouldUseSyntheticPlotId_WhenPlotHasNoNameAttribute()
    {
        const string xml = """
<Story>
  <Plot>
    <Event Name="STORY_GALACTIC_TURN" LuaScript="Galactic.lua" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Data/XML/Story/NoName.xml");
        report.Plots[0].PlotId.Should().Be("NoName");
    }

    [Fact]
    public void Extract_ShouldUsePlotIdAttribute_WhenNameIsMissing()
    {
        const string xml = """
<Story>
  <Plot Id="my_plot_id">
    <Event Name="STORY_GALACTIC_TURN" LuaScript="Galactic.lua" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Data/XML/Story/WithId.xml");
        report.Plots[0].PlotId.Should().Be("my_plot_id");
    }

    [Fact]
    public void Extract_ShouldReadScriptFromStoryScriptAttribute()
    {
        const string xml = """
<Story>
  <Event Name="STORY_GALACTIC_TURN" StoryScript="Story/GalacticStory.lua" />
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Data/XML/Story/Test.xml");
        report.Plots[0].Events[0].ScriptReference.Should().Be("Story/GalacticStory.lua");
    }

    [Fact]
    public void Extract_ShouldReadScriptFromScriptNameAttribute()
    {
        const string xml = """
<Story>
  <Event Name="STORY_GALACTIC_TURN" ScriptName="Story/Named.lua" />
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Test.xml");
        report.Plots[0].Events[0].ScriptReference.Should().Be("Story/Named.lua");
    }

    [Fact]
    public void Extract_ShouldResolveModeHintUnknown_ForNonMatchingEventName()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_UNKNOWN_MODE" LuaScript="Script.lua" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Data/XML/Test.xml");
        report.Plots[0].Events[0].ModeHint.Should().Be(FlowModeHint.Unknown);
    }

    [Fact]
    public void Extract_ShouldExtractEventAttributesExcludingName()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_SPACE_TACTICAL" Reward="REWARD_X" LuaScript="Script.lua" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Data/XML/Test.xml");
        var evt = report.Plots[0].Events[0];
        evt.Attributes.Should().ContainKey("Reward");
        evt.Attributes.Should().ContainKey("LuaScript");
        evt.Attributes.Should().NotContainKey("Name");
    }

    [Fact]
    public void Extract_ShouldHandleNullRootElement()
    {
        const string xml = """<?xml version="1.0"?><root/>""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Data/XML/Test.xml");
        report.Plots.Should().ContainSingle();
        report.Plots[0].Events.Should().BeEmpty();
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldReturnEmpty_WhenNoPlots()
    {
        var emptyReport = FlowIndexReport.Empty;
        var megaFiles = new MegaFilesIndex(
            Files: new[] { new MegaFileEntry("Base.meg", 0, true, new Dictionary<string, string>()) },
            Diagnostics: Array.Empty<string>());

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(emptyReport, megaFiles, "{}");
        linkage.Should().Be(FlowCapabilityLinkReport.Empty);
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldReturnDiagnostic_WhenSymbolPackIsEmpty()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_GALACTIC_TURN" LuaScript="Script.lua" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Test.xml");
        var megaFiles = new MegaFilesIndex(
            Files: new[] { new MegaFileEntry("Base.meg", 0, true, new Dictionary<string, string>()) },
            Diagnostics: Array.Empty<string>());

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, "  ");
        linkage.Diagnostics.Should().Contain("symbol-pack payload is empty");
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldReturnDiagnostic_WhenSymbolPackIsInvalidJson()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_GALACTIC_TURN" LuaScript="Script.lua" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Test.xml");
        var megaFiles = new MegaFilesIndex(
            Files: new[] { new MegaFileEntry("Base.meg", 0, true, new Dictionary<string, string>()) },
            Diagnostics: Array.Empty<string>());

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, "not-json{{{");
        linkage.Diagnostics.Should().Contain(x => x.Contains("symbol-pack parse failed"));
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldReturnDiagnostic_WhenCapabilitiesArrayIsNull()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_GALACTIC_TURN" LuaScript="Script.lua" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Test.xml");
        var megaFiles = new MegaFilesIndex(
            Files: new[] { new MegaFileEntry("Base.meg", 0, true, new Dictionary<string, string>()) },
            Diagnostics: Array.Empty<string>());

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, """{"capabilities":null}""");
        linkage.Diagnostics.Should().Contain("symbol-pack capabilities are missing");
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldReturnDiagnostic_WhenCapabilitiesArrayIsEmpty()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_GALACTIC_TURN" LuaScript="Script.lua" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Test.xml");
        var megaFiles = new MegaFilesIndex(
            Files: new[] { new MegaFileEntry("Base.meg", 0, true, new Dictionary<string, string>()) },
            Diagnostics: Array.Empty<string>());

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, """{"capabilities":[]}""");
        linkage.Diagnostics.Should().Contain("symbol-pack capabilities are missing");
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldUseFallbackCapability_WhenFeatureIdNotFound()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_SPACE_TACTICAL" LuaScript="Script.lua" />
  </Plot>
</Story>
""";
        const string symbolPack = """
{
  "capabilities": [
    { "featureId": "freeze_timer", "available": true, "state": "Verified", "reasonCode": "OK" }
  ]
}
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Test.xml");
        var megaFiles = new MegaFilesIndex(
            Files: new[] { new MegaFileEntry("Base.meg", 0, true, new Dictionary<string, string>()) },
            Diagnostics: Array.Empty<string>());

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, symbolPack);
        linkage.Links.Should().Contain(x => x.FeatureId == "toggle_fog_reveal" && !x.Available && x.ReasonCode == "CAPABILITY_REQUIRED_MISSING");
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldUseFallbackMegaSource_WhenNoEnabledFiles()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_GALACTIC_TURN" LuaScript="Script.lua" />
  </Plot>
</Story>
""";
        const string symbolPack = """
{
  "capabilities": [
    { "featureId": "set_credits", "available": true, "state": "Verified", "reasonCode": "OK" }
  ]
}
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Test.xml");
        var megaFiles = new MegaFilesIndex(
            Files: Array.Empty<MegaFileEntry>(),
            Diagnostics: Array.Empty<string>());

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, symbolPack);
        linkage.Links.Should().Contain(x => x.MegaFileSource == "unknown");
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldProduceEmptyLinks_ForUnknownModeHintEvents()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_UNKNOWN_MODE" LuaScript="Script.lua" />
  </Plot>
</Story>
""";
        const string symbolPack = """
{
  "capabilities": [
    { "featureId": "freeze_timer", "available": true, "state": "Verified", "reasonCode": "OK" }
  ]
}
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Test.xml");
        var megaFiles = new MegaFilesIndex(
            Files: new[] { new MegaFileEntry("Base.meg", 0, true, new Dictionary<string, string>()) },
            Diagnostics: Array.Empty<string>());

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, symbolPack);
        linkage.Links.Should().BeEmpty();
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldHandleCapabilityWithNullStateAndReasonCode()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_GALACTIC_TURN" LuaScript="Script.lua" />
  </Plot>
</Story>
""";
        const string symbolPack = """
{
  "capabilities": [
    { "featureId": "set_credits", "available": false, "state": null, "reasonCode": null }
  ]
}
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Test.xml");
        var megaFiles = new MegaFilesIndex(
            Files: new[] { new MegaFileEntry("Base.meg", 0, true, new Dictionary<string, string>()) },
            Diagnostics: Array.Empty<string>());

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, symbolPack);
        linkage.Links.Should().Contain(x => x.FeatureId == "set_credits" && x.State == "Unknown");
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldSkipCapabilitiesWithBlankFeatureId()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_GALACTIC_TURN" LuaScript="Script.lua" />
  </Plot>
</Story>
""";
        const string symbolPack = """
{
  "capabilities": [
    { "featureId": "", "available": true, "state": "Verified", "reasonCode": "OK" },
    { "featureId": "set_credits", "available": true, "state": "Verified", "reasonCode": "OK" }
  ]
}
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Test.xml");
        var megaFiles = new MegaFilesIndex(
            Files: new[] { new MegaFileEntry("Base.meg", 0, true, new Dictionary<string, string>()) },
            Diagnostics: Array.Empty<string>());

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, symbolPack);
        linkage.Links.Where(x => x.FeatureId == "set_credits").Should().NotBeEmpty();
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldLinkLandTacticalEvents()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_LAND_TACTICAL" LuaScript="LandBattle.lua" />
  </Plot>
</Story>
""";
        const string symbolPack = """
{
  "capabilities": [
    { "featureId": "freeze_timer", "available": true, "state": "Verified", "reasonCode": "OK" },
    { "featureId": "toggle_fog_reveal", "available": true, "state": "OK", "reasonCode": "OK" },
    { "featureId": "toggle_ai", "available": true, "state": "OK", "reasonCode": "OK" },
    { "featureId": "set_unit_cap", "available": true, "state": "OK", "reasonCode": "OK" },
    { "featureId": "toggle_instant_build_patch", "available": true, "state": "OK", "reasonCode": "OK" }
  ]
}
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Test.xml");
        var megaFiles = new MegaFilesIndex(
            Files: new[] { new MegaFileEntry("Base.meg", 0, true, new Dictionary<string, string>()) },
            Diagnostics: Array.Empty<string>());

        var linkage = StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, symbolPack);
        linkage.Links.Should().HaveCount(5);
        linkage.Links.Should().Contain(x => x.FeatureId == "freeze_timer" && x.ModeHint == FlowModeHint.TacticalLand);
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldThrow_WhenArgumentsAreNull()
    {
        var report = FlowIndexReport.Empty;
        var megaFiles = MegaFilesIndex.Empty;

        var act1 = () => StoryPlotFlowExtractor.BuildCapabilityLinkReport(null!, megaFiles, "{}");
        var act2 = () => StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, null!, "{}");
        var act3 = () => StoryPlotFlowExtractor.BuildCapabilityLinkReport(report, megaFiles, null!);

        act1.Should().Throw<ArgumentNullException>();
        act2.Should().Throw<ArgumentNullException>();
        act3.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Extract_ShouldHandleMultiplePlots()
    {
        const string xml = """
<Story>
  <Plot Name="Plot_A">
    <Event Name="STORY_SPACE_TACTICAL" LuaScript="A.lua" />
  </Plot>
  <Plot Name="Plot_B">
    <Event Name="STORY_LAND_TACTICAL" LuaScript="B.lua" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "MultiPlot.xml");
        report.Plots.Should().HaveCount(2);
        report.Plots[0].PlotId.Should().Be("Plot_A");
        report.Plots[1].PlotId.Should().Be("Plot_B");
    }
}
