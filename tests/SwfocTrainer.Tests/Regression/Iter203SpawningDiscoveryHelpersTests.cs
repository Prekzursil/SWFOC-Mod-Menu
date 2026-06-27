using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 203) — pins the new "Discovery helpers" GroupBox added
/// to the Spawning tab. Surfaces 4 LIVE getter wires:
/// - iter-177 SWFOC_FindObjectTypeLua / SWFOC_FindPlanetLua /
///   SWFOC_FindFirstObjectLua (1-arg getters via existing
///   BuildUnitLuaNoArgCall helper).
/// - iter-186 SWFOC_FindNearestLua (3-arg getter via NEW iter-203
///   BuildSwfocLua3ArgCall generic builder — distinct from iter-200's
///   FOW-specific builder that hardcoded the SWFOC name).
///
/// All four return engine handles into LastStatus so operators can paste
/// them into the spawn-Lua fields above without dropping into Lua
/// Playground. Pairs the iter-200 FOWReveal partial-reveal workflow with
/// a discovery surface (find a planet → reveal FOW around its position).
/// </summary>
public sealed class Iter203SpawningDiscoveryHelpersTests
{
    [Fact]
    public void DispatcherMethods_BindToCorrectSwfocNames()
    {
        var t = typeof(V2UnitMutationDispatcher);
        t.GetMethod(nameof(V2UnitMutationDispatcher.FindObjectTypeLuaAsync))
            .Should().NotBeNull("Spawning 'Find object type' button binds to FindObjectTypeLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.FindPlanetLuaAsync))
            .Should().NotBeNull("Spawning 'Find planet' button binds to FindPlanetLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.FindFirstObjectLuaAsync))
            .Should().NotBeNull("Spawning 'Find first object' button binds to FindFirstObjectLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.FindNearestLuaAsync))
            .Should().NotBeNull("Spawning 'Find nearest' button binds to FindNearestLuaAsync");
    }

    [Fact]
    public void CatalogAction_AllFourEntriesAreLive()
    {
        var swfocNames = new[]
        {
            "SWFOC_FindObjectTypeLua",
            "SWFOC_FindPlanetLua",
            "SWFOC_FindFirstObjectLua",
            "SWFOC_FindNearestLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-203 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_DocumentsIter203Surfacing()
    {
        var fot = CapabilityStatusCatalog.Entries["SWFOC_FindObjectTypeLua"].Note;
        var fp = CapabilityStatusCatalog.Entries["SWFOC_FindPlanetLua"].Note;
        var ffo = CapabilityStatusCatalog.Entries["SWFOC_FindFirstObjectLua"].Note;
        var fn = CapabilityStatusCatalog.Entries["SWFOC_FindNearestLua"].Note;

        fot.Should().Contain("Iter 203");
        fp.Should().Contain("Iter 203");
        ffo.Should().Contain("Iter 203");
        fn.Should().Contain("Iter 203");
    }

    [Fact]
    public void CatalogRationale_FindNearestPinsNewBuilderDistinction()
    {
        // Pin: Find_Nearest is the FIRST wire to use the new iter-203
        // BuildSwfocLua3ArgCall generic 3-arg builder. iter-200's
        // BuildFOWRevealCommand hardcoded the SWFOC name; the new one
        // accepts SWFOC name as a parameter. Future iters that need 3-arg
        // wires (e.g. another camera primitive) can reuse the new builder.
        var note = CapabilityStatusCatalog.Entries["SWFOC_FindNearestLua"].Note;
        note.Should().Contain("BuildSwfocLua3ArgCall");
        note.Should().Contain("hardcoded");
    }

    [Fact]
    public void CatalogRationale_FindPlanetReferencesFOWRevealPairing()
    {
        // Pin: Find_Planet's iter-203 surfacing notes the iter-200 FOWReveal
        // pairing. Operators can find a planet handle, then call FOWReveal
        // around its position. This breadcrumb stays in the rationale so
        // future surfacing iters know the cross-tab workflow.
        var note = CapabilityStatusCatalog.Entries["SWFOC_FindPlanetLua"].Note;
        note.Should().Contain("iter-200 FOWReveal");
    }

    [Fact]
    public void BuildSwfocLua3ArgCall_PinsGenericThreeArgWireFormat()
    {
        // Pin the wire format. iter-203 introduces BuildSwfocLua3ArgCall —
        // generic version of iter-200's BuildFOWRevealCommand. Wire format
        // matches the FOW one but with operator-supplied SWFOC name.
        var lua = V2UnitMutationDispatcher.BuildSwfocLua3ArgCall(
            "SWFOC_FindNearestLua",
            "Find_Object_Type(\"Empire_AT_AT\")",
            "Vector(100, 0, 200)",
            "Find_Player(\"REBEL\")");

        lua.Should().Be(
            "return SWFOC_FindNearestLua('Find_Object_Type(\"Empire_AT_AT\")', "
            + "'Vector(100, 0, 200)', 'Find_Player(\"REBEL\")')");
    }
}
