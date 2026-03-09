using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class AppModelCoverageSweepTests
{
    [Fact]
    public void AppViewItemModels_ShouldPreserveAssignedValues()
    {
        var reliability = new ActionReliabilityViewItem("set_credits", "stable", "OK", 0.95, "verified");
        var roster = new RosterEntityViewItem(
            "EMPIRE_STORMTROOPER_SQUAD",
            "Stormtrooper Squad",
            "TEXT_STORMTROOPER",
            "Unit",
            "base_swfoc",
            "1125571106",
            "base",
            "Empire",
            "Empire, Pentastar",
            "2",
            "150",
            "ui/stormtrooper.tga",
            "resolved",
            RosterEntityVisualState.Resolved,
            "Compatible",
            RosterEntityCompatibilityState.Compatible,
            "transplant-001",
            "dep_a; dep_b");
        var calibration = new RuntimeCalibrationCandidateViewItem("48 8B ??", 16, "absolute", "int32", "0x1000", 3, "mov eax, [rcx]");
        var saveField = new SaveFieldViewItem("galactic.credits", "Credits", "int32", "1000");
        var compatibility = new SavePatchCompatibilityViewItem("warning", "schema_mismatch", "Schema differs");
        var operation = new SavePatchOperationViewItem("replace", "galactic.credits", "credits", "int32", "1000", "2000");
        var transaction = new SelectedUnitTransactionViewItem(
            "txn-001",
            DateTimeOffset.Parse("2026-03-09T00:00:00Z"),
            IsRollback: true,
            "restore",
            "set_hp;set_speed");

        reliability.ActionId.Should().Be("set_credits");
        reliability.Detail.Should().Be("verified");
        roster.VisualState.Should().Be(RosterEntityVisualState.Resolved);
        roster.CompatibilityState.Should().Be(RosterEntityCompatibilityState.Compatible);
        roster.TransplantReportId.Should().Be("transplant-001");
        calibration.ReferenceCount.Should().Be(3);
        saveField.Path.Should().Be("galactic.credits");
        compatibility.Code.Should().Be("schema_mismatch");
        operation.NewValue.Should().Be("2000");
        transaction.IsRollback.Should().BeTrue();
        transaction.AppliedActions.Should().Be("set_hp;set_speed");
    }

    [Fact]
    public void HotkeyBindingItem_ShouldPreserveValues_AndIgnoreDuplicateAssignments()
    {
        var item = new HotkeyBindingItem();

        item.Gesture.Should().Be("Ctrl+Shift+1");
        item.ActionId.Should().Be("set_credits");
        item.PayloadJson.Should().Be("{}");

        item.Gesture = "Ctrl+1";
        item.ActionId = "spawn_tactical_entity";
        item.PayloadJson = "{\"unitId\":\"u\"}";

        item.Gesture = "Ctrl+1";
        item.ActionId = "spawn_tactical_entity";
        item.PayloadJson = "{\"unitId\":\"u\"}";

        item.Gesture.Should().Be("Ctrl+1");
        item.ActionId.Should().Be("spawn_tactical_entity");
        item.PayloadJson.Should().Be("{\"unitId\":\"u\"}");
    }

    [Fact]
    public void SpawnPresetViewItem_ShouldConvertToCorePreset()
    {
        var viewItem = new SpawnPresetViewItem(
            "preset-1",
            "Stormtrooper Drop",
            "EMPIRE_STORMTROOPER_SQUAD",
            "Empire",
            "ENTRY_A",
            3,
            250,
            "Battle-only");

        var preset = viewItem.ToCorePreset();

        preset.Id.Should().Be("preset-1");
        preset.Name.Should().Be("Stormtrooper Drop");
        preset.UnitId.Should().Be("EMPIRE_STORMTROOPER_SQUAD");
        preset.Faction.Should().Be("Empire");
        preset.EntryMarker.Should().Be("ENTRY_A");
        preset.DefaultQuantity.Should().Be(3);
        preset.DefaultDelayMs.Should().Be(250);
        preset.Description.Should().Be("Battle-only");
    }

    [Fact]
    public void DraftBuildResult_ShouldCoverFactoryHelpers()
    {
        var emptyDraft = new SelectedUnitDraft();
        var populatedDraft = new SelectedUnitDraft(Hp: 250f, OwnerFaction: 3);

        emptyDraft.IsEmpty.Should().BeTrue();
        populatedDraft.IsEmpty.Should().BeFalse();

        var failed = DraftBuildResult.Failed("invalid");
        var success = DraftBuildResult.FromDraft(populatedDraft);

        failed.Succeeded.Should().BeFalse();
        failed.Message.Should().Be("invalid");
        failed.Draft.Should().BeNull();
        success.Succeeded.Should().BeTrue();
        success.Message.Should().Be("ok");
        success.Draft.Should().Be(populatedDraft);
    }

    [Fact]
    public void SelectedUnitParsingHelpers_ShouldCoverSuccessAndFailureBranches()
    {
        MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(
                "",
                "bad float",
                out var emptyFloat,
                out var emptyFloatError)
            .Should()
            .BeTrue();
        emptyFloat.Should().BeNull();
        emptyFloatError.Should().BeEmpty();

        MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(
                "1.25",
                "bad float",
                out var parsedFloat,
                out var parsedFloatError)
            .Should()
            .BeTrue();
        parsedFloat.Should().BeApproximately(1.25f, 0.001f);
        parsedFloatError.Should().BeEmpty();

        MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(
                "not-a-number",
                "bad float",
                out var failedFloat,
                out var failedFloatError)
            .Should()
            .BeFalse();
        failedFloat.Should().BeNull();
        failedFloatError.Should().Be("bad float");

        MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt(
                "",
                "bad int",
                out var emptyInt,
                out var emptyIntError)
            .Should()
            .BeTrue();
        emptyInt.Should().BeNull();
        emptyIntError.Should().BeEmpty();

        MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt(
                "7",
                "bad int",
                out var parsedInt,
                out var parsedIntError)
            .Should()
            .BeTrue();
        parsedInt.Should().Be(7);
        parsedIntError.Should().BeEmpty();

        MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt(
                "seven",
                "bad int",
                out var failedInt,
                out var failedIntError)
            .Should()
            .BeFalse();
        failedInt.Should().BeNull();
        failedIntError.Should().Be("bad int");
    }
}
