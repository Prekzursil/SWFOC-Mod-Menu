using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 189) — pins the iter-189 PlayerState tab read-side
/// native UX continuation of the iter-188 surfacing arc. 3 buttons for
/// iter-169 player-receiver wires (Get_Credits, Get_Tech_Level, Get_Faction).
///
/// All three reuse the iter-167 helper via the shape-agnostic dispatcher
/// pattern — player Lua expressions compose into the same (handle):method()
/// codegen as units.
/// </summary>
public sealed class Iter189PlayerStateReadSideNativeUxTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewBridge()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        return (sim, new V2BridgeAdapter(pipe));
    }

    [Fact]
    public void Dispatcher_HasThreePlayerReadSideMethods()
    {
        var (sim, adapter) = NewBridge();
        using (sim)
        {
            var dispatcher = new V2UnitMutationDispatcher(adapter);
            var t1 = dispatcher.GetPlayerCreditsLuaAsync("Find_Player(\"REBEL\")", System.Threading.CancellationToken.None);
            var t2 = dispatcher.GetPlayerTechLevelLuaAsync("Find_Player(\"REBEL\")", System.Threading.CancellationToken.None);
            var t3 = dispatcher.GetPlayerFactionLuaAsync("Find_Player(\"REBEL\")", System.Threading.CancellationToken.None);
            t1.Should().NotBeNull();
            t2.Should().NotBeNull();
            t3.Should().NotBeNull();
        }
    }

    [Fact]
    public void CatalogAction_PointsToLiveCatalogEntries()
    {
        var swfocNames = new[]
        {
            "SWFOC_GetCreditsLua",
            "SWFOC_GetTechLevelLua",
            "SWFOC_GetFactionLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-189 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_ReferencesIter167HelperShapeAgnosticism()
    {
        // Pin: iter-169 wires used iter-167's helper (proven shape-agnostic
        // for player receivers). Catalog rationale should reference iter-167
        // so future readers see the helper-reuse pattern.
        var creditsNote = CapabilityStatusCatalog.Entries["SWFOC_GetCreditsLua"].Note;
        creditsNote.Should().Contain("iter-167");
    }
}
