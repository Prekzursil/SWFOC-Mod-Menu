using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 5 branch coverage for MainViewModelPayloadHelpers:
/// BuildRequiredPayloadTemplate all key branches (floatValue, boolValue, enable,
/// patchBytes, originalBytes, helperHookId with/without mapping, unitId, entryMarker,
/// faction, globalKey, nodePath, value, unknown key),
/// ApplyActionSpecificPayloadDefaults for freeze_symbol with existing intValue,
/// BuildCreditsPayload edge cases.
/// </summary>
public sealed class MainViewModelPayloadHelpersWave5Tests
{
    [Fact]
    public void BuildRequiredPayloadTemplate_FloatValueKey_ShouldReturn1()
    {
        var required = new JsonArray(JsonValue.Create("floatValue")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["floatValue"]!.GetValue<float>().Should().Be(1.0f);
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_BoolValueKey_ShouldReturnTrue()
    {
        var required = new JsonArray(JsonValue.Create("boolValue")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["boolValue"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_EnableKey_ShouldReturnTrue()
    {
        var required = new JsonArray(JsonValue.Create("enable")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["enable"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_OriginalBytesKey_ShouldReturnDefault()
    {
        var required = new JsonArray(JsonValue.Create("originalBytes")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["originalBytes"]!.GetValue<string>().Should().Be("48 8B 74 24 68");
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_HelperHookIdWithMapping_ShouldReturnMappedValue()
    {
        var required = new JsonArray(JsonValue.Create("helperHookId")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "spawn_unit_helper", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["helperHookId"]!.GetValue<string>().Should().Be("spawn_bridge");
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_HelperHookIdWithoutMapping_ShouldFallbackToActionId()
    {
        var required = new JsonArray(JsonValue.Create("helperHookId")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "unmapped_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["helperHookId"]!.GetValue<string>().Should().Be("unmapped_action");
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_UnitIdKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("unitId")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["unitId"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_EntryMarkerKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("entryMarker")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["entryMarker"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_FactionKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("faction")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["faction"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_GlobalKeyKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("globalKey")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["globalKey"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_NodePathKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("nodePath")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["nodePath"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_ValueKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("value")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["value"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_UnknownKey_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("customUnknownField")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "test_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["customUnknownField"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_SymbolWithMapping_ShouldReturnMappedSymbol()
    {
        var required = new JsonArray(JsonValue.Create("symbol")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "set_credits", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["symbol"]!.GetValue<string>().Should().Be("credits");
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_SymbolWithoutMapping_ShouldReturnEmpty()
    {
        var required = new JsonArray(JsonValue.Create("symbol")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "no_mapping_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["symbol"]!.GetValue<string>().Should().BeEmpty();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_IntValueForSetUnitCap_ShouldReturnDefaultUnitCap()
    {
        var required = new JsonArray(JsonValue.Create("intValue")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "set_unit_cap", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["intValue"]!.GetValue<int>().Should().Be(MainViewModelDefaults.DefaultUnitCapValue);
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_IntValueForUnknownAction_ShouldReturnZero()
    {
        var required = new JsonArray(JsonValue.Create("intValue")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "unknown_action", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["intValue"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_FreezeForUnfreezeSymbol_ShouldReturnFalse()
    {
        var required = new JsonArray(JsonValue.Create("freeze")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "unfreeze_symbol", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["freeze"]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_FreezeForFreezeSymbol_ShouldReturnTrue()
    {
        var required = new JsonArray(JsonValue.Create("freeze")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "freeze_symbol", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload["freeze"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_UnknownAction_ShouldNotModifyPayload()
    {
        var payload = new JsonObject();
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("unknown", payload);
        payload.Count.Should().Be(0);
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_FreezeSymbol_WithExistingIntValue_ShouldNotOverwrite()
    {
        var payload = new JsonObject { ["intValue"] = 42 };
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("freeze_symbol", payload);
        payload["intValue"]!.GetValue<int>().Should().Be(42);
    }

    [Fact]
    public void ApplyActionSpecificPayloadDefaults_FreezeSymbol_WithoutIntValue_ShouldSetDefault()
    {
        var payload = new JsonObject();
        MainViewModelPayloadHelpers.ApplyActionSpecificPayloadDefaults("freeze_symbol", payload);
        payload["intValue"]!.GetValue<int>().Should().Be(MainViewModelDefaults.DefaultCreditsValue);
    }

    [Fact]
    public void BuildCreditsPayload_ShouldSetAllExpectedFields()
    {
        var payload = MainViewModelPayloadHelpers.BuildCreditsPayload(5000, true);
        payload["symbol"]!.GetValue<string>().Should().Be("credits");
        payload["intValue"]!.GetValue<int>().Should().Be(5000);
        payload["lockCredits"]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildCreditsPayload_FalseLock_ShouldSetLockFalse()
    {
        var payload = MainViewModelPayloadHelpers.BuildCreditsPayload(0, false);
        payload["lockCredits"]!.GetValue<bool>().Should().BeFalse();
        payload["intValue"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public void BuildRequiredPayloadTemplate_NullNodeInArray_ShouldBeSkipped()
    {
        var required = new JsonArray(
            JsonValue.Create("symbol")!,
            null,
            JsonValue.Create("")!,
            JsonValue.Create("intValue")!);
        var payload = MainViewModelPayloadHelpers.BuildRequiredPayloadTemplate(
            "set_credits", required,
            MainViewModelDefaults.DefaultSymbolByActionId,
            MainViewModelDefaults.DefaultHelperHookByActionId);
        payload.ContainsKey("symbol").Should().BeTrue();
        payload.ContainsKey("intValue").Should().BeTrue();
        // null and empty should have been skipped
        payload.Count.Should().Be(2);
    }
}
