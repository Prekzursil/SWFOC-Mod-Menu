using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-04 (iter 177) — pins discovery LIVE batch shipped via NEW
/// Lua_DispatchGlobalGetterArg helper (8th in dispatcher set; mirrors
/// iter-173 arg-getter shape but for no-receiver globals).
/// LIVE flips #120-122; master loop now at 122 LIVE wires.
/// Find_Object_Type / FindPlanet / Find_First_Object return engine
/// handles for further composition in operator workflows.
/// </summary>
public sealed class Iter177DiscoveryGlobalGetterBatchTests
{
    [Theory]
    [InlineData("SWFOC_FindObjectTypeLua")]
    [InlineData("SWFOC_FindPlanetLua")]
    [InlineData("SWFOC_FindFirstObjectLua")]
    public void DiscoveryBatch_StatusIsLive(string entryName)
    {
        CapabilityStatusCatalog.Entries[entryName].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void FindObjectType_NotePinsAsFirstWireViaNewHelper()
    {
        // Pin: catalog rationale should call out that this is the FIRST
        // wire shipped via the iter-177 global-getter-with-arg helper.
        // Future readers need to know the helper exists before reusing.
        CapabilityStatusCatalog.Entries["SWFOC_FindObjectTypeLua"].Note
            .Should().Contain("NEW iter-177");
    }

    [Fact]
    public void FindObjectType_NoteIncludesCompositionExample()
    {
        // Pin: operator-facing rationale should illustrate composition
        // (e.g. Spawn_Unit + Find_Object_Type), not just describe in isolation.
        CapabilityStatusCatalog.Entries["SWFOC_FindObjectTypeLua"].Note
            .Should().Contain("Spawn_Unit");
    }

    [Fact]
    public void FindPlanet_NoteIncludesCompositionExample()
    {
        CapabilityStatusCatalog.Entries["SWFOC_FindPlanetLua"].Note
            .Should().Contain("Launch_Units");
    }

    [Fact]
    public void DiscoveryBatch_AllReuseIter177Helper()
    {
        var iter177Entries = new[]
        {
            "SWFOC_FindObjectTypeLua",
            "SWFOC_FindPlanetLua",
            "SWFOC_FindFirstObjectLua",
        };
        foreach (var name in iter177Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("iter-177",
                    $"{name} should reference iter-177 global-getter helper");
        }
    }

    [Fact]
    public void DiscoveryBatch_AllTaggedIter177()
    {
        var iter177Entries = new[]
        {
            "SWFOC_FindObjectTypeLua",
            "SWFOC_FindPlanetLua",
            "SWFOC_FindFirstObjectLua",
        };
        foreach (var name in iter177Entries)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("Iter 177 LIVE",
                    $"{name} should be tagged as iter 177 LIVE");
        }
    }
}
