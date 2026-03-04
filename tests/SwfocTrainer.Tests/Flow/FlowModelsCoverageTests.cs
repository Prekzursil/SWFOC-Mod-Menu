using FluentAssertions;
using SwfocTrainer.Flow.Models;
using SwfocTrainer.Flow.Services;
using SwfocTrainer.Tests.Common;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

public sealed class FlowModelsCoverageTests
{
    [Fact]
    public void FlowIndexReport_GetAllEvents_ShouldFlattenPlots()
    {
        var report = new FlowIndexReport(
            Plots:
            [
                new FlowPlotRecord(
                    "plot_a",
                    "a.xml",
                    [
                        new FlowEventRecord("event1", FlowModeHint.Galactic, "a.xml", null, new Dictionary<string, string>()),
                        new FlowEventRecord("event2", FlowModeHint.TacticalLand, "a.xml", "script.lua", new Dictionary<string, string>())
                    ])
            ],
            Diagnostics: Array.Empty<string>());

        var events = report.GetAllEvents();

        events.Should().HaveCount(2);
        events[0].EventName.Should().Be("event1");
        events[1].EventName.Should().Be("event2");
    }

    [Fact]
    public void EmptyModelSingletons_ShouldExposeEmptyCollections()
    {
        FlowIndexReport.Empty.Plots.Should().BeEmpty();
        FlowCapabilityLinkReport.Empty.Links.Should().BeEmpty();
        StoryFlowGraphReport.Empty.Nodes.Should().BeEmpty();
        FlowLabSnapshot.Empty.ModeCounts.Should().BeEmpty();
    }

    [Fact]
    public void LuaHarnessModels_ShouldRetainConstructorData()
    {
        var request = new LuaHarnessRunRequest("script.lua");
        var result = new LuaHarnessRunResult(
            Succeeded: false,
            ReasonCode: "error",
            Message: "failure",
            OutputLines: new[] { "line1" },
            ArtifactPath: "artifact.json");

        request.Mode.Should().Be("TacticalLand");
        result.ArtifactPath.Should().Be("artifact.json");
        result.OutputLines.Should().ContainSingle().Which.Should().Be("line1");
    }

    [Fact]
    public async Task LuaHarnessRunner_ShouldFail_WhenHarnessRunnerScriptMissing()
    {
        var root = TestPaths.FindRepoRoot();
        var luaScriptPath = Path.Combine(Path.GetTempPath(), $"lua-{Guid.NewGuid():N}.lua");
        await File.WriteAllTextAsync(luaScriptPath, "SWFOC_TRAINER_TELEMETRY\nfunction SwfocTrainer_Emit_Telemetry_Mode() end");

        try
        {
            var runner = new LuaHarnessRunner(Path.Combine(root, "tools", "lua-harness", "missing-runner.ps1"));
            var result = await runner.RunAsync(new LuaHarnessRunRequest(luaScriptPath, "Galactic"));

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be("harness_runner_missing");
        }
        finally
        {
            File.Delete(luaScriptPath);
        }
    }

    [Fact]
    public async Task LuaHarnessRunner_ShouldFail_WhenTelemetryMarkersMissing()
    {
        var root = TestPaths.FindRepoRoot();
        var harnessScript = Path.Combine(root, "tools", "lua-harness", "run-lua-harness.ps1");
        var luaScriptPath = Path.Combine(Path.GetTempPath(), $"lua-{Guid.NewGuid():N}.lua");
        await File.WriteAllTextAsync(luaScriptPath, "print('hello')");

        try
        {
            var runner = new LuaHarnessRunner(harnessScript);
            var result = await runner.RunAsync(new LuaHarnessRunRequest(luaScriptPath, "TacticalSpace"));

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be("telemetry_marker_missing");
        }
        finally
        {
            File.Delete(luaScriptPath);
        }
    }
}
