using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 200) — pins Galactic-tab Fog-of-War native UX surface
/// for iter-180 (FOWRevealAll/UndoRevealAll, 1-arg player) + iter-184
/// (FOWReveal, 3-arg player/position/radius) wires.
///
/// 200th iteration milestone — first NEW Galactic-tab GroupBox since the
/// iter-34 story-arrival spawn. Crosses the 138-wire mark at iter 184; this
/// surface ships 3 of the 7 highest-value LIVE wires that previously only
/// had Lua Playground presets.
///
/// The 3 dispatcher methods reuse the iter-117/119 builder helpers
/// (BuildUnitLuaNoArgCall — name says "Unit" but it's just `return
/// SWFOC_X('arg')`, shape-agnostic) plus a new BuildFOWRevealCommand
/// for the 3-arg form.
/// </summary>
public sealed class Iter200GalacticFOWNativeUxTests
{
    [Fact]
    public void DispatcherMethods_BindToCorrectSwfocNames()
    {
        var t = typeof(V2UnitMutationDispatcher);
        t.GetMethod(nameof(V2UnitMutationDispatcher.FOWRevealAllLuaAsync))
            .Should().NotBeNull("Galactic FOW 'Reveal map' button binds to FOWRevealAllLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.FOWUndoRevealAllLuaAsync))
            .Should().NotBeNull("Galactic FOW 'Restore fog' button binds to FOWUndoRevealAllLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.FOWRevealLuaAsync))
            .Should().NotBeNull("Galactic FOW 'Reveal at position' button binds to FOWRevealLuaAsync");
    }

    [Fact]
    public void CatalogAction_AllThreeEntriesAreLive()
    {
        var swfocNames = new[]
        {
            "SWFOC_FOWRevealAllLua",
            "SWFOC_FOWUndoRevealAllLua",
            "SWFOC_FOWRevealLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-200 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_DocumentsIter200Surfacing()
    {
        var revealAll = CapabilityStatusCatalog.Entries["SWFOC_FOWRevealAllLua"].Note;
        var undoRevealAll = CapabilityStatusCatalog.Entries["SWFOC_FOWUndoRevealAllLua"].Note;
        var reveal = CapabilityStatusCatalog.Entries["SWFOC_FOWRevealLua"].Note;

        revealAll.Should().Contain("Iter 200");
        undoRevealAll.Should().Contain("Iter 200");
        reveal.Should().Contain("Iter 200");
    }

    [Fact]
    public void CatalogRationale_PreservesIter180NamespacedDispatchFinding()
    {
        // Pin: extending the iter-180 rationale with iter-200 surfacing must
        // NOT lose the iter-180 finding that the helper is namespace-agnostic
        // ("Demonstrates NAMESPACED method-name dispatch"). That finding is
        // load-bearing for iter-181/200 and shouldn't drift if a future
        // catalog edit touches the rationale text.
        var note = CapabilityStatusCatalog.Entries["SWFOC_FOWRevealAllLua"].Note;
        note.Should().Contain("NAMESPACED");
        note.Should().Contain("namespace-agnostic");
    }

    [Fact]
    public void CatalogRationale_FOWRevealKeepsArchitecturalOpeningPin()
    {
        // Pin: iter-184 architectural-opening note (3-arg helper unlocks
        // future Set_Cinematic_Camera_Key / Find_Nearest wires) must
        // survive the iter-200 surfacing edit. iter-186 already used this
        // opening to ship Find_Nearest; deleting the note would lose the
        // breadcrumb for future iterators.
        var note = CapabilityStatusCatalog.Entries["SWFOC_FOWRevealLua"].Note;
        note.Should().Contain("Set_Cinematic_Camera_Key");
        note.Should().Contain("Find_Nearest");
    }

    [Fact]
    public void BuildFOWRevealCommand_PinsThreeArgWireFormat()
    {
        // Pin the exact wire format. Three independent inner expressions,
        // each wrapped in single quotes with single-quote escaping —
        // identical pattern to iter-119 SpawnUnitLua but with a different
        // SWFOC_* name and engine method.
        var lua = V2UnitMutationDispatcher.BuildFOWRevealCommand(
            "Find_Player(\"REBEL\")",
            "FindPlanet(\"Yavin\"):Get_Position()",
            "500");

        lua.Should().Be(
            "return SWFOC_FOWRevealLua('Find_Player(\"REBEL\")', "
            + "'FindPlanet(\"Yavin\"):Get_Position()', '500')");
    }
}
