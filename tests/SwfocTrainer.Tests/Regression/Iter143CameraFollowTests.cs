using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 143) — pins the SWFOC_CameraFollow LIVE wire.
///
/// Iter 106 finding pinned Camera_To_Follow at LuaUserVar registry slot
/// 0x140898d70 (engine Lua API). Iter 107 wired the sibling
/// Scroll_Camera_To primitive via DoString. Iter 143 closes the camera
/// pair: SWFOC_CameraFollow composes "Camera_To_Follow(EXPR)" and
/// dispatches via DoString — same proven engine-Lua-API + DoString
/// pattern as iter 100/107/108/109/110/111/112/113/133.
///
/// Operator-facing impact: complement to ScrollCameraToTarget. Where
/// Scroll_Camera_To pans once to a target, Camera_To_Follow attaches
/// the camera so it tracks the target as it moves.
///
/// 24th LIVE flip in master loop (was 23 from iter 100-136).
/// </summary>
public sealed class Iter143CameraFollowTests
{
    [Fact]
    public void CameraFollow_StatusIsLive()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_CameraFollow"];
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 143 ships LIVE via Camera_To_Follow Lua API + DoString — mirror of iter 107 ScrollCameraToTarget pattern");
    }

    [Fact]
    public void CameraFollow_NoteCitesEngineLuaApi()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_CameraFollow"];
        entry.Note.Should().Contain("Camera_To_Follow",
            "operator-trust note must name the engine Lua function being dispatched");
        entry.Note.Should().Contain("0x140898d70",
            "operator-trust note must cite the LuaUserVar registry slot iter 106 pinned");
        entry.Note.Should().Contain("DoString",
            "the dispatch mechanism (DoString) must be plain in the catalog note");
    }

    [Fact]
    public void CameraFollow_NoteReferencesIter107SiblingPattern()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_CameraFollow"];
        entry.Note.Should().Contain("Scroll_Camera_To",
            "operator should know about the sibling pan primitive (iter 107)");
    }

    [Fact]
    public void CameraFollow_ComposedBadgeReportsLive()
    {
        var badge = CapabilityStatusCatalog.ComposeBadge("SWFOC_CameraFollow");
        badge.Should().Be("LIVE");
    }

    [Fact]
    public async System.Threading.Tasks.Task CameraFollow_DispatchedExpressionRoundTrips()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        // Use iter 107's wire-format convention: outer SINGLE quotes
        // around the target expression, inner DOUBLE quotes for any
        // Lua string literals. The simulator's existing string regex
        // doesn't honour escape sequences but accepts this shape
        // cleanly.
        var resp = await adapter.SendRawAsync(
            "return SWFOC_CameraFollow('Find_First_Object(\"Empire_AT_AT\")')",
            System.Threading.CancellationToken.None);

        resp.Succeeded.Should().BeTrue();
        resp.Response.Should().Contain("OK").And.Contain("LIVE");
        sim.GameState.LastCameraFollowTarget
            .Should().Be("Find_First_Object(\"Empire_AT_AT\")",
                "simulator captures the raw target expression for round-trip verification");
    }

    [Fact]
    public async System.Threading.Tasks.Task CameraFollow_PreservesLastTarget_AcrossCalls()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        await adapter.SendRawAsync(
            "return SWFOC_CameraFollow('Find_Player(\"REBEL\")')",
            System.Threading.CancellationToken.None);
        sim.GameState.LastCameraFollowTarget.Should().Contain("REBEL");

        await adapter.SendRawAsync(
            "return SWFOC_CameraFollow('Find_Player(\"EMPIRE\")')",
            System.Threading.CancellationToken.None);
        sim.GameState.LastCameraFollowTarget.Should().Contain("EMPIRE");
        sim.GameState.LastCameraFollowTarget.Should().NotContain("REBEL",
            "second call overrides — single-target follow semantics");
    }
}
