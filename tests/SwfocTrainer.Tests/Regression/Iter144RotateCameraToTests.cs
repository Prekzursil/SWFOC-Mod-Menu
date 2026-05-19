using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 144) — pins the SWFOC_RotateCameraTo LIVE wire.
/// Closes the camera primitive trio iter 107/143/144:
///   - iter 107 Scroll_Camera_To: pan once to a target
///   - iter 143 Camera_To_Follow: track target as it moves
///   - iter 144 Rotate_Camera_To: rotate camera to face target
///
/// All three reuse the engine-Lua-API + DoString pattern verbatim;
/// each LIVE wire ships ~50 LoC end-to-end (bridge + simulator + tests).
/// 25th LIVE flip in master loop.
/// </summary>
public sealed class Iter144RotateCameraToTests
{
    [Fact]
    public void RotateCameraTo_StatusIsLive()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_RotateCameraTo"];
        entry.Status.Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void RotateCameraTo_NoteCitesEngineLuaApiAndRva()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_RotateCameraTo"];
        entry.Note.Should().Contain("Rotate_Camera_To");
        entry.Note.Should().Contain("0x140898db0");
        entry.Note.Should().Contain("DoString");
    }

    [Fact]
    public void RotateCameraTo_NoteReferencesCameraTrio()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_RotateCameraTo"];
        entry.Note.Should().Contain("iter 107");
        entry.Note.Should().Contain("iter 143");
    }

    [Fact]
    public void RotateCameraTo_ComposedBadgeReportsLive()
    {
        CapabilityStatusCatalog.ComposeBadge("SWFOC_RotateCameraTo")
            .Should().Be("LIVE");
    }

    [Fact]
    public async System.Threading.Tasks.Task RotateCameraTo_DispatchedExpressionRoundTrips()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        var resp = await adapter.SendRawAsync(
            "return SWFOC_RotateCameraTo('Find_First_Object(\"Empire_AT_AT\")')",
            System.Threading.CancellationToken.None);

        resp.Succeeded.Should().BeTrue();
        resp.Response.Should().Contain("OK").And.Contain("LIVE");
        sim.GameState.LastRotateCameraToTarget
            .Should().Be("Find_First_Object(\"Empire_AT_AT\")");
    }

    [Fact]
    public void CameraTrio_AllLive_InvariantPin()
    {
        // Iter 107 + 143 + 144 together: the camera primitive trio must
        // remain LIVE as a unit. If any one regresses to PHASE 2 PENDING
        // this assertion fires and surfaces the drift across the trio.
        CapabilityStatusCatalog.Entries["SWFOC_ScrollCameraToTarget"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_CameraFollow"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_RotateCameraTo"].Status
            .Should().Be(CapabilityStatus.Live);
    }
}
