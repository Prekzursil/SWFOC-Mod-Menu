using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 186) — pins the iter-186 batch shipping the SECOND
/// 3-arg helper (12th overall): NEW Lua_DispatchGlobalGetter3Arg, symmetric
/// counterpart to iter-184's 3-arg setter, with engine return-value capture.
/// 1-wire batch SWFOC_FindNearestLua via Find_Nearest(type, position, player).
///
/// LIVE flip #142; master loop now at 142 LIVE wires.
/// </summary>
public sealed class Iter186GlobalGetter3ArgHelperTests
{
    [Fact]
    public void FindNearest_StatusIsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_FindNearestLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void FindNearest_NotePinsAsFirstWireViaNewHelper()
    {
        // Pin: catalog rationale should call out the NEW iter-186 helper
        // and that it's the 12th in the dispatcher set, mirroring iter-184.
        var note = CapabilityStatusCatalog.Entries["SWFOC_FindNearestLua"].Note;
        note.Should().Contain("NEW iter-186");
        note.Should().Contain("12th helper");
        note.Should().Contain("symmetric");
    }

    [Fact]
    public void FindNearest_NoteCallsOutMirrorRelationshipWithIter184()
    {
        // Pin: iter-186 is the read-side mirror of iter-184. Catalog should
        // make the symmetry explicit so future readers see the pattern.
        var note = CapabilityStatusCatalog.Entries["SWFOC_FindNearestLua"].Note;
        note.Should().Contain("iter-184");
        note.Should().Contain("3-arg setter");
    }

    [Fact]
    public void FindNearest_NoteShowsFullCompositionWorkflow()
    {
        // Pin: rationale should show the full operator workflow combining
        // iter-177 Find_Object_Type + iter-178 Get_Local_Player + iter-186.
        var note = CapabilityStatusCatalog.Entries["SWFOC_FindNearestLua"].Note;
        note.Should().Contain("Find_Object_Type");
        note.Should().Contain("Get_Local_Player");
    }

    [Fact]
    public void FindNearest_TaggedIter186Live()
    {
        CapabilityStatusCatalog.Entries["SWFOC_FindNearestLua"].Note
            .Should().Contain("Iter 186 LIVE");
    }
}
