using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 238) -- pins the simulator handler + round-trip behavior
/// for the iter-237 LIVE wires SWFOC_SetCameraPos / SWFOC_GetCameraPos.
/// Closes A1.x SetCameraPos arc at simulator level (iter 239 adds Camera tab
/// UX, iter 240 verifies live).
///
/// Bridge-side: direct call to CameraClass::SetTransformMatrix @ 0x261BD0 +
/// CameraClass::GetPosition @ 0x261A40 (NOT MinHook detour — pattern
/// parallels iter-100 SetSpeedOverride). LookupActiveCamera() walks
/// GameModeRoot+0x90 with vftable[28] mode==2 check (tactical-only).
///
/// Simulator-side: existing iter-140 handlers (HandleSetCameraPos +
/// HandleGetCameraPos from FakeGameState.CameraPos tuple) already provide
/// the round-trip. Iter 238 adds operator-facing pin tests pinning the
/// 3-coord independence + sequential-set semantics + iter-236/237
/// catalog rationale references.
///
/// Pattern parallels iter-226 SetFireRate + iter-232 FreezeCredits sim pin
/// files. Differs in that iter-238's simulator handlers are pre-existing
/// from iter 140 (when bridge was Phase-1 stub), not new this iter — only
/// pin tests + reverse-orphan rebalance + ledger entries are new.
/// </summary>
public sealed class Iter238SetCameraPosSimulatorTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, adapter);
    }

    [Fact]
    public void Catalog_BothCameraPosEntriesAreLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_SetCameraPos"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_GetCameraPos"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_DocumentsIter237WireAndIter236Design()
    {
        // Pin: catalog rationale must reference iter-237 (the implementation
        // iter), iter-236 RE design doc filename, RVA 0x261BD0
        // (SetTransformMatrix), tactical-only caveat, and the engine semantic
        // caveats. These cross-references make the operator-facing
        // documentation traceable.
        var setNote = CapabilityStatusCatalog.Entries["SWFOC_SetCameraPos"].Note;
        setNote.Should().Contain("Iter 237 LIVE");
        setNote.Should().Contain("SetTransformMatrix");
        setNote.Should().Contain("0x261BD0");
        setNote.Should().Contain("iter236_setcamerapos_per_coord_re_kickoff.md");
        setNote!.ToLowerInvariant().Should().Contain("tactical-only");
        setNote.ToLowerInvariant().Should().Contain("animation pipeline");
        setNote.ToLowerInvariant().Should().Contain("direct call");

        var getNote = CapabilityStatusCatalog.Entries["SWFOC_GetCameraPos"].Note;
        getNote.Should().Contain("Iter 237 LIVE");
        getNote.Should().Contain("GetPosition");
        getNote.Should().Contain("0x261A40");
        getNote!.ToLowerInvariant().Should().Contain("pair-flip");
    }

    [Fact]
    public void FakeGameState_HasCameraPosTupleFieldDefaultZero()
    {
        // Pin: FakeGameState.CameraPos exists as 3-float tuple with default
        // (0, 0, 0). Reflection walk so we don't depend on construction args.
        var t = typeof(FakeGameState);
        var prop = t.GetProperty("CameraPos");
        prop.Should().NotBeNull("iter-140 introduced CameraPos tuple field");

        var state = FakeGameState.NewTacticalSkirmish();
        state.CameraPos.X.Should().Be(0.0f, "default zero X");
        state.CameraPos.Y.Should().Be(0.0f, "default zero Y");
        state.CameraPos.Z.Should().Be(0.0f, "default zero Z");
    }

    [Fact]
    public async Task Simulator_RoundTripSetGet_PreservesAllThreeCoords()
    {
        // Pin: SWFOC_SetCameraPos(1.0, 2.0, 3.0) → SWFOC_GetCameraPos() = "1,2,3"
        // exercises the simulator's existing iter-140 round-trip handler
        // backed by the iter-237 LIVE bridge wire.
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var setResult = await adapter.SendRawAsync(
            "return SWFOC_SetCameraPos(1.0, 2.0, 3.0)", CancellationToken.None);
        setResult.Succeeded.Should().BeTrue();
        setResult.Response.Should().Be("ok");

        sim.GameState.CameraPos.X.Should().Be(1.0f);
        sim.GameState.CameraPos.Y.Should().Be(2.0f);
        sim.GameState.CameraPos.Z.Should().Be(3.0f);

        var getResult = await adapter.SendRawAsync(
            "return SWFOC_GetCameraPos()", CancellationToken.None);
        getResult.Succeeded.Should().BeTrue();
        getResult.Response.Should().Contain("1");
        getResult.Response.Should().Contain("2");
        getResult.Response.Should().Contain("3");
    }

    [Fact]
    public async Task Simulator_AxisIndependence_EachCoordStoredSeparately()
    {
        // Pin: setting (10, 20, 30) updates all three independently — catches
        // any regression where the handler accidentally clobbers axes.
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_SetCameraPos(10.0, 20.0, 30.0)", CancellationToken.None);

        sim.GameState.CameraPos.X.Should().Be(10.0f);
        sim.GameState.CameraPos.Y.Should().Be(20.0f);
        sim.GameState.CameraPos.Z.Should().Be(30.0f);
    }

    [Fact]
    public async Task Simulator_SequentialSet_LatestWins()
    {
        // Pin: sequential SWFOC_SetCameraPos calls overwrite cleanly — catches
        // any regression where the handler accidentally accumulates rather
        // than replaces.
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_SetCameraPos(0.0, 0.0, 0.0)", CancellationToken.None);
        sim.GameState.CameraPos.X.Should().Be(0.0f);

        await adapter.SendRawAsync(
            "return SWFOC_SetCameraPos(100.0, 0.0, 0.0)", CancellationToken.None);
        sim.GameState.CameraPos.X.Should().Be(100.0f, "latest set wins");
        sim.GameState.CameraPos.Y.Should().Be(0.0f);
        sim.GameState.CameraPos.Z.Should().Be(0.0f);

        // Final mixed update — all 3 axes change.
        await adapter.SendRawAsync(
            "return SWFOC_SetCameraPos(-50.0, 75.5, 200.25)", CancellationToken.None);
        sim.GameState.CameraPos.X.Should().Be(-50.0f);
        sim.GameState.CameraPos.Y.Should().Be(75.5f);
        sim.GameState.CameraPos.Z.Should().Be(200.25f);
    }

    [Fact]
    public void CatalogPair_GetSiblingExistsAndDocumentsPairFlip()
    {
        // Pin: every Set wire has a corresponding Get pair-flip (mirrors
        // iter-225 + iter-227 pattern). Catches regressions where Get
        // sibling is accidentally removed.
        CapabilityStatusCatalog.Entries.Should().ContainKey("SWFOC_GetCameraPos",
            "Set/Get pair-flip required by iter-237 design");

        var getNote = CapabilityStatusCatalog.Entries["SWFOC_GetCameraPos"].Note;
        getNote!.ToLowerInvariant().Should().Contain("pair-flip");
    }
}
