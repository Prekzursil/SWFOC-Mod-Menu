using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Full branch coverage for MainViewModelHotkeyHelpers.
/// </summary>
public sealed class MainViewModelHotkeyHelpersFullCoverageTests
{
    // ── ParseHotkeyPayload ──

    [Fact]
    public void ParseHotkeyPayload_NullBinding_ShouldThrow()
    {
        var act = () => MainViewModelHotkeyHelpers.ParseHotkeyPayload(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ParseHotkeyPayload_ValidJson_ShouldReturnParsedObject()
    {
        var binding = new HotkeyBindingItem
        {
            Gesture = "Ctrl+1",
            ActionId = "set_credits",
            PayloadJson = """{"symbol":"credits","intValue":500}"""
        };

        var obj = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);

        obj["symbol"]!.GetValue<string>().Should().Be("credits");
        obj["intValue"]!.GetValue<int>().Should().Be(500);
    }

    [Fact]
    public void ParseHotkeyPayload_EmptyObjectJson_ShouldReturnParsedEmptyObject()
    {
        // HotkeyBindingItem normalizes null PayloadJson to "{}".
        var binding = new HotkeyBindingItem
        {
            Gesture = "Ctrl+1",
            ActionId = MainViewModelDefaults.ActionSetCredits,
            PayloadJson = "{}"
        };

        var obj = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);

        obj.Should().NotBeNull();
    }

    [Fact]
    public void ParseHotkeyPayload_InvalidJson_ShouldReturnDefaultPayload()
    {
        var binding = new HotkeyBindingItem
        {
            Gesture = "Ctrl+1",
            ActionId = MainViewModelDefaults.ActionFreezeTimer,
            PayloadJson = "NOT_JSON{{{"
        };

        var obj = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);

        // Should be the default payload for freeze_timer
        obj[MainViewModelDefaults.PayloadSymbol]!.GetValue<string>()
            .Should().Be(MainViewModelDefaults.SymbolGameTimerFreeze);
    }

    [Fact]
    public void ParseHotkeyPayload_JsonArray_ShouldReturnDefaultPayload()
    {
        // JsonNode.Parse("[1,2]") returns JsonArray, not JsonObject
        var binding = new HotkeyBindingItem
        {
            Gesture = "Ctrl+1",
            ActionId = MainViewModelDefaults.ActionToggleFogReveal,
            PayloadJson = "[1,2,3]"
        };

        var obj = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);

        obj[MainViewModelDefaults.PayloadSymbol]!.GetValue<string>()
            .Should().Be(MainViewModelDefaults.SymbolFogReveal);
    }

    // ── BuildDefaultHotkeyPayloadJson ──

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_NullActionId_ShouldThrow()
    {
        var act = () => MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(MainViewModelDefaults.ActionSetCredits, MainViewModelDefaults.PayloadSymbol)]
    [InlineData(MainViewModelDefaults.ActionFreezeTimer, MainViewModelDefaults.PayloadSymbol)]
    [InlineData(MainViewModelDefaults.ActionToggleFogReveal, MainViewModelDefaults.PayloadSymbol)]
    [InlineData(MainViewModelDefaults.ActionSetUnitCap, MainViewModelDefaults.PayloadSymbol)]
    [InlineData(MainViewModelDefaults.ActionToggleInstantBuildPatch, MainViewModelDefaults.PayloadEnable)]
    [InlineData(MainViewModelDefaults.ActionSetGameSpeed, MainViewModelDefaults.PayloadSymbol)]
    [InlineData(MainViewModelDefaults.ActionFreezeSymbol, MainViewModelDefaults.PayloadSymbol)]
    [InlineData(MainViewModelDefaults.ActionUnfreezeSymbol, MainViewModelDefaults.PayloadSymbol)]
    public void BuildDefaultHotkeyPayloadJson_KnownActions_ShouldContainExpectedKey(string actionId, string expectedKey)
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(actionId);
        var obj = JsonNode.Parse(json) as JsonObject;

        obj.Should().NotBeNull();
        obj![expectedKey].Should().NotBeNull();
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_UnknownAction_ShouldReturnEmptyObject()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson("unknown_action");
        var obj = JsonNode.Parse(json) as JsonObject;

        obj.Should().NotBeNull();
        obj!.Count.Should().Be(0);
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_SetCredits_ShouldHaveLockCreditsKey()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionSetCredits);
        var obj = JsonNode.Parse(json) as JsonObject;

        obj![MainViewModelDefaults.PayloadLockCredits]!.GetValue<bool>().Should().BeFalse();
        obj[MainViewModelDefaults.PayloadIntValue]!.GetValue<int>()
            .Should().Be(MainViewModelDefaults.DefaultCreditsValue);
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_FreezeSymbol_ShouldHaveFreezeKey()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionFreezeSymbol);
        var obj = JsonNode.Parse(json) as JsonObject;

        obj![MainViewModelDefaults.PayloadFreeze]!.GetValue<bool>().Should().BeTrue();
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_UnfreezeSymbol_ShouldHaveFreezeKeyFalse()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionUnfreezeSymbol);
        var obj = JsonNode.Parse(json) as JsonObject;

        obj![MainViewModelDefaults.PayloadFreeze]!.GetValue<bool>().Should().BeFalse();
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_SetGameSpeed_ShouldHaveFloatValue()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionSetGameSpeed);
        var obj = JsonNode.Parse(json) as JsonObject;

        obj![MainViewModelDefaults.PayloadFloatValue]!.GetValue<float>()
            .Should().Be(MainViewModelDefaults.DefaultGameSpeedValue);
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_SetUnitCap_ShouldHaveEnableKey()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(MainViewModelDefaults.ActionSetUnitCap);
        var obj = JsonNode.Parse(json) as JsonObject;

        obj![MainViewModelDefaults.PayloadEnable]!.GetValue<bool>().Should().BeTrue();
        obj[MainViewModelDefaults.PayloadIntValue]!.GetValue<int>()
            .Should().Be(MainViewModelDefaults.DefaultUnitCapValue);
    }

    // ── BuildHotkeyStatus ──

    [Fact]
    public void BuildHotkeyStatus_NullGesture_ShouldThrow()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.None);
        var act = () => MainViewModelHotkeyHelpers.BuildHotkeyStatus(null!, "a", result);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildHotkeyStatus_NullActionId_ShouldThrow()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.None);
        var act = () => MainViewModelHotkeyHelpers.BuildHotkeyStatus("g", null!, result);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildHotkeyStatus_NullResult_ShouldThrow()
    {
        var act = () => MainViewModelHotkeyHelpers.BuildHotkeyStatus("g", "a", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildHotkeyStatus_Succeeded_ShouldContainSucceeded()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.None);
        var status = MainViewModelHotkeyHelpers.BuildHotkeyStatus("Ctrl+1", "set_credits", result);

        status.Should().Contain("succeeded");
        status.Should().Contain("Ctrl+1");
        status.Should().Contain("set_credits");
    }

    [Fact]
    public void BuildHotkeyStatus_Failed_ShouldContainFailed()
    {
        var result = new ActionExecutionResult(false, "blocked", AddressSource.None);
        var status = MainViewModelHotkeyHelpers.BuildHotkeyStatus("Ctrl+2", "freeze_timer", result);

        status.Should().Contain("failed");
        status.Should().Contain("blocked");
    }

    [Fact]
    public void BuildHotkeyStatus_WithDiagnostics_ShouldAppendSuffix()
    {
        // The "backend" segment uses candidateKeys=["backendRoute"], so "backendRoute"
        // must be present for the segment to appear in the suffix.
        var result = new ActionExecutionResult(true, "ok", AddressSource.None,
            Diagnostics: new Dictionary<string, object?>
            {
                ["backendRoute"] = "sdk"
            });

        var status = MainViewModelHotkeyHelpers.BuildHotkeyStatus("Ctrl+1", "a", result);
        status.Should().Contain("backend=sdk");
    }
}
