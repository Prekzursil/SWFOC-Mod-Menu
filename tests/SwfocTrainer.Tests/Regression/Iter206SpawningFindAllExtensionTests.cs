using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 206) — pins the iter-179 SWFOC_FindAllObjectsOfTypeLua
/// 5th button extension to the iter-203 Spawning Discovery helpers
/// GroupBox. Completes the "first / nearest / all" discovery trio
/// alongside iter-203 FindFirstObject (single instance) and iter-186
/// FindNearest (closest instance).
///
/// Reuses the iter-203 FindTypeNameLuaExpr field — no new input.
/// 1-arg via existing BuildUnitLuaNoArgCall (regex-invisible to the
/// reverse-orphan snapshot, so no snapshot churn).
/// </summary>
public sealed class Iter206SpawningFindAllExtensionTests
{
    [Fact]
    public void DispatcherMethod_BindsToCorrectSwfocName()
    {
        var t = typeof(V2UnitMutationDispatcher);
        t.GetMethod(nameof(V2UnitMutationDispatcher.FindAllObjectsOfTypeLuaAsync))
            .Should().NotBeNull("Spawning 'Find all of type' button binds to FindAllObjectsOfTypeLuaAsync");
    }

    [Fact]
    public void CatalogEntry_RemainsLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_FindAllObjectsOfTypeLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_DocumentsIter206Surfacing()
    {
        var note = CapabilityStatusCatalog.Entries["SWFOC_FindAllObjectsOfTypeLua"].Note;
        note.Should().Contain("Iter 206");
        note.Should().Contain("Spawning");
        note.Should().Contain("first / nearest / all",
            "rationale must reference the operator-facing trio framing");
    }

    [Fact]
    public void CatalogRationale_PreservesIter179DiscoveryComplementFinding()
    {
        // Pin: extending the rationale with iter-206 surfacing must NOT
        // lose the iter-179 framing that this is a discovery complement
        // to iter-177 FindFirstObject. That distinction (single vs. all)
        // is load-bearing for operators choosing between buttons.
        var note = CapabilityStatusCatalog.Entries["SWFOC_FindAllObjectsOfTypeLua"].Note;
        note.Should().Contain("Find_First_Object");
        note.Should().Contain("complement");
    }

    [Fact]
    public void Vm_ExposesFindAllObjectsOfTypeCommandAndCapability()
    {
        var t = typeof(SwfocTrainer.App.V2.ViewModels.SpawningTabViewModel);
        var cmd = t.GetProperty("FindAllObjectsOfTypeLuaCommand");
        var action = t.GetProperty("FindAllObjectsOfTypeLua");
        cmd.Should().NotBeNull("Spawning 'Find all of type' button binds to FindAllObjectsOfTypeLuaCommand");
        action.Should().NotBeNull("Spawning Discovery helpers GroupBox capability surface includes FindAllObjectsOfTypeLua");
    }
}
