using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 201) — pins WorldState-tab Story+Audio native UX surface
/// for the iter-159 string-arg global wires (Story_Event / Add_Objective /
/// Play_Music / Play_SFX_Event). Distinct from the upper service-mediated
/// "Fire event" button — these 4 hit the engine Lua API directly via the
/// shape-agnostic iter-158 global-arg helper.
///
/// 9th tab to receive native UX for iter-100+ LIVE wires (after UnitControl,
/// PlayerState, Diagnostics, Inspector, Camera, Combat, Spawning, Galactic).
/// Constructor cascade: WorldStateTabViewModel(7 deps) → (8 deps with
/// V2UnitMutationDispatcher).
/// </summary>
public sealed class Iter201WorldStateStoryAudioNativeUxTests
{
    [Fact]
    public void DispatcherMethods_BindToCorrectSwfocNames()
    {
        var t = typeof(V2UnitMutationDispatcher);
        t.GetMethod(nameof(V2UnitMutationDispatcher.StoryEventLuaAsync))
            .Should().NotBeNull("WorldState 'Story_Event' button binds to StoryEventLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.AddObjectiveLuaAsync))
            .Should().NotBeNull("WorldState 'Add_Objective' button binds to AddObjectiveLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.PlayMusicLuaAsync))
            .Should().NotBeNull("WorldState 'Play_Music' button binds to PlayMusicLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.PlaySfxEventLuaAsync))
            .Should().NotBeNull("WorldState 'Play_SFX_Event' button binds to PlaySfxEventLuaAsync");
    }

    [Fact]
    public void CatalogAction_AllFourEntriesAreLive()
    {
        var swfocNames = new[]
        {
            "SWFOC_StoryEventLua",
            "SWFOC_AddObjectiveLua",
            "SWFOC_PlayMusicLua",
            "SWFOC_PlaySfxEventLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-201 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_DocumentsIter201Surfacing()
    {
        var storyEvent = CapabilityStatusCatalog.Entries["SWFOC_StoryEventLua"].Note;
        var addObjective = CapabilityStatusCatalog.Entries["SWFOC_AddObjectiveLua"].Note;
        var playMusic = CapabilityStatusCatalog.Entries["SWFOC_PlayMusicLua"].Note;
        var playSfx = CapabilityStatusCatalog.Entries["SWFOC_PlaySfxEventLua"].Note;

        storyEvent.Should().Contain("Iter 201");
        addObjective.Should().Contain("Iter 201");
        playMusic.Should().Contain("Iter 201");
        playSfx.Should().Contain("Iter 201");
    }

    [Fact]
    public void CatalogRationale_StoryEventDistinguishesFromServiceMediated()
    {
        // Pin: the WorldState tab now has TWO story-event surfaces — the
        // upper "Fire event" button which routes through IStoryEventService
        // (catalog/profile-mediated) and the lower iter-201 button which
        // hits the engine Lua API directly. Catalog rationale must make
        // this distinction explicit so operators (and future iterators)
        // don't accidentally consolidate the two surfaces.
        var note = CapabilityStatusCatalog.Entries["SWFOC_StoryEventLua"].Note;
        note.Should().Contain("IStoryEventService");
        note.Should().Contain("directly");
    }

    [Fact]
    public void CatalogRationale_PlayMusicReferencesCinematicWorkflow()
    {
        // Pin: Play_Music's iter-201 surfacing notes the cinematic workflow
        // pairing with iter-145 cinematic camera primitives. Operators
        // recording cinematic shots want sound+camera control in one place;
        // this breadcrumb stays in the rationale so future surfacing iters
        // know to keep them paired (or know the connection if Camera tab
        // gets the audio buttons too).
        var note = CapabilityStatusCatalog.Entries["SWFOC_PlayMusicLua"].Note;
        note.Should().Contain("iter-145");
        note.Should().Contain("cinematic");
    }

    [Fact]
    public void BuildUnitLuaNoArgCall_PinsIter159WireFormatForStoryEvent()
    {
        // Pin the wire format. iter-159 wires reuse BuildUnitLuaNoArgCall
        // (helper name says "Unit" but is shape-agnostic — works for any
        // 1-string-arg call). Single-quote wrap with embedded-quote escape
        // identical to iter-117/119/195/200 wire format.
        var lua = V2UnitMutationDispatcher.BuildUnitLuaNoArgCall(
            "SWFOC_StoryEventLua",
            "\"DEATH_STAR_DESTROYED\"");

        lua.Should().Be("return SWFOC_StoryEventLua('\"DEATH_STAR_DESTROYED\"')");
    }
}
