using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 170) — pins read-side state-query LIVE batch.
/// Get_Name (player string) + Is_Stealthed/Is_In_Limbo/Is_Capturable
/// (unit booleans) all reuse iter-167 helper. LIVE flips #92-95;
/// master loop now at 95 LIVE wires.
/// Each Is_* wire forms a read-after-write pair with an earlier
/// writer (iter-153/157/156) — operator can verify state changes
/// via these queries.
/// </summary>
public sealed class Iter170StateQueryBatchTests
{
    [Theory]
    [InlineData("SWFOC_GetNameLua")]
    [InlineData("SWFOC_IsStealthedLua")]
    [InlineData("SWFOC_IsInLimboLua")]
    [InlineData("SWFOC_IsCapturableLua")]
    public void StateQueryBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void IsStealthed_NoteContrastsWithIter153EnableStealth()
    {
        CapabilityStatusCatalog.Entries["SWFOC_IsStealthedLua"].Note
            .Should().Contain("EnableStealth");
    }

    [Fact]
    public void IsInLimbo_NoteContrastsWithIter157SetInLimbo()
    {
        CapabilityStatusCatalog.Entries["SWFOC_IsInLimboLua"].Note
            .Should().Contain("SetInLimbo");
    }

    [Fact]
    public void IsCapturable_NoteContrastsWithIter156DisableCapture()
    {
        CapabilityStatusCatalog.Entries["SWFOC_IsCapturableLua"].Note
            .Should().Contain("DisableCapture");
    }

    [Fact]
    public void StateQueryBatch_AllReuseIter167Helper()
    {
        var iter170Entries = new[]
        {
            "SWFOC_GetNameLua",
            "SWFOC_IsStealthedLua",
            "SWFOC_IsInLimboLua",
            "SWFOC_IsCapturableLua",
        };
        foreach (var name in iter170Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("iter-167",
                    $"{name} should reference reuse of iter-167 unit-getter helper");
        }
    }

    [Fact]
    public void StateQueryBatch_AllTaggedIter170()
    {
        var iter170Entries = new[]
        {
            "SWFOC_GetNameLua",
            "SWFOC_IsStealthedLua",
            "SWFOC_IsInLimboLua",
            "SWFOC_IsCapturableLua",
        };
        foreach (var name in iter170Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 170 LIVE",
                    $"{name} should be tagged as iter 170 LIVE in catalog rationale");
        }
    }
}
