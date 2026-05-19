using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 213) — pins the iter-153 + iter-162 unit-method LIVE
/// surfacing on the UnitControl tab. 3 wires across 2 dispatcher patterns:
///   - Set_Cannot_Be_Killed + Enable_Stealth: 1-arg bool via iter-111 helper
///     (iter-204 hardcoded-bool on/off pair pattern — now spans 5 iters:
///     204→208→211→212→213)
///   - Override_Max_Speed: 1-arg float via iter-154 helper (per-unit speed
///     override; complements iter-100 SetPerFactionSpeedMultiplier global)
///
/// Operator workflow: 5 buttons surfacing 3 wires (2 wires get on/off pairs)
/// with one new dedicated field (MaxSpeedOverrideLuaExpr) for the numeric
/// speed value. All anchor on iter-117 SelectedUnitLuaExpr.
/// </summary>
public sealed class Iter213UnitControlBoolBatchTests
{
    [Fact]
    public void CatalogEntries_AllThreeRemainLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_SetCannotBeKilledLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_EnableStealthLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_OverrideMaxSpeedLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_AllThreeEntriesDocumentIter213Surfacing()
    {
        var entries = new[]
        {
            "SWFOC_SetCannotBeKilledLua",
            "SWFOC_EnableStealthLua",
            "SWFOC_OverrideMaxSpeedLua"
        };

        foreach (var key in entries)
        {
            var note = CapabilityStatusCatalog.Entries[key].Note;
            note.Should().Contain("Iter 213", $"{key} should be marked iter-213 surfaced");
            note.Should().Contain("UnitControl", $"{key} should reference UnitControl tab");
        }
    }

    [Fact]
    public void CatalogRationale_BoolPairsReferenceIter204OnOffPattern()
    {
        // Pin: Set_Cannot_Be_Killed + Enable_Stealth use iter-204 hardcoded-bool
        // on/off pattern. Lineage now spans iter-204 → 208 → 211 → 212 → 213
        // (5 iters of the pattern!).
        var cannotKill = CapabilityStatusCatalog.Entries["SWFOC_SetCannotBeKilledLua"].Note;
        var stealth = CapabilityStatusCatalog.Entries["SWFOC_EnableStealthLua"].Note;
        cannotKill.Should().Contain("iter-204");
        cannotKill.Should().Contain("on/off");
        stealth.Should().Contain("iter-204");
        stealth.Should().Contain("on/off");
    }

    [Fact]
    public void CatalogRationale_OverrideMaxSpeedReferencesNewFieldAndIter100Complement()
    {
        // Pin: Override_Max_Speed uses MaxSpeedOverrideLuaExpr field +
        // documents the per-unit-vs-global complement to iter-100
        // SetPerFactionSpeedMultiplier (different scopes, both LIVE).
        var note = CapabilityStatusCatalog.Entries["SWFOC_OverrideMaxSpeedLua"].Note;
        note.Should().Contain("MaxSpeedOverrideLuaExpr");
        note.Should().Contain("iter-100");
        note.Should().Contain("per-unit");
    }

    [Fact]
    public void Vm_ExposesAllFiveCommandsCapabilityActionsAndSpeedField()
    {
        // Pin: 5 ICommands (2 cannot-killed + 2 stealth + override-speed) +
        // 5 capability actions + new MaxSpeedOverrideLuaExpr property.
        // Reflection walk so we don't depend on the VM constructor.
        var t = typeof(SwfocTrainer.App.V2.ViewModels.UnitControlTabViewModel);
        t.GetProperty("SetCannotBeKilledOnLuaCommand").Should().NotBeNull();
        t.GetProperty("SetCannotBeKilledOffLuaCommand").Should().NotBeNull();
        t.GetProperty("EnableStealthOnLuaCommand").Should().NotBeNull();
        t.GetProperty("EnableStealthOffLuaCommand").Should().NotBeNull();
        t.GetProperty("OverrideMaxSpeedLuaCommand").Should().NotBeNull();

        t.GetProperty("SetCannotBeKilledOnLuaAction").Should().NotBeNull();
        t.GetProperty("SetCannotBeKilledOffLuaAction").Should().NotBeNull();
        t.GetProperty("EnableStealthOnLuaAction").Should().NotBeNull();
        t.GetProperty("EnableStealthOffLuaAction").Should().NotBeNull();
        t.GetProperty("OverrideMaxSpeedLuaAction").Should().NotBeNull();

        t.GetProperty("MaxSpeedOverrideLuaExpr")
            .Should().NotBeNull("Override_Max_Speed needs a dedicated speed field");
    }
}
