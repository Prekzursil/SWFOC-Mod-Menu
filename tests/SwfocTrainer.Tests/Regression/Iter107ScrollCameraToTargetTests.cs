using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-28 (iter 107, master ralph loop) — pins the iter 107 LIVE wire
/// for <c>SWFOC_ScrollCameraToTarget</c>. The bridge wraps an arbitrary
/// Lua expression in <c>Scroll_Camera_To(&lt;expr&gt;)</c> and dispatches via
/// DoString into the engine's Lua-registered camera API (see iter 106
/// finding — LuaUserVar registry at <c>sub_140546c70</c> exposes
/// <c>Scroll_Camera_To</c>, <c>Camera_To_Follow</c>, etc.). Same pattern
/// as iter 100's SetSpeedOverride wire — engine API call routed through
/// existing primitives, no MinHook detour, no new RVA pin.
///
/// Test wire-format note: iter 107 uses SINGLE-quoted Lua strings to
/// wrap the target expression (e.g. <c>'Find_Planet("Yavin")'</c>) so
/// the simulator's existing string regex (which doesn't honour escape
/// sequences) captures the inner expression cleanly. The bridge accepts
/// either form because <c>SWFOC_DoString</c> parses Lua source — Lua
/// happily takes either single or double quotes around its string
/// literals. An earlier attempt to use escaped double-quotes (\"...\")
/// inside double-quoted args broke 10+ unrelated simulator tests when
/// the regex was extended to handle escapes; reverting and using single
/// quotes externally is the simpler honest path.
///
/// RED-GREEN pair:
///   RED   — <c>SWFOC_ScrollCameraToTarget</c> didn't exist before iter
///           107 (Camera tab's per-coord SetCameraPos was the only
///           PHASE 2 PENDING knob, and the engine doesn't accept raw
///           floats).
///   GREEN — iter 107 ships the LIVE wire that splices the caller's
///           Lua expression into Scroll_Camera_To and dispatches it.
///
/// The red-green discipline catches dispatcher-table regressions
/// (someone removes the Reg() line), wire-format regressions (bridge
/// ever changes the Lua splice), and catalog regressions (someone
/// flips it back to PHASE 2 PENDING).
/// </summary>
public sealed class Iter107ScrollCameraToTargetTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewGalacticCampaign());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, adapter);
    }

    [Fact]
    public async Task ScrollCameraToTarget_WithFindPlanet_DispatchesLiveOk()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var result = await adapter.SendRawAsync(
            "return SWFOC_ScrollCameraToTarget('Find_Planet(\"Yavin\")')",
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().StartWith("OK:");
        result.Response.Should().Contain("LIVE");
        sim.GameState.LastScrollCameraToTarget
            .Should().Be("Find_Planet(\"Yavin\")",
                "simulator must capture the raw target expression that the "
                + "bridge would have spliced into Scroll_Camera_To(...)");
    }

    [Fact]
    public async Task ScrollCameraToTarget_WithUnitFinder_DispatchesLiveOk()
    {
        // Tactical-mode flavour: target a unit-type lookup result.
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var result = await adapter.SendRawAsync(
            "return SWFOC_ScrollCameraToTarget('Find_First_Object(\"Empire_AT_AT\")')",
            CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Response.Should().StartWith("OK:");
        sim.GameState.LastScrollCameraToTarget
            .Should().Be("Find_First_Object(\"Empire_AT_AT\")");
    }

    [Fact]
    public async Task ScrollCameraToTarget_PreservesLastTarget_AcrossCalls()
    {
        // Multi-call sequence — last write wins, mirrors the engine
        // semantics where each Scroll_Camera_To dispatch immediately
        // overrides the previous camera target.
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_ScrollCameraToTarget('Find_Planet(\"Hoth\")')",
            CancellationToken.None);
        sim.GameState.LastScrollCameraToTarget.Should().Contain("Hoth");

        await adapter.SendRawAsync(
            "return SWFOC_ScrollCameraToTarget('Find_Planet(\"Coruscant\")')",
            CancellationToken.None);
        sim.GameState.LastScrollCameraToTarget.Should().Contain("Coruscant");
        sim.GameState.LastScrollCameraToTarget.Should().NotContain("Hoth",
            "second call must override the first — single-target semantics");
    }

    [Fact]
    public void Catalog_MarksScrollCameraToTarget_AsLive()
    {
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_ScrollCameraToTarget");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 107 wired Scroll_Camera_To via DoString — LIVE");
        entry.Note.Should().Contain("Scroll_Camera_To");
    }

    [Fact]
    public void Catalog_PromotesSetCameraPos_ToLive_PerIter237()
    {
        // Iter-258 sibling-drift cleanup: this test originally pinned
        // SetCameraPos as Phase2HookPending per iter-106's "no engine API
        // for raw floats" finding. Iter-237 RE-walked the camera path and
        // wired SetCameraPos LIVE via direct call to SetTransformMatrix
        // (engine bypasses the Lua userdata layer entirely; see iter-237
        // close-out doc + rva_camera_set_transform_matrix in the ledger).
        // Iter-243 cascading drift catch updated Iter221's count pin
        // (_Is26 → _Is25) but missed updating THIS test. iter-258
        // collateral cleanup catches it now.
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_SetCameraPos");
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter-237 wired SetCameraPos LIVE via direct call to SetTransformMatrix @ 0x261BD0; the userdata-userdata constraint applied to the Lua API path, not the direct-call path");
    }

    [Fact]
    public void Catalog_KeepsFreeCam_AsPhase2Pending()
    {
        // Iter 106 finding: there's no engine Free_Cam Lua API. The
        // engine implements free-cam through Lua-side scripted behaviour
        // we'd need to mimic. Pinning the deferral so a future
        // "Override_Free_Cam exists, must have missed it" attempt can be
        // double-checked against this test.
        var entry = CapabilityStatusCatalog.Lookup("SWFOC_FreeCam");
        entry.Status.Should().Be(CapabilityStatus.Phase2HookPending,
            "no engine Free_Cam Lua API; would need scripted-behaviour mimic");
        entry.Note.Should().Contain("Free_Cam");
    }
}
