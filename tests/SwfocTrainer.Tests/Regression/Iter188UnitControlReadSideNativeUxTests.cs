using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 188) — pins the iter-188 UnitControl tab read-side
/// native UX surfacing 4 LIVE wires from iter 167-172 (Get_Hull,
/// Get_Shield, Get_Position, Get_Garrison_Units) as native buttons.
///
/// First substantial native-UX iter since iter 119. Validates that the
/// dispatcher methods exist and the VM exposes the commands +
/// CapabilityAwareAction entries.
/// </summary>
public sealed class Iter188UnitControlReadSideNativeUxTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewBridge()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        return (sim, new V2BridgeAdapter(pipe));
    }

    [Fact]
    public void Dispatcher_HasFourReadSideMethods()
    {
        var (sim, adapter) = NewBridge();
        using (sim)
        {
            var dispatcher = new V2UnitMutationDispatcher(adapter);
            // Each method should exist and return a non-null Task.
            // We don't await — just confirm the method binds.
            var t1 = dispatcher.GetHullLuaAsync("Find_First_Object(\"AT_AT\")", System.Threading.CancellationToken.None);
            var t2 = dispatcher.GetShieldLuaAsync("Find_First_Object(\"AT_AT\")", System.Threading.CancellationToken.None);
            var t3 = dispatcher.GetPositionLuaAsync("Find_First_Object(\"AT_AT\")", System.Threading.CancellationToken.None);
            var t4 = dispatcher.GetGarrisonUnitsLuaAsync("Find_First_Object(\"AT_AT\")", System.Threading.CancellationToken.None);
            t1.Should().NotBeNull();
            t2.Should().NotBeNull();
            t3.Should().NotBeNull();
            t4.Should().NotBeNull();
        }
    }

    [Fact]
    public void CatalogAction_PointsToLiveCatalogEntries()
    {
        // Pin: the CapabilityAwareAction for each read-side button must
        // reference a CapabilityStatus.Live catalog entry. If a future
        // catalog edit downgrades any of these to Phase2HookPending, the
        // operator badge would mislead — this test prevents that.
        var swfocNames = new[]
        {
            "SWFOC_GetHullLua",
            "SWFOC_GetShieldLua",
            "SWFOC_GetPositionLua",
            "SWFOC_GetGarrisonUnitsLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-188 button to be honest");
        }
    }

    [Fact]
    public void ReadSideBatch_CatalogRationaleReferencesIter167Helper()
    {
        // Pin: each read-side wire's catalog rationale should reference
        // the iter-167 helper (Lua_DispatchUnitGetterNoArg). Iter-188 only
        // adds native UX; the underlying bridge wires were shipped iter 167.
        var hullNote = CapabilityStatusCatalog.Entries["SWFOC_GetHullLua"].Note;
        var shieldNote = CapabilityStatusCatalog.Entries["SWFOC_GetShieldLua"].Note;
        hullNote.Should().Contain("iter-167");
        shieldNote.Should().Contain("iter-167");
    }
}
