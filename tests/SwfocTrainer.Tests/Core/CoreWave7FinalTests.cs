using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Wave 7 final coverage — fills remaining Core gaps:
/// Record constructors, default interface method overloads,
/// ModOnboardingServiceExtensions null guards.
/// </summary>
public sealed class CoreWave7FinalTests
{
    #region ActionModels record coverage (lines 7, 11-12)

    [Fact]
    public void ActionSpec_Constructor_ShouldStoreProperties()
    {
        var spec = new ActionSpec(
            "test_action",
            ActionCategory.Economy,
            RuntimeMode.Galactic,
            ExecutionKind.Memory,
            new JsonObject(),
            true,
            500,
            "A test action");
        spec.Id.Should().Be("test_action");
        spec.Category.Should().Be(ActionCategory.Economy);
        spec.Description.Should().Be("A test action");
    }

    [Fact]
    public void ActionExecutionRequest_WithContext_ShouldStoreProperties()
    {
        var spec = new ActionSpec("act", ActionCategory.Global, RuntimeMode.Galactic, ExecutionKind.Memory, new JsonObject(), false, 0);
        var ctx = new Dictionary<string, object?> { ["key"] = "value" };
        var request = new ActionExecutionRequest(spec, new JsonObject(), "profile1", RuntimeMode.Galactic, ctx);
        request.Context.Should().NotBeNull();
        request.Context!["key"].Should().Be("value");
    }

    #endregion

    #region BackendRoutingModels record coverage (lines 33, 66)

    [Fact]
    public void CapabilityReport_Diagnostics_ShouldStoreOptionalParam()
    {
        var diag = new Dictionary<string, object?> { ["info"] = "test" };
        var report = new CapabilityReport(
            "prof1",
            DateTimeOffset.UtcNow,
            new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase),
            RuntimeReasonCode.CAPABILITY_UNKNOWN,
            diag);
        report.Diagnostics.Should().NotBeNull();
        report.Diagnostics!["info"].Should().Be("test");
    }

    [Fact]
    public void BackendHealth_Constructor_ShouldStoreProperties()
    {
        var diag = new Dictionary<string, object?> { ["ping"] = 42 };
        var health = new BackendHealth("backend1", ExecutionBackendKind.Memory, true,
            RuntimeReasonCode.ATTACH_HOST_SELECTED, "Healthy", diag);
        health.BackendId.Should().Be("backend1");
        health.Diagnostics.Should().ContainKey("ping");
    }

    #endregion

    #region CompatibilityReportModels (line 17)

    [Fact]
    public void ModCompatibilityReport_Constructor_ShouldStoreProperties()
    {
        var report = new ModCompatibilityReport(
            "testMod", DateTimeOffset.UtcNow, RuntimeMode.Galactic,
            DependencyValidationStatus.Pass, 0, true,
            Array.Empty<ModActionCompatibility>(),
            new[] { "All checks passed" });
        report.ProfileId.Should().Be("testMod");
        report.PromotionReady.Should().BeTrue();
    }

    #endregion

    #region LiveOpsModels record constructors (lines 58-86, 115, 124-127)

    [Fact]
    public void SelectedUnitTransactionRecord_Constructor_ShouldStoreProperties()
    {
        var before = new SelectedUnitSnapshot(100f, 50f, 10f, 1.0f, 1.0f, 1, 0, DateTimeOffset.UtcNow);
        var after = new SelectedUnitSnapshot(200f, 50f, 10f, 1.0f, 1.0f, 1, 0, DateTimeOffset.UtcNow);
        var record = new SelectedUnitTransactionRecord(
            "tx1", DateTimeOffset.UtcNow, before, after, false,
            "Applied changes", new[] { "set_hp" });
        record.TransactionId.Should().Be("tx1");
        record.IsRollback.Should().BeFalse();
        record.AppliedActions.Should().ContainSingle();
    }

    [Fact]
    public void SpawnBatchPlanOptions_Constructor_ShouldStoreProperties()
    {
        var preset = new SpawnPreset("p1", "TIE Fighter", "unit_tie", "empire", "marker1");
        var options = new SpawnBatchPlanOptions(
            "prof1", preset, 5, 200, "rebels", "marker_override", true);
        options.Quantity.Should().Be(5);
        options.FactionOverride.Should().Be("rebels");
        options.StopOnFailure.Should().BeTrue();
    }

    [Fact]
    public void SpawnPreset_DefaultValues_ShouldBeCorrect()
    {
        var preset = new SpawnPreset("p1", "Test", "unit1", "faction1", "entry1");
        preset.DefaultQuantity.Should().Be(1);
        preset.DefaultDelayMs.Should().Be(125);
        preset.Description.Should().BeNull();
    }

    [Fact]
    public void SpawnBatchPlan_Constructor_ShouldStoreProperties()
    {
        var items = new[] { new SpawnBatchItem(1, "unit1", "empire", "marker1", 100) };
        var plan = new SpawnBatchPlan("prof1", "preset1", true, items);
        plan.StopOnFailure.Should().BeTrue();
        plan.Items.Should().ContainSingle();
    }

    [Fact]
    public void SpawnBatchItemResult_WithDiagnostics_ShouldStoreProperties()
    {
        var diag = new Dictionary<string, object?> { ["detail"] = "info" };
        var result = new SpawnBatchItemResult(1, "unit1", true, "ok", diag);
        result.Diagnostics.Should().NotBeNull();
    }

    #endregion

    #region SdkCapabilityModels — record constructors

    [Fact]
    public void BinaryFingerprint_Constructor_ShouldStoreProperties()
    {
        var fp = new BinaryFingerprint(
            "fp1", "abc123", "sweaw.exe", "1.0", "1.0.0",
            DateTimeOffset.UtcNow, new[] { "sweaw.exe", "d3d9.dll" }, @"C:\Game\sweaw.exe");
        fp.FingerprintId.Should().Be("fp1");
        fp.ModuleList.Should().HaveCount(2);
    }

    [Fact]
    public void CapabilityAnchor_Constructor_ShouldStoreProperties()
    {
        var anchor = new CapabilityAnchor("a1", "pattern", "48 8B 05", true, "test anchor");
        anchor.Required.Should().BeTrue();
        anchor.Notes.Should().Be("test anchor");
    }

    [Fact]
    public void CapabilityMap_Constructor_ShouldStoreProperties()
    {
        var map = new CapabilityMap(
            "1.0", "fp1", "default_profile", DateTimeOffset.UtcNow,
            new Dictionary<string, CapabilityOperationMap>(),
            new Dictionary<string, CapabilityAvailabilityHint>());
        map.DefaultProfileId.Should().Be("default_profile");
    }

    [Fact]
    public void CapabilityResolutionResult_Constructor_ShouldStoreProperties()
    {
        var result = new CapabilityResolutionResult(
            "prof1", "set_credits", SdkCapabilityStatus.Available,
            CapabilityReasonCode.AllRequiredAnchorsPresent, 1.0, "fp1",
            new[] { "anchor1" }, Array.Empty<string>(),
            CapabilityResolutionMetadata.Empty);
        result.State.Should().Be(SdkCapabilityStatus.Available);
        result.Confidence.Should().Be(1.0);
    }

    [Fact]
    public void ProfileVariantResolution_Constructor_ShouldStoreProperties()
    {
        var resolution = new ProfileVariantResolution(
            "requested", "resolved", "exact_match", 1.0, "fp1", 1234, "sweaw.exe");
        resolution.ProcessId.Should().Be(1234);
        resolution.ProcessName.Should().Be("sweaw.exe");
    }

    [Fact]
    public void SdkOperationRequest_Constructor_ShouldStoreProperties()
    {
        var request = new SdkOperationRequest("set_credits", new JsonObject(), true, RuntimeMode.Galactic, "prof1");
        request.OperationId.Should().Be("set_credits");
    }

    #endregion

    #region RuntimeCalibrationModels — record constructors

    [Fact]
    public void RuntimeCalibrationCandidate_Constructor_ShouldStoreProperties()
    {
        var candidate = new RuntimeCalibrationCandidate(
            "48 8B 05 ?? ?? ?? ??", 0x100, SignatureAddressMode.HitPlusOffset,
            SymbolValueType.Float, "0x401000", "mov rax, [rip+0x123]", 3);
        candidate.SuggestedPattern.Should().StartWith("48");
        candidate.ReferenceCount.Should().Be(3);
    }

    [Fact]
    public void RuntimeCalibrationScanResult_WithArtifactPath_ShouldStoreIt()
    {
        var result = new RuntimeCalibrationScanResult(
            true, "success", "ok", Array.Empty<RuntimeCalibrationCandidate>(), "/tmp/artifact.json");
        result.ArtifactPath.Should().Be("/tmp/artifact.json");
    }

    #endregion

    #region ModOnboardingModels — record constructors

    [Fact]
    public void ModOnboardingBatchItemResult_Constructor_ShouldStoreProperties()
    {
        var result = new ModOnboardingBatchItemResult(
            0, "seed1", true, "mod_profile", "/output/path",
            new[] { "workshop1" }, new[] { "/hint" }, new[] { "alias1" },
            new[] { "warn1" }, Array.Empty<string>());
        result.InferredWorkshopIds.Should().ContainSingle();
        result.InferredPathHints.Should().ContainSingle();
    }

    #endregion

    #region ModOnboardingServiceExtensions (lines 10-14, 19-23)

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ExtensionNullService_ShouldThrow()
    {
        IModOnboardingService? service = null;
        var request = new ModOnboardingRequest("prof1", "name", "base", Array.Empty<ModLaunchSample>());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ModOnboardingServiceExtensions.ScaffoldDraftProfileAsync(service!, request));
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ExtensionNullService_ShouldThrow()
    {
        IModOnboardingService? service = null;
        var batch = new ModOnboardingSeedBatchRequest(null, Array.Empty<GeneratedProfileSeed>());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ModOnboardingServiceExtensions.ScaffoldDraftProfilesFromSeedsAsync(service!, batch));
    }

    [Fact]
    public async Task ScaffoldDraftProfileAsync_ExtensionNullRequest_ShouldThrow()
    {
        var service = new NoOpModOnboardingService();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ModOnboardingServiceExtensions.ScaffoldDraftProfileAsync(service, null!));
    }

    [Fact]
    public async Task ScaffoldDraftProfilesFromSeedsAsync_ExtensionNullRequest_ShouldThrow()
    {
        var service = new NoOpModOnboardingService();
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            ModOnboardingServiceExtensions.ScaffoldDraftProfilesFromSeedsAsync(service, null!));
    }

    #endregion

    #region Default interface method overloads — IActionReliabilityService (line 25-27)

    [Fact]
    public void IActionReliabilityService_TwoArgOverload_ShouldDelegateToThreeArg()
    {
        IActionReliabilityService service = new StubReliabilityService();
        var profile = CreateMinimalProfile("test");
        var session = CreateMinimalSession();
        var result = service.Evaluate(profile, session);
        result.Should().NotBeNull();
    }

    #endregion

    #region Default interface method overloads — IRuntimeAdapter

    [Fact]
    public async Task IRuntimeAdapter_ScanCalibrationCandidatesAsync_DefaultImpl_ShouldReturnNotSupported()
    {
        var adapter = new MinimalRuntimeAdapter();
        var request = new RuntimeCalibrationScanRequest("test_symbol");
        var result = await ((IRuntimeAdapter)adapter).ScanCalibrationCandidatesAsync(request, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("not_supported");
    }

    [Fact]
    public async Task IRuntimeAdapter_ScanCalibrationCandidatesAsync_NoTokenOverload_ShouldDelegate()
    {
        var adapter = new MinimalRuntimeAdapter();
        var request = new RuntimeCalibrationScanRequest("test_symbol");
        var result = await ((IRuntimeAdapter)adapter).ScanCalibrationCandidatesAsync(request);
        result.Succeeded.Should().BeFalse();
    }

    #endregion

    #region Default interface method overloads — ISavePatchPackService

    [Fact]
    public async Task ISavePatchPackService_ExportAsync_NoTokenOverload_ShouldDelegate()
    {
        var service = new MinimalSavePatchPackService();
        var doc = new SaveDocument("path", "schema1", new byte[10], new SaveNode("/", "root", "root", null));
        var result = await ((ISavePatchPackService)service).ExportAsync(doc, doc, "prof1");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ISavePatchPackService_LoadPackAsync_NoTokenOverload_ShouldDelegate()
    {
        var service = new MinimalSavePatchPackService();
        var result = await ((ISavePatchPackService)service).LoadPackAsync("path.json");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ISavePatchPackService_ValidateCompatibilityAsync_NoTokenOverload_ShouldDelegate()
    {
        var service = new MinimalSavePatchPackService();
        var pack = CreateMinimalPack();
        var doc = new SaveDocument("path", "schema1", new byte[10], new SaveNode("/", "root", "root", null));
        var result = await ((ISavePatchPackService)service).ValidateCompatibilityAsync(pack, doc, "prof1");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ISavePatchPackService_PreviewApplyAsync_NoTokenOverload_ShouldDelegate()
    {
        var service = new MinimalSavePatchPackService();
        var pack = CreateMinimalPack();
        var doc = new SaveDocument("path", "schema1", new byte[10], new SaveNode("/", "root", "root", null));
        var result = await ((ISavePatchPackService)service).PreviewApplyAsync(pack, doc, "prof1");
        result.Should().NotBeNull();
    }

    #endregion

    #region Default interface overloads — IModOnboardingService (lines 13-14, 22-23)

    [Fact]
    public async Task IModOnboardingService_ScaffoldDraftProfileAsync_NoTokenOverload_ShouldDelegate()
    {
        var service = new NoOpModOnboardingService();
        var request = new ModOnboardingRequest("prof1", "name", "base", Array.Empty<ModLaunchSample>());
        var result = await ((IModOnboardingService)service).ScaffoldDraftProfileAsync(request);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task IModOnboardingService_ScaffoldDraftProfilesFromSeedsAsync_NoTokenOverload_ShouldDelegate()
    {
        var service = new NoOpModOnboardingService();
        var batch = new ModOnboardingSeedBatchRequest(null, Array.Empty<GeneratedProfileSeed>());
        var result = await ((IModOnboardingService)service).ScaffoldDraftProfilesFromSeedsAsync(batch);
        result.Should().NotBeNull();
    }

    #endregion

    #region WorkshopInventoryModels (line 36)

    [Fact]
    public void WorkshopInventoryGraph_WithChains_ShouldStoreOptionalParam()
    {
        var chains = new[] { new WorkshopInventoryChain("chain1", new[] { "item1" }, "reason") };
        var graph = new WorkshopInventoryGraph(
            "32470", DateTimeOffset.UtcNow, Array.Empty<WorkshopInventoryItem>(),
            Array.Empty<string>(), chains);
        graph.Chains.Should().ContainSingle();
    }

    #endregion

    #region ProfileModels (line 23) — HelperHookSpec optional params

    [Fact]
    public void HelperHookSpec_WithAllOptionalParams_ShouldStore()
    {
        var args = new Dictionary<string, string> { ["arg1"] = "val1" };
        var verify = new Dictionary<string, string> { ["check"] = "true" };
        var meta = new Dictionary<string, string> { ["version"] = "2.0" };
        var spec = new HelperHookSpec("hook1", "script.lua", "1.0", "main", args, verify, meta);
        spec.EntryPoint.Should().Be("main");
        spec.ArgContract.Should().ContainKey("arg1");
        spec.Metadata.Should().ContainKey("version");
    }

    #endregion

    #region ProfileUpdateModels (lines 8, 20)

    [Fact]
    public void ProfileInstallResult_Constructor_ShouldStoreProperties()
    {
        var result = new ProfileInstallResult(true, "prof1", "/path", "/backup", "/receipt", "ok", null);
        result.Succeeded.Should().BeTrue();
        result.ReasonCode.Should().BeNull();
    }

    [Fact]
    public void ProfileRollbackResult_Constructor_ShouldStoreProperties()
    {
        var result = new ProfileRollbackResult(true, "prof1", "/restored", "/backup", "Restored", null);
        result.Restored.Should().BeTrue();
    }

    #endregion

    #region SaveModels (lines 9, 32-33, 35, 41)

    [Fact]
    public void SaveArrayDefinition_Constructor_ShouldStoreProperties()
    {
        var def = new SaveArrayDefinition("arr1", "Units", "unit", 0x100, 10, 64, "units.array");
        def.Path.Should().Be("units.array");
        def.Stride.Should().Be(64);
    }

    [Fact]
    public void SaveFieldDefinition_Path_ShouldStoreOptionalValue()
    {
        var def = new SaveFieldDefinition("f1", "Credits", "int32", 0x10, 4, "Player credits", "player.credits");
        def.Path.Should().Be("player.credits");
        def.Description.Should().Be("Player credits");
    }

    [Fact]
    public void ValidationRule_Constructor_ShouldStoreProperties()
    {
        var rule = new ValidationRule("v1", "range_check", "credits", "Credits out of range", "warning");
        rule.Severity.Should().Be("warning");
    }

    #endregion

    #region SdkExecutionDecision

    [Fact]
    public void SdkExecutionDecision_Constructor_ShouldStoreProperties()
    {
        var decision = new SdkExecutionDecision(true, CapabilityReasonCode.AllRequiredAnchorsPresent, "Operation allowed");
        decision.Allowed.Should().BeTrue();
    }

    #endregion

    #region Helpers

    private static TrainerProfile CreateMinimalProfile(string id)
    {
        return new TrainerProfile(
            Id: id,
            DisplayName: id,
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema1",
            HelperModHooks: Array.Empty<HelperHookSpec>());
    }

    private static AttachSession CreateMinimalSession()
    {
        var process = new ProcessMetadata(
            1234, "test.exe", "/path", null, ExeTarget.Swfoc, RuntimeMode.Galactic);
        var build = new ProfileBuild("test", "build", "/path", ExeTarget.Swfoc);
        return new AttachSession("test", process, build, new SymbolMap(new Dictionary<string, SymbolInfo>()), DateTimeOffset.UtcNow);
    }

    private static SavePatchPack CreateMinimalPack()
    {
        return new SavePatchPack(
            new SavePatchMetadata("1.0", "prof1", "schema1", "hash1", DateTimeOffset.UtcNow),
            new SavePatchCompatibility(new[] { "prof1" }, "schema1", null),
            Array.Empty<SavePatchOperation>());
    }

    #endregion

    #region Stubs

    private sealed class StubReliabilityService : IActionReliabilityService
    {
        public IReadOnlyList<ActionReliabilityInfo> Evaluate(
            TrainerProfile profile,
            AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
        {
            return Array.Empty<ActionReliabilityInfo>();
        }
    }

    private sealed class MinimalRuntimeAdapter : IRuntimeAdapter
    {
        public bool IsAttached => false;
        public AttachSession? CurrentSession => null;

        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken)
            => throw new NotImplementedException();
        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged
            => throw new NotImplementedException();
        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged
            => throw new NotImplementedException();
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
            => throw new NotImplementedException();
        public Task DetachAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;
    }

    private sealed class MinimalSavePatchPackService : ISavePatchPackService
    {
        public Task<SavePatchPack> ExportAsync(SaveDocument originalDoc, SaveDocument editedDoc, string profileId, CancellationToken cancellationToken)
            => Task.FromResult(new SavePatchPack(
                new SavePatchMetadata("1.0", profileId, "schema1", "hash1", DateTimeOffset.UtcNow),
                new SavePatchCompatibility(new[] { profileId }, "schema1", null),
                Array.Empty<SavePatchOperation>()));

        public Task<SavePatchPack> LoadPackAsync(string path, CancellationToken cancellationToken)
            => Task.FromResult(new SavePatchPack(
                new SavePatchMetadata("1.0", "prof1", "schema1", "hash1", DateTimeOffset.UtcNow),
                new SavePatchCompatibility(new[] { "prof1" }, "schema1", null),
                Array.Empty<SavePatchOperation>()));

        public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId, CancellationToken cancellationToken)
            => Task.FromResult(new SavePatchCompatibilityResult(true, true, "hash", Array.Empty<string>(), Array.Empty<string>()));

        public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId, CancellationToken cancellationToken)
            => Task.FromResult(new SavePatchPreview(true, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<SavePatchOperation>()));
    }

    private sealed class NoOpModOnboardingService : IModOnboardingService
    {
        public Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ModOnboardingResult(true, "prof1", "/out",
                Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));

        public Task<ModOnboardingBatchResult> ScaffoldDraftProfilesFromSeedsAsync(ModOnboardingSeedBatchRequest request, CancellationToken cancellationToken)
            => Task.FromResult(new ModOnboardingBatchResult(
                true, 0, 0, 0, Array.Empty<ModOnboardingBatchItemResult>()));
    }

    #endregion
}
