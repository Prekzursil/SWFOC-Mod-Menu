using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 165) — pins camera/cinematic complement LIVE batch.
/// Fade_Screen_Out complements iter-162 Fade_Screen_In, Rotate_Camera_By
/// complements iter-144 Rotate_Camera_To (relative vs absolute), and
/// Point_Camera_At completes the camera primitive arc. LIVE flips
/// #76-78. Master loop now at 78 LIVE wires.
/// </summary>
public sealed class Iter165CameraCinematicComplementBatchTests
{
    [Theory]
    [InlineData("SWFOC_FadeScreenOutLua")]
    [InlineData("SWFOC_RotateCameraByLua")]
    [InlineData("SWFOC_PointCameraAtLua")]
    public void CameraComplementBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void FadeScreenOut_NoteContrastsWithIter162FadeScreenIn()
    {
        // Pin: catalog should pair the fade-in/fade-out relationship
        // for future readers.
        CapabilityStatusCatalog.Entries["SWFOC_FadeScreenOutLua"].Note
            .Should().Contain("Fade_Screen_In");
    }

    [Fact]
    public void RotateCameraBy_NoteContrastsWithIter144RotateCameraTo()
    {
        // Pin: relative-vs-absolute distinction must survive in the
        // catalog rationale.
        CapabilityStatusCatalog.Entries["SWFOC_RotateCameraByLua"].Note
            .Should().Contain("Rotate_Camera_To");
    }

    [Fact]
    public void PointCameraAt_NoteReferencesCinematicSection()
    {
        CapabilityStatusCatalog.Entries["SWFOC_PointCameraAtLua"].Note
            .Should().Contain("Cinematics");
    }

    [Fact]
    public void CameraComplementBatch_AllTaggedIter165()
    {
        var iter165Entries = new[]
        {
            "SWFOC_FadeScreenOutLua",
            "SWFOC_RotateCameraByLua",
            "SWFOC_PointCameraAtLua",
        };
        foreach (var name in iter165Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 165 LIVE",
                    $"{name} should be tagged as iter 165 LIVE in catalog rationale");
        }
    }
}
