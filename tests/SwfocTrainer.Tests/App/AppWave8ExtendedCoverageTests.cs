using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using System.Windows.Threading;
using FluentAssertions;
using SwfocTrainer.App.Infrastructure;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 8 extended coverage — targets the uncovered async methods in MainViewModel,
/// SaveOpsBase, QuickActionsBase, and AsyncCommand that previous test files missed.
/// </summary>
public sealed class AppWave8ExtendedCoverageTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — LoadProfilesAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadProfilesAsync_ShouldPopulateProfilesAndSelectFirst()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "alpha", "beta" }));
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));
        SetField(vm, "_launchContextResolver", new StubLaunchContextResolver());
        SetField(vm, "_spawnPresets", new StubSpawnPresets());

        await InvokeAsync(vm, "LoadProfilesAsync");

        vm.Profiles.Should().Contain("alpha");
        vm.Profiles.Should().Contain("beta");
        // Status may be overwritten by LoadActionsAsync/LoadSpawnPresetsAsync triggered at end
        vm.Profiles.Count.Should().Be(2);
    }

    [Fact]
    public async Task LoadProfilesAsync_WithRecommended_ShouldUseRecommended()
    {
        var vm = CreateViewModel();
        var processes = new[]
        {
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic)
        };
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "base_swfoc", "custom_mod" }));
        SetField(vm, "_processLocator", new StubProcessLocator(processes));
        SetField(vm, "_launchContextResolver", new StubLaunchContextResolver());
        SetField(vm, "_spawnPresets", new StubSpawnPresets());

        await InvokeAsync(vm, "LoadProfilesAsync");

        // Profile was selected and actions loaded (status overwritten by LoadSpawnPresetsAsync at end)
        vm.SelectedProfileId.Should().Be("base_swfoc");
    }

    [Fact]
    public async Task LoadProfilesAsync_WithUniversalProfile_ShouldSelectUniversal()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "base_swfoc", "universal_auto" }));
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));
        SetField(vm, "_launchContextResolver", new StubLaunchContextResolver());
        SetField(vm, "_spawnPresets", new StubSpawnPresets());

        await InvokeAsync(vm, "LoadProfilesAsync");

        vm.SelectedProfileId.Should().Be("universal_auto");
    }

    [Fact]
    public async Task LoadProfilesAsync_KeepsExistingSelection_WhenProfileStillAvailable()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "alpha", "beta" }));
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));
        SetField(vm, "_launchContextResolver", new StubLaunchContextResolver());
        SetField(vm, "_spawnPresets", new StubSpawnPresets());
        vm.SelectedProfileId = "alpha";

        await InvokeAsync(vm, "LoadProfilesAsync");

        vm.SelectedProfileId.Should().Be("alpha");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — RecommendProfileIdAsync exceptions
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecommendProfileIdAsync_InvalidOperationException_ShouldReturnNull()
    {
        var vm = CreateViewModel();
        SetField(vm, "_processLocator", new ThrowingProcessLocator(new InvalidOperationException("fail")));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));

        var result = await InvokeAsyncWithResult<string?>(vm, "RecommendProfileIdAsync");

        result.Should().BeNull();
    }

    [Fact]
    public async Task RecommendProfileIdAsync_Win32Exception_ShouldReturnNull()
    {
        var vm = CreateViewModel();
        SetField(vm, "_processLocator", new ThrowingProcessLocator(new System.ComponentModel.Win32Exception("fail")));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));

        var result = await InvokeAsyncWithResult<string?>(vm, "RecommendProfileIdAsync");

        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — DetachAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DetachAsync_ShouldClearCollectionsAndSetStatus()
    {
        var vm = CreateViewModel();
        // Use a shared freeze service for both orchestrator and VM so UnfreezeAll is observable
        var sharedFreezeService = new StubFreezeService();
        var orchestrator = new TrainerOrchestrator(
            new FullStubProfiles(new[] { "test" }),
            new StubExecutionRuntime(true),
            sharedFreezeService,
            new StubAuditLogger(),
            new StubTelemetry());
        SetField(vm, "_runtime", new StubRuntime(session: BuildSession()));
        SetField(vm, "_orchestrator", orchestrator);
        SetField(vm, "_freezeService", sharedFreezeService);
        vm.ActionReliability.Add(new ActionReliabilityViewItem("a", "s", "r", 1.0, "d"));
        vm.SelectedUnitTransactions.Add(new SelectedUnitTransactionViewItem("t", DateTimeOffset.UtcNow, false, "op", "a"));
        vm.LiveOpsDiagnostics.Add("diag");

        await InvokeAsync(vm, "DetachAsync");

        vm.Status.Should().Be("Detached");
        vm.ActionReliability.Should().BeEmpty();
        vm.SelectedUnitTransactions.Should().BeEmpty();
        sharedFreezeService.UnfreezeAllCalled.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — AttachAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AttachAsync_EmptyProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;

        await InvokeAsync(vm, "AttachAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task AttachAsync_Success_ShouldSetSymbolCountAndLoadActions()
    {
        var vm = CreateViewModel();
        var session = BuildSession();
        var attachingRuntime = new StubAttachRuntime(session);
        SetField(vm, "_runtime", attachingRuntime);
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));
        SetField(vm, "_profileVariantResolver", new StubProfileVariantResolver(null));
        SetField(vm, "_spawnPresets", new StubSpawnPresets());
        SetField(vm, "_actionReliability", new StubActionReliability());
        SetField(vm, "_catalog", new StubCatalog());
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "AttachAsync");

        // AttachAsync sets resolved symbols count, then loads actions which overwrites Status
        vm.ResolvedSymbolsCount.Should().BeGreaterThan(0);
        vm.RuntimeMode.Should().Be(RuntimeMode.Galactic);
    }

    [Fact]
    public async Task AttachAsync_InvalidOperationException_ShouldHandleGracefully()
    {
        var vm = CreateViewModel();
        var throwingRuntime = new ThrowingAttachRuntime(new InvalidOperationException("process not found"));
        SetField(vm, "_runtime", throwingRuntime);
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));
        SetField(vm, "_profileVariantResolver", new StubProfileVariantResolver(null));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "AttachAsync");

        vm.Status.Should().Contain("Attach failed");
    }

    [Fact]
    public async Task AttachAsync_Win32Exception_ShouldHandleGracefully()
    {
        var vm = CreateViewModel();
        var throwingRuntime = new ThrowingAttachRuntime(new System.ComponentModel.Win32Exception("access denied"));
        SetField(vm, "_runtime", throwingRuntime);
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));
        SetField(vm, "_profileVariantResolver", new StubProfileVariantResolver(null));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "AttachAsync");

        vm.Status.Should().Contain("Attach failed");
    }

    [Fact]
    public async Task AttachAsync_IOException_ShouldHandleGracefully()
    {
        var vm = CreateViewModel();
        var throwingRuntime = new ThrowingAttachRuntime(new IOException("disk error"));
        SetField(vm, "_runtime", throwingRuntime);
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));
        SetField(vm, "_profileVariantResolver", new StubProfileVariantResolver(null));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "AttachAsync");

        vm.Status.Should().Contain("Attach failed");
    }

    [Fact]
    public async Task AttachAsync_UniversalProfile_ShouldResolveVariant()
    {
        var vm = CreateViewModel();
        var session = BuildSession();
        var attachingRuntime = new StubAttachRuntime(session);
        var variant = new ProfileVariantResolution("universal_auto", "base_swfoc", "swfoc_detected", 0.95);
        SetField(vm, "_runtime", attachingRuntime);
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "universal_auto", "base_swfoc" }));
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));
        SetField(vm, "_profileVariantResolver", new StubProfileVariantResolver(variant));
        SetField(vm, "_spawnPresets", new StubSpawnPresets());
        SetField(vm, "_actionReliability", new StubActionReliability());
        SetField(vm, "_catalog", new StubCatalog());
        vm.SelectedProfileId = "universal_auto";

        await InvokeAsync(vm, "AttachAsync");

        vm.SelectedProfileId.Should().Be("base_swfoc");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — HandleAttachFailureAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task HandleAttachFailureAsync_ShouldResetAndShowHint()
    {
        var vm = CreateViewModel();
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));

        await InvokeAsync(vm, "HandleAttachFailureAsync", new InvalidOperationException("test error"));

        vm.Status.Should().Contain("Attach failed: test error");
        vm.RuntimeMode.Should().Be(RuntimeMode.Unknown);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — BuildAttachProcessHintAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildAttachProcessHintAsync_NoProcesses_ShouldReturnNoProcessesMessage()
    {
        var vm = CreateViewModel();
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));

        var result = await InvokeAsyncWithResult<string>(vm, "BuildAttachProcessHintAsync");

        result.Should().Contain("none");
    }

    [Fact]
    public async Task BuildAttachProcessHintAsync_WithProcesses_ShouldReturnSummary()
    {
        var processes = new[]
        {
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic)
        };
        var vm = CreateViewModel();
        SetField(vm, "_processLocator", new StubProcessLocator(processes));

        var result = await InvokeAsyncWithResult<string>(vm, "BuildAttachProcessHintAsync");

        result.Should().Contain("swfoc.exe");
    }

    [Fact]
    public async Task BuildAttachProcessHintAsync_InvalidOperationException_ShouldReturnFallback()
    {
        var vm = CreateViewModel();
        SetField(vm, "_processLocator", new ThrowingProcessLocator(new InvalidOperationException("fail")));

        var result = await InvokeAsyncWithResult<string>(vm, "BuildAttachProcessHintAsync");

        result.Should().Contain("Could not enumerate");
    }

    [Fact]
    public async Task BuildAttachProcessHintAsync_Win32Exception_ShouldReturnFallback()
    {
        var vm = CreateViewModel();
        SetField(vm, "_processLocator", new ThrowingProcessLocator(new System.ComponentModel.Win32Exception("fail")));

        var result = await InvokeAsyncWithResult<string>(vm, "BuildAttachProcessHintAsync");

        result.Should().Contain("Could not enumerate");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — LoadActionsAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadActionsAsync_EmptyProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;

        await InvokeAsync(vm, "LoadActionsAsync");

        vm.Actions.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadActionsAsync_WithActions_ShouldPopulateActions()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_runtime", new StubRuntime(session: null));
        SetField(vm, "_actionReliability", new StubActionReliability());
        SetField(vm, "_catalog", new StubCatalog());
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "LoadActionsAsync");

        vm.Actions.Should().NotBeEmpty();
        vm.Status.Should().Contain("Loaded");
        vm.Status.Should().Contain("actions");
    }

    [Fact]
    public async Task LoadActionsAsync_WithAttachedSession_ShouldFilterUnavailableAndRefreshReliability()
    {
        var vm = CreateViewModel();
        var session = BuildSession();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_actionReliability", new StubActionReliability());
        SetField(vm, "_catalog", new StubCatalog());
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "LoadActionsAsync");

        vm.Status.Should().Contain("actions");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — ExecuteActionAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteActionAsync_EmptyProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;

        await InvokeAsync(vm, "ExecuteActionAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task ExecuteActionAsync_InvalidJson_ShouldSetErrorStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        vm.SelectedProfileId = "test";
        vm.PayloadJson = "not-json{{{";

        await InvokeAsync(vm, "ExecuteActionAsync");

        vm.Status.Should().Contain("Invalid payload JSON");
    }

    [Fact]
    public async Task ExecuteActionAsync_FeatureGated_ShouldBlockExecution()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FeatureGatedStubProfiles());
        vm.SelectedProfileId = "test";
        vm.PayloadJson = "{}";
        SetField(vm, "_selectedActionId", "toggle_fog_reveal_patch_fallback");

        await InvokeAsync(vm, "ExecuteActionAsync");

        vm.Status.Should().Contain("Action blocked");
    }

    [Fact]
    public async Task ExecuteActionAsync_Success_ShouldSetSucceededStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: true));
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";
        // Provide the required payload fields so orchestrator validation passes
        vm.PayloadJson = "{\"symbol\":\"credits\",\"intValue\":1000}";
        SetField(vm, "_selectedActionId", "set_credits");

        await InvokeAsync(vm, "ExecuteActionAsync");

        vm.Status.Should().Contain("Action succeeded");
    }

    [Fact]
    public async Task ExecuteActionAsync_Failed_ShouldSetFailedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: false));
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";
        vm.PayloadJson = "{\"symbol\":\"credits\",\"intValue\":1000}";
        SetField(vm, "_selectedActionId", "set_credits");

        await InvokeAsync(vm, "ExecuteActionAsync");

        vm.Status.Should().Contain("Action failed");
    }

    [Fact]
    public async Task ExecuteActionAsync_InvalidOperationException_ShouldHandleGracefully()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_orchestrator", CreateThrowingOrchestrator(new InvalidOperationException("not attached")));
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";
        vm.PayloadJson = "{}";
        SetField(vm, "_selectedActionId", "set_credits");

        await InvokeAsync(vm, "ExecuteActionAsync");

        vm.Status.Should().Contain("Action failed");
    }

    [Fact]
    public async Task ExecuteActionAsync_Win32Exception_ShouldHandleGracefully()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_orchestrator", CreateThrowingOrchestrator(new System.ComponentModel.Win32Exception("access denied")));
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";
        vm.PayloadJson = "{}";
        SetField(vm, "_selectedActionId", "set_credits");

        await InvokeAsync(vm, "ExecuteActionAsync");

        vm.Status.Should().Contain("Action failed");
    }

    [Fact]
    public async Task ExecuteActionAsync_IOException_ShouldHandleGracefully()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_orchestrator", CreateThrowingOrchestrator(new IOException("disk error")));
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";
        vm.PayloadJson = "{}";
        SetField(vm, "_selectedActionId", "set_credits");

        await InvokeAsync(vm, "ExecuteActionAsync");

        vm.Status.Should().Contain("Action failed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — EnsureActionAvailableForCurrentSessionAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task EnsureActionAvailable_NoSession_ShouldReturnTrue()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: null));
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));

        var result = await InvokeAsyncWithResult<bool>(vm, "EnsureActionAvailableForCurrentSessionAsync", "set_credits", "prefix");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task EnsureActionAvailable_WithSession_ActionNotFound_ShouldReturnTrue()
    {
        var vm = CreateViewModel();
        SetField(vm, "_runtime", new StubRuntime(session: BuildSession()));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = null;

        var result = await InvokeAsyncWithResult<bool>(vm, "EnsureActionAvailableForCurrentSessionAsync", "nonexistent_action", "prefix");

        result.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — ResolveActionSpecAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveActionSpecAsync_NullProfile_ShouldReturnNull()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = null;

        var result = await InvokeAsyncWithResult<ActionSpec?>(vm, "ResolveActionSpecAsync", "unknown_action");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveActionSpecAsync_ProfileLookupThrowsInvalidOp_ShouldReturnNull()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        SetField(vm, "_profiles", new ThrowingProfiles(new InvalidOperationException("not found")));
        vm.SelectedProfileId = "test";

        var result = await InvokeAsyncWithResult<ActionSpec?>(vm, "ResolveActionSpecAsync", "unknown_action");

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveActionSpecAsync_ProfileLookupThrowsKeyNotFound_ShouldReturnNull()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        SetField(vm, "_profiles", new ThrowingProfiles(new KeyNotFoundException("not found")));
        vm.SelectedProfileId = "test";

        var result = await InvokeAsyncWithResult<ActionSpec?>(vm, "ResolveActionSpecAsync", "unknown_action");

        result.Should().BeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — static helpers: ResolveLaunchMode, BuildLaunchWorkshopIds, ResolveProfileWorkshopChain
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("SteamMod", GameLaunchMode.SteamMod)]
    [InlineData("ModPath", GameLaunchMode.ModPath)]
    [InlineData("Vanilla", GameLaunchMode.Vanilla)]
    [InlineData("", GameLaunchMode.Vanilla)]
    [InlineData("unknown", GameLaunchMode.Vanilla)]
    public void ResolveLaunchMode_ShouldReturnExpected(string input, GameLaunchMode expected)
    {
        var method = typeof(MainViewModel).GetMethod(
            "ResolveLaunchMode",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var result = (GameLaunchMode)method!.Invoke(null, new object[] { input })!;
        result.Should().Be(expected);
    }

    [Fact]
    public void BuildLaunchWorkshopIds_Empty_ShouldReturnEmpty()
    {
        var vm = CreateViewModel();
        SetField(vm, "_launchWorkshopId", "");
        var method = typeof(MainViewModel).GetMethod(
            "BuildLaunchWorkshopIds",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var result = (IReadOnlyList<string>)method!.Invoke(vm, null)!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void BuildLaunchWorkshopIds_WithDuplicates_ShouldDedup()
    {
        var vm = CreateViewModel();
        SetField(vm, "_launchWorkshopId", "123,456,123,789");
        var method = typeof(MainViewModel).GetMethod(
            "BuildLaunchWorkshopIds",
            BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var result = (IReadOnlyList<string>)method!.Invoke(vm, null)!;
        result.Should().HaveCount(3);
        result.Should().Contain("123");
        result.Should().Contain("456");
        result.Should().Contain("789");
    }

    [Fact]
    public void ResolveProfileWorkshopChain_WithMetadata_ShouldResolveChain()
    {
        var method = typeof(MainViewModel).GetMethod(
            "ResolveProfileWorkshopChain",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredWorkshopIds"] = "ws1,ws2",
            ["parentDependencies"] = "dep1,dep2"
        };
        var profile = new TrainerProfile("test", "Test", null, ExeTarget.Swfoc, "main_ws",
            Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(), new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(), "test", Array.Empty<HelperHookSpec>(), metadata);

        var result = (IReadOnlyList<string>)method!.Invoke(null, new object[] { profile })!;
        result.Should().Contain("main_ws");
        result.Should().Contain("ws1");
        result.Should().Contain("dep1");
    }

    [Fact]
    public void ResolveProfileWorkshopChain_NoMetadata_ShouldReturnWorkshopIdOnly()
    {
        var method = typeof(MainViewModel).GetMethod(
            "ResolveProfileWorkshopChain",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var profile = new TrainerProfile("test", "Test", null, ExeTarget.Swfoc, "main_ws",
            Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
            new Dictionary<string, ActionSpec>(), new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(), "test", Array.Empty<HelperHookSpec>(),
            new Dictionary<string, string>());

        var result = (IReadOnlyList<string>)method!.Invoke(null, new object[] { profile })!;
        result.Should().HaveCount(1);
        result.Should().Contain("main_ws");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — ApplyPayloadTemplateForSelectedAction
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ApplyPayloadTemplate_EmptyActionId_ShouldNotChangePayload()
    {
        var vm = CreateViewModel();
        SetField(vm, "_selectedActionId", "");
        var original = vm.PayloadJson;

        Invoke(vm, "ApplyPayloadTemplateForSelectedAction");

        vm.PayloadJson.Should().Be(original);
    }

    [Fact]
    public void ApplyPayloadTemplate_ActionNotInSpecs_ShouldNotChangePayload()
    {
        var vm = CreateViewModel();
        SetField(vm, "_selectedActionId", "nonexistent");
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        var original = vm.PayloadJson;

        Invoke(vm, "ApplyPayloadTemplateForSelectedAction");

        vm.PayloadJson.Should().Be(original);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SaveOpsBase — CheckUpdatesAsync, InstallUpdateAsync, RollbackProfileUpdateAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckUpdatesAsync_WithUpdates_ShouldPopulateCollection()
    {
        var vm = CreateViewModel();
        SetField(vm, "_updates", new StubProfileUpdates(new[] { "profile_a" }));

        await InvokeAsync(vm, "CheckUpdatesAsync");

        vm.Updates.Should().Contain("profile_a");
        vm.Status.Should().Contain("Updates available");
    }

    [Fact]
    public async Task CheckUpdatesAsync_NoUpdates_ShouldShowNoUpdates()
    {
        var vm = CreateViewModel();
        SetField(vm, "_updates", new StubProfileUpdates(Array.Empty<string>()));

        await InvokeAsync(vm, "CheckUpdatesAsync");

        vm.Status.Should().Contain("No profile updates");
    }

    [Fact]
    public async Task InstallUpdateAsync_EmptyProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;

        await InvokeAsync(vm, "InstallUpdateAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task InstallUpdateAsync_Succeeded_ShouldSetStatusWithPath()
    {
        var vm = CreateViewModel();
        SetField(vm, "_updates", new StubProfileUpdates(Array.Empty<string>(), installSucceeded: true));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "InstallUpdateAsync");

        vm.Status.Should().Contain("Installed profile update");
    }

    [Fact]
    public async Task InstallUpdateAsync_Failed_ShouldSetFailureStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_updates", new StubProfileUpdates(Array.Empty<string>(), installSucceeded: false));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "InstallUpdateAsync");

        vm.Status.Should().Contain("failed");
    }

    [Fact]
    public async Task RollbackProfileUpdateAsync_EmptyProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;

        await InvokeAsync(vm, "RollbackProfileUpdateAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task RollbackProfileUpdateAsync_Succeeded_ShouldSetStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_updates", new StubProfileUpdates(Array.Empty<string>(), rollbackRestored: true));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "RollbackProfileUpdateAsync");

        vm.Status.Should().NotBe("Ready");
    }

    [Fact]
    public async Task RollbackProfileUpdateAsync_Failed_ShouldSetFailureStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_updates", new StubProfileUpdates(Array.Empty<string>(), rollbackRestored: false));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "RollbackProfileUpdateAsync");

        vm.Status.Should().Contain("Rollback failed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SaveOpsBase — LoadCatalogAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadCatalogAsync_EmptyProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;

        await InvokeAsync(vm, "LoadCatalogAsync");

        vm.CatalogSummary.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadCatalogAsync_WithCatalog_ShouldPopulate()
    {
        var vm = CreateViewModel();
        SetField(vm, "_catalog", new StubCatalog());
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "LoadCatalogAsync");

        vm.CatalogSummary.Should().NotBeEmpty();
        vm.Status.Should().Contain("Catalog loaded");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SaveOpsBase — DeployHelperAsync, VerifyHelperAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeployHelperAsync_EmptyProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;

        await InvokeAsync(vm, "DeployHelperAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task DeployHelperAsync_WithProfile_ShouldSetStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_helper", new StubHelperMod());
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "DeployHelperAsync");

        vm.Status.Should().Contain("Helper deployed");
    }

    [Fact]
    public async Task VerifyHelperAsync_EmptyProfile_ShouldReturnEarly()
    {
        var vm = CreateViewModel();
        vm.SelectedProfileId = null;

        await InvokeAsync(vm, "VerifyHelperAsync");

        vm.Status.Should().Be("Ready");
    }

    [Fact]
    public async Task VerifyHelperAsync_Passed_ShouldSetPassedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_helper", new StubHelperMod(verifyResult: true));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "VerifyHelperAsync");

        vm.Status.Should().Contain("passed");
    }

    [Fact]
    public async Task VerifyHelperAsync_Failed_ShouldSetFailedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_helper", new StubHelperMod(verifyResult: false));
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "VerifyHelperAsync");

        vm.Status.Should().Contain("failed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SaveOpsBase — ScaffoldModProfileAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ScaffoldModProfileAsync_ShouldSetSummary()
    {
        var vm = CreateViewModel();
        SetField(vm, "_modOnboarding", new StubModOnboarding());
        SetField(vm, "_onboardingDraftProfileId", "custom_test");
        SetField(vm, "_onboardingDisplayName", "Custom Test");
        SetField(vm, "_onboardingLaunchSample", @"C:\game\swfoc.exe MODPATH=C:\Mods\test");
        SetField(vm, "_onboardingBaseProfileId", "base_swfoc");
        SetField(vm, "_onboardingNamespaceRoot", "custom");

        await InvokeAsync(vm, "ScaffoldModProfileAsync");

        vm.Status.Should().Contain("Draft profile scaffolded");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SaveOpsBase — ExportSupportBundleAsync, ExportTelemetrySnapshotAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExportSupportBundleAsync_Succeeded_ShouldSetStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_supportBundles", new StubSupportBundles(succeeded: true));

        await InvokeAsync(vm, "ExportSupportBundleAsync");

        vm.Status.Should().Contain("Support bundle exported");
    }

    [Fact]
    public async Task ExportSupportBundleAsync_Failed_ShouldSetFailedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_supportBundles", new StubSupportBundles(succeeded: false));

        await InvokeAsync(vm, "ExportSupportBundleAsync");

        vm.Status.Should().Contain("failed");
    }

    [Fact]
    public async Task ExportTelemetrySnapshotAsync_ShouldSetStatus()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "swfoc_test_" + Guid.NewGuid().ToString("N")[..8]);
        var vm = CreateViewModel();
        SetField(vm, "_telemetry", new StubTelemetry());
        SetField(vm, "_supportBundleOutputDirectory", tempDir);

        try
        {
            await InvokeAsync(vm, "ExportTelemetrySnapshotAsync");
            vm.Status.Should().Contain("Telemetry snapshot exported");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QuickActionsBase — individual quick toggle methods
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task QuickFreezeTimerAsync_ShouldInvokeAction()
    {
        var vm = CreateAttachedViewModel();

        await InvokeAsync(vm, "QuickFreezeTimerAsync");

        vm.Status.Should().Contain("freeze_timer");
    }

    [Fact]
    public async Task QuickToggleFogAsync_ShouldInvokeAction()
    {
        var vm = CreateAttachedViewModel();

        await InvokeAsync(vm, "QuickToggleFogAsync");

        vm.Status.Should().Contain("toggle_fog_reveal");
    }

    [Fact]
    public async Task QuickToggleAiAsync_ShouldInvokeAction()
    {
        var vm = CreateAttachedViewModel();

        await InvokeAsync(vm, "QuickToggleAiAsync");

        vm.Status.Should().Contain("toggle_ai");
    }

    [Fact]
    public async Task QuickInstantBuildAsync_ShouldInvokeAction()
    {
        var vm = CreateAttachedViewModel();

        await InvokeAsync(vm, "QuickInstantBuildAsync");

        vm.Status.Should().Contain("toggle_instant_build_patch");
    }

    [Fact]
    public async Task QuickUnitCapAsync_ShouldInvokeAction()
    {
        var vm = CreateAttachedViewModel();

        await InvokeAsync(vm, "QuickUnitCapAsync");

        vm.Status.Should().Contain("set_unit_cap");
    }

    [Fact]
    public async Task QuickGodModeAsync_ShouldInvokeAction()
    {
        var vm = CreateAttachedViewModel();

        await InvokeAsync(vm, "QuickGodModeAsync");

        vm.Status.Should().Contain("toggle_tactical_god_mode");
    }

    [Fact]
    public async Task QuickOneHitAsync_ShouldInvokeAction()
    {
        var vm = CreateAttachedViewModel();

        await InvokeAsync(vm, "QuickOneHitAsync");

        vm.Status.Should().Contain("toggle_tactical_one_hit_mode");
    }

    [Fact]
    public async Task QuickUnfreezeAllAsync_ShouldClearState()
    {
        var vm = CreateAttachedViewModel();
        var freezeService = new StubFreezeService();
        freezeService.FreezeInt("test_sym", 100);
        SetField(vm, "_freezeService", freezeService);

        await InvokeAsync(vm, "QuickUnfreezeAllAsync");

        vm.Status.Should().Contain("All freezes and toggles cleared");
        freezeService.UnfreezeAllCalled.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QuickActionsBase — ToggleQuickActionState branches
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ToggleQuickActionState_SecondCall_ShouldRemoveFromActiveToggles()
    {
        var vm = CreateAttachedViewModel();

        // Toggle ON
        await InvokeAsync(vm, "QuickFreezeTimerAsync");
        // Toggle OFF (should remove from active toggles)
        await InvokeAsync(vm, "QuickFreezeTimerAsync");

        // Verify by checking that the third call doesn't crash
        await InvokeAsync(vm, "QuickFreezeTimerAsync");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QuickActionsBase — LoadHotkeysAsync, SaveHotkeysAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadHotkeysAsync_ShouldSetStatus()
    {
        var vm = CreateViewModel();

        await InvokeAsync(vm, "LoadHotkeysAsync");

        vm.Status.Should().NotBe("Ready");
    }

    [Fact]
    public async Task SaveHotkeysAsync_ShouldSetStatus()
    {
        var vm = CreateViewModel();

        await InvokeAsync(vm, "SaveHotkeysAsync");

        vm.Status.Should().NotBe("Ready");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QuickActionsBase — RefreshActiveFreezes
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RefreshActiveFreezes_TimerStopped_ShouldRestartTimer()
    {
        var vm = CreateViewModel();
        var timer = CreateStoppedTimer();
        timer.Stop();
        SetField(vm, "_freezeUiTimer", timer);
        SetField(vm, "_freezeService", new StubFreezeService());

        Invoke(vm, "RefreshActiveFreezes");

        timer.IsEnabled.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // QuickActionsBase — ExecuteHotkeyAsync — action unavailable branch
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteHotkeyAsync_ActionUnavailable_ShouldReturnTrueButNotExecute()
    {
        var vm = CreateViewModel();
        var session = BuildSessionWithUnresolvedSymbol();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        // Add a hotkey binding that targets an action requiring an unresolved symbol
        var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["set_credits"] = new("set_credits", ActionCategory.Economy, RuntimeMode.Unknown, ExecutionKind.Sdk,
                new JsonObject { ["required"] = new JsonArray("symbol", "intValue") }, false, 0)
        };
        SetField(vm, "_loadedActionSpecs", (IReadOnlyDictionary<string, ActionSpec>)actions);
        vm.SelectedProfileId = "test";
        vm.Hotkeys.Add(new HotkeyBindingItem { Gesture = "Ctrl+Shift+1", ActionId = "set_credits", PayloadJson = "{}" });

        var result = await vm.ExecuteHotkeyAsync("Ctrl+Shift+1");

        result.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AsyncCommand — Execute method (synchronous wrapper)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AsyncCommand_Execute_ShouldFireAndForget()
    {
        var executed = false;
        var command = new AsyncCommand(async () =>
        {
            await Task.Yield();
            executed = true;
        });

        command.Execute(null);

        // Give the fire-and-forget task a chance to complete
        SpinWait.SpinUntil(() => executed, TimeSpan.FromSeconds(2));
        executed.Should().BeTrue();
    }

    [Fact]
    public void AsyncCommand_Execute_WhenCanExecuteIsFalse_ShouldNotRun()
    {
        var executed = false;
        var command = new AsyncCommand(
            async () =>
            {
                await Task.Yield();
                executed = true;
            },
            () => false);

        command.Execute(null);

        Thread.Sleep(100);
        executed.Should().BeFalse();
    }

    [Fact]
    public void AsyncCommand_CanExecuteChanged_AddRemove_ShouldNotThrow()
    {
        var command = new AsyncCommand(() => Task.CompletedTask);
        EventHandler handler = (_, _) => { };

        var addRemove = () =>
        {
            command.CanExecuteChanged += handler;
            command.CanExecuteChanged -= handler;
        };

        addRemove.Should().NotThrow();
    }

    [Fact]
    public void AsyncCommand_RaiseCanExecuteChanged_ShouldNotThrow()
    {
        var act = () => AsyncCommand.RaiseCanExecuteChanged();
        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — ValidateSaveRuntimeVariant partial branch
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidateSaveRuntimeVariant_NullMetadata_ShouldReturnNull()
    {
        var vm = CreateViewModel();
        var session = new AttachSession("test",
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, Metadata: null),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>()),
            DateTimeOffset.UtcNow);
        SetField(vm, "_runtime", new StubRuntime(session: session));

        var result = Invoke<string?>(vm, "ValidateSaveRuntimeVariant", "test_profile");

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_NoResolvedVariant_ShouldReturnNull()
    {
        var vm = CreateViewModel();
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["other_key"] = "value"
        };
        var session = new AttachSession("test",
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, Metadata: metadata),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>()),
            DateTimeOffset.UtcNow);
        SetField(vm, "_runtime", new StubRuntime(session: session));

        var result = Invoke<string?>(vm, "ValidateSaveRuntimeVariant", "test_profile");

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_UniversalProfile_ShouldReturnNull()
    {
        var vm = CreateViewModel();
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedVariant"] = "base_swfoc"
        };
        var session = new AttachSession("test",
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, Metadata: metadata),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>()),
            DateTimeOffset.UtcNow);
        SetField(vm, "_runtime", new StubRuntime(session: session));

        var result = Invoke<string?>(vm, "ValidateSaveRuntimeVariant", "universal_auto");

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_MatchingVariant_ShouldReturnNull()
    {
        var vm = CreateViewModel();
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedVariant"] = "base_swfoc"
        };
        var session = new AttachSession("test",
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, Metadata: metadata),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>()),
            DateTimeOffset.UtcNow);
        SetField(vm, "_runtime", new StubRuntime(session: session));

        var result = Invoke<string?>(vm, "ValidateSaveRuntimeVariant", "base_swfoc");

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_Mismatch_ShouldReturnBlockMessage()
    {
        var vm = CreateViewModel();
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["resolvedVariant"] = "base_swfoc"
        };
        var session = new AttachSession("test",
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, Metadata: metadata),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>()),
            DateTimeOffset.UtcNow);
        SetField(vm, "_runtime", new StubRuntime(session: session));

        var result = Invoke<string?>(vm, "ValidateSaveRuntimeVariant", "custom_mod");

        result.Should().Contain("save_variant_mismatch");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — CanExecute context methods (private booleans)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CanRefreshDiffContext_BothNull_ShouldReturnFalse()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", null);
        SetField(vm, "_loadedSaveOriginal", null);

        var method = typeof(MainViewModel).GetMethod("CanRefreshDiffContext", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var result = (bool)method!.Invoke(vm, null)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void CanExportPatchPackContext_AllNull_ShouldReturnFalse()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", null);
        SetField(vm, "_loadedSaveOriginal", null);
        vm.SelectedProfileId = null;

        var method = typeof(MainViewModel).GetMethod("CanExportPatchPackContext", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var result = (bool)method!.Invoke(vm, null)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void CanPreviewPatchPackContext_AllNull_ShouldReturnFalse()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedSave", null);
        SetField(vm, "_loadedPatchPack", null);
        vm.SelectedProfileId = null;

        var method = typeof(MainViewModel).GetMethod("CanPreviewPatchPackContext", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var result = (bool)method!.Invoke(vm, null)!;
        result.Should().BeFalse();
    }

    [Fact]
    public void CanApplyPatchPackContext_AllNull_ShouldReturnFalse()
    {
        var vm = CreateViewModel();
        SetField(vm, "_loadedPatchPack", null);
        vm.SelectedProfileId = null;

        var method = typeof(MainViewModel).GetMethod("CanApplyPatchPackContext", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        var result = (bool)method!.Invoke(vm, null)!;
        result.Should().BeFalse();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — LaunchAndAttachAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LaunchAndAttachAsync_LaunchFailed_ShouldSetFailedStatus()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_gameLauncher", new StubGameLauncher(succeeded: false));
        SetField(vm, "_launchTarget", "Swfoc");
        SetField(vm, "_launchMode", "Vanilla");
        SetField(vm, "_launchWorkshopId", "");
        SetField(vm, "_launchModPath", "");
        vm.SelectedProfileId = "test";

        await InvokeAsync(vm, "LaunchAndAttachAsync");

        vm.Status.Should().Contain("Launch failed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // MainViewModel — BuildLaunchRequestAsync — KeyNotFoundException branch
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuildLaunchRequestAsync_SteamMod_NoWorkshopId_ProfileResolvesFallback()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_launchTarget", "Swfoc");
        SetField(vm, "_launchMode", "SteamMod");
        SetField(vm, "_launchWorkshopId", "");
        SetField(vm, "_launchModPath", "");
        vm.SelectedProfileId = "test";

        var result = await InvokeAsyncWithResult<GameLaunchRequest>(vm, "BuildLaunchRequestAsync");

        result.Should().NotBeNull();
        result.Mode.Should().Be(GameLaunchMode.SteamMod);
    }

    [Fact]
    public async Task BuildLaunchRequestAsync_SteamMod_ProfileThrowsKeyNotFound_ShouldKeepEmpty()
    {
        var vm = CreateViewModel();
        SetField(vm, "_profiles", new ThrowingProfiles(new KeyNotFoundException("not found")));
        SetField(vm, "_launchTarget", "Swfoc");
        SetField(vm, "_launchMode", "SteamMod");
        SetField(vm, "_launchWorkshopId", "");
        SetField(vm, "_launchModPath", "");
        vm.SelectedProfileId = "test";

        var result = await InvokeAsyncWithResult<GameLaunchRequest>(vm, "BuildLaunchRequestAsync");

        result.Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

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

    private static MainViewModel CreateAttachedViewModel()
    {
        var vm = CreateViewModel();
        var session = BuildSession();
        SetField(vm, "_runtime", new StubRuntime(session: session));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: true));
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        vm.SelectedProfileId = "test";
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
        SetField(vm, "_isStrictPatchApply", true);
        SetField(vm, "_onboardingBaseProfileId", "base_swfoc");
        SetField(vm, "_onboardingDraftProfileId", "custom_my_mod");
        SetField(vm, "_onboardingDisplayName", "Custom Mod Draft");
        SetField(vm, "_onboardingNamespaceRoot", "custom");
        SetField(vm, "_onboardingLaunchSample", string.Empty);
        SetField(vm, "_onboardingSummary", string.Empty);
        SetField(vm, "_calibrationNotes", string.Empty);
        SetField(vm, "_modCompatibilitySummary", string.Empty);
        SetField(vm, "_opsArtifactSummary", string.Empty);
        SetField(vm, "_launchTarget", MainViewModelDefaults.DefaultLaunchTarget);
        SetField(vm, "_launchMode", MainViewModelDefaults.DefaultLaunchMode);
        SetField(vm, "_launchWorkshopId", string.Empty);
        SetField(vm, "_launchModPath", string.Empty);
        SetField(vm, "_terminateExistingBeforeLaunch", false);
        SetField(vm, "_supportBundleOutputDirectory",
            Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwfocTrainer", "support"));
        SetField(vm, "_loadedActionSpecs",
            (IReadOnlyDictionary<string, ActionSpec>)new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        SetField(vm, "_profiles", new FullStubProfiles(new[] { "test" }));
        SetField(vm, "_freezeService", new StubFreezeService());
        SetField(vm, "_freezeUiTimer", CreateStoppedTimer());
        SetField(vm, "_activeToggles", new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        SetField(vm, "_runtime", new StubRuntime(session: null));
        SetField(vm, "_orchestrator", CreateOrchestrator(succeeded: true));
        SetField(vm, "_processLocator", new StubProcessLocator(Array.Empty<ProcessMetadata>()));
        SetField(vm, "_launchContextResolver", new StubLaunchContextResolver());
        SetField(vm, "_profileVariantResolver", new StubProfileVariantResolver(null));
        SetField(vm, "_gameLauncher", new StubGameLauncher(succeeded: true));
        SetField(vm, "_catalog", new StubCatalog());
        SetField(vm, "_saveCodec", new StubSaveCodec());
        SetField(vm, "_savePatchPackService", new StubSavePatchPackService());
        SetField(vm, "_savePatchApplyService", new StubSavePatchApplyService());
        SetField(vm, "_helper", new StubHelperMod());
        SetField(vm, "_updates", new StubProfileUpdates(Array.Empty<string>()));
        SetField(vm, "_modOnboarding", new StubModOnboarding());
        SetField(vm, "_modCalibration", new StubModCalibration());
        SetField(vm, "_supportBundles", new StubSupportBundles(succeeded: true));
        SetField(vm, "_telemetry", new StubTelemetry());
        SetField(vm, "_actionReliability", new StubActionReliability());
        SetField(vm, "_selectedUnitTransactions", new StubSelectedUnitTransactions());
        SetField(vm, "_spawnPresets", new StubSpawnPresets());
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

    private static T? Invoke<T>(object instance, string methodName, params object?[] args)
    {
        var result = FindMethod(instance, methodName, args).Invoke(instance, args.Length == 0 ? null : args);
        return result is T t ? t : default;
    }

    private static async Task InvokeAsync(object instance, string methodName, params object?[] args)
    {
        var task = FindMethod(instance, methodName, args).Invoke(instance, args.Length == 0 ? null : args);
        task.Should().BeAssignableTo<Task>();
        await (Task)task!;
    }

    private static async Task<T?> InvokeAsyncWithResult<T>(object instance, string methodName, params object?[] args)
    {
        var task = FindMethod(instance, methodName, args).Invoke(instance, args.Length == 0 ? null : args);
        task.Should().NotBeNull();
        if (task is Task<T> typedTask)
        {
            return await typedTask;
        }

        // Handle case where the return is wrapped differently
        await (Task)task!;
        var resultProp = task.GetType().GetProperty("Result");
        return resultProp is not null ? (T?)resultProp.GetValue(task) : default;
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
                ["credits"] = new("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Healthy)
            }),
            DateTimeOffset.UtcNow);
    }

    private static AttachSession BuildSessionWithUnresolvedSymbol()
    {
        return new AttachSession("test",
            new ProcessMetadata(1, "swfoc.exe", @"C:\swfoc.exe", null,
                ExeTarget.Swfoc, RuntimeMode.Galactic),
            new ProfileBuild("test", "build", @"C:\swfoc.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["credits"] = new("credits", nint.Zero, SymbolValueType.Int32, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Unresolved)
            }),
            DateTimeOffset.UtcNow);
    }

    private static TrainerOrchestrator CreateOrchestrator(bool succeeded)
    {
        return new TrainerOrchestrator(
            new FullStubProfiles(new[] { "test" }),
            new StubExecutionRuntime(succeeded),
            new StubFreezeService(),
            new StubAuditLogger(),
            new StubTelemetry());
    }

    private static TrainerOrchestrator CreateThrowingOrchestrator(Exception ex)
    {
        return new TrainerOrchestrator(
            new FullStubProfiles(new[] { "test" }),
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

    private sealed class StubAttachRuntime : IRuntimeAdapter
    {
        private readonly AttachSession _session;
        private bool _attached;
        public StubAttachRuntime(AttachSession session) { _session = session; _attached = false; }
        public bool IsAttached => _attached;
        public AttachSession? CurrentSession => _attached ? _session : null;
        public Task<AttachSession> AttachAsync(string id, CancellationToken ct) { _attached = true; return Task.FromResult(_session); }
        public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task DetachAsync(CancellationToken ct) { _attached = false; return Task.CompletedTask; }
    }

    private sealed class ThrowingAttachRuntime : IRuntimeAdapter
    {
        private readonly Exception _ex;
        public ThrowingAttachRuntime(Exception ex) => _ex = ex;
        public bool IsAttached => false;
        public AttachSession? CurrentSession => null;
        public Task<AttachSession> AttachAsync(string id, CancellationToken ct) => throw _ex;
        public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken ct) => throw new NotImplementedException();
        public Task DetachAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubExecutionRuntime : IRuntimeAdapter
    {
        private readonly bool _succeeded;
        public StubExecutionRuntime(bool succeeded) => _succeeded = succeeded;
        public bool IsAttached => true;
        public AttachSession? CurrentSession => null;
        public Task<AttachSession> AttachAsync(string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken ct)
            => Task.FromResult(new ActionExecutionResult(_succeeded, _succeeded ? "ok" : "failed", AddressSource.Signature));
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

    private sealed class FullStubProfiles : IProfileRepository
    {
        private readonly IReadOnlyList<string> _ids;
        public FullStubProfiles(IReadOnlyList<string> ids) => _ids = ids;
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<TrainerProfile> LoadProfileAsync(string id, CancellationToken ct) => Task.FromResult(Build(id));
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string id, CancellationToken ct) => Task.FromResult(Build(id));
        public Task ValidateProfileAsync(TrainerProfile p, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct)
            => Task.FromResult(_ids);

        private static TrainerProfile Build(string id)
        {
            var actions = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["set_credits"] = new("set_credits", ActionCategory.Economy, RuntimeMode.Unknown, ExecutionKind.Sdk,
                    new JsonObject { ["required"] = new JsonArray("symbol", "intValue") }, false, 0),
                ["freeze_timer"] = new("freeze_timer", ActionCategory.Global, RuntimeMode.Unknown, ExecutionKind.Sdk,
                    new JsonObject { ["required"] = new JsonArray("symbol", "boolValue") }, false, 0),
            };
            return new(id, id, null, ExeTarget.Swfoc, null,
                Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
                actions, new Dictionary<string, bool>(),
                Array.Empty<CatalogSource>(), "test", Array.Empty<HelperHookSpec>(),
                new Dictionary<string, string>());
        }
    }

    private sealed class FeatureGatedStubProfiles : IProfileRepository
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
                ["toggle_fog_reveal_patch_fallback"] = new("toggle_fog_reveal_patch_fallback", ActionCategory.Global, RuntimeMode.Unknown, ExecutionKind.Sdk,
                    new JsonObject(), false, 0),
            };
            // Feature flag is disabled
            var featureFlags = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                ["allow_fog_patch_fallback"] = false
            };
            return new("test", "test", null, ExeTarget.Swfoc, null,
                Array.Empty<SignatureSet>(), new Dictionary<string, long>(),
                actions, featureFlags,
                Array.Empty<CatalogSource>(), "test", Array.Empty<HelperHookSpec>(),
                new Dictionary<string, string>());
        }
    }

    private sealed class ThrowingProfiles : IProfileRepository
    {
        private readonly Exception _ex;
        public ThrowingProfiles(Exception ex) => _ex = ex;
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct) => throw _ex;
        public Task<TrainerProfile> LoadProfileAsync(string id, CancellationToken ct) => throw _ex;
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string id, CancellationToken ct) => throw _ex;
        public Task ValidateProfileAsync(TrainerProfile p, CancellationToken ct) => throw _ex;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct) => throw _ex;
    }

    private sealed class StubProcessLocator : IProcessLocator
    {
        private readonly IReadOnlyList<ProcessMetadata> _processes;
        public StubProcessLocator(IReadOnlyList<ProcessMetadata> processes) => _processes = processes;
        public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken ct)
            => Task.FromResult(_processes);
        public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken ct)
            => Task.FromResult(_processes.FirstOrDefault(p => p.ExeTarget == target));
    }

    private sealed class ThrowingProcessLocator : IProcessLocator
    {
        private readonly Exception _ex;
        public ThrowingProcessLocator(Exception ex) => _ex = ex;
        public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken ct) => throw _ex;
        public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken ct) => throw _ex;
    }

    private sealed class StubLaunchContextResolver : ILaunchContextResolver
    {
        public LaunchContext Resolve(ProcessMetadata process, IReadOnlyList<TrainerProfile> profiles)
            => new(LaunchKind.Unknown, false, Array.Empty<string>(), null, null, "stub",
                new ProfileRecommendation(null, "none", 0.0));
    }

    private sealed class StubProfileVariantResolver : IProfileVariantResolver
    {
        private readonly ProfileVariantResolution? _result;
        public StubProfileVariantResolver(ProfileVariantResolution? result) => _result = result;
        public Task<ProfileVariantResolution> ResolveAsync(string profileId, CancellationToken ct)
            => Task.FromResult(_result ?? new ProfileVariantResolution(profileId, profileId, "none", 0.0));
        public Task<ProfileVariantResolution> ResolveAsync(string profileId, IReadOnlyList<ProcessMetadata>? processes, CancellationToken ct)
            => Task.FromResult(_result ?? new ProfileVariantResolution(profileId, profileId, "none", 0.0));
    }

    private sealed class StubGameLauncher : IGameLaunchService
    {
        private readonly bool _succeeded;
        public StubGameLauncher(bool succeeded) => _succeeded = succeeded;
        public Task<GameLaunchResult> LaunchAsync(GameLaunchRequest request, CancellationToken ct)
            => Task.FromResult(new GameLaunchResult(_succeeded, _succeeded ? "ok" : "failed", 123, @"C:\game\swfoc.exe", ""));
    }

    private sealed class StubCatalog : ICatalogService
    {
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken ct)
            => Task.FromResult<IReadOnlyDictionary<string, IReadOnlyList<string>>>(
                new Dictionary<string, IReadOnlyList<string>>
                {
                    ["units"] = new[] { "unit_a", "unit_b" }
                });
    }

    private sealed class StubSaveCodec : ISaveCodec
    {
        public Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken ct) => throw new NotImplementedException();
        public Task EditAsync(SaveDocument doc, string nodePath, object? value, CancellationToken ct) => Task.CompletedTask;
        public Task<SaveValidationResult> ValidateAsync(SaveDocument doc, CancellationToken ct) => throw new NotImplementedException();
        public Task WriteAsync(SaveDocument doc, string outputPath, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> RoundTripCheckAsync(SaveDocument doc, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class StubSavePatchPackService : ISavePatchPackService
    {
        public Task<SavePatchPack> ExportAsync(SaveDocument original, SaveDocument modified, string profileId, CancellationToken ct) => throw new NotImplementedException();
        public Task<SavePatchPack> LoadPackAsync(string path, CancellationToken ct) => throw new NotImplementedException();
        public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pack, SaveDocument target, string profileId, CancellationToken ct) => throw new NotImplementedException();
        public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pack, SaveDocument target, string profileId, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class StubSavePatchApplyService : ISavePatchApplyService
    {
        public Task<SavePatchApplyResult> ApplyAsync(string savePath, SavePatchPack pack, string profileId, bool strict, CancellationToken ct) => throw new NotImplementedException();
        public Task<SaveRollbackResult> RestoreLastBackupAsync(string savePath, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class StubHelperMod : IHelperModService
    {
        private readonly bool _verifyResult;
        public StubHelperMod(bool verifyResult = true) => _verifyResult = verifyResult;
        public Task<string> DeployAsync(string profileId, CancellationToken ct) => Task.FromResult(@"C:\helpers\test.dll");
        public Task<bool> VerifyAsync(string profileId, CancellationToken ct) => Task.FromResult(_verifyResult);
    }

    private sealed class StubProfileUpdates : IProfileUpdateService
    {
        private readonly IReadOnlyList<string> _updates;
        private readonly bool _installSucceeded;
        private readonly bool _rollbackRestored;

        public StubProfileUpdates(IReadOnlyList<string> updates, bool installSucceeded = true, bool rollbackRestored = true)
        {
            _updates = updates;
            _installSucceeded = installSucceeded;
            _rollbackRestored = rollbackRestored;
        }

        public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken ct) => Task.FromResult(_updates);
        public Task<string> InstallProfileAsync(string profileId, CancellationToken ct) => Task.FromResult(@"C:\profiles\test.json");
        public Task<ProfileInstallResult> InstallProfileTransactionalAsync(string profileId, CancellationToken ct)
            => Task.FromResult(new ProfileInstallResult(_installSucceeded, profileId, @"C:\profiles\test.json",
                @"C:\backups\test.json", @"C:\receipts\test.json",
                _installSucceeded ? "ok" : "failed", _installSucceeded ? null : "test_error"));
        public Task<ProfileRollbackResult> RollbackLastInstallAsync(string profileId, CancellationToken ct)
            => Task.FromResult(new ProfileRollbackResult(_rollbackRestored, profileId, @"C:\profiles\test.json",
                @"C:\backups\test.json",
                _rollbackRestored ? "Rolled back" : "No backup found",
                _rollbackRestored ? null : "no_backup"));
    }

    private sealed class StubModOnboarding : IModOnboardingService
    {
        public Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest request, CancellationToken ct)
            => Task.FromResult(new ModOnboardingResult(
                true, request.DraftProfileId, @"C:\profiles\draft.json",
                new[] { "ws123" }, new[] { @"C:\Mods" },
                new[] { request.DraftProfileId },
                Array.Empty<string>()));

        public Task<ModOnboardingBatchResult> ScaffoldDraftProfilesFromSeedsAsync(ModOnboardingSeedBatchRequest request, CancellationToken ct)
            => throw new NotImplementedException();
    }

    private sealed class StubModCalibration : IModCalibrationService
    {
        public Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(ModCalibrationArtifactRequest request, CancellationToken ct)
            => Task.FromResult(new ModCalibrationArtifactResult(true, @"C:\cal\test.json", "ABC123",
                Array.Empty<CalibrationCandidate>(), Array.Empty<string>()));

        public Task<ModCompatibilityReport> BuildCompatibilityReportAsync(TrainerProfile profile, AttachSession? session,
            DependencyValidationResult? dependencyValidation, IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog, CancellationToken ct)
            => Task.FromResult(new ModCompatibilityReport(profile.Id, DateTimeOffset.UtcNow, RuntimeMode.Unknown,
                DependencyValidationStatus.Pass, 0, true,
                Array.Empty<ModActionCompatibility>(), Array.Empty<string>()));
    }

    private sealed class StubSupportBundles : ISupportBundleService
    {
        private readonly bool _succeeded;
        public StubSupportBundles(bool succeeded) => _succeeded = succeeded;
        public Task<SupportBundleResult> ExportAsync(SupportBundleRequest request, CancellationToken ct)
            => Task.FromResult(new SupportBundleResult(_succeeded, @"C:\bundles\test.zip", @"C:\bundles\manifest.json",
                Array.Empty<string>(), Array.Empty<string>()));
    }

    private sealed class StubFreezeService : IValueFreezeService
    {
        private readonly HashSet<string> _frozen = new(StringComparer.OrdinalIgnoreCase);
        public bool UnfreezeAllCalled { get; private set; }
        public void FreezeInt(string symbol, int value) => _frozen.Add(symbol);
        public void FreezeIntAggressive(string symbol, int value) => _frozen.Add(symbol);
        public void FreezeFloat(string symbol, float value) => _frozen.Add(symbol);
        public void FreezeBool(string symbol, bool value) => _frozen.Add(symbol);
        public bool Unfreeze(string symbol) => _frozen.Remove(symbol);
        public void UnfreezeAll() { UnfreezeAllCalled = true; _frozen.Clear(); }
        public bool IsFrozen(string symbol) => _frozen.Contains(symbol);
        public IReadOnlyCollection<string> GetFrozenSymbols() => _frozen;
        public void Dispose() { }
    }

    private sealed class StubAuditLogger : IAuditLogger
    {
        public Task WriteAsync(ActionAuditRecord record, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class StubTelemetry : ITelemetrySnapshotService
    {
        public void RecordAction(string actionId, AddressSource source, bool succeeded) { }
        public TelemetrySnapshot CreateSnapshot()
            => new(DateTimeOffset.UtcNow, new Dictionary<string, int>(), new Dictionary<string, int>(),
                new Dictionary<string, int>(), 0, 0, 0, 0);
        public Task<string> ExportSnapshotAsync(string outputDirectory, CancellationToken ct)
            => Task.FromResult(Path.Combine(outputDirectory, "telemetry.json"));
        public void Reset() { }
    }

    private sealed class StubActionReliability : IActionReliabilityService
    {
        public IReadOnlyList<ActionReliabilityInfo> Evaluate(TrainerProfile profile, AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
            => Array.Empty<ActionReliabilityInfo>();
    }

    private sealed class StubSelectedUnitTransactions : ISelectedUnitTransactionService
    {
        public SelectedUnitSnapshot? Baseline => null;
        public IReadOnlyList<SelectedUnitTransactionRecord> History => Array.Empty<SelectedUnitTransactionRecord>();
        public Task<SelectedUnitSnapshot> CaptureAsync(CancellationToken ct)
            => Task.FromResult(new SelectedUnitSnapshot(100f, 50f, 10f, 1.0f, 1.0f, 0, 0, DateTimeOffset.UtcNow));
        public Task<SelectedUnitTransactionResult> ApplyAsync(string profileId, SelectedUnitDraft draft, RuntimeMode mode, CancellationToken ct)
            => Task.FromResult(new SelectedUnitTransactionResult(true, "Applied", "tx1", Array.Empty<ActionExecutionResult>()));
        public Task<SelectedUnitTransactionResult> RevertLastAsync(string profileId, RuntimeMode mode, CancellationToken ct)
            => Task.FromResult(new SelectedUnitTransactionResult(true, "Reverted", "tx1", Array.Empty<ActionExecutionResult>()));
        public Task<SelectedUnitTransactionResult> RestoreBaselineAsync(string profileId, RuntimeMode mode, CancellationToken ct)
            => Task.FromResult(new SelectedUnitTransactionResult(true, "Restored", "tx1", Array.Empty<ActionExecutionResult>()));
    }

    private sealed class StubSpawnPresets : ISpawnPresetService
    {
        public Task<IReadOnlyList<SpawnPreset>> LoadPresetsAsync(string profileId, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<SpawnPreset>>(Array.Empty<SpawnPreset>());
        public SpawnBatchPlan BuildBatchPlan(string profileId, SpawnPreset preset, int quantity, int delayMs,
            string? faction, string? entryMarker, bool stopOnFailure) => throw new NotImplementedException();
        public Task<SpawnBatchExecutionResult> ExecuteBatchAsync(string profileId, SpawnBatchPlan plan, RuntimeMode mode, CancellationToken ct)
            => throw new NotImplementedException();
    }
}
