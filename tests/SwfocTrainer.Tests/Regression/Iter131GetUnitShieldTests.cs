using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 131) — GetUnitShield reader catalog flip from
/// <see cref="CapabilityStatus.Phase2HookPending"/> → <see cref="CapabilityStatus.Live"/>.
/// <para>
/// Iter 129 flipped the writer LIVE via SetFrontShield/SetRearShield.
/// Iter 131 closes the read pair: <c>FrontShield_Read</c> @ RVA
/// <c>0x3963C0</c> is <c>double __fastcall(__int64 unit)</c> — clean
/// engine reader that was already pinned in the verified ledger as
/// <c>rva_front_shield_read</c>. Pre-iter-131 the bridge's
/// <c>Lua_GetUnitShield</c> read from a stale cache map (returning -1
/// for any unit that hadn't been written through the iter-129 setter).
/// </para>
/// </summary>
public sealed class Iter131GetUnitShieldTests
{
    [Fact]
    public void GetUnitShield_IsLive_NotPhase2Pending()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_GetUnitShield"];
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 131 flipped GetUnitShield LIVE via FrontShield_Read engine " +
            "call (rva_front_shield_read @ 0x3963C0). Pre-iter-131 returned " +
            "stale cache value (-1 for un-written units).");
    }

    [Fact]
    public void GetUnitShield_NoteReferencesEngineReader()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_GetUnitShield"];
        entry.Note.Should().Contain("FrontShield_Read");
        entry.Note.Should().Contain("0x3963C0");
        entry.Note.Should().Contain("LIVE");
    }

    [Fact]
    public void GetUnitShield_Badge_Reads_LIVE()
    {
        var badge = CapabilityStatusCatalog.ComposeBadge("SWFOC_GetUnitShield");
        badge.Should().Be("LIVE");
    }

    [Fact]
    public void ShieldRead_AndWrite_BothLIVE_FormsCompletePair()
    {
        // Pair invariant: iter 129 made SetUnitShield LIVE; iter 131 makes
        // the read partner LIVE. Both must be LIVE for the operator's
        // "round-trip a shield value" workflow to give honest results.
        CapabilityStatusCatalog.Entries["SWFOC_SetUnitShield"]
            .Status.Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_GetUnitShield"]
            .Status.Should().Be(CapabilityStatus.Live);
    }
}
