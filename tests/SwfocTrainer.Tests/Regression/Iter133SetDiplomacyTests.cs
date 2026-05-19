using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 133) — SetDiplomacy catalog flip from
/// <see cref="CapabilityStatus.Phase2HookPending"/> → <see cref="CapabilityStatus.Live"/>.
/// <para>
/// Iter 132's Phase2HookPending audit pass identified SetDiplomacy as a
/// strong drift candidate: ledger had <c>rva_make_ally_make_enemy_engine</c>
/// @ <c>0x288800</c> = <c>__int64 __fastcall(PlayerClass*, int target_slot,
/// int state_code)</c> already pinned, with Lua wrappers at <c>0x6046A0</c>
/// (Make_Ally) and <c>0x604780</c> (Make_Enemy) confirming the calling
/// shape. Bridge <c>Lua_SetDiplomacy</c> was still Phase-1 mirror writing
/// to <c>g_pendingDiplomacyWrites</c>. Iter 133 shipped the LIVE wire:
/// bridge walks <c>PlayerArray_Global</c> for slot_a, calls engine writer
/// with state codes 0=ally, 1=enemy, 2=neutral.
/// </para>
/// </summary>
public sealed class Iter133SetDiplomacyTests
{
    [Fact]
    public void SetDiplomacy_IsLive_NotPhase2Pending()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetDiplomacy"];
        entry.Status.Should().Be(CapabilityStatus.Live,
            "iter 133 flipped SetDiplomacy LIVE via MakeAllyEnemy engine writer " +
            "at 0x288800. Iter 132 audit caught the catalog drift; iter 133 " +
            "shipped the bridge call (PlayerArray walker + Resolve<>() pattern).");
    }

    [Fact]
    public void SetDiplomacy_NoteReferencesEngineWriter()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetDiplomacy"];
        entry.Note.Should().Contain("MakeAllyEnemy");
        entry.Note.Should().Contain("0x288800");
        entry.Note.Should().Contain("LIVE");
    }

    [Fact]
    public void SetDiplomacy_NoteCallsOutOneWayScope()
    {
        // Operator-trust signal: the Note clarifies symmetry must be
        // requested explicitly (two calls). Engine writer is one-way
        // per the Lua wrapper at 0x6046A0 (PlayerWrapper::Make_Ally
        // only writes from this->target, not target->this).
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetDiplomacy"];
        entry.Note.Should().Contain("One-way");
        entry.Note.Should().Contain("symmetric",
            "operator should know to call twice for mutual ally/enemy");
    }

    [Fact]
    public void SetDiplomacy_NoteEnumeratesSupportedStates()
    {
        var entry = CapabilityStatusCatalog.Entries["SWFOC_SetDiplomacy"];
        entry.Note.Should().Contain("ally");
        entry.Note.Should().Contain("enemy");
        entry.Note.Should().Contain("neutral");
    }

    [Fact]
    public void SetDiplomacy_Badge_Reads_LIVE()
    {
        var badge = CapabilityStatusCatalog.ComposeBadge("SWFOC_SetDiplomacy");
        badge.Should().Be("LIVE");
    }
}
