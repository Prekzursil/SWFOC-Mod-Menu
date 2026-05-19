using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 163) — pins combat-order LIVE batch (Attack_Target,
/// Guard_Target, Divert) shipped via existing iter-154 generic 2-arg
/// helper. LIVE flips #70-72; master loop now at 72 LIVE wires.
/// </summary>
public sealed class Iter163CombatOrderBatchTests
{
    [Theory]
    [InlineData("SWFOC_AttackTargetLua")]
    [InlineData("SWFOC_GuardTargetLua")]
    [InlineData("SWFOC_DivertLua")]
    public void CombatOrderBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void AttackTarget_NoteDescribesAttackOrder()
    {
        CapabilityStatusCatalog.Entries["SWFOC_AttackTargetLua"].Note
            .Should().Contain("attack");
    }

    [Fact]
    public void GuardTarget_NoteDescribesDefensiveEscort()
    {
        CapabilityStatusCatalog.Entries["SWFOC_GuardTargetLua"].Note
            .Should().Contain("guard");
    }

    [Fact]
    public void Divert_NoteMentionsPositionArg()
    {
        CapabilityStatusCatalog.Entries["SWFOC_DivertLua"].Note
            .Should().Contain("position");
    }

    [Fact]
    public void CombatOrderBatch_AllTaggedIter163()
    {
        var iter163Entries = new[]
        {
            "SWFOC_AttackTargetLua",
            "SWFOC_GuardTargetLua",
            "SWFOC_DivertLua",
        };
        foreach (var name in iter163Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 163 LIVE",
                    $"{name} should be tagged as iter 163 LIVE in catalog rationale");
        }
    }
}
