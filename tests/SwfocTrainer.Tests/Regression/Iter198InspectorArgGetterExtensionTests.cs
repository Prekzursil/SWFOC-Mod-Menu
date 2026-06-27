using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 198) — pins Inspector tab arg-getter extension for iter
/// 173 unit-receiver wires (IsAbilityActive, HasProperty, IsCategory,
/// GetDistance). NOT a new tab — extends iter-191/197 GroupBox from 10 → 14
/// buttons. New UnitArgExpr field for the 2nd Lua expression.
///
/// All 4 wires take unit + 1 string arg via V2UnitMutationDispatcher and
/// the iter-173 Lua_DispatchUnitGetterArg helper (7th in dispatcher set).
/// </summary>
public sealed class Iter198InspectorArgGetterExtensionTests
{
    [Fact]
    public void DispatcherMethods_BindToCorrectSwfocNames()
    {
        var t = typeof(V2UnitMutationDispatcher);
        t.GetMethod(nameof(V2UnitMutationDispatcher.IsAbilityActiveLuaAsync))
            .Should().NotBeNull("Inspector tab Is ability active button binds to IsAbilityActiveLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.HasPropertyLuaAsync))
            .Should().NotBeNull("Inspector tab Has property button binds to HasPropertyLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.IsCategoryLuaAsync))
            .Should().NotBeNull("Inspector tab Is category button binds to IsCategoryLuaAsync");
        t.GetMethod(nameof(V2UnitMutationDispatcher.GetDistanceLuaAsync))
            .Should().NotBeNull("Inspector tab Get distance button binds to GetDistanceLuaAsync");
    }

    [Fact]
    public void DispatcherMethods_TakeUnitAndArg()
    {
        // Pin: all 4 methods MUST have parameter signature
        // (string unitLuaExpr, string argLuaExpr, CancellationToken ct).
        var t = typeof(V2UnitMutationDispatcher);
        var methods = new[]
        {
            t.GetMethod(nameof(V2UnitMutationDispatcher.IsAbilityActiveLuaAsync))!,
            t.GetMethod(nameof(V2UnitMutationDispatcher.HasPropertyLuaAsync))!,
            t.GetMethod(nameof(V2UnitMutationDispatcher.IsCategoryLuaAsync))!,
            t.GetMethod(nameof(V2UnitMutationDispatcher.GetDistanceLuaAsync))!,
        };
        foreach (var method in methods)
        {
            var parameters = method.GetParameters();
            parameters.Should().HaveCount(3,
                $"{method.Name} takes (unitLuaExpr, argLuaExpr, CancellationToken)");
            parameters[0].ParameterType.Should().Be(typeof(string));
            parameters[1].ParameterType.Should().Be(typeof(string));
        }
    }

    [Fact]
    public void CatalogAction_AllFourEntriesAreLive()
    {
        var swfocNames = new[]
        {
            "SWFOC_IsAbilityActiveLua",
            "SWFOC_HasPropertyLua",
            "SWFOC_IsCategoryLua",
            "SWFOC_GetDistanceLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-198 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_DocumentsIter198Surfacing()
    {
        var isActive = CapabilityStatusCatalog.Entries["SWFOC_IsAbilityActiveLua"].Note;
        var hasProp = CapabilityStatusCatalog.Entries["SWFOC_HasPropertyLua"].Note;
        var isCat = CapabilityStatusCatalog.Entries["SWFOC_IsCategoryLua"].Note;
        var getDist = CapabilityStatusCatalog.Entries["SWFOC_GetDistanceLua"].Note;

        isActive.Should().Contain("Iter 198");
        hasProp.Should().Contain("Iter 198");
        isCat.Should().Contain("Iter 198");
        getDist.Should().Contain("Iter 198");
    }

    [Fact]
    public void CatalogRationale_ReferencesIter173Helper()
    {
        // Pin: all 4 wires use the iter-173 unit-getter-with-arg helper.
        var swfocNames = new[]
        {
            "SWFOC_IsAbilityActiveLua",
            "SWFOC_HasPropertyLua",
            "SWFOC_IsCategoryLua",
            "SWFOC_GetDistanceLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Note
                .Should().Contain("iter-173",
                    $"{name} must reference the iter-173 helper for provenance");
        }
    }

    [Fact]
    public void CatalogRationale_IsAbilityActiveReferencesIter156Pairing()
    {
        // Pin: IsAbilityActive forms read-after-write pair with iter-156
        // ActivateAbility writer. The catalog rationale must reference
        // iter-156 so operators can find the writer.
        var note = CapabilityStatusCatalog.Entries["SWFOC_IsAbilityActiveLua"].Note;
        note.Should().Contain("iter-156");
        note.Should().Contain("Read-after-write pair");
    }
}
