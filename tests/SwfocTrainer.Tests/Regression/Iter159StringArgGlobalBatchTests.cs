using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 159) — pins string-arg global LIVE batch shipped via the
/// existing iter-158 Lua_DispatchGlobalArgMethod helper. LIVE flips #56-59.
/// Master loop now at 59 LIVE wires.
/// Story_Event, Add_Objective, Play_Music, Play_SFX_Event are all 1-arg
/// no-receiver globals — same dispatcher shape as iter 158, just string args
/// where iter 158 had bool args. The helper is shape-agnostic.
/// </summary>
public sealed class Iter159StringArgGlobalBatchTests
{
    [Theory]
    [InlineData("SWFOC_StoryEventLua")]
    [InlineData("SWFOC_AddObjectiveLua")]
    [InlineData("SWFOC_PlayMusicLua")]
    [InlineData("SWFOC_PlaySfxEventLua")]
    public void StringArgGlobalBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void StoryEvent_NoteMentionsStoryEvent()
    {
        CapabilityStatusCatalog.Entries["SWFOC_StoryEventLua"].Note
            .Should().Contain("Story_Event");
    }

    [Fact]
    public void AddObjective_NoteMentionsAddObjective()
    {
        CapabilityStatusCatalog.Entries["SWFOC_AddObjectiveLua"].Note
            .Should().Contain("Add_Objective");
    }

    [Fact]
    public void PlayMusic_NoteMentionsPlayMusic()
    {
        CapabilityStatusCatalog.Entries["SWFOC_PlayMusicLua"].Note
            .Should().Contain("Play_Music");
    }

    [Fact]
    public void PlaySfxEvent_NoteMentionsPlaySfxEvent()
    {
        CapabilityStatusCatalog.Entries["SWFOC_PlaySfxEventLua"].Note
            .Should().Contain("Play_SFX_Event");
    }

    [Fact]
    public void StringArgGlobalBatch_AllReuseIter158Helper()
    {
        // Pin: all 4 wires reference iter 159 in their note (timestamp pin).
        // This is a regression guard — if a future "simplification" pulls these
        // into a different helper without updating the catalog rationale, the
        // pin breaks.
        var iter159Entries = new[]
        {
            "SWFOC_StoryEventLua",
            "SWFOC_AddObjectiveLua",
            "SWFOC_PlayMusicLua",
            "SWFOC_PlaySfxEventLua",
        };
        foreach (var name in iter159Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 159 LIVE",
                    $"{name} should be tagged as iter 159 LIVE in catalog rationale");
        }
    }
}
