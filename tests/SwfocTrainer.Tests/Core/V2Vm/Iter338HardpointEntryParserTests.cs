using FluentAssertions;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.Core.V2Vm;

/// <summary>
/// 2026-05-07 (iter 338): pin tests for HardpointEntry.ParseListFromBridgeReply.
/// Validates the operator-facing parser layer above SWFOC_GetHardpoints
/// bridge wire's raw "count=N child0=0x... hp0=..." format. iter-336 preflight
/// confirmed the wire returns child addresses + HP (not weapon names) — this
/// parser is the foundation for the iter-338 Combat tab Hardpoint Inspector.
/// Defers icon resolution to iter-339+ (would need 2-bridge-call chain).
/// </summary>
public sealed class Iter338HardpointEntryParserTests
{
    [Fact]
    public void ParseListFromBridgeReply_NullInput_ReturnsEmpty()
    {
        var result = HardpointEntry.ParseListFromBridgeReply(null);
        result.Should().BeEmpty(because: "defensive null-safe for operator-facing parser");
    }

    [Fact]
    public void ParseListFromBridgeReply_EmptyInput_ReturnsEmpty()
    {
        HardpointEntry.ParseListFromBridgeReply("").Should().BeEmpty();
        HardpointEntry.ParseListFromBridgeReply("   ").Should().BeEmpty();
    }

    [Fact]
    public void ParseListFromBridgeReply_CountZero_ReturnsEmpty()
    {
        // Bridge returns "count=0" when unit has no hardpoints (or empty Components array).
        var result = HardpointEntry.ParseListFromBridgeReply("count=0");
        result.Should().BeEmpty(because: "count=0 sentinel means no hardpoints");
    }

    [Fact]
    public void ParseListFromBridgeReply_SingleHardpoint_ReturnsOneEntry()
    {
        // Format per lua_bridge.cpp:2228 SafeAppendFmt output.
        var raw = "count=1 child0=0x000000014001ABCD hp0=750.500";
        var result = HardpointEntry.ParseListFromBridgeReply(raw);

        result.Should().HaveCount(1, because: "1 hardpoint = 1 entry");
        var entry = result[0];
        entry.Index.Should().Be(0);
        entry.ChildAddr.Should().Be(0x14001ABCD, because: "hex parse must preserve 64-bit pointer");
        entry.Hp.Should().Be(750.5f);
    }

    [Fact]
    public void ParseListFromBridgeReply_MultipleHardpoints_ReturnsAllInOrder()
    {
        var raw = "count=3 child0=0x0000000140012340 hp0=100.000" +
                  " child1=0x0000000140012358 hp1=85.250" +
                  " child2=0x0000000140012370 hp2=42.125";
        var result = HardpointEntry.ParseListFromBridgeReply(raw);

        result.Should().HaveCount(3);
        result[0].Index.Should().Be(0);
        result[0].ChildAddr.Should().Be(0x140012340);
        result[0].Hp.Should().Be(100f);
        result[1].Index.Should().Be(1);
        result[1].ChildAddr.Should().Be(0x140012358);
        result[1].Hp.Should().Be(85.25f);
        result[2].Index.Should().Be(2);
        result[2].ChildAddr.Should().Be(0x140012370);
        result[2].Hp.Should().Be(42.125f);
    }

    [Fact]
    public void ParseListFromBridgeReply_Malformed_ReturnsEmpty_DoesNotThrow()
    {
        // Defensive: garbage input → empty list, not exception.
        var act = () => HardpointEntry.ParseListFromBridgeReply("garbage random text 123");
        act.Should().NotThrow();
        HardpointEntry.ParseListFromBridgeReply("garbage random text 123")
            .Should().BeEmpty(because: "no count= sentinel = no parse");
    }

    [Fact]
    public void ParseListFromBridgeReply_NegativeHp_Allowed()
    {
        // iter-285 Tier 3 overlay observed negative HP for dead-but-not-cleaned hardpoints.
        var raw = "count=1 child0=0x0000000140000100 hp0=-1.000";
        var result = HardpointEntry.ParseListFromBridgeReply(raw);
        result.Should().HaveCount(1);
        result[0].Hp.Should().Be(-1f);
    }

    [Fact]
    public void ParseListFromBridgeReply_PartialEntries_ParsesValidOnly()
    {
        // If bridge reply has count=3 but only 2 properly-formatted child/hp pairs,
        // parser should return the 2 valid entries (defensive degradation).
        var raw = "count=3 child0=0x0000000140000100 hp0=50.0 child1=0x0000000140000200 hp1=25.0 child2=BROKEN";
        var result = HardpointEntry.ParseListFromBridgeReply(raw);
        result.Should().HaveCount(2,
            because: "regex skips malformed pair tokens; valid prefix entries still surface");
        result[0].Index.Should().Be(0);
        result[1].Index.Should().Be(1);
    }
}
