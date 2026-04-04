using System.Collections.ObjectModel;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Branch coverage for MainViewModelHotkeyHelpers: ParseHotkeyPayload,
/// BuildDefaultHotkeyPayloadJson, BuildHotkeyStatus, LoadHotkeysAsync, SaveHotkeysAsync.
/// </summary>
public sealed class MainViewModelHotkeyHelpersBranchTests
{
    [Fact]
    public void GetHotkeyFilePath_ShouldReturnNonEmptyPath()
    {
        var path = MainViewModelHotkeyHelpers.GetHotkeyFilePath();
        path.Should().NotBeNullOrWhiteSpace();
        path.Should().Contain("hotkeys.json");
    }

    [Fact]
    public void ParseHotkeyPayload_ShouldThrow_WhenBindingIsNull()
    {
        var act = () => MainViewModelHotkeyHelpers.ParseHotkeyPayload(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ParseHotkeyPayload_ShouldParseValidJson()
    {
        var binding = new HotkeyBindingItem
        {
            ActionId = "set_credits",
            PayloadJson = """{"symbol":"credits","intValue":5000}"""
        };
        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        payload["symbol"]!.GetValue<string>().Should().Be("credits");
        payload["intValue"]!.GetValue<int>().Should().Be(5000);
    }

    [Fact]
    public void ParseHotkeyPayload_ShouldReturnEmptyObject_WhenJsonIsNull()
    {
        // When PayloadJson is null, the ?? operator substitutes "{}" which parses to an empty JsonObject
        var binding = new HotkeyBindingItem
        {
            ActionId = MainViewModelDefaults.ActionSetCredits,
            PayloadJson = null
        };
        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        payload.Should().NotBeNull();
    }

    [Fact]
    public void ParseHotkeyPayload_ShouldFallbackToDefault_WhenJsonIsInvalid()
    {
        var binding = new HotkeyBindingItem
        {
            ActionId = MainViewModelDefaults.ActionFreezeTimer,
            PayloadJson = "{ not valid json"
        };
        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        payload.Should().NotBeNull();
    }

    [Fact]
    public void ParseHotkeyPayload_ShouldFallbackToDefault_WhenJsonIsArray()
    {
        var binding = new HotkeyBindingItem
        {
            ActionId = MainViewModelDefaults.ActionSetCredits,
            PayloadJson = "[1,2,3]"
        };
        var payload = MainViewModelHotkeyHelpers.ParseHotkeyPayload(binding);
        payload.Should().NotBeNull();
    }

    [Fact]
    public void BuildDefaultHotkeyPayloadJson_ShouldThrow_WhenActionIdIsNull()
    {
        var act = () => MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(null!);
        act.Should().Throw<ArgumentNullException>();
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
    [InlineData("unknown_action")]
    public void BuildDefaultHotkeyPayloadJson_ShouldReturnValidJson_ForAllActions(string actionId)
    {
        var json = MainViewModelHotkeyHelpers.BuildDefaultHotkeyPayloadJson(actionId);
        json.Should().NotBeNullOrWhiteSpace();
        json.Should().StartWith("{");
    }

    [Fact]
    public void BuildHotkeyStatus_ShouldThrow_WhenGestureIsNull()
    {
        var act = () => MainViewModelHotkeyHelpers.BuildHotkeyStatus(
            null!, "action", new ActionExecutionResult(true, "ok", AddressSource.None));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildHotkeyStatus_ShouldThrow_WhenActionIdIsNull()
    {
        var act = () => MainViewModelHotkeyHelpers.BuildHotkeyStatus(
            "Ctrl+1", null!, new ActionExecutionResult(true, "ok", AddressSource.None));
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildHotkeyStatus_ShouldThrow_WhenResultIsNull()
    {
        var act = () => MainViewModelHotkeyHelpers.BuildHotkeyStatus("Ctrl+1", "action", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildHotkeyStatus_ShouldIncludeSucceeded_WhenResultSucceeded()
    {
        var result = new ActionExecutionResult(true, "done", AddressSource.Signature);
        var status = MainViewModelHotkeyHelpers.BuildHotkeyStatus("Ctrl+1", "set_credits", result);
        status.Should().Contain("succeeded");
        status.Should().Contain("Ctrl+1");
        status.Should().Contain("set_credits");
    }

    [Fact]
    public void BuildHotkeyStatus_ShouldIncludeFailed_WhenResultFailed()
    {
        var result = new ActionExecutionResult(false, "error msg", AddressSource.None);
        var status = MainViewModelHotkeyHelpers.BuildHotkeyStatus("Ctrl+2", "freeze_timer", result);
        status.Should().Contain("failed");
        status.Should().Contain("error msg");
    }

    [Fact]
    public void BuildHotkeyStatus_ShouldIncludeDiagnosticsSuffix()
    {
        var result = new ActionExecutionResult(true, "ok", AddressSource.Signature,
            new Dictionary<string, object?> { ["backendRoute"] = "extender" });
        var status = MainViewModelHotkeyHelpers.BuildHotkeyStatus("Ctrl+3", "set_credits", result);
        status.Should().Contain("backend=extender");
    }

    [Fact]
    public async Task LoadHotkeysAsync_ShouldThrow_WhenHotkeysIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            MainViewModelHotkeyHelpers.LoadHotkeysAsync(null!));
    }

    [Fact]
    public async Task LoadHotkeysAsync_ShouldPopulateDefaults_WhenFileDoesNotExist()
    {
        // Delete hotkey file if it exists to test default population
        var path = MainViewModelHotkeyHelpers.GetHotkeyFilePath();
        var existed = File.Exists(path);
        byte[]? backup = null;
        if (existed)
        {
            backup = await File.ReadAllBytesAsync(path);
            File.Delete(path);
        }

        try
        {
            var hotkeys = new ObservableCollection<HotkeyBindingItem>();
            var status = await MainViewModelHotkeyHelpers.LoadHotkeysAsync(hotkeys);
            hotkeys.Should().HaveCountGreaterOrEqualTo(5);
            status.Should().Contain("default");
        }
        finally
        {
            if (backup is not null)
            {
                await File.WriteAllBytesAsync(path, backup);
            }
        }
    }

    [Fact]
    public async Task SaveHotkeysAsync_ShouldThrow_WhenHotkeysIsNull()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            MainViewModelHotkeyHelpers.SaveHotkeysAsync(null!));
    }

    [Fact]
    public async Task SaveHotkeysAsync_ThenLoadHotkeysAsync_ShouldRoundTrip()
    {
        var path = MainViewModelHotkeyHelpers.GetHotkeyFilePath();
        var existed = File.Exists(path);
        byte[]? backup = null;
        if (existed)
        {
            backup = await File.ReadAllBytesAsync(path);
        }

        try
        {
            var items = new List<HotkeyBindingItem>
            {
                new() { Gesture = "Ctrl+9", ActionId = "test_action", PayloadJson = "{}" }
            };

            var saveStatus = await MainViewModelHotkeyHelpers.SaveHotkeysAsync(items);
            saveStatus.Should().Contain("1");

            var loaded = new ObservableCollection<HotkeyBindingItem>();
            var loadStatus = await MainViewModelHotkeyHelpers.LoadHotkeysAsync(loaded);
            loaded.Should().HaveCount(1);
            loaded[0].Gesture.Should().Be("Ctrl+9");
            loadStatus.Should().Contain("1");
        }
        finally
        {
            if (backup is not null)
                await File.WriteAllBytesAsync(path, backup);
            else if (File.Exists(path))
                File.Delete(path);
        }
    }
}
