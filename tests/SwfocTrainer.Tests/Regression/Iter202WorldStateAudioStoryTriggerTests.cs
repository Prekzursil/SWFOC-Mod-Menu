using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 202) — pins the iter-201 WorldState Story+Audio GroupBox
/// extension from 4 → 7 buttons. New wires:
/// - iter-166 SWFOC_StopAllMusicLua (no-arg global) — first dispatcher
///   method using the NEW iter-202 BuildGlobalLuaNoArgCall builder.
/// - iter-166 SWFOC_ResumeModeBasedMusicLua (no-arg global) — same builder.
/// - iter-160 SWFOC_StoryEventTriggerLua (1-arg name) — reuses iter-159
///   shape via BuildUnitLuaNoArgCall.
///
/// Operator workflow this iter unlocks: cinematic soundtrack swap
/// (Stop_All_Music → Play_Music("CINEMATIC_TRACK") → Resume_Mode_Based_Music
/// after the cutscene). Story_Event_Trigger sits next to Story_Event for
/// debugging mod listener semantics.
/// </summary>
public sealed class Iter202WorldStateAudioStoryTriggerTests
{
    [Fact]
    public void DispatcherMethods_BindToCorrectSwfocNames()
    {
        var t = typeof(V2UnitMutationDispatcher);
        t.GetMethod(nameof(V2UnitMutationDispatcher.StopAllMusicLuaAsync))
            .Should().NotBeNull("WorldState 'Stop_All_Music' button binds to StopAllMusicLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.ResumeModeBasedMusicLuaAsync))
            .Should().NotBeNull("WorldState 'Resume_Mode_Based_Music' button binds to ResumeModeBasedMusicLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.StoryEventTriggerLuaAsync))
            .Should().NotBeNull("WorldState 'Story_Event_Trigger' button binds to StoryEventTriggerLuaAsync");
    }

    [Fact]
    public void CatalogAction_AllThreeEntriesAreLive()
    {
        var swfocNames = new[]
        {
            "SWFOC_StopAllMusicLua",
            "SWFOC_ResumeModeBasedMusicLua",
            "SWFOC_StoryEventTriggerLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-202 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_DocumentsIter202Surfacing()
    {
        var stop = CapabilityStatusCatalog.Entries["SWFOC_StopAllMusicLua"].Note;
        var resume = CapabilityStatusCatalog.Entries["SWFOC_ResumeModeBasedMusicLua"].Note;
        var trigger = CapabilityStatusCatalog.Entries["SWFOC_StoryEventTriggerLua"].Note;

        stop.Should().Contain("Iter 202");
        resume.Should().Contain("Iter 202");
        trigger.Should().Contain("Iter 202");
    }

    [Fact]
    public void CatalogRationale_StopAllMusicReferencesPlayMusicPairing()
    {
        // Pin: Stop_All_Music's iter-202 surfacing notes the cinematic
        // soundtrack-swap workflow with iter-201 Play_Music. Operators
        // recording cinematics need both surfaces in one place. Future
        // catalog edits must preserve this pairing breadcrumb.
        var note = CapabilityStatusCatalog.Entries["SWFOC_StopAllMusicLua"].Note;
        note.Should().Contain("iter-201 Play_Music");
        note.Should().Contain("cinematic");
    }

    [Fact]
    public void BuildGlobalLuaNoArgCall_PinsNoArgWireFormat()
    {
        // Pin the wire format. iter-202 introduces BuildGlobalLuaNoArgCall —
        // distinct from BuildUnitLuaNoArgCall (always emits a single quoted
        // arg) because Stop_All_Music takes ZERO args. Wire format:
        // `return SWFOC_X()` with no parens contents.
        var stopLua = V2UnitMutationDispatcher.BuildGlobalLuaNoArgCall("SWFOC_StopAllMusicLua");
        var resumeLua = V2UnitMutationDispatcher.BuildGlobalLuaNoArgCall("SWFOC_ResumeModeBasedMusicLua");

        stopLua.Should().Be("return SWFOC_StopAllMusicLua()");
        resumeLua.Should().Be("return SWFOC_ResumeModeBasedMusicLua()");
    }

    [Fact]
    public void StoryEventTrigger_PinsIter160LineageDistinctFromIter159()
    {
        // Pin: Story_Event_Trigger is iter-160 (alternative engine semantic)
        // distinct from iter-159 Story_Event. Catalog rationale must keep
        // BOTH the iter-160 lineage AND the iter-159 sibling reference
        // so future readers know they're paired surfaces, not duplicates.
        var note = CapabilityStatusCatalog.Entries["SWFOC_StoryEventTriggerLua"].Note;
        note.Should().Contain("Iter 160");
        note.Should().Contain("iter 159");
        note.Should().Contain("trigger variant");
    }
}
