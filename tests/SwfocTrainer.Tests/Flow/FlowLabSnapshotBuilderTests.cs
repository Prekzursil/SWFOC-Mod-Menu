using FluentAssertions;
using SwfocTrainer.DataIndex.Models;
using SwfocTrainer.Flow.Models;
using SwfocTrainer.Flow.Services;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

public sealed class FlowLabSnapshotBuilderTests
{
    [Fact]
    public void Build_ShouldAggregateModeCountsScriptsAndMegaLoadOrder()
    {
        var flowReport = new FlowIndexReport(
            [
                new FlowPlotRecord(
                    "Campaign",
                    "Data/XML/Story/Campaign.xml",
                    [
                        new FlowEventRecord("STORY_GALACTIC_TURN", FlowModeHint.Galactic, "Campaign.xml", "Story/Galactic.lua", new Dictionary<string, string>()),
                        new FlowEventRecord("STORY_LAND_TACTICAL", FlowModeHint.TacticalLand, "Campaign.xml", "Story/Land.lua", new Dictionary<string, string>()),
                        new FlowEventRecord("STORY_SPACE_TACTICAL", FlowModeHint.TacticalSpace, "Campaign.xml", "Story/Space.lua", new Dictionary<string, string>())
                    ])
            ],
            []);

        var megaIndex = new MegaFilesIndex(
            [
                new MegaFileEntry("Config.meg", 0, true, new Dictionary<string, string>()),
                new MegaFileEntry("AOTR.meg", 1, false, new Dictionary<string, string>()),
                new MegaFileEntry("Campaign.meg", 2, true, new Dictionary<string, string>())
            ],
            []);

        var builder = new FlowLabSnapshotBuilder();

        var snapshot = builder.Build(flowReport, megaIndex);

        snapshot.ModeCounts.Should().ContainSingle(x => x.Mode == FlowModeHint.Galactic && x.Count == 1);
        snapshot.ModeCounts.Should().ContainSingle(x => x.Mode == FlowModeHint.TacticalLand && x.Count == 1);
        snapshot.ModeCounts.Should().ContainSingle(x => x.Mode == FlowModeHint.TacticalSpace && x.Count == 1);
        snapshot.ScriptReferences.Should().ContainInOrder("Story/Galactic.lua", "Story/Land.lua", "Story/Space.lua");
        snapshot.MegaLoadOrder.Should().ContainInOrder("Config.meg", "Campaign.meg");
        snapshot.Diagnostics.Should().BeEmpty();
    }

    [Fact]
    public void Build_ShouldMergeDiagnosticsAndIgnoreEmptyScriptReferences()
    {
        var flowReport = new FlowIndexReport(
            [
                new FlowPlotRecord(
                    "Skirmish",
                    "Data/XML/Story/Skirmish.xml",
                    [
                        new FlowEventRecord("STORY_SPACE_TACTICAL", FlowModeHint.TacticalSpace, "Skirmish.xml", null, new Dictionary<string, string>()),
                        new FlowEventRecord("STORY_SPACE_TACTICAL", FlowModeHint.TacticalSpace, "Skirmish.xml", " ", new Dictionary<string, string>())
                    ])
            ],
            ["flow-warning"]);

        var megaIndex = new MegaFilesIndex([], ["meg-warning"]);
        var builder = new FlowLabSnapshotBuilder();

        var snapshot = builder.Build(flowReport, megaIndex);

        snapshot.ScriptReferences.Should().BeEmpty();
        snapshot.ModeCounts.Should().ContainSingle(x => x.Mode == FlowModeHint.TacticalSpace && x.Count == 2);
        snapshot.Diagnostics.Should().ContainInOrder("flow-warning", "meg-warning");
    }

    [Fact]
    public void Build_ShouldDeduplicateTrimAndSortScriptReferences_CaseInsensitive()
    {
        var flowReport = new FlowIndexReport(
            [
                new FlowPlotRecord(
                    "Campaign",
                    "Data/XML/Story/Campaign.xml",
                    [
                        new FlowEventRecord("STORY_GALACTIC_TURN", FlowModeHint.Galactic, "Campaign.xml", " story/a.lua ", new Dictionary<string, string>()),
                        new FlowEventRecord("STORY_GALACTIC_TURN", FlowModeHint.Galactic, "Campaign.xml", "Story/A.lua", new Dictionary<string, string>()),
                        new FlowEventRecord("STORY_SPACE_TACTICAL", FlowModeHint.TacticalSpace, "Campaign.xml", "Story/B.lua", new Dictionary<string, string>())
                    ])
            ],
            []);

        var megaIndex = new MegaFilesIndex(
            [
                new MegaFileEntry("Zulu.meg", 3, true, new Dictionary<string, string>()),
                new MegaFileEntry("Alpha.meg", 1, true, new Dictionary<string, string>()),
                new MegaFileEntry("Hidden.meg", 2, false, new Dictionary<string, string>())
            ],
            []);

        var snapshot = new FlowLabSnapshotBuilder().Build(flowReport, megaIndex);

        snapshot.ScriptReferences.Should().ContainInOrder("story/a.lua", "Story/B.lua");
        snapshot.ScriptReferences.Should().HaveCount(2);
        snapshot.MegaLoadOrder.Should().ContainInOrder("Alpha.meg", "Zulu.meg");
    }
}
