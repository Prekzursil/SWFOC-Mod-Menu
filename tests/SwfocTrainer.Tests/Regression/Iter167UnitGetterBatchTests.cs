using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 167) — pins read-side LIVE batch shipped via NEW
/// Lua_DispatchUnitGetterNoArg helper (6th in dispatcher set; first
/// helper to capture engine return values). LIVE flips #82-84;
/// master loop now at 84 LIVE wires.
/// Get_Hull / Get_Health / Get_Shield form the canonical health
/// query trio per docs/lua-api.md GameObjectWrapper Health &amp; Combat.
/// </summary>
public sealed class Iter167UnitGetterBatchTests
{
    [Theory]
    [InlineData("SWFOC_GetHullLua")]
    [InlineData("SWFOC_GetHealthLua")]
    [InlineData("SWFOC_GetShieldLua")]
    public void UnitGetterBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void GetHull_NotePinsAsFirstWireViaNewGetterHelper()
    {
        // Pin: catalog rationale should explicitly call out that this
        // is the first wire shipped via the iter-167 unit-getter
        // helper. The previous 5 helpers all DISCARD return values;
        // iter-167 is the first to CAPTURE them. Future readers must
        // know to use this helper for read-side operations.
        CapabilityStatusCatalog.Entries["SWFOC_GetHullLua"].Note
            .Should().Contain("NEW iter-167");
    }

    [Fact]
    public void GetHull_NoteEmphasizesReturnValueCapture()
    {
        // Pin: the value-capture semantic is the key differentiator
        // for this helper class. Future iter shouldn't accidentally
        // collapse this back into a discarding helper.
        CapabilityStatusCatalog.Entries["SWFOC_GetHullLua"].Note
            .Should().Contain("CAPTURES");
    }

    [Fact]
    public void GetShield_NoteContrastsWithIter129SetUnitShield()
    {
        // Pin: the read/write pairing relationship with iter-129 should
        // survive in the catalog rationale. Operators benefit from
        // knowing both directions exist.
        CapabilityStatusCatalog.Entries["SWFOC_GetShieldLua"].Note
            .Should().Contain("SetUnitShield");
    }

    [Fact]
    public void UnitGetterBatch_AllTaggedIter167()
    {
        var iter167Entries = new[]
        {
            "SWFOC_GetHullLua",
            "SWFOC_GetHealthLua",
            "SWFOC_GetShieldLua",
        };
        foreach (var name in iter167Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 167 LIVE",
                    $"{name} should be tagged as iter 167 LIVE in catalog rationale");
        }
    }
}
