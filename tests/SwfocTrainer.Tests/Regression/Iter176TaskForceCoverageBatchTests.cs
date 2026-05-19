using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-04 (iter 176) — pins TaskForce coverage extension batch:
/// Attack_Target / Guard_Target (variants of iter-163 unit commands),
/// Land_Units (GalacticTaskForce complement to iter-175 LaunchUnits),
/// Set_As_Goal_System_Removable (TaskForceClass bool flag, iter-111
/// helper). LIVE flips #116-119; master loop now at 119 LIVE wires.
/// </summary>
public sealed class Iter176TaskForceCoverageBatchTests
{
    [Theory]
    [InlineData("SWFOC_TaskForceAttackTargetLua")]
    [InlineData("SWFOC_TaskForceGuardTargetLua")]
    [InlineData("SWFOC_TaskForceLandUnitsLua")]
    [InlineData("SWFOC_TaskForceSetAsGoalSystemRemovableLua")]
    public void TaskForceCoverageBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void TaskForceAttackTarget_NoteContrastsWithIter163UnitVersion()
    {
        CapabilityStatusCatalog.Entries["SWFOC_TaskForceAttackTargetLua"].Note
            .Should().Contain("iter-163");
    }

    [Fact]
    public void TaskForceLandUnits_NotePairsWithIter175LaunchUnits()
    {
        // Pin: launch/land complementarity must survive in catalog.
        CapabilityStatusCatalog.Entries["SWFOC_TaskForceLandUnitsLua"].Note
            .Should().Contain("LaunchUnits");
    }

    [Fact]
    public void TaskForceSetAsGoalSystemRemovable_NoteFlagsAiGoalUseCase()
    {
        // Pin: AI-internal flag — operator should know this is for
        // AI goal cleanup, not gameplay control.
        CapabilityStatusCatalog.Entries["SWFOC_TaskForceSetAsGoalSystemRemovableLua"].Note
            .Should().Contain("AI goal");
    }

    [Fact]
    public void TaskForceGuardTarget_NoteFlagsSpaceTaskForceConstraint()
    {
        // Pin: SpaceTaskForce-only constraint per docs/lua-api.md.
        CapabilityStatusCatalog.Entries["SWFOC_TaskForceGuardTargetLua"].Note
            .Should().Contain("SpaceTaskForce");
    }

    [Fact]
    public void TaskForceCoverageBatch_AllTaggedIter176()
    {
        var iter176Entries = new[]
        {
            "SWFOC_TaskForceAttackTargetLua",
            "SWFOC_TaskForceGuardTargetLua",
            "SWFOC_TaskForceLandUnitsLua",
            "SWFOC_TaskForceSetAsGoalSystemRemovableLua",
        };
        foreach (var name in iter176Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 176 LIVE",
                    $"{name} should be tagged as iter 176 LIVE");
        }
    }
}
