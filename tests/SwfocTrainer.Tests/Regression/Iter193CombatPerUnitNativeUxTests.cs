using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-05 (iter 193) — pins the Combat tab native UX for the iter 154
/// per-unit float-arg combat wires (Heal, Take_Damage, Set_Damage_Modifier,
/// Set_Rate_Of_Fire_Modifier). Sixth tab in the iter-188-192 surfacing arc;
/// continues iter-192's write-side pivot.
///
/// All 4 wires use V2UnitMutationDispatcher: Heal is no-arg
/// (BuildUnitLuaNoArgCall), the other 3 take a single float arg
/// (BuildUnitLuaMethodCall). Operator types unit Lua expression + arg, picks
/// any of 4 buttons. Result lands in Combat tab LastStatus.
/// </summary>
public sealed class Iter193CombatPerUnitNativeUxTests
{
    [Fact]
    public void DispatcherMethods_BindToCorrectSwfocNames()
    {
        // Pin: V2UnitMutationDispatcher exposes the 4 iter-154 methods that
        // the Combat tab buttons invoke. Compile-time guard for the surface
        // contract — a future rename in the dispatcher fails the build.
        var heal = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.HealUnitLuaAsync));
        var takeDamage = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.TakeDamageLuaAsync));
        var setDmgMod = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.SetDamageModifierLuaAsync));
        var setRofMod = typeof(V2UnitMutationDispatcher)
            .GetMethod(nameof(V2UnitMutationDispatcher.SetRateOfFireModifierLuaAsync));

        heal.Should().NotBeNull("Combat tab Heal button binds to HealUnitLuaAsync");
        takeDamage.Should().NotBeNull("Combat tab Take damage button binds to TakeDamageLuaAsync");
        setDmgMod.Should().NotBeNull("Combat tab Set damage mod button binds to SetDamageModifierLuaAsync");
        setRofMod.Should().NotBeNull("Combat tab Set RoF mod button binds to SetRateOfFireModifierLuaAsync");
    }

    [Fact]
    public void CatalogAction_AllFourEntriesAreLive()
    {
        // Pin: all 4 SWFOC_* names referenced by the iter-193 buttons must
        // resolve to LIVE catalog entries.
        var swfocNames = new[]
        {
            "SWFOC_HealUnitLua",
            "SWFOC_TakeDamageLua",
            "SWFOC_SetDamageModifierLua",
            "SWFOC_SetRateOfFireModifierLua",
        };
        foreach (var name in swfocNames)
        {
            CapabilityStatusCatalog.Entries[name].Status
                .Should().Be(CapabilityStatus.Live,
                    $"{name} must remain LIVE for the iter-193 button to be honest");
        }
    }

    [Fact]
    public void CatalogRationale_DocumentsIter193Surfacing()
    {
        // Pin: iter-193 added native UX surfacing language to all 4 entries.
        var heal = CapabilityStatusCatalog.Entries["SWFOC_HealUnitLua"].Note;
        var takeDamage = CapabilityStatusCatalog.Entries["SWFOC_TakeDamageLua"].Note;
        var setDmgMod = CapabilityStatusCatalog.Entries["SWFOC_SetDamageModifierLua"].Note;
        var setRofMod = CapabilityStatusCatalog.Entries["SWFOC_SetRateOfFireModifierLua"].Note;

        heal.Should().Contain("Iter 193");
        takeDamage.Should().Contain("Iter 193");
        setDmgMod.Should().Contain("Iter 193");
        setRofMod.Should().Contain("Iter 193");
    }

    [Fact]
    public void CatalogRationale_TakeDamageDocumentsGlobalMultiplierComposition()
    {
        // Pin: TakeDamage rationale must document the iter-96 Take_Damage_Outer
        // composition — operators using the Combat tab need to know that the
        // GLOBAL multiplier (also bound to Combat tab UI) applies to TakeDamage
        // dispatched through this button. Surfacing this in BOTH places is
        // important: the per-unit button + the global slider live in the same tab.
        var note = CapabilityStatusCatalog.Entries["SWFOC_TakeDamageLua"].Note;
        note.Should().Contain("iter 96");
        note.Should().Contain("Take_Damage_Outer");
        note.Should().Contain("GLOBAL multiplier applies");
    }

    [Fact]
    public void CatalogRationale_SetRateOfFireClosesIter101Gap()
    {
        // Pin: SetRateOfFireModifier (per-unit) closes the iter-101 SetFireRate
        // gap that was DEFERRED at the global level (no engine setter exists).
        // The catalog rationale must document this so future readers don't
        // re-attempt the iter-101 global pattern.
        var note = CapabilityStatusCatalog.Entries["SWFOC_SetRateOfFireModifierLua"].Note;
        note.Should().Contain("iter 101");
        note.Should().Contain("per-unit");
    }
}
