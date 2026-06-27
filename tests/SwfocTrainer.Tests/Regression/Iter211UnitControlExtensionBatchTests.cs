using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 211) — pins the iter-156 unit-method extension LIVE batch
/// (Activate_Ability / Disable_Capture / Set_Garrison_Spawn / Cancel_Hyperspace)
/// surfacing on the UnitControl tab. Mixed-arity batch:
///   - Activate_Ability: 1-arg ability-name string via iter-154 helper (BuildUnitLuaMethodCall)
///   - Disable_Capture: 1-arg bool via iter-111 helper (hardcoded "1"/"0")
///   - Set_Garrison_Spawn: 1-arg bool via iter-111 helper (hardcoded "1"/"0")
///   - Cancel_Hyperspace: no-arg via iter-112 helper (BuildUnitLuaNoArgCall)
///
/// Operator workflow this iter unlocks: per-unit advanced ops layered onto
/// the iter-117 SelectedUnitLuaExpr anchor — type unit handle once and
/// activate abilities, toggle capture/garrison-spawn flags, cancel
/// hyperspace jumps. Activate_Ability uses dedicated AbilityNameLuaExpr
/// field for the string arg.
/// </summary>
public sealed class Iter211UnitControlExtensionBatchTests
{
    [Fact]
    public void CatalogEntries_AllFourRemainLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_ActivateAbilityLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_DisableCaptureLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_SetGarrisonSpawnLua"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_CancelHyperspaceLua"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_AllFourEntriesDocumentIter211Surfacing()
    {
        var ability = CapabilityStatusCatalog.Entries["SWFOC_ActivateAbilityLua"].Note;
        var capture = CapabilityStatusCatalog.Entries["SWFOC_DisableCaptureLua"].Note;
        var garrison = CapabilityStatusCatalog.Entries["SWFOC_SetGarrisonSpawnLua"].Note;
        var hyperspace = CapabilityStatusCatalog.Entries["SWFOC_CancelHyperspaceLua"].Note;

        ability.Should().Contain("Iter 211");
        capture.Should().Contain("Iter 211");
        garrison.Should().Contain("Iter 211");
        hyperspace.Should().Contain("Iter 211");

        // All four must mention UnitControl tab — the surfacing location.
        ability.Should().Contain("UnitControl");
        capture.Should().Contain("UnitControl");
        garrison.Should().Contain("UnitControl");
        hyperspace.Should().Contain("UnitControl");
    }

    [Fact]
    public void CatalogRationale_ActivateAbilityReferencesAbilityNameField()
    {
        // Pin: Activate_Ability is the only iter-211 wire needing a NEW
        // dedicated input field (AbilityNameLuaExpr) — others reuse
        // SelectedUnitLuaExpr alone or hardcoded bool args.
        var note = CapabilityStatusCatalog.Entries["SWFOC_ActivateAbilityLua"].Note;
        note.Should().Contain("AbilityNameLuaExpr");
        note.Should().Contain("SelectedUnitLuaExpr");
    }

    [Fact]
    public void CatalogRationale_BoolPairsReferenceIter204HardcodedPattern()
    {
        // Pin: Disable_Capture + Set_Garrison_Spawn use the iter-204
        // hardcoded-bool on/off button pair pattern. Both rationales
        // must reference iter-204 to preserve the pattern lineage.
        var capture = CapabilityStatusCatalog.Entries["SWFOC_DisableCaptureLua"].Note;
        var garrison = CapabilityStatusCatalog.Entries["SWFOC_SetGarrisonSpawnLua"].Note;
        capture.Should().Contain("iter-204");
        garrison.Should().Contain("iter-204");
        capture.Should().Contain("on/off");
        garrison.Should().Contain("on/off");
    }

    [Fact]
    public void Vm_ExposesAllSixCommandsCapabilityActionsAndAbilityNameField()
    {
        // Pin: 6 ICommands (Activate + Capture-on + Capture-off + Garrison-on
        // + Garrison-off + Cancel-hyperspace) + 6 capability actions + new
        // AbilityNameLuaExpr property. Reflection walk so we don't depend on
        // the VM constructor (which has a real bridge dependency).
        var t = typeof(SwfocTrainer.App.V2.ViewModels.UnitControlTabViewModel);
        t.GetProperty("ActivateAbilityLuaCommand").Should().NotBeNull();
        t.GetProperty("DisableCaptureOnLuaCommand").Should().NotBeNull();
        t.GetProperty("DisableCaptureOffLuaCommand").Should().NotBeNull();
        t.GetProperty("SetGarrisonSpawnOnLuaCommand").Should().NotBeNull();
        t.GetProperty("SetGarrisonSpawnOffLuaCommand").Should().NotBeNull();
        t.GetProperty("CancelHyperspaceLuaCommand").Should().NotBeNull();

        t.GetProperty("ActivateAbilityLuaAction").Should().NotBeNull();
        t.GetProperty("DisableCaptureOnLuaAction").Should().NotBeNull();
        t.GetProperty("DisableCaptureOffLuaAction").Should().NotBeNull();
        t.GetProperty("SetGarrisonSpawnOnLuaAction").Should().NotBeNull();
        t.GetProperty("SetGarrisonSpawnOffLuaAction").Should().NotBeNull();
        t.GetProperty("CancelHyperspaceLuaAction").Should().NotBeNull();

        t.GetProperty("AbilityNameLuaExpr")
            .Should().NotBeNull("Activate_Ability needs a dedicated ability-name input field");
    }
}
