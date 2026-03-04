using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelPayloadHelpersAdditionalTests
{
    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldThrow_OnNullArguments()
    {
        var required = new JsonArray();

        var actActionId = () => MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            null!,
            required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        var actRequired = () => MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            MainViewModelDefaults.ActionSetCredits,
            null!,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        var actSymbols = () => MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            MainViewModelDefaults.ActionSetCredits,
            required,
            null!,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        var actHooks = () => MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            MainViewModelDefaults.ActionSetCredits,
            required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            null!);

        actActionId.Should().Throw<ArgumentNullException>().WithParameterName("actionId");
        actRequired.Should().Throw<ArgumentNullException>().WithParameterName("required");
        actSymbols.Should().Throw<ArgumentNullException>().WithParameterName("defaultSymbolByActionId");
        actHooks.Should().Throw<ArgumentNullException>().WithParameterName("defaultHelperHookByActionId");
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldCoverSwitchDefaults_ForExtendedKeys()
    {
        var required = new JsonArray(
            JsonValue.Create("uint32Key"),
            JsonValue.Create(MainViewModelDefaults.PayloadKeyFloatValue),
            JsonValue.Create(MainViewModelDefaults.PayloadKeyBoolValue),
            JsonValue.Create(MainViewModelDefaults.PayloadKeyEnable),
            JsonValue.Create("originalBytes"),
            JsonValue.Create("unitId"),
            JsonValue.Create("entryMarker"),
            JsonValue.Create("faction"),
            JsonValue.Create("globalKey"),
            JsonValue.Create("desiredState"),
            JsonValue.Create("populationPolicy"),
            JsonValue.Create("persistencePolicy"),
            JsonValue.Create("placementMode"),
            JsonValue.Create("allowCrossFaction"),
            JsonValue.Create("allowDuplicate"),
            JsonValue.Create("forceOverride"),
            JsonValue.Create("planetFlipMode"),
            JsonValue.Create("flipMode"),
            JsonValue.Create("variantGenerationMode"),
            JsonValue.Create("nodePath"),
            JsonValue.Create("value"),
            JsonValue.Create("helperHookId"),
            JsonValue.Create(MainViewModelDefaults.PayloadKeyFreeze),
            JsonValue.Create(MainViewModelDefaults.PayloadKeyIntValue));

        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "unknown_action",
            required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload["uint32Key"]!.ToString().Should().BeEmpty();
        payload[MainViewModelDefaults.PayloadKeyFloatValue]!.GetValue<float>().Should().Be(1.0f);
        payload[MainViewModelDefaults.PayloadKeyBoolValue]!.GetValue<bool>().Should().BeTrue();
        payload[MainViewModelDefaults.PayloadKeyEnable]!.GetValue<bool>().Should().BeTrue();
        payload["originalBytes"]!.ToString().Should().Be("48 8B 74 24 68");
        payload["unitId"]!.ToString().Should().BeEmpty();
        payload["entryMarker"]!.ToString().Should().BeEmpty();
        payload["faction"]!.ToString().Should().BeEmpty();
        payload["globalKey"]!.ToString().Should().BeEmpty();
        payload["desiredState"]!.ToString().Should().Be("alive");
        payload["populationPolicy"]!.ToString().Should().Be("Normal");
        payload["persistencePolicy"]!.ToString().Should().Be("PersistentGalactic");
        payload["placementMode"]!.ToString().Should().BeEmpty();
        payload["allowCrossFaction"]!.GetValue<bool>().Should().BeTrue();
        payload["allowDuplicate"]!.GetValue<bool>().Should().BeFalse();
        payload["forceOverride"]!.GetValue<bool>().Should().BeFalse();
        payload["planetFlipMode"]!.ToString().Should().Be("convert_everything");
        payload["flipMode"]!.ToString().Should().Be("convert_everything");
        payload["variantGenerationMode"]!.ToString().Should().Be("patch_mod_overlay");
        payload["nodePath"]!.ToString().Should().BeEmpty();
        payload["value"]!.ToString().Should().BeEmpty();
        payload["helperHookId"]!.ToString().Should().Be("unknown_action");
        payload[MainViewModelDefaults.PayloadKeyFreeze]!.GetValue<bool>().Should().BeTrue();
        payload[MainViewModelDefaults.PayloadKeyIntValue]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldSetUnitCapIntDefault_WhenActionMatches()
    {
        var required = new JsonArray(JsonValue.Create(MainViewModelDefaults.PayloadKeyIntValue));

        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            MainViewModelDefaults.ActionSetUnitCap,
            required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload[MainViewModelDefaults.PayloadKeyIntValue]!.GetValue<int>().Should().Be(MainViewModelDefaults.DefaultUnitCapValue);
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_ShouldThrow_OnNullArguments()
    {
        var actAction = () => MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults(null!, new JsonObject());
        var actPayload = () => MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults(MainViewModelDefaults.ActionSetCredits, null!);

        actAction.Should().Throw<ArgumentNullException>().WithParameterName("actionId");
        actPayload.Should().Throw<ArgumentNullException>().WithParameterName("payload");
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_ShouldNotOverwrite_ExistingSpawnPolicies()
    {
        var payload = new JsonObject
        {
            ["populationPolicy"] = "ManualPolicy",
            ["persistencePolicy"] = "ManualPersist",
            ["allowCrossFaction"] = false,
            ["placementMode"] = "manual_zone"
        };

        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("spawn_tactical_entity", payload);

        payload["populationPolicy"]!.ToString().Should().Be("ManualPolicy");
        payload["persistencePolicy"]!.ToString().Should().Be("ManualPersist");
        payload["allowCrossFaction"]!.GetValue<bool>().Should().BeFalse();
        payload["placementMode"]!.ToString().Should().Be("manual_zone");
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_ShouldApplyTransferAndSwitchDefaults()
    {
        var transferPayload = new JsonObject();
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("transfer_fleet_safe", transferPayload);
        transferPayload["placementMode"]!.ToString().Should().Be("safe_transfer");
        transferPayload["allowCrossFaction"]!.GetValue<bool>().Should().BeTrue();
        transferPayload["forceOverride"]!.GetValue<bool>().Should().BeFalse();

        var switchPayload = new JsonObject();
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("switch_player_faction", switchPayload);
        switchPayload["allowCrossFaction"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_ShouldMirrorFlipMode_WhenProvided()
    {
        var payload = new JsonObject
        {
            ["flipMode"] = "empty_and_retreat"
        };

        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("flip_planet_owner", payload);

        payload["flipMode"]!.ToString().Should().Be("empty_and_retreat");
        payload["planetFlipMode"]!.ToString().Should().Be("empty_and_retreat");
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_ShouldApplyCreateVariantDefaults_WhenFieldsMissing()
    {
        var payload = new JsonObject();

        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("create_hero_variant", payload);

        payload["variantGenerationMode"]!.ToString().Should().Be("patch_mod_overlay");
        payload["allowCrossFaction"]!.GetValue<bool>().Should().BeTrue();
    }
}
