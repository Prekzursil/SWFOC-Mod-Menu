using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 5 branch coverage for MainViewModelHotkeyHelpers:
/// ParseHotkeyPayload (valid JSON, invalid JSON, null payload),
/// BuildDefaultHotkeyPayload all switch arms via BuildDefaultHotkeyPayloadJson,
/// BuildHotkeyStatus succeeded and failed branches.
/// </summary>
public sealed class MainViewModelHotkeyHelpersWave5Tests
{
    [Fact]
    public void ParseHotkeyPayload_ValidJson_ShouldReturnParsedObject()
    {
        var binding = new HotkeyBindingItem
        {
            Gesture = "Ctrl+1",
            ActionId = "set_credits",
            PayloadJson = "{\"symbol\":\"credits\",\"intValue\":500}"
        };

        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        payload["symbol"]!.GetValue<string>().Should().Be("credits");
        payload["intValue"]!.GetValue<int>().Should().Be(500);
    }

    [Fact]
    public void ParseHotkeyPayload_InvalidJson_ShouldReturnDefaultPayload()
    {
        var binding = new HotkeyBindingItem
        {
            Gesture = "Ctrl+1",
            ActionId = "set_credits",
            PayloadJson = "not-json{{"
        };

        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        // Should fall back to default for set_credits
        payload.Should().NotBeNull();
        payload[MainViewModelDefaults.PayloadSymbol]!.GetValue<string>().Should().Be("credits");
    }

    [Fact]
    public void ParseHotkeyPayload_NullPayloadJson_ShouldReturnEmptyObject()
    {
        var binding = new HotkeyBindingItem
        {
            Gesture = "Ctrl+1",
            ActionId = "unknown_action",
            PayloadJson = null
        };

        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        payload.Should().NotBeNull();
    }

    [Fact]
    public void ParseHotkeyPayload_EmptyObject_ShouldReturnEmptyObject()
    {
        var binding = new HotkeyBindingItem
        {
            Gesture = "Ctrl+1",
            ActionId = "set_credits",
            PayloadJson = "{}"
        };

        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        payload.Should().NotBeNull();
    }

    [Fact]
    public void ParseHotkeyPayload_JsonArray_ShouldFallToDefault()
    {
        var binding = new HotkeyBindingItem
        {
            Gesture = "Ctrl+1",
            ActionId = "set_credits",
            PayloadJson = "[1,2,3]"
        };

        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        // Array is not JsonObject, should fallback to default
        payload.Should().NotBeNull();
        payload[MainViewModelDefaults.PayloadSymbol]!.GetValue<string>().Should().Be("credits");
    }

    [Theory]
    [InlineData(MainViewModelDefaults.ActionSetCredits)]
    [InlineData(MainViewModelDefaults.ActionFreezeTimer)]
    [InlineData(MainViewModelDefaults.ActionToggleFogReveal)]
    [InlineData(MainViewModelDefaults.ActionSetUnitCap)]
    [InlineData(MainViewModelDefaults.ActionToggleInstantBuildPatch)]
    [InlineData(MainViewModelDefaults.ActionSetGameSpeed)]
    [InlineData(MainViewModelDefaults.ActionFreezeSymbol)]
    [InlineData(MainViewModelDefaults.ActionUnfreezeSymbol)]
    [InlineData("completely_unknown_action")]
    public void BuildDefaultHotkeyPayloadJson_AllActions_ShouldReturnValidJson(string actionId)
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(actionId);
        json.Should().NotBeNullOrWhiteSpace();

        // Should be valid JSON
        var parsed = JsonNode.Parse(json);
        parsed.Should().NotBeNull();
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_SetCredits_ShouldHaveCorrectKeys()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionSetCredits);
        var obj = JsonNode.Parse(json) as JsonObject;
        obj.Should().NotBeNull();
        obj![MainViewModelDefaults.PayloadSymbol]!.GetValue<string>().Should().Be("credits");
        obj[MainViewModelDefaults.PayloadIntValue]!.GetValue<int>().Should().Be(MainViewModelDefaults.DefaultCreditsValue);
        obj[MainViewModelDefaults.PayloadLockCredits]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_FreezeTimer_ShouldHaveBoolValueTrue()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionFreezeTimer);
        var obj = JsonNode.Parse(json) as JsonObject;
        obj.Should().NotBeNull();
        obj![MainViewModelDefaults.PayloadBoolValue]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_SetGameSpeed_ShouldHaveFloatValue()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionSetGameSpeed);
        var obj = JsonNode.Parse(json) as JsonObject;
        obj.Should().NotBeNull();
        obj![MainViewModelDefaults.PayloadFloatValue]!.GetValue<float>().Should().Be(MainViewModelDefaults.DefaultGameSpeedValue);
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_UnfreezeSymbol_ShouldHaveFreezeFalse()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionUnfreezeSymbol);
        var obj = JsonNode.Parse(json) as JsonObject;
        obj.Should().NotBeNull();
        obj![MainViewModelDefaults.PayloadFreeze]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_FreezeSymbol_ShouldHaveFreezeTrue()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionFreezeSymbol);
        var obj = JsonNode.Parse(json) as JsonObject;
        obj.Should().NotBeNull();
        obj![MainViewModelDefaults.PayloadFreeze]!.GetValue<bool>().Should().BeTrue();
        obj[MainViewModelDefaults.PayloadIntValue]!.GetValue<int>().Should().Be(MainViewModelDefaults.DefaultCreditsValue);
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_ToggleInstantBuild_ShouldHaveEnableTrue()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionToggleInstantBuildPatch);
        var obj = JsonNode.Parse(json) as JsonObject;
        obj.Should().NotBeNull();
        obj![MainViewModelDefaults.PayloadEnable]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_SetUnitCap_ShouldHaveCorrectValues()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionSetUnitCap);
        var obj = JsonNode.Parse(json) as JsonObject;
        obj.Should().NotBeNull();
        obj![MainViewModelDefaults.PayloadSymbol]!.GetValue<string>().Should().Be("unit_cap");
        obj[MainViewModelDefaults.PayloadIntValue]!.GetValue<int>().Should().Be(MainViewModelDefaults.DefaultUnitCapValue);
        obj[MainViewModelDefaults.PayloadEnable]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildHotkeyStatus_Succeeded_ShouldContainSucceeded()
    {
        var result = new ActionExecutionResult(true, "done", AddressSource.Signature);
        var status = MainViewModelHotkeyHelpers.BuildHotkeyStatus("Ctrl+1", "set_credits", result);
        status.Should().Contain("succeeded");
        status.Should().Contain("Ctrl+1");
        status.Should().Contain("set_credits");
    }

    [Fact]
    public void BuildHotkeyStatus_Failed_ShouldContainFailed()
    {
        var result = new ActionExecutionResult(false, "error msg", AddressSource.Signature);
        var status = MainViewModelHotkeyHelpers.BuildHotkeyStatus("Ctrl+2", "freeze_timer", result);
        status.Should().Contain("failed");
        status.Should().Contain("Ctrl+2");
        status.Should().Contain("error msg");
    }

    [Fact]
    public void BuildHotkeyStatus_WithDiagnostics_ShouldAppendSuffix()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature,
            new Dictionary<string, object?>
            {
                ["backendRoute"] = "extender"
            });
        var status = MainViewModelHotkeyHelpers.BuildHotkeyStatus("Ctrl+1", "set_credits", result);
        status.Should().Contain("backend=extender");
    }
}
