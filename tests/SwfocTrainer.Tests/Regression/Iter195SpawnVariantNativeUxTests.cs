using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 195) — pins Spawning tab spawn variant extension for
/// iter 185 wires (Reinforce_Unit, Spawn_From_Reinforcement_Pool,
/// Create_Generic_Object). Extends the existing iter-119 GroupBox with 3
/// more buttons reusing SpawnPlayerLuaExpr/SpawnTypeLuaExpr/SpawnPositionLuaExpr.
///
/// CRITICAL pin: Create_Generic_Object has DIFFERENT param order
/// (type, position, player) vs Spawn_Unit (player, type, position).
/// Dispatcher signature mirrors engine; UI reorders the fields so operators
/// don't have to re-type.
/// </summary>
public sealed class Iter195SpawnVariantNativeUxTests
{
    [Fact]
    public void DispatcherMethods_BindToCorrectSwfocNames()
    {
        var reinforce = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.ReinforceUnitLuaAsync));
        var pool = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.SpawnFromReinforcementPoolLuaAsync));
        var generic = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.CreateGenericObjectLuaAsync));

        reinforce.Should().NotBeNull("Spawning tab Reinforce button binds to ReinforceUnitLuaAsync");
        pool.Should().NotBeNull("Spawning tab SpawnFromPool button binds to SpawnFromReinforcementPoolLuaAsync");
        generic.Should().NotBeNull("Spawning tab CreateGenericObject button binds to CreateGenericObjectLuaAsync");
    }

    [Fact]
    public void CatalogAction_AllThreeEntriesAreLive()
    {
        var swfocNames = new[]
        {
            "SWFOC_ReinforceUnitLua",
            "SWFOC_SpawnFromReinforcementPoolLua",
            "SWFOC_CreateGenericObjectLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-195 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_DocumentsIter195Surfacing()
    {
        var reinforce = CapabilityStatusCatalog.Entries["SWFOC_ReinforceUnitLua"].Note;
        var pool = CapabilityStatusCatalog.Entries["SWFOC_SpawnFromReinforcementPoolLua"].Note;
        var generic = CapabilityStatusCatalog.Entries["SWFOC_CreateGenericObjectLua"].Note;

        reinforce.Should().Contain("Iter 195");
        pool.Should().Contain("Iter 195");
        generic.Should().Contain("Iter 195");
    }

    [Fact]
    public void DispatcherSignature_PinsCreateGenericObjectParamOrder()
    {
        // Pin: CreateGenericObjectLuaAsync MUST have parameter order
        // (typeLuaExpr, positionLuaExpr, playerLuaExpr) — matches engine API
        // exactly. Reordering this would silently break operator workflows
        // because the dispatcher swaps args internally to keep the UI
        // player-first.
        var method = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.CreateGenericObjectLuaAsync));
        method.Should().NotBeNull();
        var parameters = method!.GetParameters();
        parameters.Should().HaveCount(4, "method takes 3 string args + CancellationToken");
        parameters[0].Name.Should().Be("typeLuaExpr",
            "GOTCHA: param[0] must be type (engine API order)");
        parameters[1].Name.Should().Be("positionLuaExpr",
            "GOTCHA: param[1] must be position (engine API order)");
        parameters[2].Name.Should().Be("playerLuaExpr",
            "GOTCHA: param[2] must be player (engine API order — DIFFERS from Spawn_Unit)");
    }

    [Fact]
    public void CatalogRationale_CreateGenericObjectFlagsParamOrderGotcha()
    {
        // Pin: CreateGenericObject rationale must explicitly call out the
        // param-order divergence from Spawn_Unit. iter-185 already had this
        // pin; iter-195 adds a UX reference that mentions the dispatcher
        // reorders internally so the UI stays player-first.
        var note = CapabilityStatusCatalog.Entries["SWFOC_CreateGenericObjectLua"].Note;
        note.Should().Contain("GOTCHA");
        note.Should().Contain("param order differs from Spawn_Unit");
        note.Should().Contain("(type, position, player)");
        note.Should().Contain("Iter 195");
        note.Should().Contain("dispatcher reorders");
    }

    [Fact]
    public void BuildCommandHelpers_ProduceCorrectWireFormat()
    {
        // Pin: BuildSpawnVariantPlayerTypePosCommand emits the engine-order
        // 3-arg form for Reinforce/SpawnFromPool. BuildCreateGenericObjectCommand
        // emits the (type, position, player) form even though the dispatcher
        // method signature accepts the engine-order arguments in that exact order.
        var reinforce = V2UnitMutationDispatcher.BuildSpawnVariantPlayerTypePosCommand(
            "SWFOC_ReinforceUnitLua",
            "Find_Player(\"REBEL\")",
            "Find_Object_Type(\"Rebel_Trooper_Squad\")",
            "Create_Position(0, 0, 0)");
        reinforce.Should().Be(
            "return SWFOC_ReinforceUnitLua('Find_Player(\"REBEL\")', 'Find_Object_Type(\"Rebel_Trooper_Squad\")', 'Create_Position(0, 0, 0)')");

        var generic = V2UnitMutationDispatcher.BuildCreateGenericObjectCommand(
            "Find_Object_Type(\"Crate\")",
            "Create_Position(100, 200, 300)",
            "Find_Player(\"NEUTRAL\")");
        // Engine API order: type, position, player — wire format reflects this.
        generic.Should().Be(
            "return SWFOC_CreateGenericObjectLua('Find_Object_Type(\"Crate\")', 'Create_Position(100, 200, 300)', 'Find_Player(\"NEUTRAL\")')");
    }
}
