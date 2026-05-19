using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 168) — pins read-side getter expansion via iter-167
/// helper. LIVE flips #85-87; master loop now at 87 LIVE wires.
/// Has_Attack_Target / Are_Engines_Online return booleans (string
/// "true"/"false"). Get_Owner returns a PlayerWrapper handle.
/// </summary>
public sealed class Iter168ReadSideExpansionTests
{
    [Theory]
    [InlineData("SWFOC_HasAttackTargetLua")]
    [InlineData("SWFOC_AreEnginesOnlineLua")]
    [InlineData("SWFOC_GetOwnerLua")]
    public void ReadSideExpansion_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void HasAttackTarget_NoteMentionsBooleanReturn()
    {
        // Pin: catalog should explain the boolean stringification
        // semantic so future readers don't expect a numeric result.
        CapabilityStatusCatalog.Entries["SWFOC_HasAttackTargetLua"].Note
            .Should().Contain("boolean");
    }

    [Fact]
    public void AreEnginesOnline_NoteMentionsShipsAndTactical()
    {
        CapabilityStatusCatalog.Entries["SWFOC_AreEnginesOnlineLua"].Note
            .Should().Contain("tactical");
    }

    [Fact]
    public void GetOwner_NoteFlagsHandleReturn()
    {
        // Pin: the 'table: 0x...' return format is non-obvious — the
        // catalog rationale should warn future readers it's a handle
        // confirming the call rather than a human-readable result.
        CapabilityStatusCatalog.Entries["SWFOC_GetOwnerLua"].Note
            .Should().Contain("PlayerWrapper");
    }

    [Fact]
    public void ReadSideExpansion_AllReuseIter167Helper()
    {
        // Pin: all 3 should explicitly reference reuse of iter-167
        // helper. This makes the dispatcher-set genealogy clear.
        var iter168Entries = new[]
        {
            "SWFOC_HasAttackTargetLua",
            "SWFOC_AreEnginesOnlineLua",
            "SWFOC_GetOwnerLua",
        };
        foreach (var name in iter168Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("iter-167",
                    $"{name} should reference reuse of iter-167 unit-getter helper");
        }
    }

    [Fact]
    public void ReadSideExpansion_AllTaggedIter168()
    {
        var iter168Entries = new[]
        {
            "SWFOC_HasAttackTargetLua",
            "SWFOC_AreEnginesOnlineLua",
            "SWFOC_GetOwnerLua",
        };
        foreach (var name in iter168Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 168 LIVE",
                    $"{name} should be tagged as iter 168 LIVE in catalog rationale");
        }
    }
}
