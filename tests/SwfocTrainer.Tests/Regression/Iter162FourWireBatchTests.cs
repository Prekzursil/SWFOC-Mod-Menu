using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 162) — pins 4-wire LIVE batch demonstrating
/// dispatcher-set reuse across receiver shapes (1 unit method + 3
/// globals) for binary-confirmed Lua API entries from docs/lua-api.md
/// sections 1+2. LIVE flips #66-69. Master loop now at 69 LIVE wires.
/// </summary>
public sealed class Iter162FourWireBatchTests
{
    [Theory]
    [InlineData("SWFOC_OverrideMaxSpeedLua")]
    [InlineData("SWFOC_SuspendAiLua")]
    [InlineData("SWFOC_FadeScreenInLua")]
    [InlineData("SWFOC_ZoomCameraLua")]
    public void FourWireBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void OverrideMaxSpeed_NoteContrastsWithIter100SetUnitSpeed()
    {
        // Pin: catalog should explain the relationship to iter-100's
        // engine-helper-based SetUnitSpeed so future readers don't
        // think the two are duplicates.
        CapabilityStatusCatalog.Entries["SWFOC_OverrideMaxSpeedLua"].Note
            .Should().Contain("SetUnitSpeed");
    }

    [Fact]
    public void SuspendAi_NoteIdentifiesAsCinematicHelper()
    {
        CapabilityStatusCatalog.Entries["SWFOC_SuspendAiLua"].Note
            .Should().Contain("cinematic");
    }

    [Fact]
    public void ZoomCamera_NoteReferencesCameraArcContext()
    {
        // Pin: catalog should connect this back to the camera arc
        // shipped iter-107 + iter-143-145.
        CapabilityStatusCatalog.Entries["SWFOC_ZoomCameraLua"].Note
            .Should().Contain("camera primitive arc");
    }

    [Fact]
    public void FourWireBatch_AllTaggedIter162()
    {
        var iter162Entries = new[]
        {
            "SWFOC_OverrideMaxSpeedLua",
            "SWFOC_SuspendAiLua",
            "SWFOC_FadeScreenInLua",
            "SWFOC_ZoomCameraLua",
        };
        foreach (var name in iter162Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 162 LIVE",
                    $"{name} should be tagged as iter 162 LIVE in catalog rationale");
        }
    }
}
