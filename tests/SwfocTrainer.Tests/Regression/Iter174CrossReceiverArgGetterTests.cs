using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-04 (iter 174) — pins cross-receiver arg-getter LIVE batch:
/// Get_Bone_Position (unit, binary-confirmed), Contains_Object_Type
/// (unit, community-doc), Get_Space_Station_Level (player, community-doc),
/// Get_Type_Of_Unit (TaskForce, binary-confirmed). All four reuse the
/// iter-173 helper, demonstrating it is fully receiver-agnostic for
/// `(obj):method(arg)` shape regardless of static type. LIVE flips
/// #108-111; master loop now at 111 LIVE wires.
/// </summary>
public sealed class Iter174CrossReceiverArgGetterTests
{
    [Theory]
    [InlineData("SWFOC_GetBonePositionLua")]
    [InlineData("SWFOC_ContainsObjectTypeLua")]
    [InlineData("SWFOC_GetSpaceStationLevelLua")]
    [InlineData("SWFOC_GetTypeOfUnitLua")]
    public void CrossReceiverBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void GetTypeOfUnit_NoteIdentifiesAsFirstTaskForceWire()
    {
        // Pin: catalog should call out this is the first TaskForce-receiver
        // wire shipped via iter-173 helper. Demonstrates receiver-agnostic
        // dispatch for future readers.
        CapabilityStatusCatalog.Entries["SWFOC_GetTypeOfUnitLua"].Note
            .Should().Contain("TaskForce");
    }

    [Fact]
    public void GetSpaceStationLevel_NoteFlagsPlayerReceiver()
    {
        // Pin: player-receiver semantics must survive in catalog.
        CapabilityStatusCatalog.Entries["SWFOC_GetSpaceStationLevelLua"].Note
            .Should().Contain("PlayerWrapper");
    }

    [Fact]
    public void ContainsObjectType_NoteMentionsGarrisonUseCase()
    {
        // Pin: operator-facing rationale (garrison-content query).
        CapabilityStatusCatalog.Entries["SWFOC_ContainsObjectTypeLua"].Note
            .Should().Contain("garrison");
    }

    [Fact]
    public void GetBonePosition_NoteIdentifiesAsBinaryConfirmed()
    {
        // Pin: docs source (binary-confirmed Movement & Position section).
        CapabilityStatusCatalog.Entries["SWFOC_GetBonePositionLua"].Note
            .Should().Contain("Movement");
    }

    [Fact]
    public void CrossReceiverBatch_AllReuseIter173Helper()
    {
        var iter174Entries = new[]
        {
            "SWFOC_GetBonePositionLua",
            "SWFOC_ContainsObjectTypeLua",
            "SWFOC_GetSpaceStationLevelLua",
            "SWFOC_GetTypeOfUnitLua",
        };
        foreach (var name in iter174Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("iter-173",
                    $"{name} should reference iter-173 helper reuse");
        }
    }

    [Fact]
    public void CrossReceiverBatch_AllTaggedIter174()
    {
        var iter174Entries = new[]
        {
            "SWFOC_GetBonePositionLua",
            "SWFOC_ContainsObjectTypeLua",
            "SWFOC_GetSpaceStationLevelLua",
            "SWFOC_GetTypeOfUnitLua",
        };
        foreach (var name in iter174Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 174 LIVE",
                    $"{name} should be tagged as iter 174 LIVE");
        }
    }
}
