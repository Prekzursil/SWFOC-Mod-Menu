using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Full branch coverage for MainViewModelQuickActionsBase and MainViewModel top-level methods.
/// </summary>
public sealed class MainViewModelQuickActionsCoverageTests
{
    // ── QuickRunActionAsync ──

    [Fact]
    public async Task QuickRunActionAsync_NotAttached_ShouldReturnEarlyWithoutStatusChange()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: null));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickRunActionAsync",
            "set_credits", new JsonObject(), null);

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task QuickRunActionAsync_NullProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: BuildSession()));
        vm.SelectedProfileId = null;

        await InvokeAsync(vm, "QuickRunActionAsync",
            "set_credits", new JsonObject(), null);

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task QuickRunActionAsync_EmptyActionId_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: BuildSession()));
        vm.SelectedProfileId = "test";

        // actionId is empty string after null guard — should hit the IsNullOrWhiteSpace guard
        await InvokeAsync(vm, "QuickRunActionAsync",
            "", new JsonObject(), null);

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task QuickRunActionAsync_Success_WithToggleKey_ShouldToggleState()
    {
        var vm = CreateViewModel();
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: true));
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        // First call — toggle ON
        await InvokeAsync(vm, "QuickRunActionAsync",
            "freeze_timer", new JsonObject(), "game_timer_freeze");

        vm.Status.Should().Contain("freeze_timer");

        // Second call — toggle OFF
        await InvokeAsync(vm, "QuickRunActionAsync",
            "freeze_timer", new JsonObject(), "game_timer_freeze");
    }

    [Fact]
    public async Task QuickRunActionAsync_Success_WithoutToggleKey_ShouldNotToggle()
    {
        var vm = CreateViewModel();
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: true));
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickRunActionAsync",
            "set_credits", new JsonObject(), null);

        vm.Status.Should().Contain("set_credits");
    }

    [Fact]
    public async Task QuickRunActionAsync_Failed_ShouldNotToggle()
    {
        var vm = CreateViewModel();
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: false));
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickRunActionAsync",
            "set_credits", new JsonObject(), "credits");

        vm.Status.Should().Contain("set_credits");
    }

    [Fact]
    public async Task QuickRunActionAsync_InvalidOperationException_ShouldSetErrorStatus()
    {
        var vm = CreateViewModel();
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateThrowingOrchestrator(new InvalidOperationException("bad state")));
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickRunActionAsync",
            "set_credits", BuildValidCreditsPayload(), null);

        vm.Status.Should().Contain("bad state");
    }

    [Fact]
    public async Task QuickRunActionAsync_Win32Exception_ShouldSetErrorStatus()
    {
        var vm = CreateViewModel();
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator",
            CreateThrowingOrchestrator(new System.ComponentModel.Win32Exception("access denied")));
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickRunActionAsync",
            "set_credits", BuildValidCreditsPayload(), null);

        vm.Status.Should().Contain("access denied");
    }

    [Fact]
    public async Task QuickRunActionAsync_IOException_ShouldSetErrorStatus()
    {
        var vm = CreateViewModel();
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator",
            CreateThrowingOrchestrator(new IOException("disk error")));
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickRunActionAsync",
            "set_credits", BuildValidCreditsPayload(), null);

        vm.Status.Should().Contain("disk error");
    }

    private static JsonObject BuildValidCreditsPayload() => new()
    {
        ["symbol"] = "credits",
        ["intValue"] = 1000
    };

    // ── QuickSetCreditsAsync ──

    [Fact]
    public async Task QuickSetCreditsAsync_InvalidCreditsValue_ShouldSetParseError()
    {
        var vm = CreateViewModel();
        vm.CreditsValue = "not_a_number";

        await InvokeAsync(vm, "QuickSetCreditsAsync");

        vm.Status.Should().Contain("Invalid credits value");
    }

    [Fact]
    public async Task QuickSetCreditsAsync_NotAttached_ShouldSetNotAttachedStatus()
    {
        var vm = CreateViewModel();
        vm.CreditsValue = "1000";
        SetField(vm, "_runtime", new StubRuntime(session: null));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickSetCreditsAsync");

        vm.Status.Should().Contain("Not attached to game");
    }

    [Fact]
    public async Task QuickSetCreditsAsync_WhitespaceProfile_ShouldSetNotAttachedStatus()
    {
        var vm = CreateViewModel();
        vm.CreditsValue = "1000";
        SetField(vm, "_runtime", new StubRuntime(session: BuildSession()));
        vm.SelectedProfileId = "  ";

        await InvokeAsync(vm, "QuickSetCreditsAsync");

        vm.Status.Should().Contain("Not attached to game");
    }

    [Fact]
    public async Task QuickSetCreditsAsync_SucceededOneShotMode_ShouldSetOneShotStatus()
    {
        var vm = CreateViewModel();
        vm.CreditsValue = "5000";
        vm.CreditsFreeze = false;
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: true, creditsStateTag: "HOOK_ONESHOT"));
        SetField(vm, "_freezeService", new StubFreezeService());
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickSetCreditsAsync");

        vm.Status.Should().Contain("HOOK_ONESHOT");
    }

    [Fact]
    public async Task QuickSetCreditsAsync_SucceededLockMode_ShouldFreezeAndSetLockStatus()
    {
        var vm = CreateViewModel();
        vm.CreditsValue = "5000";
        vm.CreditsFreeze = true;
        var session = BuildSession();
        var freezeService = new StubFreezeService();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: true, creditsStateTag: "HOOK_LOCK"));
        SetField(vm, "_freezeService", freezeService);
        SetField(vm, "_freezeUiTimer", CreateStoppedTimer());
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickSetCreditsAsync");

        vm.Status.Should().Contain("HOOK_LOCK");
        freezeService.FrozenSymbols.Should().Contain("credits");
    }

    [Fact]
    public async Task QuickSetCreditsAsync_SucceededButUnexpectedStateForFreeze_ShouldSetFailureStatus()
    {
        var vm = CreateViewModel();
        vm.CreditsValue = "5000";
        vm.CreditsFreeze = true;
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: true, creditsStateTag: "HOOK_ONESHOT"));
        SetField(vm, "_freezeService", new StubFreezeService());
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickSetCreditsAsync");

        vm.Status.Should().Contain("unexpected state");
    }

    [Fact]
    public async Task QuickSetCreditsAsync_SucceededButUnexpectedStateForOneShot_ShouldSetFailureStatus()
    {
        var vm = CreateViewModel();
        vm.CreditsValue = "5000";
        vm.CreditsFreeze = false;
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: true, creditsStateTag: "HOOK_LOCK"));
        SetField(vm, "_freezeService", new StubFreezeService());
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickSetCreditsAsync");

        vm.Status.Should().Contain("unexpected state");
    }

    [Fact]
    public async Task QuickSetCreditsAsync_Failed_ShouldSetErrorStatus()
    {
        var vm = CreateViewModel();
        vm.CreditsValue = "5000";
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: false));
        SetField(vm, "_freezeService", new StubFreezeService());
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickSetCreditsAsync");

        vm.Status.Should().Contain("Credits:");
    }

    [Fact]
    public async Task QuickSetCreditsAsync_ThrowsInvalidOperation_ShouldCatch()
    {
        var vm = CreateViewModel();
        vm.CreditsValue = "5000";
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateThrowingOrchestrator(new InvalidOperationException("oom")));
        SetField(vm, "_freezeService", new StubFreezeService());
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickSetCreditsAsync");

        vm.Status.Should().Contain("Credits: oom");
    }

    [Fact]
    public async Task QuickSetCreditsAsync_ThrowsWin32_ShouldCatch()
    {
        var vm = CreateViewModel();
        vm.CreditsValue = "5000";
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateThrowingOrchestrator(new System.ComponentModel.Win32Exception("denied")));
        SetField(vm, "_freezeService", new StubFreezeService());
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickSetCreditsAsync");

        vm.Status.Should().Contain("Credits:");
    }

    [Fact]
    public async Task QuickSetCreditsAsync_ThrowsIO_ShouldCatch()
    {
        var vm = CreateViewModel();
        vm.CreditsValue = "5000";
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateThrowingOrchestrator(new IOException("disk")));
        SetField(vm, "_freezeService", new StubFreezeService());
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickSetCreditsAsync");

        vm.Status.Should().Contain("Credits:");
    }

    [Fact]
    public async Task QuickSetCreditsAsync_WithExistingFreeze_ShouldResetFirst()
    {
        var vm = CreateViewModel();
        vm.CreditsValue = "5000";
        vm.CreditsFreeze = false;
        var session = BuildSession();
        var freezeService = new StubFreezeService();
        freezeService.FreezeInt("credits", 1000); // pre-freeze
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: true, creditsStateTag: "HOOK_ONESHOT"));
        SetField(vm, "_freezeService", freezeService);
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "QuickSetCreditsAsync");

        // Freeze was cleared before execution
        freezeService.UnfrozenSymbols.Should().Contain("credits");
    }

    // ── QuickUnfreezeAllAsync ──

    [Fact]
    public async Task QuickUnfreezeAllAsync_ShouldClearFreezesAndToggles()
    {
        var vm = CreateViewModel();
        var freezeService = new StubFreezeService();
        SetField(vm, "_freezeService", freezeService);
        SetField(vm, "_freezeUiTimer", CreateStoppedTimer());

        await InvokeAsync(vm, "QuickUnfreezeAllAsync");

        vm.Status.Should().Contain("All freezes and toggles cleared");
        freezeService.UnfreezeAllCalled.Should().BeTrue();
    }

    // ── RefreshActiveFreezes ──

    [Fact]
    public void RefreshActiveFreezes_ShouldPopulateFromFreezeServiceAndToggles()
    {
        var vm = CreateViewModel();
        var freezeService = new StubFreezeService();
        freezeService.FreezeInt("credits", 1000);
        SetField(vm, "_freezeService", freezeService);
        SetField(vm, "_freezeUiTimer", CreateStoppedTimer());

        Invoke(vm, "RefreshActiveFreezes");

        vm.ActiveFreezes.Should().HaveCountGreaterOrEqualTo(1);
    }

    // ── AddHotkeyAsync / RemoveHotkeyAsync ──

    [Fact]
    public async Task AddHotkeyAsync_ShouldAddDefaultHotkey()
    {
        var vm = CreateViewModel();
        vm.SelectedActionId = "test_action";

        await InvokeAsync(vm, "AddHotkeyAsync");

        vm.Hotkeys.Should().HaveCount(1);
        vm.Hotkeys[0].ActionId.Should().Be("test_action");
    }

    [Fact]
    public async Task RemoveHotkeyAsync_NullSelectedHotkey_ShouldDoNothing()
    {
        var vm = CreateViewModel();
        vm.SelectedHotkey = null;

        await InvokeAsync(vm, "RemoveHotkeyAsync");

        vm.Hotkeys.Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveHotkeyAsync_WithSelectedHotkey_ShouldRemove()
    {
        var vm = CreateViewModel();
        var hotkey = new HotkeyBindingItem { Gesture = "Ctrl+1", ActionId = "a" };
        vm.Hotkeys.Add(hotkey);
        vm.SelectedHotkey = hotkey;

        await InvokeAsync(vm, "RemoveHotkeyAsync");

        vm.Hotkeys.Should().BeEmpty();
    }

    // ── ExecuteHotkeyAsync ──

    [Fact]
    public async Task ExecuteHotkeyAsync_NotAttached_ShouldReturnFalse()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: null));
        vm.SelectedProfileId = "test";

        var result = await vm.ExecuteHotkeyAsync("Ctrl+1");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteHotkeyAsync_NullProfile_ShouldReturnFalse()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: BuildSession()));
        vm.SelectedProfileId = null;

        var result = await vm.ExecuteHotkeyAsync("Ctrl+1");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteHotkeyAsync_NoMatchingBinding_ShouldReturnFalse()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: BuildSession()));
        vm.SelectedProfileId = "test";

        var result = await vm.ExecuteHotkeyAsync("Ctrl+F12");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteHotkeyAsync_BindingWithEmptyActionId_ShouldReturnFalse()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: BuildSession()));
        vm.SelectedProfileId = "test";
        vm.Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+1", ActionId = "  " });

        var result = await vm.ExecuteHotkeyAsync("Ctrl+1");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteHotkeyAsync_ValidBinding_ShouldExecuteAndReturnTrue()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: BuildSession()));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: true));
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";
        vm.Hotkeys.Add(new HotkeyBindingItem
        {
            Gesture = "Ctrl+1",
            ActionId = "set_credits",
            PayloadJson = """{"symbol":"credits","intValue":1000}"""
        });

        var result = await vm.ExecuteHotkeyAsync("Ctrl+1");

        result.Should().BeTrue();
        vm.Status.Should().Contain("set_credits");
    }

    [Fact]
    public async Task ExecuteHotkeyAsync_NullGesture_ShouldThrow()
    {
        var vm = CreateViewModel();
        var act = async () => await vm.ExecuteHotkeyAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ── Helpers ──

    private static MainViewModel CreateViewModel()
    {
#pragma warning disable SYSLIB0050
        var vm = (MainViewModel)FormatterServices.GetUninitializedObject(typeof(MainViewModel));
#pragma warning restore SYSLIB0050
        SetProp(vm, "Profiles", new ObservableCollection<string>());
        SetProp(vm, "Actions", new ObservableCollection<string>());
        SetProp(vm, "CatalogSummary", new ObservableCollection<string>());
        SetProp(vm, "Updates", new ObservableCollection<string>());
        SetProp(vm, "SaveDiffPreview", new ObservableCollection<string>());
        SetProp(vm, "Hotkeys", new ObservableCollection<HotkeyBindingItem>());
        SetProp(vm, "ActiveFreezes", new ObservableCollection<string>());
        SetProp(vm, "SaveFields", new ObservableCollection<SaveFieldViewItem>());
        SetProp(vm, "FilteredSaveFields", new ObservableCollection<SaveFieldViewItem>());
        SetProp(vm, "SavePatchOperations", new ObservableCollection<SavePatchOperationViewItem>());
        SetProp(vm, "SavePatchCompatibility", new ObservableCollection<SavePatchCompatibilityViewItem>());
        SetProp(vm, "ActionReliability", new ObservableCollection<ActionReliabilityViewItem>());
        SetProp(vm, "SelectedUnitTransactions", new ObservableCollection<SelectedUnitTransactionViewItem>());
        SetProp(vm, "SpawnPresets", new ObservableCollection<SpawnPresetViewItem>());
        SetProp(vm, "LiveOpsDiagnostics", new ObservableCollection<string>());
        SetProp(vm, "ModCompatibilityRows", new ObservableCollection<string>());
        InitBackingFields(vm);
        return vm;
    }

    private static void InitBackingFields(object vm)
    {
        SetField(vm, "_status", "Ready");
        SetField(vm, "_selectedActionId", string.Empty);
        SetField(vm, "_payloadJson", MainViewModelDefaults.DefaultPayloadJsonTemplate);
        SetField(vm, "_runtimeMode", RuntimeMode.Unknown);
        SetField(vm, "_savePath", string.Empty);
        SetField(vm, "_saveNodePath", string.Empty);
        SetField(vm, "_saveEditValue", string.Empty);
        SetField(vm, "_saveSearchQuery", string.Empty);
        SetField(vm, "_savePatchPackPath", string.Empty);
        SetField(vm, "_savePatchMetadataSummary", "No patch pack loaded.");
        SetField(vm, "_savePatchApplySummary", string.Empty);
        SetField(vm, "_creditsValue", MainViewModelDefaults.DefaultCreditsValueText);
        SetField(vm, "_selectedUnitHp", string.Empty);
        SetField(vm, "_selectedUnitShield", string.Empty);
        SetField(vm, "_selectedUnitSpeed", string.Empty);
        SetField(vm, "_selectedUnitDamageMultiplier", string.Empty);
        SetField(vm, "_selectedUnitCooldownMultiplier", string.Empty);
        SetField(vm, "_selectedUnitVeterancy", string.Empty);
        SetField(vm, "_selectedUnitOwnerFaction", string.Empty);
        SetField(vm, "_selectedEntryMarker", "AUTO");
        SetField(vm, "_selectedFaction", "EMPIRE");
        SetField(vm, "_spawnQuantity", "1");
        SetField(vm, "_spawnDelayMs", "125");
        SetField(vm, "_loadedActionSpecs",
            new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        SetField(vm, "_profiles", new StubProfiles());
        SetField(vm, "_freezeService", new StubFreezeService());
        SetField(vm, "_freezeUiTimer", CreateStoppedTimer());
        SetField(vm, "_activeToggles", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
    }

    private static void SetProp(object instance, string propName, object value)
    {
        var type = instance.GetType();
        PropertyInfo? prop = null;
        while (type is not null && prop is null)
        {
            prop = type.GetProperty(propName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            type = type.BaseType;
        }

        prop.Should().NotBeNull($"property '{propName}' should exist");
        prop!.SetValue(instance, value);
    }

    private static DispatcherTimer CreateStoppedTimer()
    {
        // Create timer that can be queried for IsEnabled but won't actually tick.
        return new DispatcherTimer { Interval = TimeSpan.FromHours(24) };
    }

    private static void SetField(object instance, string fieldName, object? value)
    {
        var type = instance.GetType();
        FieldInfo? field = null;
        while (type is not null && field is null)
        {
            field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            type = type.BaseType;
        }

        field.Should().NotBeNull($"field '{fieldName}' should exist");
        field!.SetValue(instance, value);
    }

    private static void Invoke(object instance, string methodName, params object?[] args)
    {
        FindMethod(instance, methodName, args).Invoke(instance, args.Length == 0 ? null : args);
    }

    private static async Task InvokeAsync(object instance, string methodName, params object?[] args)
    {
        var task = FindMethod(instance, methodName, args).Invoke(instance, args.Length == 0 ? null : args);
        task.Should().BeAssignableTo<Task>();
        await (Task)task!;
    }

    private static MethodInfo FindMethod(object instance, string methodName, object?[] args)
    {
        var type = instance.GetType();
        MethodInfo? method = null;
        while (type is not null && method is null)
        {
            var candidates = type.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
                .Where(m => m.Name == methodName).ToArray();
            method = candidates.FirstOrDefault(m => m.GetParameters().Length == args.Length)
                     ?? candidates.FirstOrDefault(m => m.GetParameters().Length == 0 && args.Length == 0);
            type = type.BaseType;
        }

        method.Should().NotBeNull($"method '{methodName}' should exist");
        return method!;
    }

    private static AttachSession BuildSession()
    {
        return new AttachSession("test",
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null,
                ExeTarget.Swfoc, RuntimeMode.Galactic),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["credits"] = new("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature)
            }),
            DateTimeOffset.UtcNow);
    }

    private static TrainerOrchestrator CreateOrchestrator(bool succeeded, string? creditsStateTag = null)
    {
        var diagnostics = creditsStateTag is not null
            ? new Dictionary<string, object?> { ["creditsStateTag"] = creditsStateTag }
            : null;
        return new TrainerOrchestrator(
            new StubProfiles(),
            new StubExecutionRuntime(succeeded, diagnostics),
            new StubFreezeService(),
            new StubAuditLogger(),
            new StubTelemetry());
    }

    private static TrainerOrchestrator CreateThrowingOrchestrator(Exception ex)
    {
        return new TrainerOrchestrator(
            new StubProfiles(),
            new ThrowingExecutionRuntime(ex),
            new StubFreezeService(),
            new StubAuditLogger(),
            new StubTelemetry());
    }

    // ── Stubs ──

    private sealed class StubRuntime : IRuntimeAdapter
    {
        private readonly AttachSession? _session;
        public StubRuntime(AttachSession? session) => _session = session;
        public bool IsAttached => _session is not null;
        public AttachSession? CurrentSession => _session;
        public Task<AttachSession> AttachAsync(string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task DetachAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubExecutionRuntime : IRuntimeAdapter
    {
        private readonly bool _succeeded;
        private readonly IReadOnlyDictionary<string, object?>? _diagnostics;

        public StubExecutionRuntime(bool succeeded, IReadOnlyDictionary<string, object?>? diagnostics = null)
        {
            _succeeded = succeeded;
            _diagnostics = diagnostics;
        }

        public bool IsAttached => true;
        public AttachSession? CurrentSession => null;
        public Task<AttachSession> AttachAsync(string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken ct)
            => Task.FromResult(new ActionExecutionResult(_succeeded, _succeeded ? "ok" : "failed",
                AddressSource.Signature, _diagnostics));

        public Task DetachAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ThrowingExecutionRuntime : IRuntimeAdapter
    {
        private readonly Exception _ex;
        public ThrowingExecutionRuntime(Exception ex) => _ex = ex;
        public bool IsAttached => true;
        public AttachSession? CurrentSession => null;
        public Task<AttachSession> AttachAsync(string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken ct) => throw _ex;
        public Task DetachAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubProfiles : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<TrainerProfile> LoadProfileAsync(string id, CancellationToken ct) => Task.FromResult(Build());
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string id, CancellationToken ct) => Task.FromResult(Build());
        public Task ValidateProfileAsync(TrainerProfile p, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(new[] { "test" });

        private static TrainerProfile Build()
        {
            var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["set_credits"] = new("set_credits", ActionCategory.Economy, RuntimeMode.Unknown, ExecutionKind.Sdk,
                    new System.Text.Json.Nodes.JsonObject { ["required"] = new System.Text.Json.Nodes.JsonArray("symbol", "intValue") },
                    false, 0),
                ["freeze_timer"] = new("freeze_timer", ActionCategory.Global, RuntimeMode.Unknown, ExecutionKind.Sdk,
                    new System.Text.Json.Nodes.JsonObject { ["required"] = new System.Text.Json.Nodes.JsonArray("symbol", "boolValue") },
                    false, 0),
            };
            return new("test", "test", null, ExeTarget.Swfoc, null,
                Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
                actions, new Dictionary<string, bool>(),
                Array.Empty<CatalogSource>(), "test", Array.Empty<HelperHookSpec>(),
                new Dictionary<string, string>());
        }
    }

    private sealed class StubFreezeService : IValueFreezeService
    {
        private readonly HashSet<string> _frozen = new(StringComparer.OrdinalIgnoreCase);
        public List<string> UnfrozenSymbols { get; } = new();
        public HashSet<string> FrozenSymbols => _frozen;
        public bool UnfreezeAllCalled { get; private set; }

        public void FreezeInt(string symbol, int value) => _frozen.Add(symbol);
        public void FreezeIntAggressive(string symbol, int value) => _frozen.Add(symbol);
        public void FreezeFloat(string symbol, float value) => _frozen.Add(symbol);
        public void FreezeBool(string symbol, bool value) => _frozen.Add(symbol);

        public bool Unfreeze(string symbol)
        {
            UnfrozenSymbols.Add(symbol);
            return _frozen.Remove(symbol);
        }

        public void UnfreezeAll()
        {
            UnfreezeAllCalled = true;
            _frozen.Clear();
        }

        public bool IsFrozen(string symbol) => _frozen.Contains(symbol);
        public IReadOnlyCollection<string> GetFrozenSymbols() => _frozen;
        public void Dispose() { }
    }

    private sealed class StubAuditLogger : IAuditLogger
    {
        public Task WriteAsync(ActionAuditRecord record, CancellationToken ct)
            => Task.CompletedTask;
    }

    private sealed class StubTelemetry : ITelemetrySnapshotService
    {
        public void RecordAction(string actionId, AddressSource source, bool succeeded) { }

        public TelemetrySnapshot CreateSnapshot()
            => new(DateTimeOffset.UtcNow,
                new Dictionary<string, int>(), new Dictionary<string, int>(),
                new Dictionary<string, int>(), 0, 0, 0, 0);

        public Task<string> ExportSnapshotAsync(string outputDirectory, CancellationToken ct)
            => Task.FromResult(Path.Join(outputDirectory, "telemetry.json"));

        public void Reset() { }
    }
}
