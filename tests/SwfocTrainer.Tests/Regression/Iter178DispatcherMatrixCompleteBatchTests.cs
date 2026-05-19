using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-04 (iter 178) — pins NEW Lua_DispatchGlobalGetterNoArg helper
/// (9th in dispatcher set) + 3 wires that close the receiver × arg ×
/// read/write matrix. After iter 178, future wires using any known shape
/// ship at ~3 LoC marginal bridge cost — only multi-arg / table-arg /
/// varargs would need new helpers.
/// LIVE flips #123-125; master loop now at 125 LIVE wires.
/// Get_Game_Mode / Get_Local_Player / Get_Seconds_Per_Game_Minute return
/// engine state strings/handles for further composition.
/// </summary>
public sealed class Iter178DispatcherMatrixCompleteBatchTests
{
    [Theory]
    [InlineData("SWFOC_GetGameModeLua")]
    [InlineData("SWFOC_GetLocalPlayerLua")]
    [InlineData("SWFOC_GetSecondsPerGameMinuteLua")]
    public void DispatcherMatrixCompleteBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void GetGameMode_NotePinsAsFirstWireViaNewHelper()
    {
        // Pin: catalog rationale should call out that this is the FIRST
        // wire shipped via the NEW iter-178 global-no-arg-getter helper,
        // and that the helper closes the dispatcher matrix.
        var note = CapabilityStatusCatalog.Entries["SWFOC_GetGameModeLua"].Note;
        note.Should().Contain("NEW iter-178");
        note.Should().Contain("9th in dispatcher set");
        note.Should().Contain("closes the receiver");
    }

    [Fact]
    public void GetGameMode_NoteIncludesCompositionExample()
    {
        // Pin: operator-facing rationale should illustrate composition
        // (gating tactical-only commands), not just describe in isolation.
        CapabilityStatusCatalog.Entries["SWFOC_GetGameModeLua"].Note
            .Should().Contain("Land");
    }

    [Fact]
    public void GetLocalPlayer_NoteIncludesCompositionExample()
    {
        // Pin: operator-facing rationale should show pairing with iter-155
        // PlayerGiveMoney for "give MY player credits" workflow.
        var note = CapabilityStatusCatalog.Entries["SWFOC_GetLocalPlayerLua"].Note;
        note.Should().Contain("Give_Money");
        note.Should().Contain("PlayerGiveMoney");
    }

    [Fact]
    public void GetSecondsPerGameMinute_NotePinsTimeScaleSemantics()
    {
        var note = CapabilityStatusCatalog.Entries["SWFOC_GetSecondsPerGameMinuteLua"].Note;
        note.Should().Contain("time-scale");
        note.Should().Contain("SetGameSpeed");
    }

    [Fact]
    public void DispatcherMatrixCompleteBatch_AllReuseIter178Helper()
    {
        var iter178Entries = new[]
        {
            "SWFOC_GetGameModeLua",
            "SWFOC_GetLocalPlayerLua",
            "SWFOC_GetSecondsPerGameMinuteLua",
        };
        foreach (var name in iter178Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("iter-178",
                    $"{name} should reference iter-178 global-no-arg-getter helper");
        }
    }

    [Fact]
    public void DispatcherMatrixCompleteBatch_AllTaggedIter178Live()
    {
        var iter178Entries = new[]
        {
            "SWFOC_GetGameModeLua",
            "SWFOC_GetLocalPlayerLua",
            "SWFOC_GetSecondsPerGameMinuteLua",
        };
        foreach (var name in iter178Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 178 LIVE",
                    $"{name} should be tagged as iter 178 LIVE");
        }
    }
}
