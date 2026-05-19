using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 129) — SetUnitShield catalog flip from
/// <see cref="CapabilityStatus.Phase2HookPending"/> → <see cref="CapabilityStatus.Live"/>.
/// <para>
/// Iter 105 (2026-04-28) wrongly deferred this as "XML-attribute-only,
/// needs RTTI dissection". Iter 128 (2026-04-29) re-audit using the
/// iter-124-fixed callgraph CLI found the verified ledger ALREADY had
/// <c>rva_set_front_shield</c> @ <c>0x3A8630</c> and <c>rva_set_rear_shield</c>
/// @ <c>0x3A91E0</c>, both with <c>void __fastcall(__int64 unit, float val)</c>
/// — the same shape as iter 100's SetSpeedOverride. Iter 129 ships the
/// LIVE wire (this test pins the catalog flip + the engine RVAs the
/// bridge uses).
/// </para>
/// <para>
/// Red-green semantic: this test FAILS on the iter-105 PHASE 2 PENDING
/// shape AND PASSES on the iter-129 LIVE shape, locking in the flip.
/// </para>
/// </summary>
public sealed class Iter129SetUnitShieldTests
{
    [Fact]
    public void SetUnitShield_IsLive_NotPhase2Pending()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitShield"];
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 129 flipped SetUnitShield LIVE via SetFrontShield + SetRearShield " +
            "engine helpers (rva_set_front_shield @ 0x3A8630 + rva_set_rear_shield " +
            "@ 0x3A91E0). Iter 105's 'XML-attribute-only, defer' finding was wrong; " +
            "iter 128 callgraph re-audit found the helpers already in the verified " +
            "ledger.");
    }

    [Fact]
    public void SetUnitShield_NoteReferencesEngineHelpers()
    {
        // The catalog Note must mention the actual RVAs the bridge calls
        // so the operator-facing tooltip surfaces them. Locks in
        // operator-trust narrative — if someone later silently flips the
        // wire to a different mechanism, the Note drift fires this test.
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetUnitShield"];
        entry.Note.Should().Contain("SetFrontShield");
        entry.Note.Should().Contain("SetRearShield");
        entry.Note.Should().Contain("0x3A8630");
        entry.Note.Should().Contain("0x3A91E0");
        entry.Note.Should().Contain("LIVE");
    }

    [Fact]
    public void SetUnitShield_Badge_Reads_LIVE()
    {
        // Operator surface check: the badge string seen in the editor's
        // CapabilityAwareAction tooltip must surface "LIVE".
        var badge = CapabilityStatusCatalog.ComposeBadge("SWFOC_SetUnitShield");
        badge.Should().Be("LIVE");
    }
}
