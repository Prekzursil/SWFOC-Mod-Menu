using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.Flow.Models;
using SwfocTrainer.Flow.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

/// <summary>
/// Wave 2 coverage: fills remaining branches in StoryPlotFlowExtractor
/// and LuaHarnessRunner.
/// </summary>
public sealed class FlowWave2CoverageTests
{
    #region StoryPlotFlowExtractor — remaining branches

    [Fact]
    public void Extract_ShouldFallbackToPlotIDAttribute_WhenNameAndIdAreMissing()
    {
        const string xml = """
<Story>
  <Plot ID="uppercase_id_attr">
    <Event Name="STORY_GALACTIC_TURN" LuaScript="Galactic.lua" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Data/XML/Story/IDTest.xml");
        report.Plots[0].PlotId.Should().Be("uppercase_id_attr");
    }

    [Fact]
    public void Extract_ShouldHandleEventWithNoScriptAttributes()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_SPACE_TACTICAL" Reward="SPACE_REWARD" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Data/XML/Test.xml");
        report.Plots[0].Events[0].ScriptReference.Should().BeNull();
    }

    [Fact]
    public void Extract_ShouldIncludeAllNonNameAttributes()
    {
        const string xml = """
<Story>
  <Plot Name="Campaign">
    <Event Name="STORY_GALACTIC_TURN" LuaScript="G.lua" Reward="R" CustomAttr="C" />
  </Plot>
</Story>
""";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "Test.xml");
        var evt = report.Plots[0].Events[0];
        evt.Attributes.Should().ContainKey("LuaScript");
        evt.Attributes.Should().ContainKey("Reward");
        evt.Attributes.Should().ContainKey("CustomAttr");
        evt.Attributes.Should().NotContainKey("Name");
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldHandleCapabilityWithNullFeatureId()
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
    { "featureId": null, "available": true, "state": "Verified", "reasonCode": "OK" },
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
        linkage.Links.Should().Contain(x => x.FeatureId == "set_credits" && x.Available);
    }

    [Fact]
    public void BuildCapabilityLinkReport_ShouldSortBySourceFileThenPlotIdThenEventName()
    {
        const string xml = """
<Story>
  <Plot Name="B_Plot">
    <Event Name="STORY_GALACTIC_TURN" LuaScript="G.lua" />
  </Plot>
  <Plot Name="A_Plot">
    <Event Name="STORY_SPACE_TACTICAL" LuaScript="S.lua" />
  </Plot>
</Story>
""";
        const string symbolPack = """
{
  "capabilities": [
    { "featureId": "set_credits", "available": true, "state": "Verified", "reasonCode": "OK" },
    { "featureId": "toggle_ai", "available": true, "state": "Verified", "reasonCode": "OK" },
    { "featureId": "freeze_timer", "available": true, "state": "OK", "reasonCode": "OK" },
    { "featureId": "toggle_fog_reveal", "available": true, "state": "OK", "reasonCode": "OK" },
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
        linkage.Links.Should().NotBeEmpty();
        // A_Plot events should come before B_Plot events (sorted by plotId)
        var plotIds = linkage.Links.Select(x => x.PlotId).ToList();
        var firstAIndex = plotIds.IndexOf("A_Plot");
        var firstBIndex = plotIds.IndexOf("B_Plot");
        firstAIndex.Should().BeLessThan(firstBIndex);
    }

    [Fact]
    public void Extract_ShouldHandleXmlWithOnlyRootElement()
    {
        const string xml = "<Root />";
        var extractor = new StoryPlotFlowExtractor();
        var report = extractor.Extract(xml, "bare.xml");
        report.Plots.Should().ContainSingle();
        report.Plots[0].Events.Should().BeEmpty();
    }

    #endregion

    #region LuaHarnessRunner — remaining branches

    [Fact]
    public async Task RunAsync_ShouldFail_WhenScriptPathIsNull()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Join(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var runner = new LuaHarnessRunner(harnessScript);

        var result = await runner.RunAsync(new LuaHarnessRunRequest(null!));
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("lua_script_missing");
    }

    [Fact]
    public async Task RunAsync_ShouldSucceed_WhenBothMarkersPresent()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Join(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var tempDir = Path.Join(Path.GetTempPath(), $"swfoc-lua-w2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempLua = Path.Join(tempDir, "both_markers.lua");
            await File.WriteAllTextAsync(tempLua,
                "-- SWFOC_TRAINER_TELEMETRY marker\nfunction SwfocTrainer_Emit_Telemetry_Mode() end");
            var runner = new LuaHarnessRunner(harnessScript);

            var result = await runner.RunAsync(new LuaHarnessRunRequest(tempLua, Mode: "Galactic"), CancellationToken.None);
            result.Succeeded.Should().BeTrue();
            result.ReasonCode.Should().Be("ok");
            result.OutputLines.Should().Contain(x => x.Contains("mode=Galactic"));
            result.OutputLines.Should().Contain(x => x.Contains("runner="));
            result.OutputLines.Should().Contain(x => x.Contains("script="));
            result.OutputLines.Should().Contain(x => x.Contains("emitted="));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task RunAsync_ShouldFail_WhenScriptHasEmitterButNoMarker()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Join(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var tempDir = Path.Join(Path.GetTempPath(), $"swfoc-lua-w2-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var tempLua = Path.Join(tempDir, "emitter_only.lua");
            await File.WriteAllTextAsync(tempLua,
                "function SwfocTrainer_Emit_Telemetry_Mode() end\n-- no marker line");
            var runner = new LuaHarnessRunner(harnessScript);

            var result = await runner.RunAsync(new LuaHarnessRunRequest(tempLua));
            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be("telemetry_marker_missing");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    #endregion
}
