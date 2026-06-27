using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// 2026-04-27 (iter 58) — pins per-button capability metadata on the
/// Camera &amp; Debug tab. FreeCam + SetCameraPos are PHASE 2 PENDING;
/// SetCameraZoom + SubmitRaw use SWFOC_DoString (LIVE — escape hatch).
/// </summary>
public sealed class CameraDebugTabViewModelCapabilityTests
{
    private static CameraDebugTabViewModel NewVm(out SwfocSimulator sim)
    {
        sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return new CameraDebugTabViewModel(adapter);
    }

    [Fact]
    public void ToggleFreeCam_BadgeIsPhase2Pending()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ToggleFreeCam.Badge.Should().Be("PHASE 2 PENDING");
    }

    [Fact]
    public void SetCameraPos_BadgeIsLive()
    {
        // 2026-05-06 (iter 237): flipped Phase2HookPending → Live via direct
        // call to CameraClass::SetTransformMatrix @ 0x261BD0. Pattern parallels
        // iter-100 SetSpeedOverride. Tactical-only (galactic returns ERR).
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SetCameraPos.Badge.Should().Be("LIVE",
            "iter 237 wired SetCameraPos via SetTransformMatrix direct call");
    }

    [Fact]
    public void GetCameraPos_BadgeIsLive()
    {
        // 2026-05-06 (iter 237/239): NEW LIVE pair-flip with iter-237 SetCameraPos.
        // Direct call to CameraClass::GetPosition @ 0x261A40.
        var vm = NewVm(out var sim); using var _ = sim;
        vm.GetCameraPos.Badge.Should().Be("LIVE",
            "iter 237 wired GetCameraPos via GetPosition direct call");
    }

    [Fact]
    public void SetCameraZoom_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SetCameraZoom.Badge.Should().Be("LIVE",
            "Camera_Set_Zoom routes via SWFOC_DoString — escape hatch is LIVE");
    }

    [Fact]
    public void SubmitRaw_BadgeIsLive()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.SubmitRaw.Badge.Should().Be("LIVE",
            "Raw Lua escape hatch is the catalogued LIVE entry");
    }

    [Fact]
    public void HasPhase2PendingAction_TrueForCameraDebugTab()
    {
        var vm = NewVm(out var sim); using var _ = sim;
        vm.HasPhase2PendingAction.Should().BeTrue();
    }

    [Fact]
    public void Phase2PendingWarning_OnlyContainsFreeCam()
    {
        // 2026-05-06 (iter 237): SetCameraPos flipped to LIVE via direct call.
        // FreeCam stays Phase2HookPending — no Free_Cam(enable) Lua API exists
        // (per iter-106/107 RE — would need Lua-side scripted-behaviour mimic).
        var vm = NewVm(out var sim); using var _ = sim;
        var warning = vm.Phase2PendingWarning;
        warning.Should().Contain("Toggle free cam");
        warning.Should().NotContain("Set camera pos",
            "iter 237 flipped SetCameraPos to LIVE; should not appear in Phase2 warning");
        warning.Should().NotContain("Set camera zoom");
        warning.Should().NotContain("Submit raw");
    }

    [Fact]
    public void AllActions_EnumeratesEveryActionInDeclaredOrder()
    {
        // 2026-04-28 (iter 107): added ScrollCameraToTarget — 5 actions total.
        // 2026-04-29 (iter 148): added camera arc 6 actions — 11 total.
        // 2026-05-05 (iter 192): added 4 camera primitives (Zoom/FadeOut/
        // RotateBy/PointAt) — 15 total.
        // 2026-05-06 (iter 239): added GetCameraPos LIVE pair-flip with
        // iter-237 SetCameraPos — 16 total.
        var vm = NewVm(out var sim); using var _ = sim;
        vm.AllActions.Should().HaveCount(16);
        vm.AllActions[0].Should().BeSameAs(vm.ToggleFreeCam);
        vm.AllActions[1].Should().BeSameAs(vm.SetCameraPos);
        vm.AllActions[2].Should().BeSameAs(vm.ScrollCameraToTarget);
        vm.AllActions[3].Should().BeSameAs(vm.SetCameraZoom);
        vm.AllActions[4].Should().BeSameAs(vm.SubmitRaw);
        vm.AllActions[5].Should().BeSameAs(vm.CameraFollow);
        vm.AllActions[6].Should().BeSameAs(vm.RotateCameraTo);
        vm.AllActions[7].Should().BeSameAs(vm.StartCinematicCamera);
        vm.AllActions[8].Should().BeSameAs(vm.EndCinematicCamera);
        vm.AllActions[9].Should().BeSameAs(vm.SetCinematicCameraKey);
        vm.AllActions[10].Should().BeSameAs(vm.TransitionCinematicCameraKey);
        vm.AllActions[11].Should().BeSameAs(vm.ZoomCamera);
        vm.AllActions[12].Should().BeSameAs(vm.FadeScreenOut);
        vm.AllActions[13].Should().BeSameAs(vm.RotateCameraBy);
        vm.AllActions[14].Should().BeSameAs(vm.PointCameraAt);
        // iter 239: NEW LIVE pair-flip with iter-107/237 SetCameraPos.
        vm.AllActions[15].Should().BeSameAs(vm.GetCameraPos);
    }

    [Fact]
    public void CameraArc_AllSixBadgesAreLive()
    {
        // iter 148 — 6 camera arc actions all LIVE (iter 143/144/145 wires).
        var vm = NewVm(out var sim); using var _ = sim;
        vm.CameraFollow.Badge.Should().Be("LIVE");
        vm.RotateCameraTo.Badge.Should().Be("LIVE");
        vm.StartCinematicCamera.Badge.Should().Be("LIVE");
        vm.EndCinematicCamera.Badge.Should().Be("LIVE");
        vm.SetCinematicCameraKey.Badge.Should().Be("LIVE");
        vm.TransitionCinematicCameraKey.Badge.Should().Be("LIVE");
    }

    [Fact]
    public void ScrollCameraToTarget_BadgeIsLive()
    {
        // Iter 107: LIVE via engine's Scroll_Camera_To Lua API.
        var vm = NewVm(out var sim); using var _ = sim;
        vm.ScrollCameraToTarget.Badge.Should().Be("LIVE",
            "iter 107 wired Scroll_Camera_To via DoString — LIVE");
    }
}
