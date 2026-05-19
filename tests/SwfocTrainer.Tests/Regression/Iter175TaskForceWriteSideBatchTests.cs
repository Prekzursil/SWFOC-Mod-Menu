using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-04 (iter 175) — pins TaskForce write-side LIVE batch
/// (Move_To / Reinforce / Release_Reinforcements / Launch_Units).
/// All four use the TaskForce-prefixed SWFOC_* naming convention to
/// disambiguate from the iter-157 unit-method version of Move_To.
/// LIVE flips #112-115; master loop now at 115 LIVE wires.
/// </summary>
public sealed class Iter175TaskForceWriteSideBatchTests
{
    [Theory]
    [InlineData("SWFOC_TaskForceMoveToLua")]
    [InlineData("SWFOC_TaskForceReinforceLua")]
    [InlineData("SWFOC_TaskForceReleaseReinforcementsLua")]
    [InlineData("SWFOC_TaskForceLaunchUnitsLua")]
    public void TaskForceWriteBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void TaskForceMoveTo_NoteContrastsWithIter157UnitMoveTo()
    {
        // Pin: the TaskForce-prefixed naming convention only makes sense
        // if the catalog rationale explicitly contrasts it with the
        // unit-method version. If a future refactor collapses the names,
        // this test fires.
        CapabilityStatusCatalog.Entries["SWFOC_TaskForceMoveToLua"].Note
            .Should().Contain("iter-157");
    }

    [Fact]
    public void TaskForceReleaseReinforcements_NoteFlagsSpaceTaskForceVariant()
    {
        // Pin: per docs/lua-api.md, Release_Reinforcements is specifically
        // a SpaceTaskForce method — operator should know the engine-side
        // restriction.
        CapabilityStatusCatalog.Entries["SWFOC_TaskForceReleaseReinforcementsLua"].Note
            .Should().Contain("SpaceTaskForce");
    }

    [Fact]
    public void TaskForceLaunchUnits_NoteFlagsGalacticVariant()
    {
        // Pin: Launch_Units is GalacticTaskForce-specific. Mismatched
        // receiver type at the engine level fails silently — catalog
        // should warn.
        CapabilityStatusCatalog.Entries["SWFOC_TaskForceLaunchUnitsLua"].Note
            .Should().Contain("GalacticTaskForce");
    }

    [Fact]
    public void TaskForceWriteBatch_AllTaggedIter175()
    {
        var iter175Entries = new[]
        {
            "SWFOC_TaskForceMoveToLua",
            "SWFOC_TaskForceReinforceLua",
            "SWFOC_TaskForceReleaseReinforcementsLua",
            "SWFOC_TaskForceLaunchUnitsLua",
        };
        foreach (var name in iter175Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 175 LIVE",
                    $"{name} should be tagged as iter 175 LIVE");
        }
    }

    [Fact]
    public void TaskForceMoveTo_AndUnitMoveTo_AreDistinctEntries()
    {
        // Pin: the iter-157 unit Move_To and iter-175 TaskForce Move_To
        // are distinct catalog entries. Both should be LIVE; collapsing
        // them would lose the receiver semantics.
        CapabilityStatusCatalog.Entries["SWFOC_MoveToLua"].Status
            .Should().Be(CapabilityStatus.Live, "iter-157 unit Move_To stays LIVE");
        CapabilityStatusCatalog.Entries["SWFOC_TaskForceMoveToLua"].Status
            .Should().Be(CapabilityStatus.Live, "iter-175 TaskForce Move_To stays LIVE");
    }
}
