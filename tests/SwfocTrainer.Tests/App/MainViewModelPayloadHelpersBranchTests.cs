using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Fills remaining branch coverage for MainViewModelPayloadHelpers.
/// Covers every switch arm in BuildRequiredPayloadValue and null-guard branches.
/// </summary>
public sealed class MainViewModelPayloadHelpersBranchTests
{
    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldThrow_WhenActionIdIsNull()
    {
        var act = () => MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            null!, new JsonArray(), MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldThrow_WhenRequiredIsNull()
    {
        var act = () => MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "action", null!, MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldThrow_WhenDefaultSymbolMapIsNull()
    {
        var act = () => MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "action", new JsonArray(), null!,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldThrow_WhenDefaultHelperHookMapIsNull()
    {
        var act = () => MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "action", new JsonArray(), MainViewModelDefaults.DefaultSymbolByActionId, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnFloatValueDefault()
    {
        var required = new JsonArray(JsonValue.Create(MainViewModelDefaults.PayloadFloatValue));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "some_action", required, MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload[MainViewModelDefaults.PayloadFloatValue]!.GetValue<float>().Should().Be(1.0f);
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnBoolValueDefault()
    {
        var required = new JsonArray(JsonValue.Create(MainViewModelDefaults.PayloadBoolValue));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "some_action", required, MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload[MainViewModelDefaults.PayloadBoolValue]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnEnableDefault()
    {
        var required = new JsonArray(JsonValue.Create(MainViewModelDefaults.PayloadEnable));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "some_action", required, MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload[MainViewModelDefaults.PayloadEnable]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnFreezeTrue_ForFreezeSymbolAction()
    {
        var required = new JsonArray(JsonValue.Create(MainViewModelDefaults.PayloadFreeze));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            MainViewModelDefaults.ActionFreezeSymbol, required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload[MainViewModelDefaults.PayloadFreeze]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnFreezeFalse_ForUnfreezeSymbolAction()
    {
        var required = new JsonArray(JsonValue.Create(MainViewModelDefaults.PayloadFreeze));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            MainViewModelDefaults.ActionUnfreezeSymbol, required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload[MainViewModelDefaults.PayloadFreeze]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnOriginalBytesDefault()
    {
        var required = new JsonArray(JsonValue.Create("originalBytes"));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "some_action", required, MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload["originalBytes"]!.GetValue<string>().Should().Be("48 8B 74 24 68");
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnEmptyString_ForUnitId()
    {
        var required = new JsonArray(JsonValue.Create("unitId"));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "some_action", required, MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload["unitId"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnEmptyString_ForEntryMarker()
    {
        var required = new JsonArray(JsonValue.Create("entryMarker"));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "some_action", required, MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload["entryMarker"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnEmptyString_ForFaction()
    {
        var required = new JsonArray(JsonValue.Create("faction"));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "some_action", required, MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload["faction"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnEmptyString_ForGlobalKey()
    {
        var required = new JsonArray(JsonValue.Create("globalKey"));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "some_action", required, MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload["globalKey"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnEmptyString_ForNodePath()
    {
        var required = new JsonArray(JsonValue.Create("nodePath"));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "some_action", required, MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload["nodePath"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnEmptyString_ForValue()
    {
        var required = new JsonArray(JsonValue.Create("value"));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "some_action", required, MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload["value"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnEmptyString_ForUnknownKey()
    {
        var required = new JsonArray(JsonValue.Create("unknownKey123"));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "some_action", required, MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload["unknownKey123"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnEmptySymbol_WhenActionNotInSymbolMap()
    {
        var required = new JsonArray(JsonValue.Create(MainViewModelDefaults.PayloadSymbol));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "unknown_action_not_in_map", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload[MainViewModelDefaults.PayloadSymbol]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnZeroInt_ForUnknownAction()
    {
        var required = new JsonArray(JsonValue.Create(MainViewModelDefaults.PayloadIntValue));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "unknown_action_not_in_map", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload[MainViewModelDefaults.PayloadIntValue]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnUnitCapDefault_ForSetUnitCapAction()
    {
        var required = new JsonArray(JsonValue.Create(MainViewModelDefaults.PayloadIntValue));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            MainViewModelDefaults.ActionSetUnitCap, required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload[MainViewModelDefaults.PayloadIntValue]!.GetValue<int>().Should().Be(MainViewModelDefaults.DefaultUnitCapValue);
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnActionIdAsHook_WhenNotInHelperHookMap()
    {
        var required = new JsonArray(JsonValue.Create("helperHookId"));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "unknown_action_xyz", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload["helperHookId"]!.GetValue<string>().Should().Be("unknown_action_xyz");
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ShouldReturnKnownHook_WhenInHelperHookMap()
    {
        var required = new JsonArray(JsonValue.Create("helperHookId"));
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "spawn_unit_helper", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);

        payload["helperHookId"]!.GetValue<string>().Should().Be("spawn_bridge");
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_ShouldThrow_WhenActionIdIsNull()
    {
        var act = () => MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults(null!, new JsonObject());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_ShouldThrow_WhenPayloadIsNull()
    {
        var act = () => MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("action", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_ShouldDoNothing_ForUnrelatedAction()
    {
        var payload = new JsonObject();
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("toggle_fog_reveal", payload);
        payload.Count.Should().Be(0);
    }

    [Fact]
    public void BuildCreditsPayload_ShouldSetAllFields()
    {
        var payload = MainViewModelPayloadHelpers.BuildCreditsPayload(999, false);
        payload[MainViewModelDefaults.PayloadSymbol]!.GetValue<string>().Should().Be("credits");
        payload[MainViewModelDefaults.PayloadIntValue]!.GetValue<int>().Should().Be(999);
        payload[MainViewModelDefaults.PayloadLockCredits]!.GetValue<bool>().Should().BeFalse();
    }
}
