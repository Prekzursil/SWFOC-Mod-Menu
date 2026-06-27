using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 192) — pins the Camera &amp; Debug tab native UX for the
/// iter 162/165 camera primitive write-side wires (Zoom_Camera,
/// Fade_Screen_Out, Rotate_Camera_By, Point_Camera_At). Fifth tab in the
/// iter-188-191 surfacing arc and FIRST write-side surfacing — pivots from
/// read-side coverage (UnitControl/PlayerState/Diagnostics/Inspector).
///
/// All 4 wires take a single string arg; bridge dispatches through the
/// iter-158 global-arg helper. Operator types one arg into the shared
/// CameraExtraArg field, then clicks any of the 4 buttons.
/// </summary>
public sealed class Iter192CameraPrimitiveExtrasNativeUxTests
{
    [Fact]
    public void DispatcherInterface_DefinesAllFourMethods()
    {
        // Pin: ICameraDebugDispatcher (Core interface) must define the 4
        // iter-192 methods. The default-impl returns Task.FromResult(false)
        // to keep older mocks compiling — so this assertion confirms the
        // method names are visible (compile-time guard).
        var iface = typeof(ICameraDebugDispatcher);
        iface.GetMethod(nameof(ICameraDebugDispatcher.ZoomCameraAsync))
            .Should().NotBeNull("Zoom (time) button binds to ZoomCameraAsync");
        iface.GetMethod(nameof(ICameraDebugDispatcher.FadeScreenOutAsync))
            .Should().NotBeNull("Fade out (time) button binds to FadeScreenOutAsync");
        iface.GetMethod(nameof(ICameraDebugDispatcher.RotateCameraByAsync))
            .Should().NotBeNull("Rotate by (deg) button binds to RotateCameraByAsync");
        iface.GetMethod(nameof(ICameraDebugDispatcher.PointCameraAtAsync))
            .Should().NotBeNull("Point at (target) button binds to PointCameraAtAsync");
    }

    [Fact]
    public void CatalogAction_AllFourEntriesAreLive()
    {
        // Pin: all 4 SWFOC_* names referenced by the iter-192 buttons must
        // resolve to LIVE catalog entries. If any of these flips to
        // Phase2HookPending or Unavailable the operator-trust badge under
        // each button will reflect it; the test catches an inconsistency.
        var swfocNames = new[]
        {
            "SWFOC_ZoomCameraLua",
            "SWFOC_FadeScreenOutLua",
            "SWFOC_RotateCameraByLua",
            "SWFOC_PointCameraAtLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-192 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_DocumentsIter192Surfacing()
    {
        // Pin: iter-192 added native UX surfacing language. Future cleanups
        // must keep the iter-192 marker so operators reading the surface
        // report can identify the surfacing iter.
        var zoom = CapabilityStatusCatalog.Entries["SWFOC_ZoomCameraLua"].Note;
        var fade = CapabilityStatusCatalog.Entries["SWFOC_FadeScreenOutLua"].Note;
        var rotateBy = CapabilityStatusCatalog.Entries["SWFOC_RotateCameraByLua"].Note;
        var pointAt = CapabilityStatusCatalog.Entries["SWFOC_PointCameraAtLua"].Note;

        zoom.Should().Contain("Iter 192");
        fade.Should().Contain("Iter 192");
        rotateBy.Should().Contain("Iter 192");
        pointAt.Should().Contain("Iter 192");
    }

    [Fact]
    public void CatalogRationale_RotateByIsRelativeNotAbsolute()
    {
        // Pin: Rotate_Camera_By(degrees) is the RELATIVE rotation primitive
        // (vs iter-144 Rotate_Camera_To which is ABSOLUTE-target). The
        // catalog rationale must document this distinction so operators
        // don't confuse the two iter-144/iter-165 pairings.
        var note = CapabilityStatusCatalog.Entries["SWFOC_RotateCameraByLua"].Note;
        note.Should().Contain("relative");
        note.Should().Contain("absolute");
        note.Should().Contain("iter-144");
    }
}
