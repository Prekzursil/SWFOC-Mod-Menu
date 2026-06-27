using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Flow.Models;
using SwfocTrainer.Flow.Services;
using Xunit;

namespace SwfocTrainer.Tests.Flow;

public sealed class FlowWave11CoverageTests
{
    // ── FlowModels.cs L62: StoryFlowGraphNode constructor ──
    [Fact]
    public void StoryFlowGraphNode_Constructor_ShouldSetAllProperties()
    {
        var features = new[] { "feature_a", "feature_b" };
        var node = new StoryFlowGraphNode(
            NodeId: "node_1",
            PlotId: "plot_1",
            EventName: "OnEnter",
            ModeHint: FlowModeHint.Galactic,
            SourceFile: "plots/galactic.xml",
            ScriptReference: "scripts/galactic.lua",
            ExpectedFeatureIds: features);

        node.NodeId.Should().Be("node_1");
        node.PlotId.Should().Be("plot_1");
        node.EventName.Should().Be("OnEnter");
        node.ModeHint.Should().Be(FlowModeHint.Galactic);
        node.SourceFile.Should().Be("plots/galactic.xml");
        node.ScriptReference.Should().Be("scripts/galactic.lua");
        node.ExpectedFeatureIds.Should().HaveCount(2);
    }

    [Fact]
    public void StoryFlowGraphNode_NullScriptReference_ShouldBeAllowed()
    {
        var node = new StoryFlowGraphNode(
            NodeId: "node_2",
            PlotId: "plot_2",
            EventName: "OnExit",
            ModeHint: FlowModeHint.TacticalSpace,
            SourceFile: "plots/space.xml",
            ScriptReference: null,
            ExpectedFeatureIds: Array.Empty<string>());

        node.ScriptReference.Should().BeNull();
        node.ExpectedFeatureIds.Should().BeEmpty();
    }

    // ── StoryFlowGraphEdge constructor ──
    [Fact]
    public void StoryFlowGraphEdge_Constructor_ShouldSetAllProperties()
    {
        var edge = new StoryFlowGraphEdge(
            FromNodeId: "n1",
            ToNodeId: "n2",
            EdgeType: "triggers",
            Reason: "event sequence");

        edge.FromNodeId.Should().Be("n1");
        edge.ToNodeId.Should().Be("n2");
        edge.EdgeType.Should().Be("triggers");
        edge.Reason.Should().Be("event sequence");
    }

    // ── StoryFlowGraphReport constructor and Empty ──
    [Fact]
    public void StoryFlowGraphReport_Empty_ShouldHaveDefaults()
    {
        StoryFlowGraphReport.Empty.Nodes.Should().BeEmpty();
        StoryFlowGraphReport.Empty.Edges.Should().BeEmpty();
        StoryFlowGraphReport.Empty.Diagnostics.Should().BeEmpty();
    }

    // ── LuaHarnessRunner L78/L89: ResolveDefaultHarnessScriptPath ──
    // The while loop walks parent directories; when no match is found,
    // current becomes null and the fallback on L89 is reached.
    // The existing test covers that it returns a path. We verify
    // both the loop exhaustion (current becomes null) and fallback path.
    [Fact]
    public void ResolveDefaultHarnessScriptPath_Fallback_EndsWithExpectedFileName()
    {
        var method = typeof(LuaHarnessRunner).GetMethod(
            "ResolveDefaultHarnessScriptPath",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var result = (string)method!.Invoke(null, null)!;
        result.Should().NotBeNullOrWhiteSpace();
        // The fallback path always ends with run-lua-harness.ps1
        result.Should().EndWith("run-lua-harness.ps1");
    }

    // ── LuaHarnessRunner: parameterless constructor (exercises ResolveDefaultHarnessScriptPath) ──
    [Fact]
    public void LuaHarnessRunner_DefaultConstructor_ShouldNotThrow()
    {
        var runner = new LuaHarnessRunner();
        runner.Should().NotBeNull();
    }

    // ── LuaHarnessRunner: RunAsync with non-existent harness script ──
    [Fact]
    public async Task RunAsync_HarnessScriptMissing_ShouldFail()
    {
        // Use a known non-existent path for the harness script
        var runner = new LuaHarnessRunner(Path.Join(Path.GetTempPath(), $"missing_{Guid.NewGuid():N}.ps1"));

        // Create a real script file that the runner can check
        var scriptPath = Path.Join(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.lua");
        try
        {
            await File.WriteAllTextAsync(scriptPath, "-- SWFOC_TRAINER_TELEMETRY\n-- SwfocTrainer_Emit_Telemetry_Mode");
            var request = new LuaHarnessRunRequest(ScriptPath: scriptPath, Mode: "Galactic");
            var result = await runner.RunAsync(request, CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be("harness_runner_missing");
        }
        finally
        {
            if (File.Exists(scriptPath)) File.Delete(scriptPath);
        }
    }

    // ── StoryPlotFlowExtractor L243: partial branch ──
    // The condition is: payload?.Capabilities is null || payload.Capabilities.Count == 0
    // We need to exercise all three sub-branches:
    // 1. payload is null (deserialized to null)
    // 2. payload.Capabilities is null
    // 3. payload.Capabilities.Count == 0 (already tested in wave10)
    [Fact]
    public void TryParseCapabilities_PayloadDeserializesToNull_ReturnsFalse()
    {
        var method = typeof(StoryPlotFlowExtractor).GetMethod(
            "TryParseCapabilities",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        // JSON "null" will deserialize SymbolPackDto to null
        var args = new object?[] { "null", null, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void TryParseCapabilities_CapabilitiesPropertyNull_ReturnsFalse()
    {
        var method = typeof(StoryPlotFlowExtractor).GetMethod(
            "TryParseCapabilities",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        // JSON with capabilities explicitly set to null
        var json = "{\"capabilities\":null}";
        var args = new object?[] { json, null, null };
        var result = (bool)method!.Invoke(null, args)!;
        result.Should().BeFalse();
    }
}
