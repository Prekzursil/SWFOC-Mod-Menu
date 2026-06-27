using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 184) — pins the iter-184 batch shipping the SECOND
/// multi-arg expansion: NEW Lua_DispatchGlobalArg3Method (11th helper)
/// + 1-wire batch SWFOC_FOWRevealLua via FOWManager.Reveal(player, position, radius).
///
/// Partial-reveal complement to iter-180 SWFOC_FOWRevealAllLua.
/// LIVE flip #138; master loop now at 138 LIVE wires.
/// </summary>
public sealed class Iter184GlobalArg3HelperTests
{
    [Fact]
    public void FOWReveal_StatusIsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_FOWRevealLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void FOWReveal_NotePinsAsFirstWireViaNewHelper()
    {
        // Pin: catalog rationale should call out that this is the FIRST
        // wire shipped via the NEW iter-184 global-3-arg helper, and that
        // the helper is the 11th (second multi-arg expansion).
        var note = CapabilityStatusCatalog.Entries["SWFOC_FOWRevealLua"].Note;
        note.Should().Contain("NEW iter-184");
        note.Should().Contain("11th helper");
        note.Should().Contain("multi-arg expansion");
    }

    [Fact]
    public void FOWReveal_NoteDistinguishesFromIter180RevealAll()
    {
        // Pin: iter-180 has the no-position whole-map reveal
        // (FOWRevealAll); iter-184 has the partial-area reveal
        // (FOWReveal). Catalog should make the distinction explicit.
        var note = CapabilityStatusCatalog.Entries["SWFOC_FOWRevealLua"].Note;
        note.Should().Contain("iter-180");
        note.Should().Contain("Partial-reveal");
    }

    [Fact]
    public void FOWReveal_NoteShowsFullyQualifiedMethodName()
    {
        // Pin: rationale should show actual FOWManager.Reveal expression.
        CapabilityStatusCatalog.Entries["SWFOC_FOWRevealLua"].Note
            .Should().Contain("FOWManager.Reveal");
    }

    [Fact]
    public void FOWReveal_NoteFlagsArchitecturalOpening()
    {
        // Pin: 3-arg helper unlocks future wires for engine APIs that take
        // 3 args (Set_Cinematic_Camera_Key, Find_Nearest, etc). Rationale
        // should signal this so future iters know they can use the helper.
        var note = CapabilityStatusCatalog.Entries["SWFOC_FOWRevealLua"].Note;
        note.Should().Contain("Set_Cinematic_Camera_Key");
        note.Should().Contain("Find_Nearest");
    }

    [Fact]
    public void FOWReveal_TaggedIter184Live()
    {
        CapabilityStatusCatalog.Entries["SWFOC_FOWRevealLua"].Note
            .Should().Contain("Iter 184 LIVE");
    }
}
