using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelAdditionalCoverageTests
{
    [Fact]
    public void PopulateActiveFreezes_ShouldAddNone_WhenNoEntries()
    {
        var output = new ObservableCollection<string>();

        MainViewModelQuickActionHelpers.PopulateActiveFreezes(output, Array.Empty<string>(), Array.Empty<string>());

        output.Should().ContainSingle().Which.Should().Be("(none)");
    }

    [Fact]
    public void PopulateActiveFreezes_ShouldPrefixFrozenAndToggleEntries()
    {
        var output = new ObservableCollection<string>();

        MainViewModelQuickActionHelpers.PopulateActiveFreezes(
            output,
            new[] { "credits", "timer" },
            new[] { "instant_build" });

        output.Should().Equal("❄️ credits", "❄️ timer", "🔒 instant_build");
    }

    [Fact]
    public void HotkeyBindingItem_Defaults_ShouldMatchExpectedValues()
    {
        var item = new HotkeyBindingItem();

        item.Gesture.Should().Be("Ctrl+Shift+1");
        item.ActionId.Should().Be("set_credits");
        item.PayloadJson.Should().Be("{}");
    }

    [Fact]
    public void HotkeyBindingItem_Setters_ShouldAllowMutationAndNoOpOnSameValue()
    {
        var item = new HotkeyBindingItem();
        item.Gesture = "Ctrl+Shift+9";
        item.ActionId = "spawn_tactical_entity";
        item.PayloadJson = "{\"entityId\":\"ATAT\"}";

        item.Gesture = "Ctrl+Shift+9";
        item.ActionId = "spawn_tactical_entity";
        item.PayloadJson = "{\"entityId\":\"ATAT\"}";

        item.Gesture.Should().Be("Ctrl+Shift+9");
        item.ActionId.Should().Be("spawn_tactical_entity");
        item.PayloadJson.Should().Contain("entityId");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("  ", true)]
    [InlineData("hook_tag", false)]
    public void ResolveCreditsStateTag_ShouldFallbackWhenDiagnosticMissing(string? value, bool shouldFallback)
    {
        var diagnostics = new Dictionary<string, object?>();
        if (value is not null)
        {
            diagnostics["creditsStateTag"] = value;
        }

        var tag = MainViewModelCreditsHelpers.ResolveCreditsStateTag(
            new ActionExecutionResult(true, "ok", AddressSource.Signature, diagnostics),
            creditsFreeze: true);

        if (shouldFallback)
        {
            tag.Should().Be("HOOK_LOCK");
        }
        else
        {
            tag.Should().Be("hook_tag");
        }
    }

    [Fact]
    public void ResolveCreditsStateTag_ShouldFallbackToOneShotForNonFreezeMode()
    {
        var tag = MainViewModelCreditsHelpers.ResolveCreditsStateTag(
            new ActionExecutionResult(true, "ok", AddressSource.Signature, new Dictionary<string, object?>()),
            creditsFreeze: false);

        tag.Should().Be("HOOK_ONESHOT");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_ShouldReturnFailureForUnexpectedLockTag()
    {
        var result = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(
            creditsFreeze: true,
            value: 1000,
            stateTag: "HOOK_ONESHOT",
            diagnosticsSuffix: " [diag]");

        result.IsValid.Should().BeFalse();
        result.StatusMessage.Should().Contain("unexpected state");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_ShouldReturnFailureForUnexpectedOneShotTag()
    {
        var result = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(
            creditsFreeze: false,
            value: 1000,
            stateTag: "HOOK_LOCK",
            diagnosticsSuffix: " [diag]");

        result.IsValid.Should().BeFalse();
        result.StatusMessage.Should().Contain("unexpected state");
    }

    [Fact]
    public void BuildCreditsSuccessStatus_ShouldReturnSuccessForExpectedTags()
    {
        var locked = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(true, 5000, "HOOK_LOCK", string.Empty);
        var oneShot = MainViewModelCreditsHelpers.BuildCreditsSuccessStatus(false, 5000, "HOOK_ONESHOT", string.Empty);

        locked.IsValid.Should().BeTrue();
        locked.ShouldFreeze.Should().BeTrue();
        oneShot.IsValid.Should().BeTrue();
        oneShot.ShouldFreeze.Should().BeFalse();
    }

    [Theory]
    [InlineData("10", true, "")]
    [InlineData("0", true, "")]
    [InlineData("-1", false, "✗ Invalid credits value. Enter a positive whole number.")]
    [InlineData("abc", false, "✗ Invalid credits value. Enter a positive whole number.")]
    public void TryParseCreditsValue_ShouldValidateInput(string input, bool expected, string expectedError)
    {
        var ok = MainViewModelCreditsHelpers.TryParseCreditsValue(input, out var value, out var error);

        ok.Should().Be(expected);
        error.Should().Be(expectedError);
        if (!expected)
        {
            value.Should().Be(0);
        }
    }

    [Fact]
    public void ParseHotkeyPayload_ShouldFallbackToDefaultPayloadOnInvalidJson()
    {
        var binding = new HotkeyBindingItem
        {
            ActionId = MainViewModelDefaults.ActionSetCredits,
            PayloadJson = "{invalid"
        };

        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);

        payload[MainViewModelDefaults.PayloadKeySymbol]!.GetValue<string>().Should().Be(MainViewModelDefaults.SymbolCredits);
        payload[MainViewModelDefaults.PayloadKeyIntValue]!.GetValue<int>().Should().Be(MainViewModelDefaults.DefaultCreditsValue);
    }

    [Fact]
    public void ParseHotkeyPayload_ShouldRespectValidObjectPayload()
    {
        var binding = new HotkeyBindingItem
        {
            ActionId = MainViewModelDefaults.ActionFreezeTimer,
            PayloadJson = "{\"symbol\":\"timer\",\"boolValue\":false}"
        };

        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);

        payload["symbol"]!.GetValue<string>().Should().Be("timer");
        payload["boolValue"]!.GetValue<bool>().Should().BeFalse();
    }

    [Theory]
    [InlineData(MainViewModelDefaults.ActionFreezeTimer, MainViewModelDefaults.PayloadKeyBoolValue)]
    [InlineData(MainViewModelDefaults.ActionToggleFogReveal, MainViewModelDefaults.PayloadKeyBoolValue)]
    [InlineData(MainViewModelDefaults.ActionSetUnitCap, MainViewModelDefaults.PayloadKeyIntValue)]
    [InlineData(MainViewModelDefaults.ActionSetGameSpeed, MainViewModelDefaults.PayloadKeyFloatValue)]
    [InlineData(MainViewModelDefaults.ActionFreezeSymbol, MainViewModelDefaults.PayloadKeyFreeze)]
    [InlineData(MainViewModelDefaults.ActionUnfreezeSymbol, MainViewModelDefaults.PayloadKeyFreeze)]
    public void BuildDefaultHotkeyPayloadJson_ShouldContainExpectedKey(string actionId, string expectedKey)
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(actionId);
        var payload = JsonNode.Parse(json)!.AsObject();

        payload.ContainsKey(expectedKey).Should().BeTrue();
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_ShouldReturnEmptyObjectForUnknownAction()
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson("unknown_action");

        json.Should().Be("{}");
    }

    [Fact]
    public void BuildHotkeyStatus_ShouldRenderSuccessAndFailureModes()
    {
        var success = MainViewModelHotkeyHelpers.BuildHotkeyStatus(
            "Ctrl+1",
            "set_credits",
            new ActionExecutionResult(true, "ok", AddressSource.Signature, new Dictionary<string, object?>()));

        var failure = MainViewModelHotkeyHelpers.BuildHotkeyStatus(
            "Ctrl+1",
            "set_credits",
            new ActionExecutionResult(false, "boom", AddressSource.Signature, new Dictionary<string, object?>()));

        success.Should().Contain("succeeded");
        failure.Should().Contain("failed (boom)");
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldFailWhenProfileOrPresetMissing()
    {
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                SelectedProfileId: null,
                SelectedSpawnPreset: null,
                RuntimeMode: RuntimeMode.Galactic,
                SpawnQuantity: "1",
                SpawnDelayMs: "0"));

        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("select profile and preset");
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldFailWhenRuntimeModeUnknown()
    {
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                SelectedProfileId: "base_swfoc",
                SelectedSpawnPreset: new SpawnPresetViewItem("id", "label", "unit_a", "Empire", "marker_a", 1, 0, "desc"),
                RuntimeMode: RuntimeMode.Unknown,
                SpawnQuantity: "1",
                SpawnDelayMs: "0"));

        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain("runtime mode is unknown");
    }

    [Theory]
    [InlineData("0", "0", "Invalid spawn quantity")]
    [InlineData("abc", "0", "Invalid spawn quantity")]
    [InlineData("1", "-1", "Invalid spawn delay")]
    [InlineData("1", "abc", "Invalid spawn delay")]
    public void TryBuildBatchInputs_ShouldValidateQuantityAndDelay(string quantity, string delay, string expected)
    {
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                SelectedProfileId: "base_swfoc",
                SelectedSpawnPreset: new SpawnPresetViewItem("id", "label", "unit_a", "Empire", "marker_a", 1, 0, "desc"),
                RuntimeMode: RuntimeMode.Galactic,
                SpawnQuantity: quantity,
                SpawnDelayMs: delay));

        result.Succeeded.Should().BeFalse();
        result.FailureStatus.Should().Contain(expected);
    }

    [Fact]
    public void TryBuildBatchInputs_ShouldSucceedForValidInputs()
    {
        var preset = new SpawnPresetViewItem("id", "label", "unit_a", "Empire", "marker_a", 1, 0, "desc");
        var result = MainViewModelSpawnHelpers.TryBuildBatchInputs(
            new MainViewModelSpawnHelpers.SpawnBatchInputRequest(
                SelectedProfileId: "base_swfoc",
                SelectedSpawnPreset: preset,
                RuntimeMode: RuntimeMode.Galactic,
                SpawnQuantity: "3",
                SpawnDelayMs: "250"));

        result.Succeeded.Should().BeTrue();
        result.ProfileId.Should().Be("base_swfoc");
        result.SelectedPreset.Should().BeSameAs(preset);
        result.Quantity.Should().Be(3);
        result.DelayMs.Should().Be(250);
    }
}

