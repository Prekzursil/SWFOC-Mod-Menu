using System.Collections.ObjectModel;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class AppWave6CoverageTests
{
    [Theory]
    [InlineData("SteamMod", GameLaunchMode.SteamMod)]
    [InlineData("steammod", GameLaunchMode.SteamMod)]
    [InlineData("ModPath", GameLaunchMode.ModPath)]
    [InlineData("modpath", GameLaunchMode.ModPath)]
    [InlineData("Vanilla", GameLaunchMode.Vanilla)]
    [InlineData("", GameLaunchMode.Vanilla)]
    [InlineData("Unknown", GameLaunchMode.Vanilla)]
    public void ResolveLaunchMode_ShouldReturnExpected(string input, GameLaunchMode expected)
    {
        var method = typeof(MainViewModel).GetMethod("ResolveLaunchMode", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var result = (GameLaunchMode)method!.Invoke(null, new object[] { input })!;
        result.Should().Be(expected);
    }

    [Fact]
    public void ResolveProfileWorkshopChain_NoWorkshopId_NoMetadata_ShouldReturnEmpty()
    {
        var profile = BuildProfile(steamWorkshopId: null, metadata: new Dictionary<string, string>());
        InvokeResolveProfileWorkshopChain(profile).Should().BeEmpty();
    }

    [Fact]
    public void ResolveProfileWorkshopChain_WithWorkshopId_ShouldInclude()
    {
        InvokeResolveProfileWorkshopChain(BuildProfile(steamWorkshopId: "12345", metadata: new Dictionary<string, string>())).Should().Equal("12345");
    }

    [Fact]
    public void ResolveProfileWorkshopChain_WithRequiredIds_ShouldDedup()
    {
        InvokeResolveProfileWorkshopChain(BuildProfile("12345", metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["requiredWorkshopIds"] = "12345,67890" })).Should().Equal("12345", "67890");
    }

    [Fact]
    public void ResolveProfileWorkshopChain_WithParentDeps_ShouldPrepend()
    {
        InvokeResolveProfileWorkshopChain(BuildProfile("12345", metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["parentDependencies"] = "99999" })).Should().Equal("99999", "12345");
    }

    [Fact]
    public void ResolveProfileWorkshopChain_EmptyRequiredIds_ShouldIgnore()
    {
        InvokeResolveProfileWorkshopChain(BuildProfile("12345", metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["requiredWorkshopIds"] = "" })).Should().Equal("12345");
    }

    [Fact]
    public void ResolveProfileWorkshopChain_NullMetadata_ShouldReturnWorkshopId()
    {
        InvokeResolveProfileWorkshopChain(BuildProfile("12345", metadata: null)).Should().Equal("12345");
    }

    [Fact]
    public void ResolveProfileWorkshopChain_WhitespaceWorkshopId_ShouldReturnEmpty()
    {
        InvokeResolveProfileWorkshopChain(BuildProfile("  ", metadata: new Dictionary<string, string>())).Should().BeEmpty();
    }

    [Fact]
    public void BuildLaunchWorkshopIds_Whitespace_ShouldReturnEmpty()
    {
        var vm = CreateVM(); vm.LaunchWorkshopId = "   ";
        InvokeBuildLaunchWorkshopIds(vm).Should().BeEmpty();
    }

    [Fact]
    public void BuildLaunchWorkshopIds_WithDuplicates_ShouldDeduplicate()
    {
        var vm = CreateVM(); vm.LaunchWorkshopId = "111,222,111,333";
        InvokeBuildLaunchWorkshopIds(vm).Should().Equal("111", "222", "333");
    }

    [Fact]
    public void FeatureGate_FogPatchFallback_Enabled_ShouldReturnNull()
    {
        InvokeFeatureGate("toggle_fog_reveal_patch_fallback", BuildProfile(featureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { ["allow_fog_patch_fallback"] = true })).Should().BeNull();
    }

    [Fact]
    public void FeatureGate_ExtenderCredits_Enabled_ShouldReturnNull()
    {
        InvokeFeatureGate("set_credits_extender_experimental", BuildProfile(featureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase) { ["allow_extender_credits"] = true })).Should().BeNull();
    }

    [Fact]
    public void TryGetRequiredPayloadKeys_EmptyActionId_ShouldReturnFalse()
    {
        var vm = CreateVM(); SetField(vm, "_selectedActionId", string.Empty);
        SetField(vm, "_loadedActionSpecs", new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        InvokeTryGetRequired(vm, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetRequiredPayloadKeys_ActionNotInSpecs_ShouldReturnFalse()
    {
        var vm = CreateVM(); SetField(vm, "_loadedActionSpecs", new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)); SetField(vm, "_selectedActionId", "missing");
        SetField(vm, "_loadedActionSpecs", new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase));
        InvokeTryGetRequired(vm, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetRequiredPayloadKeys_NoRequired_ShouldReturnFalse()
    {
        var vm = CreateVM();
        var specs1 = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase) { ["a"] = new("a", ActionCategory.Global, RuntimeMode.Unknown, ExecutionKind.Sdk, new JsonObject(), false, 0) };
        SetField(vm, "_loadedActionSpecs", specs1); SetField(vm, "_selectedActionId", "a");
        InvokeTryGetRequired(vm, out _).Should().BeFalse();
    }

    [Fact]
    public void TryGetRequiredPayloadKeys_WithRequired_ShouldReturnTrue()
    {
        var vm = CreateVM();
        var specs2 = new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase) { ["a"] = new("a", ActionCategory.Global, RuntimeMode.Unknown, ExecutionKind.Sdk, new JsonObject { ["required"] = new JsonArray(JsonValue.Create("intValue")!) }, false, 0) };
        SetField(vm, "_loadedActionSpecs", specs2); SetField(vm, "_selectedActionId", "a");
        InvokeTryGetRequired(vm, out _).Should().BeTrue();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_NullSession_ShouldReturnNull()
    {
        var vm = CreateVM(); SetField(vm, "_runtime", new StubRuntime(null));
        InvokeVariant(vm, "base_swfoc").Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_NoResolvedVariant_ShouldReturnNull()
    {
        var vm = CreateVM(); SetField(vm, "_runtime", new StubRuntime(BuildSession(metadata: new Dictionary<string, string>())));
        InvokeVariant(vm, "base_swfoc").Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_UniversalProfile_ShouldReturnNull()
    {
        var vm = CreateVM(); SetField(vm, "_runtime", new StubRuntime(BuildSession(metadata: new Dictionary<string, string> { ["resolvedVariant"] = "roe_swfoc" })));
        InvokeVariant(vm, "universal_auto").Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_MatchingVariant_ShouldReturnNull()
    {
        var vm = CreateVM(); SetField(vm, "_runtime", new StubRuntime(BuildSession(metadata: new Dictionary<string, string> { ["resolvedVariant"] = "base_swfoc" })));
        InvokeVariant(vm, "base_swfoc").Should().BeNull();
    }

    [Fact]
    public void ValidateSaveRuntimeVariant_Mismatch_ShouldReturnBlocked()
    {
        var vm = CreateVM(); SetField(vm, "_runtime", new StubRuntime(BuildSession(metadata: new Dictionary<string, string> { ["resolvedVariant"] = "roe_swfoc" })));
        InvokeVariant(vm, "base_swfoc").Should().Contain("save_variant_mismatch");
    }

    [Fact]
    public void FlattenNodes_Leaf_ShouldReturnOne()
    {
        var vm = CreateVM();
        InvokeFlatten(vm, new SaveNode("/credits", "credits", "int32", 1000)).ToList().Should().HaveCount(1);
    }

    [Fact]
    public void FlattenNodes_RootNoChildren_ShouldReturnEmpty()
    {
        InvokeFlatten(CreateVM(), new SaveNode("/", "root", "root", null)).ToList().Should().BeEmpty();
    }

    [Fact]
    public void FlattenNodes_Nested_ShouldFlatten()
    {
        var gc = new SaveNode("/a/b", "b", "int32", 42);
        var c = new SaveNode("/a", "a", "container", null, new List<SaveNode> { gc });
        var r = new SaveNode("/", "root", "root", null, new List<SaveNode> { c });
        InvokeFlatten(CreateVM(), r).ToList().Should().HaveCount(1);
    }

    [Fact]
    public void ApplySaveSearch_WithQuery_ShouldFilter()
    {
        var vm = CreateVM();
        SetField(vm, "SaveFields", new ObservableCollection<SaveFieldViewItem> { new("/credits", "credits", "int32", "1000"), new("/health", "health", "float", "500") });
        SetField(vm, "FilteredSaveFields", new ObservableCollection<SaveFieldViewItem>());
        vm.SaveSearchQuery = "credits";
        InvokeSearch(vm);
        GetProp<ObservableCollection<SaveFieldViewItem>>(vm, "FilteredSaveFields").Should().HaveCount(1);
    }

    [Fact]
    public void ApplySaveSearch_EmptyQuery_ShouldReturnAll()
    {
        var vm = CreateVM();
        SetField(vm, "SaveFields", new ObservableCollection<SaveFieldViewItem> { new("/a", "a", "int32", "1"), new("/b", "b", "int32", "2") });
        SetField(vm, "FilteredSaveFields", new ObservableCollection<SaveFieldViewItem>());
        vm.SaveSearchQuery = "";
        InvokeSearch(vm);
        GetProp<ObservableCollection<SaveFieldViewItem>>(vm, "FilteredSaveFields").Should().HaveCount(2);
    }

    [Fact]
    public void ClearPatchPreviewState_ClearPack_ShouldNullify()
    {
        var vm = CreateVM();
        SetField(vm, "SavePatchOperations", new ObservableCollection<SavePatchOperationViewItem>());
        SetField(vm, "SavePatchCompatibility", new ObservableCollection<SavePatchCompatibilityViewItem>());
        InvokeClear(vm, true);
        vm.SavePatchMetadataSummary.Should().Be("No patch pack loaded.");
    }

    [Fact]
    public void AppendPatchArtifactRows_BothNull_ShouldNotAdd()
    {
        var vm = CreateVM(); SetField(vm, "SavePatchCompatibility", new ObservableCollection<SavePatchCompatibilityViewItem>());
        InvokeAppendArtifact(vm, null, null);
        GetProp<ObservableCollection<SavePatchCompatibilityViewItem>>(vm, "SavePatchCompatibility").Should().BeEmpty();
    }

    [Fact]
    public void AppendPatchArtifactRows_Both_ShouldAddTwo()
    {
        var vm = CreateVM(); SetField(vm, "SavePatchCompatibility", new ObservableCollection<SavePatchCompatibilityViewItem>());
        InvokeAppendArtifact(vm, "/bak", "/rec");
        GetProp<ObservableCollection<SavePatchCompatibilityViewItem>>(vm, "SavePatchCompatibility").Should().HaveCount(2);
    }

    [Fact]
    public void PopulatePatchCompatibilityRows_ShouldIncludeInfoRows()
    {
        var vm = CreateVM();
        SetField(vm, "SavePatchCompatibility", new ObservableCollection<SavePatchCompatibilityViewItem>());
        vm.IsStrictPatchApply = true;
        var compat = new SavePatchCompatibilityResult(true, true, "hash", Array.Empty<string>(), Array.Empty<string>());
        var preview = new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SavePatchOperation>());
        InvokePopulateCompat(vm, compat, preview);
        GetProp<ObservableCollection<SavePatchCompatibilityViewItem>>(vm, "SavePatchCompatibility").Should().Contain(x => x.Code == "source_hash_match");
    }

    [Fact]
    public void PopulatePatchCompatibilityRows_HashMismatch_ShouldShowMismatch()
    {
        var vm = CreateVM();
        SetField(vm, "SavePatchCompatibility", new ObservableCollection<SavePatchCompatibilityViewItem>());
        vm.IsStrictPatchApply = false;
        var compat = new SavePatchCompatibilityResult(true, false, "hash", Array.Empty<string>(), Array.Empty<string>());
        var preview = new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SavePatchOperation>());
        InvokePopulateCompat(vm, compat, preview);
        GetProp<ObservableCollection<SavePatchCompatibilityViewItem>>(vm, "SavePatchCompatibility").Should().Contain(x => x.Message.Contains("mismatch"));
    }

    [Fact]
    public void CanEditSaveContext_WithSaveAndPath_ShouldReturnTrue()
    {
        var vm = CreateVM(); SetField(vm, "_loadedSave", BuildDoc()); vm.SaveNodePath = "/credits";
        InvokeMethod<bool>(vm, "CanEditSaveContext").Should().BeTrue();
    }

    [Fact]
    public void CanValidateSaveContext_WithSave_ShouldReturnTrue()
    {
        var vm = CreateVM(); SetField(vm, "_loadedSave", BuildDoc());
        InvokeMethod<bool>(vm, "CanValidateSaveContext").Should().BeTrue();
    }

    [Fact]
    public void CanWriteSaveContext_WithSave_ShouldReturnTrue()
    {
        var vm = CreateVM(); SetField(vm, "_loadedSave", BuildDoc());
        InvokeMethod<bool>(vm, "CanWriteSaveContext").Should().BeTrue();
    }

    [Fact]
    public void CanExportPatchPackContext_AllSet_ShouldReturnTrue()
    {
        var vm = CreateVM(); SetField(vm, "_loadedSave", BuildDoc()); SetField(vm, "_loadedSaveOriginal", new byte[] { 0x01 }); vm.SelectedProfileId = "test";
        InvokeMethod<bool>(vm, "CanExportPatchPackContext").Should().BeTrue();
    }

    [Fact]
    public void CanApplyPatchPackContext_MissingSavePath_ShouldReturnFalse()
    {
        var vm = CreateVM(); SetField(vm, "_loadedPatchPack", BuildPack()); vm.SavePath = ""; vm.SelectedProfileId = "test";
        InvokeMethod<bool>(vm, "CanApplyPatchPackContext").Should().BeFalse();
    }

    [Fact]
    public void PreparePatchPreview_NoMismatch_ShouldReturnTrue()
    {
        var vm = CreateVM(); SetField(vm, "_runtime", new StubRuntime(null));
        SetField(vm, "SavePatchCompatibility", new ObservableCollection<SavePatchCompatibilityViewItem>());
        InvokePreparePatch(vm, "base_swfoc").Should().BeTrue();
    }

    [Fact]
    public void PreparePatchPreview_WithMismatch_ShouldReturnFalse()
    {
        var vm = CreateVM(); SetField(vm, "_runtime", new StubRuntime(BuildSession(metadata: new Dictionary<string, string> { ["resolvedVariant"] = "roe_swfoc" })));
        SetField(vm, "SavePatchCompatibility", new ObservableCollection<SavePatchCompatibilityViewItem>());
        InvokePreparePatch(vm, "base_swfoc").Should().BeFalse();
    }

    [Fact]
    public void SavePatchCompatibilityViewItem_ShouldStoreAll()
    {
        var item = new SavePatchCompatibilityViewItem("error", "reason_code", "message");
        item.Severity.Should().Be("error"); item.Code.Should().Be("reason_code");
    }

    [Fact]
    public void SavePatchOperationViewItem_ShouldStoreAll()
    {
        var item = new SavePatchOperationViewItem("SetValue", "/path", "fid", "int32", "10", "20");
        item.Kind.Should().Be("SetValue"); item.FieldPath.Should().Be("/path");
    }

    [Fact]
    public void SaveFieldViewItem_ShouldStoreAll()
    {
        var item = new SaveFieldViewItem("/root/credits", "credits", "int32", "50000");
        item.Path.Should().Be("/root/credits"); item.Value.Should().Be("50000");
    }

#pragma warning disable SYSLIB0050
    private static MainViewModel CreateVM() => (MainViewModel)FormatterServices.GetUninitializedObject(typeof(MainViewModel));
#pragma warning restore SYSLIB0050

    private static T InvokeMethod<T>(MainViewModel vm, string name)
    {
        var m = typeof(MainViewModel).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);
        m.Should().NotBeNull(); return (T)m!.Invoke(vm, Array.Empty<object?>())!;
    }

    private static void SetField(object inst, string name, object? val)
    {
        var t = inst.GetType(); FieldInfo? f = null;
        while (t is not null && f is null) { f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic); t = t.BaseType; }
        if (f is not null) { f.SetValue(inst, val); return; }
        var t2 = inst.GetType(); PropertyInfo? p = null;
        while (t2 is not null && p is null) { p = t2.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); t2 = t2.BaseType; }
        p.Should().NotBeNull($"field or property '{name}' should exist"); p!.SetValue(inst, val);
    }

    private static T GetProp<T>(object inst, string name)
    {
        var t = inst.GetType(); PropertyInfo? p = null;
        while (t is not null && p is null) { p = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic); t = t.BaseType; }
        p.Should().NotBeNull(); return (T)p!.GetValue(inst)!;
    }

    private static string? InvokeFeatureGate(string actionId, TrainerProfile profile) =>
        typeof(MainViewModel).GetMethod("ResolveProfileFeatureGateReason", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object?[] { actionId, profile }) as string;

    private static IReadOnlyList<string> InvokeResolveProfileWorkshopChain(TrainerProfile p) =>
        (IReadOnlyList<string>)typeof(MainViewModel).GetMethod("ResolveProfileWorkshopChain", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, new object[] { p })!;

    private static IReadOnlyList<string> InvokeBuildLaunchWorkshopIds(MainViewModel vm) =>
        (IReadOnlyList<string>)typeof(MainViewModel).GetMethod("BuildLaunchWorkshopIds", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, Array.Empty<object?>())!;

    private static bool InvokeTryGetRequired(MainViewModel vm, out JsonArray? keys)
    {
        var args = new object?[] { null };
        var r = (bool)typeof(MainViewModel).GetMethod("TryGetRequiredPayloadKeysForSelectedAction", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, args)!;
        keys = args[0] as JsonArray; return r;
    }

    private static string? InvokeVariant(MainViewModel vm, string pid) =>
        (typeof(MainViewModelSaveOpsBase).GetMethod("ValidateSaveRuntimeVariant", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null) ??
         typeof(MainViewModel).GetMethod("ValidateSaveRuntimeVariant", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(string) }, null))!.Invoke(vm, new object[] { pid }) as string;

    private static IEnumerable<SaveFieldViewItem> InvokeFlatten(MainViewModel vm, SaveNode root) =>
        (IEnumerable<SaveFieldViewItem>)typeof(MainViewModelSaveOpsBase).GetMethod("FlattenNodes", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(SaveNode) }, null)!.Invoke(vm, new object[] { root })!;

    private static void InvokeSearch(MainViewModel vm) =>
        typeof(MainViewModelSaveOpsBase).GetMethod("ApplySaveSearch", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, Array.Empty<object?>());

    private static void InvokeClear(MainViewModel vm, bool clearPack) =>
        typeof(MainViewModelSaveOpsBase).GetMethod("ClearPatchPreviewState", BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(bool) }, null)!.Invoke(vm, new object[] { clearPack });

    private static void InvokeAppendArtifact(MainViewModel vm, string? bp, string? rp) =>
        typeof(MainViewModelSaveOpsBase).GetMethod("AppendPatchArtifactRows", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, new object?[] { bp, rp });

    private static void InvokePopulateCompat(MainViewModel vm, SavePatchCompatibilityResult c, SavePatchPreview p) =>
        typeof(MainViewModelSaveOpsBase).GetMethod("PopulatePatchCompatibilityRows", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, new object[] { c, p });

    private static bool InvokePreparePatch(MainViewModel vm, string pid) =>
        (bool)typeof(MainViewModelSaveOpsBase).GetMethod("PreparePatchPreview", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(vm, new object[] { pid })!;

    private static TrainerProfile BuildProfile(string? steamWorkshopId = null, IReadOnlyDictionary<string, string>? metadata = null, IReadOnlyDictionary<string, bool>? featureFlags = null) =>
        new("test", "test", null, ExeTarget.Swfoc, steamWorkshopId, Array.Empty<SignatureSet>(), new Dictionary<string, long>(), new Dictionary<string, ActionSpec>(), featureFlags ?? new Dictionary<string, bool>(), Array.Empty<CatalogSource>(), "test", Array.Empty<HelperHookSpec>(), metadata ?? new Dictionary<string, string>());

    private static AttachSession BuildSession(IReadOnlyDictionary<string, string>? metadata = null) =>
        new("test", new ProcessMetadata(100, "swfoc.exe", @"C:\Games\swfoc.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, metadata), new ProfileBuild("test", "build", @"C:\Games\swfoc.exe", ExeTarget.Swfoc), new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)), DateTimeOffset.UtcNow);

    private static SaveDocument BuildDoc() => new(@"C:\test.sav", "test", new byte[] { 0x01 }, new SaveNode("/", "root", "root", null));

    private static SavePatchPack BuildPack() => new(new SavePatchMetadata("v1", "p1", "s1", "hash", DateTimeOffset.UtcNow), new SavePatchCompatibility(Array.Empty<string>(), "s1", null), Array.Empty<SavePatchOperation>());

    private sealed class StubRuntime : IRuntimeAdapter
    {
        private readonly AttachSession? _s;
        public StubRuntime(AttachSession? s) => _s = s;
        public bool IsAttached => _s is not null;
        public AttachSession? CurrentSession => _s;
        public Task<AttachSession> AttachAsync(string profileId, CancellationToken ct) => throw new NotImplementedException();
        public Task<T> ReadAsync<T>(string symbol, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task WriteAsync<T>(string symbol, T value, CancellationToken ct) where T : unmanaged => throw new NotImplementedException();
        public Task DetachAsync(CancellationToken ct) => Task.CompletedTask;
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken ct) => throw new NotImplementedException();
    }
}


