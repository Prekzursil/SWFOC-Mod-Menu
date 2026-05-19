using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 145) — pins the cinematic camera quad LIVE wires.
/// LIVE flips #26-29:
///   - SWFOC_StartCinematicCamera @ engine slot 0x140898ec0
///   - SWFOC_EndCinematicCamera @ 0x140898ed8
///   - SWFOC_SetCinematicCameraKey @ 0x140898f30
///   - SWFOC_TransitionCinematicCameraKey @ 0x140898f50
///
/// Forms a state machine: Start mode -- SetKey N times -- Transition --
/// End mode. Same iter-107 engine-Lua-API + DoString skeleton verbatim.
/// </summary>
public sealed class Iter145CinematicCameraQuadTests
{
    [Fact]
    public void Quad_AllFour_StatusIsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_StartCinematicCamera"].Status.Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_EndCinematicCamera"].Status.Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_SetCinematicCameraKey"].Status.Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_TransitionCinematicCameraKey"].Status.Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void Quad_NotesEnumerateEngineRvas()
    {
        CapabilityStatusCatalog.Entries["SWFOC_StartCinematicCamera"].Note.Should().Contain("0x140898ec0");
        CapabilityStatusCatalog.Entries["SWFOC_EndCinematicCamera"].Note.Should().Contain("0x140898ed8");
        CapabilityStatusCatalog.Entries["SWFOC_SetCinematicCameraKey"].Note.Should().Contain("0x140898f30");
        CapabilityStatusCatalog.Entries["SWFOC_TransitionCinematicCameraKey"].Note.Should().Contain("0x140898f50");
    }

    [Fact]
    public async System.Threading.Tasks.Task StateMachine_StartSetKeyTransitionEnd_RoundTrips()
    {
        using var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);

        // Start cinematic mode
        var startResp = await adapter.SendRawAsync(
            "return SWFOC_StartCinematicCamera()", System.Threading.CancellationToken.None);
        startResp.Succeeded.Should().BeTrue();
        startResp.Response.Should().Contain("OK");
        sim.GameState.CinematicCameraActive.Should().BeTrue();

        // Set a keyframe
        var setKeyResp = await adapter.SendRawAsync(
            "return SWFOC_SetCinematicCameraKey('1, Find_Planet(\"Yavin\"), 5.0')",
            System.Threading.CancellationToken.None);
        setKeyResp.Succeeded.Should().BeTrue();
        sim.GameState.LastCinematicCameraKeyArgs.Should().Contain("Yavin");

        // Transition between keys
        var transitionResp = await adapter.SendRawAsync(
            "return SWFOC_TransitionCinematicCameraKey('1, 2, 2.5')",
            System.Threading.CancellationToken.None);
        transitionResp.Succeeded.Should().BeTrue();
        sim.GameState.LastCinematicCameraTransitionArgs.Should().Contain("2.5");

        // End cinematic mode
        var endResp = await adapter.SendRawAsync(
            "return SWFOC_EndCinematicCamera()", System.Threading.CancellationToken.None);
        endResp.Succeeded.Should().BeTrue();
        sim.GameState.CinematicCameraActive.Should().BeFalse();
    }

    [Fact]
    public void CinematicQuad_ComposedBadge_AllLive()
    {
        var badge = CapabilityStatusCatalog.ComposeBadge(
            "SWFOC_StartCinematicCamera",
            "SWFOC_EndCinematicCamera",
            "SWFOC_SetCinematicCameraKey",
            "SWFOC_TransitionCinematicCameraKey");
        badge.Should().Be("LIVE",
            "all 4 cinematic camera primitives must compose to a uniform LIVE badge");
    }
}
