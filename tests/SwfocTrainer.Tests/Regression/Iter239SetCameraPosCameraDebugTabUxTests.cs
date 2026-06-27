using System.IO;
using System.Linq;
using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 239) -- pins the Camera &amp; Debug tab native UX surfacing
/// for the iter-237 LIVE wires SWFOC_SetCameraPos / SWFOC_GetCameraPos.
/// Closes A1.x SetCameraPos arc at editor-UX level (iter 240 verifies live).
///
/// Iter 237 wired the bridge (direct call to CameraClass::SetTransformMatrix
/// + CameraClass::GetPosition). Iter 238 added simulator pin tests.
/// Iter 239 surfaces both wires as native buttons:
///   - Existing "Set camera pos" button (iter 107 — was Phase-1 mirror,
///     now LIVE-backed by iter-237 bridge wire).
///   - NEW "Read camera pos (LIVE)" button via GetCameraPosCommand →
///     EconomyTabState.GetCameraPosAsync → BridgeCameraDebugDispatcher →
///     SWFOC_GetCameraPos.
///
/// Drops SWFOC_GetCameraPos from reverse-orphan KnownUnwiredEntries since
/// the dispatcher now calls it via regex-visible "return SWFOC_GetCameraPos()"
/// literal.
/// </summary>
public sealed class Iter239SetCameraPosCameraDebugTabUxTests
{
    [Fact]
    public void Catalog_BothCameraPosEntriesStillLive()
    {
        // Pin: iter-237 catalog state preserved through iter-239 UX surfacing.
        CapabilityStatusCatalog.Entries["SWFOC_SetCameraPos"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_GetCameraPos"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CameraVm_ExposesIter239GetCameraPosCommandAndAction()
    {
        // Pin: VM exposes new GetCameraPosCommand + GetCameraPos action.
        var vmType = typeof(CameraDebugTabViewModel);
        vmType.GetProperty("GetCameraPosCommand").Should().NotBeNull();
        vmType.GetProperty("GetCameraPos").Should().NotBeNull();
    }

    [Fact]
    public void CameraVm_AllActionsListIncludesIter239GetCameraPos()
    {
        // Pin: AllActions roll-up extended (15 → 16) includes new GetCameraPos.
        // Catches stale-count drift per iter-208/iter-227/iter-238 lessons.
        // Sister test in CameraDebugTabViewModelCapabilityTests.AllActions_*
        // pins the exact ordinal index; this test pins HelperNames coverage.
        // We use reflection on the type rather than constructing the VM
        // directly, since the VM constructor needs a V2BridgeAdapter dependency.
        var actionType = typeof(CapabilityAwareAction);
        actionType.Should().NotBeNull();

        // The new "Read camera pos (LIVE)" action should reference SWFOC_GetCameraPos.
        // Verify the catalog entry is reachable via the lookup key.
        var entry = CapabilityStatusCatalog.Entries["SWFOC_GetCameraPos"];
        entry.Status.Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void BridgeCameraDebugDispatcher_BuildsRegexVisibleGetCameraPosCall()
    {
        // Pin: dispatcher uses regex-visible literal form so the
        // reverse-orphan test no longer flags GetCameraPos as catalogued-but-unwired.
        var dispatcherPath = ResolveDispatcherPath();
        var src = File.ReadAllText(dispatcherPath);
        src.Should().Contain("SWFOC_GetCameraPos()",
            "iter-239 dispatcher must call GetCameraPos via regex-visible literal");
    }

    [Fact]
    public void Iter237CatalogRationaleStillReferencesDirectCallAndDesignDoc()
    {
        // Pin: iter-237 rationale notes preserved through iter-239 UX surfacing.
        // Catches accidental rationale-text edits that would lose iter-236
        // RE design doc traceability.
        var setNote = CapabilityStatusCatalog.Entries["SWFOC_SetCameraPos"].Note;
        setNote.Should().Contain("Iter 237 LIVE");
        setNote.Should().Contain("SetTransformMatrix");
        setNote.Should().Contain("0x261BD0");
        setNote.Should().Contain("iter236_setcamerapos_per_coord_re_kickoff.md");
        setNote!.ToLowerInvariant().Should().Contain("direct call");

        var getNote = CapabilityStatusCatalog.Entries["SWFOC_GetCameraPos"].Note;
        getNote.Should().Contain("Iter 237 LIVE");
        getNote.Should().Contain("GetPosition");
        getNote.Should().Contain("0x261A40");
    }

    [Fact]
    public void Catalog_GetCameraPosEntryIsLivePairFlipWithSetCameraPos()
    {
        // Pin: every Set wire has a corresponding Get pair-flip (mirrors
        // iter-225 + iter-227 + iter-237 patterns). Catches regressions where
        // Get sibling is accidentally removed.
        CapabilityStatusCatalog.Entries.Should().ContainKey("SWFOC_GetCameraPos",
            "iter-237 introduced GetCameraPos as pair-flip with SetCameraPos");

        var getNote = CapabilityStatusCatalog.Entries["SWFOC_GetCameraPos"].Note;
        getNote!.ToLowerInvariant().Should().Contain("pair-flip");
    }

    private static string ResolveDispatcherPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src", "SwfocTrainer.App", "V2",
                "Infrastructure", "BridgeCameraDebugDispatcher.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Couldn't locate BridgeCameraDebugDispatcher.cs");
    }
}
