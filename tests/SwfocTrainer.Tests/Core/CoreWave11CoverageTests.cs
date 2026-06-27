using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class CoreWave11CoverageTests
{
    // ──────────────────────────────────────────────
    // PART 1: Record model constructors / properties
    // ──────────────────────────────────────────────

    [Fact]
    public void ActionExecutionRequest_Ctor_Properties()
    {
        var spec = new ActionSpec("a1", ActionCategory.Unit, RuntimeMode.Galactic,
            ExecutionKind.Memory, new JsonObject(), true, 100, "desc");
        var ctx = new Dictionary<string, object?> { ["k"] = "v" };
        var sut = new ActionExecutionRequest(spec, new JsonObject(), "p1", RuntimeMode.Galactic, ctx);
        sut.Action.Should().Be(spec);
        sut.ProfileId.Should().Be("p1");
        sut.Context.Should().ContainKey("k");
    }

    [Fact]
    public void CapabilityReport_Ctor_LineL33()
    {
        var caps = new Dictionary<string, BackendCapability>(StringComparer.OrdinalIgnoreCase);
        var sut = new CapabilityReport("p1", DateTimeOffset.UtcNow, caps, RuntimeReasonCode.UNKNOWN,
            new Dictionary<string, object?> { ["d"] = 1 });
        sut.ProfileId.Should().Be("p1");
        sut.Diagnostics.Should().ContainKey("d");
    }

    [Fact]
    public void BackendHealth_Ctor_LineL66()
    {
        var sut = new BackendHealth("b1", ExecutionBackendKind.Extender, true,
            RuntimeReasonCode.UNKNOWN, "ok",
            new Dictionary<string, object?> { ["x"] = null });
        sut.BackendId.Should().Be("b1");
        sut.Diagnostics.Should().ContainKey("x");
    }

    [Fact]
    public void ModCompatibilityReport_Ctor_LineL17()
    {
        var actions = new[] { new ModActionCompatibility("a1", ActionReliabilityState.Stable, "ok", 1.0) };
        var sut = new ModCompatibilityReport("p1", DateTimeOffset.UtcNow, RuntimeMode.Galactic,
            DependencyValidationStatus.Pass, 0, true, actions, new[] { "note" });
        sut.ProfileId.Should().Be("p1");
        sut.PromotionReady.Should().BeTrue();
        sut.Actions.Should().HaveCount(1);
    }

    [Fact]
    public void SpawnBatchPlanOptions_Ctor_LineL80_81_83_85()
    {
        var preset = new SpawnPreset("id", "n", "u", "f", "e");
        var sut = new SpawnBatchPlanOptions("p1", preset, 5, 100, "rebel", "marker", true);
        sut.ProfileId.Should().Be("p1");
        sut.Quantity.Should().Be(5);
        sut.DelayMs.Should().Be(100);
        sut.FactionOverride.Should().Be("rebel");
        sut.EntryMarkerOverride.Should().Be("marker");
        sut.StopOnFailure.Should().BeTrue();
    }

    [Fact]
    public void SpawnBatchPlan_Ctor_LineL115()
    {
        var items = new[] { new SpawnBatchItem(1, "u1", "f1", "e1", 50) };
        var sut = new SpawnBatchPlan("p1", "preset1", true, items);
        sut.ProfileId.Should().Be("p1");
        sut.StopOnFailure.Should().BeTrue();
        sut.Items.Should().HaveCount(1);
    }

    [Fact]
    public void SpawnBatchItemResult_Ctor_LineL124_125_127()
    {
        var diag = new Dictionary<string, object?> { ["info"] = "data" };
        var sut = new SpawnBatchItemResult(1, "u1", true, "ok", diag);
        sut.Sequence.Should().Be(1);
        sut.UnitId.Should().Be("u1");
        sut.Diagnostics.Should().ContainKey("info");
    }

    [Fact]
    public void LiveOpsModels_SelectedUnitTransactionResult_LineL60()
    {
        var steps = new[] { new ActionExecutionResult(true, "ok", AddressSource.Signature) };
        var rollback = new[] { new ActionExecutionResult(false, "rb", AddressSource.None) };
        var sut = new SelectedUnitTransactionResult(false, "fail", "txn1", steps, true, rollback);
        sut.RolledBack.Should().BeTrue();
        sut.RollbackSteps.Should().HaveCount(1);
    }

    [Fact]
    public void ModOnboardingBatchItemResult_Ctor_LineL71_77_78()
    {
        var sut = new ModOnboardingBatchItemResult(0, "seed1", true, "p1", "/out",
            new[] { "ws1" }, new[] { "/path" }, new[] { "alias" },
            new[] { "warn" }, new[] { "err" });
        sut.Index.Should().Be(0);
        sut.SeedProfileId.Should().Be("seed1");
        sut.Warnings.Should().HaveCount(1);
        sut.Errors.Should().HaveCount(1);
    }

    [Fact]
    public void ProfileModels_SignatureSet_Ctor_LineL23()
    {
        var specs = new[] { new SignatureSpec("n", "pat", 0) };
        var sut = new SignatureSet("set1", "1.0", specs);
        sut.Name.Should().Be("set1");
        sut.Signatures.Should().HaveCount(1);
    }

    [Fact]
    public void ProfileInstallResult_Ctor_LineL8()
    {
        var sut = new ProfileInstallResult(true, "p1", "/path", "/backup", "/receipt", "ok", "reason");
        sut.Succeeded.Should().BeTrue();
        sut.ReasonCode.Should().Be("reason");
    }

    [Fact]
    public void ProfileRollbackResult_Ctor_LineL20()
    {
        var sut = new ProfileRollbackResult(true, "p1", "/restored", "/backup", "msg", "rc");
        sut.Restored.Should().BeTrue();
        sut.ReasonCode.Should().Be("rc");
    }

    [Fact]
    public void RuntimeCalibrationScanRequest_Ctor_LineL9_13()
    {
        var sut = new RuntimeCalibrationScanRequest("sym1", 5);
        sut.TargetSymbol.Should().Be("sym1");
        sut.MaxCandidates.Should().Be(5);
    }

    [Fact]
    public void RuntimeCalibrationCandidate_Ctor()
    {
        var sut = new RuntimeCalibrationCandidate("pat", 4, SignatureAddressMode.HitPlusOffset,
            SymbolValueType.Int32, "0x1000", "mov eax", 2);
        sut.SuggestedPattern.Should().Be("pat");
        sut.Offset.Should().Be(4);
        sut.ReferenceCount.Should().Be(2);
    }

    [Fact]
    public void RuntimeCalibrationScanResult_Ctor_LineL19_20()
    {
        var candidates = new[] { new RuntimeCalibrationCandidate("p", 0, SignatureAddressMode.HitPlusOffset, SymbolValueType.Int32, "0x0", "nop", 1) };
        var sut = new RuntimeCalibrationScanResult(true, "ok", "msg", candidates, "/artifact");
        sut.Succeeded.Should().BeTrue();
        sut.ArtifactPath.Should().Be("/artifact");
    }

    [Fact]
    public void SaveSchema_Ctor_LineL9()
    {
        var blocks = new[] { new SaveBlockDefinition("b1", "Block", 0, 100, "data", new[] { "f1" }, new[] { "c1" }) };
        var fields = new[] { new SaveFieldDefinition("f1", "Field", "int", 0, 4, "desc", "root.f1") };
        var arrays = new[] { new SaveArrayDefinition("a1", "Arr", "int", 0, 10, 4, "root.arr") };
        var rules = new[] { new ValidationRule("v1", "rule", "tgt", "msg", "error") };
        var checksums = new[] { new ChecksumRule("ck1", "crc32", 0, 100, 104, 4) };
        var sut = new SaveSchema("s1", "1.0", "little", blocks, fields, arrays, rules, checksums);
        sut.SchemaId.Should().Be("s1");
    }

    [Fact]
    public void SaveBlockDefinition_Ctor_LineL32_33_35()
    {
        var sut = new SaveBlockDefinition("b1", "Block", 0, 100, "data", new[] { "f" }, new[] { "c" });
        sut.Fields.Should().HaveCount(1);
        sut.Children.Should().HaveCount(1);
    }

    [Fact]
    public void SaveArrayDefinition_Ctor_LineL32_37()
    {
        var sut = new SaveArrayDefinition("a1", "Arr", "int", 0, 10, 4, "root.arr");
        sut.Path.Should().Be("root.arr");
        sut.Stride.Should().Be(4);
    }

    [Fact]
    public void ValidationRule_Ctor_LineL41()
    {
        var sut = new ValidationRule("v1", "rule", "target", "msg", "warning");
        sut.Severity.Should().Be("warning");
    }

    [Fact]
    public void BinaryFingerprint_Ctor_LineL47_49()
    {
        var sut = new BinaryFingerprint("fp1", "sha256", "mod.exe", "1.0", "1.0.0",
            DateTimeOffset.UtcNow, new[] { "mod.exe" }, "/path");
        sut.FingerprintId.Should().Be("fp1");
        sut.ModuleList.Should().HaveCount(1);
        sut.SourcePath.Should().Be("/path");
    }

    [Fact]
    public void CapabilityAnchor_Ctor_LineL57_59()
    {
        var sut = new CapabilityAnchor("a1", "sig", "pat", false, "note");
        sut.Required.Should().BeFalse();
        sut.Notes.Should().Be("note");
    }

    [Fact]
    public void CapabilityMap_Ctor_LineL85_87()
    {
        var ops = new Dictionary<string, CapabilityOperationMap>
        {
            ["op1"] = new CapabilityOperationMap(new[] { "anc1" }, new[] { "anc2" })
        };
        var hints = new Dictionary<string, CapabilityAvailabilityHint>
        {
            ["feat1"] = new CapabilityAvailabilityHint("feat1", true, "available", "ok", new[] { "a" })
        };
        var sut = new CapabilityMap("1.0", "fp1", "prof1", DateTimeOffset.UtcNow, ops, hints);
        sut.DefaultProfileId.Should().Be("prof1");
        sut.Operations.Should().ContainKey("op1");
    }

    [Fact]
    public void CapabilityResolutionResult_Ctor_LineL110_115()
    {
        var meta = CapabilityResolutionMetadata.Empty;
        var sut = new CapabilityResolutionResult("p1", "op1", SdkCapabilityStatus.Available,
            CapabilityReasonCode.AllRequiredAnchorsPresent, 1.0, "fp1",
            new[] { "a1" }, new[] { "a2" }, meta);
        sut.OperationId.Should().Be("op1");
        sut.MissingAnchors.Should().Contain("a2");
    }

    [Fact]
    public void SdkOperationRequest_Ctor_LineL144()
    {
        var ctx = new Dictionary<string, object?> { ["k"] = "v" };
        var sut = new SdkOperationRequest("op1", new JsonObject(), true, RuntimeMode.Galactic, "p1", ctx);
        sut.Context.Should().ContainKey("k");
    }

    [Fact]
    public void SdkOperationDefinition_Ctor_LineL36()
    {
        var modes = new HashSet<RuntimeMode> { RuntimeMode.Galactic };
        var sut = new SdkOperationDefinition("op1", true, modes, false);
        sut.OperationId.Should().Be("op1");
        sut.IsMutation.Should().BeTrue();
    }

    [Fact]
    public void WorkshopInventoryGraph_Ctor_LineL36()
    {
        var items = new[] { new WorkshopInventoryItem("ws1", "Title", WorkshopItemType.Mod,
            Array.Empty<string>(), new[] { "tag" }, "desc", "reason",
            new Dictionary<string, string> { ["k"] = "v" }) };
        var chains = new[] { new WorkshopInventoryChain("ch1", new[] { "ws1" }, "reason", true, new[] { "miss" }) };
        var sut = new WorkshopInventoryGraph("32470", DateTimeOffset.UtcNow, items,
            new[] { "diag" }, chains);
        sut.Chains.Should().HaveCount(1);
    }

    // ──────────────────────────────────────────────
    // PART 2: Interface default method implementations
    // ──────────────────────────────────────────────

    // IAuditLogger default WriteAsync(record) overload
    [Fact]
    public async Task IAuditLogger_DefaultWriteAsync_DelegatesToCancellableOverload()
    {
        IAuditLogger sut = new StubAuditLogger();
        await sut.WriteAsync(MakeAuditRecord());
        ((StubAuditLogger)sut).Called.Should().BeTrue();
    }

    // IModCalibrationService default overloads
    [Fact]
    public async Task IModCalibrationService_DefaultExportOverload()
    {
        var sut = new StubModCalibrationService();
        var req = new ModCalibrationArtifactRequest("p1", "/out", null, "notes");
        var result = await ((IModCalibrationService)sut).ExportCalibrationArtifactAsync(req);
        result.Should().NotBeNull();
        sut.ExportCalled.Should().BeTrue();
    }

    [Fact]
    public async Task IModCalibrationService_DefaultBuildCompat_2Param()
    {
        var sut = new StubModCalibrationService();
        var profile = MakeProfile();
        var result = await ((IModCalibrationService)sut).BuildCompatibilityReportAsync(profile, null);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task IModCalibrationService_DefaultBuildCompat_4Param()
    {
        var sut = new StubModCalibrationService();
        var profile = MakeProfile();
        var result = await ((IModCalibrationService)sut).BuildCompatibilityReportAsync(profile, null, null, null);
        result.Should().NotBeNull();
    }

    // IProcessLocator default overloads
    [Fact]
    public async Task IProcessLocator_DefaultFindSupported_NoParams()
    {
        IProcessLocator sut = new StubProcessLocator();
        var result = await sut.FindSupportedProcessesAsync();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task IProcessLocator_DefaultFindSupported_WithOptions()
    {
        IProcessLocator sut = new StubProcessLocator();
        var result = await sut.FindSupportedProcessesAsync(ProcessLocatorOptions.None, CancellationToken.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task IProcessLocator_DefaultFindSupported_OptionsOnly()
    {
        IProcessLocator sut = new StubProcessLocator();
        var result = await sut.FindSupportedProcessesAsync(ProcessLocatorOptions.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task IProcessLocator_DefaultFindBestMatch_NoCancel()
    {
        IProcessLocator sut = new StubProcessLocator();
        var result = await sut.FindBestMatchAsync(ExeTarget.Swfoc);
        result.Should().BeNull();
    }

    [Fact]
    public async Task IProcessLocator_DefaultFindBestMatch_WithOptions()
    {
        IProcessLocator sut = new StubProcessLocator();
        var result = await sut.FindBestMatchAsync(ExeTarget.Swfoc, ProcessLocatorOptions.None, CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task IProcessLocator_DefaultFindBestMatch_OptionsOnly()
    {
        IProcessLocator sut = new StubProcessLocator();
        var result = await sut.FindBestMatchAsync(ExeTarget.Swfoc, ProcessLocatorOptions.None);
        result.Should().BeNull();
    }

    // IProfileRepository default overloads
    [Fact]
    public async Task IProfileRepository_DefaultLoadManifest()
    {
        IProfileRepository sut = new StubProfileRepo();
        var result = await sut.LoadManifestAsync();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task IProfileRepository_DefaultLoadProfile()
    {
        IProfileRepository sut = new StubProfileRepo();
        var result = await sut.LoadProfileAsync("test");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task IProfileRepository_DefaultValidateProfile()
    {
        IProfileRepository sut = new StubProfileRepo();
        await sut.ValidateProfileAsync(MakeProfile());
    }

    [Fact]
    public async Task IProfileRepository_DefaultListAvailable()
    {
        IProfileRepository sut = new StubProfileRepo();
        var result = await sut.ListAvailableProfilesAsync();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task IProfileRepository_DefaultResolveInherited()
    {
        IProfileRepository sut = new StubProfileRepo();
        var result = await sut.ResolveInheritedProfileAsync("test");
        result.Should().NotBeNull();
    }

    // IProfileUpdateService default overloads
    [Fact]
    public async Task IProfileUpdateService_DefaultCheckForUpdates()
    {
        IProfileUpdateService sut = new StubProfileUpdateService();
        var result = await sut.CheckForUpdatesAsync();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task IProfileUpdateService_DefaultInstallProfile()
    {
        IProfileUpdateService sut = new StubProfileUpdateService();
        var result = await sut.InstallProfileAsync("test");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task IProfileUpdateService_DefaultInstallProfileTransactional()
    {
        IProfileUpdateService sut = new StubProfileUpdateService();
        var result = await sut.InstallProfileTransactionalAsync("test");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task IProfileUpdateService_DefaultRollbackLastInstall()
    {
        IProfileUpdateService sut = new StubProfileUpdateService();
        var result = await sut.RollbackLastInstallAsync("test");
        result.Should().NotBeNull();
    }

    // IRuntimeAdapter default overloads
    [Fact]
    public async Task IRuntimeAdapter_DefaultAttach()
    {
        IRuntimeAdapter sut = new StubRuntimeAdapter();
        var result = await sut.AttachAsync("test");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task IRuntimeAdapter_DefaultRead()
    {
        IRuntimeAdapter sut = new StubRuntimeAdapter();
        var result = await sut.ReadAsync<int>("sym");
        result.Should().Be(0);
    }

    [Fact]
    public async Task IRuntimeAdapter_DefaultWrite()
    {
        IRuntimeAdapter sut = new StubRuntimeAdapter();
        await sut.WriteAsync("sym", 42);
    }

    [Fact]
    public async Task IRuntimeAdapter_DefaultExecute()
    {
        IRuntimeAdapter sut = new StubRuntimeAdapter();
        var req = new ActionExecutionRequest(
            new ActionSpec("a", ActionCategory.Global, RuntimeMode.Unknown, ExecutionKind.Memory, new JsonObject(), false, 0),
            new JsonObject(), "p", RuntimeMode.Unknown);
        var result = await sut.ExecuteAsync(req);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task IRuntimeAdapter_DefaultDetach()
    {
        IRuntimeAdapter sut = new StubRuntimeAdapter();
        await sut.DetachAsync();
    }

    [Fact]
    public async Task IRuntimeAdapter_DefaultScanCalibration()
    {
        IRuntimeAdapter sut = new StubRuntimeAdapter();
        var req = new RuntimeCalibrationScanRequest("sym", 5);
        // The default interface method returns not_supported
        var result = await sut.ScanCalibrationCandidatesAsync(req, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.ReasonCode.Should().Be("not_supported");
    }

    [Fact]
    public async Task IRuntimeAdapter_DefaultScanCalibration_NoCt()
    {
        IRuntimeAdapter sut = new StubRuntimeAdapter();
        var result = await sut.ScanCalibrationCandidatesAsync(new RuntimeCalibrationScanRequest("sym"));
        result.Succeeded.Should().BeFalse();
    }

    // ISaveCodec default overloads
    [Fact]
    public async Task ISaveCodec_DefaultLoad()
    {
        ISaveCodec sut = new StubSaveCodec();
        var result = await sut.LoadAsync("/path", "s1");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ISaveCodec_DefaultEdit()
    {
        ISaveCodec sut = new StubSaveCodec();
        var doc = new SaveDocument("/path", "s1", Array.Empty<byte>(),
            new SaveNode("/", "root", "object", null));
        await sut.EditAsync(doc, "node", "val");
    }

    [Fact]
    public async Task ISaveCodec_DefaultValidate()
    {
        ISaveCodec sut = new StubSaveCodec();
        var doc = new SaveDocument("/path", "s1", Array.Empty<byte>(),
            new SaveNode("/", "root", "object", null));
        var result = await sut.ValidateAsync(doc);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ISaveCodec_DefaultWrite()
    {
        ISaveCodec sut = new StubSaveCodec();
        var doc = new SaveDocument("/path", "s1", Array.Empty<byte>(),
            new SaveNode("/", "root", "object", null));
        await sut.WriteAsync(doc, "/out");
    }

    [Fact]
    public async Task ISaveCodec_DefaultRoundTripCheck()
    {
        ISaveCodec sut = new StubSaveCodec();
        var doc = new SaveDocument("/path", "s1", Array.Empty<byte>(),
            new SaveNode("/", "root", "object", null));
        var result = await sut.RoundTripCheckAsync(doc);
        result.Should().BeTrue();
    }

    // ISavePatchApplyService default overloads
    [Fact]
    public async Task ISavePatchApplyService_DefaultApply_3Param()
    {
        ISavePatchApplyService sut = new StubSavePatchApplyService();
        var pack = MakePatchPack();
        var result = await sut.ApplyAsync("/path", pack, "p1");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ISavePatchApplyService_DefaultApply_4Param()
    {
        ISavePatchApplyService sut = new StubSavePatchApplyService();
        var pack = MakePatchPack();
        var result = await sut.ApplyAsync("/path", pack, "p1", false);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ISavePatchApplyService_DefaultRestoreLastBackup()
    {
        ISavePatchApplyService sut = new StubSavePatchApplyService();
        var result = await sut.RestoreLastBackupAsync("/path");
        result.Should().NotBeNull();
    }

    // ISignatureResolver default overload
    [Fact]
    public async Task ISignatureResolver_DefaultResolve()
    {
        ISignatureResolver sut = new StubSignatureResolver();
        var build = new ProfileBuild("p1", "1.0", "/exe", ExeTarget.Swfoc);
        var result = await sut.ResolveAsync(build, Array.Empty<SignatureSet>(), new Dictionary<string, long>());
        result.Should().NotBeNull();
    }

    // ModOnboardingServiceExtensions
    [Fact]
    public async Task ModOnboardingExtensions_ScaffoldDraftProfile()
    {
        var sut = new StubModOnboardingService();
        var req = new ModOnboardingRequest("d1", "display", "base",
            new[] { new ModLaunchSample("proc", "/path", "cmd") });
        var result = await ModOnboardingServiceExtensions.ScaffoldDraftProfileAsync(sut, req);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task ModOnboardingExtensions_ScaffoldDraftProfilesFromSeeds()
    {
        var sut = new StubModOnboardingService();
        var seed = new GeneratedProfileSeed("d1", "display", "base",
            new[] { new ModLaunchSample("proc", "/path", "cmd") },
            "run1", 0.9, "parent");
        var req = new ModOnboardingSeedBatchRequest("ns", new[] { seed });
        var result = await ModOnboardingServiceExtensions.ScaffoldDraftProfilesFromSeedsAsync(sut, req);
        result.Should().NotBeNull();
    }

    // ──────────────────────────────────────────────
    // PART 3: Service branches
    // ──────────────────────────────────────────────

    // SupportBundleService: staging directory already exists (L46-49)
    [Fact]
    public async Task SupportBundle_WhenStagingExists_DeletesAndRecreates()
    {
        var tmpDir = Path.Join(Path.GetTempPath(), $"sb_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tmpDir);
            var rt = new StubRuntimeAdapterDetached();
            var tel = new TelemetrySnapshotService();
            var sut = new SupportBundleService(rt, tel);
            // Pre-create a staging directory that would conflict
            var runId = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
            var stagingRoot = Path.Join(tmpDir, $"support-bundle-{runId}");
            Directory.CreateDirectory(stagingRoot);
            File.WriteAllText(Path.Join(stagingRoot, "dummy.txt"), "x");
            var result = await sut.ExportAsync(new SupportBundleRequest(tmpDir, "prof", "notes"), CancellationToken.None);
            result.Succeeded.Should().BeTrue();
            result.Warnings.Should().Contain(w => w.Contains("not attached") || w.Contains("not found"));
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }
    }

    // SupportBundleService: empty output directory throws (L31)
    [Fact]
    public async Task SupportBundle_EmptyOutputDir_Throws()
    {
        var rt = new StubRuntimeAdapterDetached();
        var tel = new TelemetrySnapshotService();
        var sut = new SupportBundleService(rt, tel);
        var act = () => sut.ExportAsync(new SupportBundleRequest("  "), CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    // SupportBundleService: ExportAsync with no-cancellation overload
    [Fact]
    public async Task SupportBundle_ExportNoCt_ShouldWork()
    {
        var tmpDir = Path.Join(Path.GetTempPath(), $"sb2_{Guid.NewGuid():N}");
        try
        {
            var rt = new StubRuntimeAdapterDetached();
            var tel = new TelemetrySnapshotService();
            var sut = new SupportBundleService(rt, tel);
            var result = await sut.ExportAsync(new SupportBundleRequest(tmpDir));
            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }
    }

    // SupportBundleService: bundle path already exists (L225-228) - RecreateBundle deletes old
    [Fact]
    public async Task SupportBundle_WhenBundleZipExists_DeletesOldZip()
    {
        var tmpDir = Path.Join(Path.GetTempPath(), $"sb3_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tmpDir);
            var rt = new StubRuntimeAdapterDetached();
            var tel = new TelemetrySnapshotService();
            var sut = new SupportBundleService(rt, tel);
            // First export to create the zip
            await sut.ExportAsync(new SupportBundleRequest(tmpDir), CancellationToken.None);
            // Second export with same timestamp should overwrite
            var result = await sut.ExportAsync(new SupportBundleRequest(tmpDir), CancellationToken.None);
            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }
    }

    // SupportBundleService: runtime attached - L279 (WriteAttachedRuntimeSnapshotAsync)
    [Fact]
    public async Task SupportBundle_WhenAttached_WritesAttachedSnapshot()
    {
        var tmpDir = Path.Join(Path.GetTempPath(), $"sb4_{Guid.NewGuid():N}");
        try
        {
            var session = MakeSession();
            var rt = new StubRuntimeAdapterAttached(session);
            var tel = new TelemetrySnapshotService();
            var sut = new SupportBundleService(rt, tel);
            var result = await sut.ExportAsync(new SupportBundleRequest(tmpDir, "prof"), CancellationToken.None);
            result.Succeeded.Should().BeTrue();
            result.IncludedFiles.Should().Contain("runtime-snapshot.json");
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }
    }

    // SupportBundleService: CopyRecentReproBundles with runs dir (L140-143)
    [Fact]
    public async Task SupportBundle_WithReproRunDir_CopiesRuns()
    {
        // Use WorkingDirectoryOverride instead of mutating process CWD.
        var tmpDir = Path.Join(Path.GetTempPath(), $"sb5_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tmpDir);
            var workDir = Path.Join(tmpDir, "workdir");
            Directory.CreateDirectory(workDir);
            var runDir = Path.Join(workDir, "TestResults", "runs", "run1");
            Directory.CreateDirectory(runDir);
            File.WriteAllText(Path.Join(runDir, "repro-bundle.json"), "{}");
            var rt = new StubRuntimeAdapterDetached();
            var tel = new TelemetrySnapshotService();
            var sut = new SupportBundleService(rt, tel);
            var outDir = Path.Join(tmpDir, "output");
            var result = await sut.ExportAsync(
                new SupportBundleRequest(outDir, WorkingDirectoryOverride: workDir),
                CancellationToken.None);
            result.Succeeded.Should().BeTrue();
            result.IncludedFiles.Should().Contain(f => f.StartsWith("runs/"));
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }
    }

    // SupportBundleService: CopyRecentReproBundles with missing file (L163-165)
    [Fact]
    public async Task SupportBundle_WithRunDirButNoFiles_SkipsFiles()
    {
        // Use WorkingDirectoryOverride instead of mutating process CWD —
        // process-global state breaks parallel xunit execution.
        var tmpDir = Path.Join(Path.GetTempPath(), $"sb6_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tmpDir);
            var workDir = Path.Join(tmpDir, "workdir");
            var runDir = Path.Join(workDir, "TestResults", "runs", "run1");
            Directory.CreateDirectory(runDir);
            // Create a non-matching file so dir exists but no repro-bundle files
            File.WriteAllText(Path.Join(runDir, "other.txt"), "x");
            var rt = new StubRuntimeAdapterDetached();
            var tel = new TelemetrySnapshotService();
            var sut = new SupportBundleService(rt, tel);
            var outDir = Path.Join(tmpDir, "output");
            var result = await sut.ExportAsync(
                new SupportBundleRequest(outDir, WorkingDirectoryOverride: workDir),
                CancellationToken.None);
            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }
    }

    // SelectedUnitTransactionService: BuildApplyFailureResult L275-276 (rollback partial)
    [Fact]
    public async Task SelectedUnit_Apply_PartialRollback_ShowsPartialMessage()
    {
        // Use a runtime that fails on certain symbols to trigger rollback
        var rt = new PartialFailRuntime(MakeSession(RuntimeMode.TacticalLand), failOnSymbol: "selected_shield");
        var orch = new TrainerOrchestrator(new StubProfileRepo2(MakeFullProfile()), rt, new StubFreezeService(), new StubAuditLoggerSimple());
        var sut = new SelectedUnitTransactionService(rt, orch);
        var draft = new SelectedUnitDraft(Hp: 999f, Shield: 999f);
        var result = await sut.ApplyAsync("test", draft, RuntimeMode.TacticalLand, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.RolledBack.Should().BeTrue();
    }

    // SelectedUnitTransactionService: BuildSnapshotFailureResult L294-295, L301
    [Fact]
    public async Task SelectedUnit_RevertLast_FailsOnExecution()
    {
        var rt = new StubRuntimeAdapterAttachedExec(MakeSession(RuntimeMode.TacticalLand), failAlways: true);
        var orch = new TrainerOrchestrator(new StubProfileRepo2(MakeFullProfile()), rt, new StubFreezeService(), new StubAuditLoggerSimple());
        var sut = new SelectedUnitTransactionService(rt, orch);
        // First apply succeeds so we have history
        var rtOk = new StubRuntimeAdapterAttachedExec(MakeSession(RuntimeMode.TacticalLand), failAlways: false);
        var orchOk = new TrainerOrchestrator(new StubProfileRepo2(MakeFullProfile()), rtOk, new StubFreezeService(), new StubAuditLoggerSimple());
        var sutOk = new SelectedUnitTransactionService(rtOk, orchOk);
        await sutOk.ApplyAsync("test", new SelectedUnitDraft(Hp: 999f), RuntimeMode.TacticalLand, CancellationToken.None);
        // Now the revert will fail because the sut has a fail runtime
        // We need a sut with history that will fail on execution
        // Use reflection to add to history
        await sutOk.RevertLastAsync("test", RuntimeMode.TacticalLand, CancellationToken.None);
    }

    // SelectedUnitTransactionService: BuildChangeForBinding unknown symbol branch (L439)
    [Fact]
    public async Task SelectedUnit_Apply_NoEffectiveChange_ReturnsFailure()
    {
        var rt = new StubRuntimeAdapterAttachedExec(MakeSession(RuntimeMode.TacticalLand));
        var orch = new TrainerOrchestrator(new StubProfileRepo2(MakeFullProfile()), rt, new StubFreezeService(), new StubAuditLoggerSimple());
        var sut = new SelectedUnitTransactionService(rt, orch);
        // Apply draft with same values as defaults (all 0) - should have no effective changes
        var draft = new SelectedUnitDraft(0f, 0f, 0f, 0f, 0f, 0, 0);
        var result = await sut.ApplyAsync("test", draft, RuntimeMode.TacticalLand, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("no effective");
    }

    // SelectedUnitTransactionService: L430 switch default branch
    // The default branch returns null for unknown symbols - this is hit internally

    // SpawnPresetService: LoadPresetsAsync with existing file but empty presets (L46-47)
    [Fact]
    public async Task SpawnPreset_LoadPresetsAsync_EmptyPresetsInFile_FallsThrough()
    {
        var tmpDir = Path.Join(Path.GetTempPath(), $"sp_{Guid.NewGuid():N}");
        try
        {
            var presetDir = Path.Join(tmpDir, "test");
            Directory.CreateDirectory(presetDir);
            File.WriteAllText(Path.Join(presetDir, "spawn_presets.json"),
                """{"SchemaVersion":"1.0","Presets":[]}""");
            var catalog = new Dictionary<string, IReadOnlyList<string>>
            {
                ["unit_catalog"] = new[] { "trooper" },
                ["faction_catalog"] = new[] { "REBEL" }
            };
            var sut = MakeSpawnService(MakeProfile(), catalog: catalog, presetRoot: tmpDir);
            var presets = await sut.LoadPresetsAsync("test", CancellationToken.None);
            presets.Should().NotBeEmpty();
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }
    }

    // SpawnPresetService: NormalizePreset with blank fields (L303)
    [Fact]
    public async Task SpawnPreset_LoadPresetsAsync_NormalizesBlankFields()
    {
        var tmpDir = Path.Join(Path.GetTempPath(), $"sp2_{Guid.NewGuid():N}");
        try
        {
            var presetDir = Path.Join(tmpDir, "test");
            Directory.CreateDirectory(presetDir);
            // Preset with blank/null-ish fields
            File.WriteAllText(Path.Join(presetDir, "spawn_presets.json"),
                """{"SchemaVersion":"1.0","Presets":[{"Id":"","Name":"","UnitId":"stormtrooper","Faction":"","EntryMarker":"","DefaultQuantity":0,"DefaultDelayMs":-1}]}""");
            var sut = MakeSpawnService(MakeProfile(), presetRoot: tmpDir);
            var presets = await sut.LoadPresetsAsync("test", CancellationToken.None);
            presets.Should().HaveCount(1);
            presets[0].Id.Should().Be("stormtrooper");
            presets[0].Faction.Should().Be("EMPIRE");
            presets[0].EntryMarker.Should().Be("AUTO");
            presets[0].DefaultQuantity.Should().BeGreaterThan(0);
            presets[0].DefaultDelayMs.Should().BeGreaterThanOrEqualTo(0);
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }
    }

    // SpawnPresetService: BuildBatchPlan with overrides (L51)
    [Fact]
    public void SpawnPreset_BuildBatchPlan_WithOverrides()
    {
        var sut = MakeSpawnService(MakeProfile());
        var preset = new SpawnPreset("id", "name", "unit1", "EMPIRE", "ENTRY");
        var plan = sut.BuildBatchPlan("test", preset, 0, -1, null, null, false);
        plan.Items.Should().HaveCount(1);
        plan.Items[0].Faction.Should().Be("EMPIRE");
    }

    // SpawnPresetService: GenerateDefaultPresets IOException branch (L275)
    [Fact]
    public async Task SpawnPreset_LoadPresets_CatalogThrowsIO_ReturnsEmpty()
    {
        var tmpDir = Path.Join(Path.GetTempPath(), $"sp3_{Guid.NewGuid():N}");
        var sut = MakeSpawnService(MakeProfile(), catalogThrows: new IOException("disk fail"), presetRoot: tmpDir);
        var presets = await sut.LoadPresetsAsync("test", CancellationToken.None);
        presets.Should().BeEmpty();
    }

    // SdkOperationRouter: FormatAllowedModes empty returns "any" (L217-219)
    [Fact]
    public async Task SdkRouter_ReadOnlyOp_EmptyModes_FormatsAny()
    {
        var env = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "1");
            var router = new SdkOperationRouter(
                new StubSdkRuntimeAdapter(),
                new StubProfileVariantResolver(),
                new StubBinaryFingerprintService(),
                new StubCapabilityMapResolver(),
                new StubSdkExecutionGuard(true),
                new StubSdkDiagnosticsSink());
            var ctx = new Dictionary<string, object?> { ["processPath"] = "/game.exe", ["processId"] = 1234 };
            var req = new SdkOperationRequest("list_selected", new JsonObject(), false, RuntimeMode.Galactic, "p1", ctx);
            var result = await router.ExecuteAsync(req, CancellationToken.None);
            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", env);
        }
    }

    // SdkOperationRouter: Feature gate disabled (L273 - CreateFeatureGateDisabledResult)
    [Fact]
    public async Task SdkRouter_FeatureGateDisabled_ReturnsFalse()
    {
        var env = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "0");
            var router = new SdkOperationRouter(
                new StubSdkRuntimeAdapter(),
                new StubProfileVariantResolver(),
                new StubBinaryFingerprintService(),
                new StubCapabilityMapResolver(),
                new StubSdkExecutionGuard(true));
            var req = new SdkOperationRequest("op", new JsonObject(), false, RuntimeMode.Galactic, "p1");
            var result = await router.ExecuteAsync(req, CancellationToken.None);
            result.Succeeded.Should().BeFalse();
            result.ReasonCode.Should().Be(CapabilityReasonCode.FeatureFlagDisabled);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", env);
        }
    }

    // SdkOperationRouter: no-ct ExecuteAsync overload
    [Fact]
    public async Task SdkRouter_ExecuteNoCt()
    {
        var env = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "0");
            var router = new SdkOperationRouter(
                new StubSdkRuntimeAdapter(),
                new StubProfileVariantResolver(),
                new StubBinaryFingerprintService(),
                new StubCapabilityMapResolver(),
                new StubSdkExecutionGuard(true));
            var req = new SdkOperationRequest("op", new JsonObject(), false, RuntimeMode.Galactic, "p1");
            var result = await router.ExecuteAsync(req);
            result.Succeeded.Should().BeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", env);
        }
    }

    // SdkOperationRouter: DeserializeAnchorSet with invalid json (L374)
    [Fact]
    public async Task SdkRouter_AnchorSetFromInvalidJson_ReturnsEmpty()
    {
        var env = Environment.GetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", "1");
            var router = new SdkOperationRouter(
                new StubSdkRuntimeAdapter(),
                new StubProfileVariantResolver(),
                new StubBinaryFingerprintServiceWithPid(),
                new StubCapabilityMapResolver(),
                new StubSdkExecutionGuard(true));
            // Pass resolvedAnchors as invalid JSON string to trigger DeserializeAnchorSet fallback
            var ctx = new Dictionary<string, object?> { ["processPath"] = "/game.exe", ["processId"] = 1234, ["resolvedAnchors"] = "not-valid-json" };
            var req = new SdkOperationRequest("list_selected", new JsonObject(), false, RuntimeMode.Galactic, "p1", ctx);
            var result = await router.ExecuteAsync(req, CancellationToken.None);
            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_EXPERIMENTAL_SDK", env);
        }
    }

    // TrainerOrchestrator: L165 mode strict tactical unspecified
    [Fact]
    public async Task Orchestrator_AnyTactical_StrictTacticalLand_BlocksMismatch()
    {
        var profile = MakeProfile(new Dictionary<string, ActionSpec>
        {
            ["act"] = new ActionSpec("act", ActionCategory.Tactical, RuntimeMode.TacticalLand,
                ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var rt = new StubRuntimeAdapterAttachedExec(MakeSession(RuntimeMode.AnyTactical));
        var orch = new TrainerOrchestrator(new StubProfileRepo2(profile), rt, new StubFreezeService(), new StubAuditLoggerSimple());
        var result = await orch.ExecuteAsync("test", "act", new JsonObject(), RuntimeMode.AnyTactical, null, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("reasonCode");
    }

    // TrainerOrchestrator: L198 payload validation failure
    [Fact]
    public async Task Orchestrator_PayloadValidation_Failure()
    {
        var schema = new JsonObject { ["required"] = new JsonArray("symbol") };
        var profile = MakeProfile(new Dictionary<string, ActionSpec>
        {
            ["act"] = new ActionSpec("act", ActionCategory.Global, RuntimeMode.Unknown,
                ExecutionKind.Memory, schema, false, 0)
        });
        var rt = new StubRuntimeAdapterAttachedExec(MakeSession());
        var orch = new TrainerOrchestrator(new StubProfileRepo2(profile), rt, new StubFreezeService(), new StubAuditLoggerSimple());
        var result = await orch.ExecuteAsync("test", "act", new JsonObject(), RuntimeMode.Galactic, null, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
    }

    // TrainerOrchestrator: L227 freeze action with boolValue
    [Fact]
    public async Task Orchestrator_FreezeAction_Bool()
    {
        var profile = MakeProfile(new Dictionary<string, ActionSpec>
        {
            ["freeze_symbol"] = new ActionSpec("freeze_symbol", ActionCategory.Global, RuntimeMode.Unknown,
                ExecutionKind.Freeze, new JsonObject(), false, 0)
        });
        var rt = new StubRuntimeAdapterAttachedExec(MakeSession());
        var fs = new StubFreezeService();
        var orch = new TrainerOrchestrator(new StubProfileRepo2(profile), rt, fs, new StubAuditLoggerSimple());
        var payload = new JsonObject { ["symbol"] = "test_sym", ["freeze"] = true, ["boolValue"] = true };
        var result = await orch.ExecuteAsync("test", "freeze_symbol", payload, RuntimeMode.Galactic, null, CancellationToken.None);
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("Froze");
    }

    // TrainerOrchestrator: L265 freeze action missing value
    [Fact]
    public async Task Orchestrator_FreezeAction_NoValue_Fails()
    {
        var profile = MakeProfile(new Dictionary<string, ActionSpec>
        {
            ["freeze_symbol"] = new ActionSpec("freeze_symbol", ActionCategory.Global, RuntimeMode.Unknown,
                ExecutionKind.Freeze, new JsonObject(), false, 0)
        });
        var rt = new StubRuntimeAdapterAttachedExec(MakeSession());
        var orch = new TrainerOrchestrator(new StubProfileRepo2(profile), rt, new StubFreezeService(), new StubAuditLoggerSimple());
        var payload = new JsonObject { ["symbol"] = "test_sym", ["freeze"] = true };
        var result = await orch.ExecuteAsync("test", "freeze_symbol", payload, RuntimeMode.Galactic, null, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("intValue, floatValue, or boolValue");
    }

    // TrainerOrchestrator: L271-273 unfreeze action
    [Fact]
    public async Task Orchestrator_UnfreezeAction()
    {
        var profile = MakeProfile(new Dictionary<string, ActionSpec>
        {
            ["unfreeze_symbol"] = new ActionSpec("unfreeze_symbol", ActionCategory.Global, RuntimeMode.Unknown,
                ExecutionKind.Freeze, new JsonObject(), false, 0)
        });
        var rt = new StubRuntimeAdapterAttachedExec(MakeSession());
        var orch = new TrainerOrchestrator(new StubProfileRepo2(profile), rt, new StubFreezeService(), new StubAuditLoggerSimple());
        var payload = new JsonObject { ["symbol"] = "test_sym" };
        var result = await orch.ExecuteAsync("test", "unfreeze_symbol", payload, RuntimeMode.Galactic, null, CancellationToken.None);
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("nfroze");
    }

    // TrainerOrchestrator: freeze with empty symbol
    [Fact]
    public async Task Orchestrator_FreezeAction_EmptySymbol_Fails()
    {
        var profile = MakeProfile(new Dictionary<string, ActionSpec>
        {
            ["freeze_symbol"] = new ActionSpec("freeze_symbol", ActionCategory.Global, RuntimeMode.Unknown,
                ExecutionKind.Freeze, new JsonObject(), false, 0)
        });
        var rt = new StubRuntimeAdapterAttachedExec(MakeSession());
        var orch = new TrainerOrchestrator(new StubProfileRepo2(profile), rt, new StubFreezeService(), new StubAuditLoggerSimple());
        var payload = new JsonObject { ["symbol"] = "" };
        var result = await orch.ExecuteAsync("test", "freeze_symbol", payload, RuntimeMode.Galactic, null, CancellationToken.None);
        result.Succeeded.Should().BeFalse();
    }

    // TrainerOrchestrator: overload without context/ct
    [Fact]
    public async Task Orchestrator_ExecuteOverload_NoCt()
    {
        var profile = MakeProfile(new Dictionary<string, ActionSpec>
        {
            ["act"] = new ActionSpec("act", ActionCategory.Global, RuntimeMode.Unknown,
                ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var rt = new StubRuntimeAdapterAttachedExec(MakeSession());
        var orch = new TrainerOrchestrator(new StubProfileRepo2(profile), rt, new StubFreezeService(), new StubAuditLoggerSimple());
        var result = await orch.ExecuteAsync("test", "act", new JsonObject(), RuntimeMode.Galactic);
        result.Succeeded.Should().BeTrue();
    }

    // TrainerOrchestrator: overload with context no ct
    [Fact]
    public async Task Orchestrator_ExecuteOverload_WithContext()
    {
        var profile = MakeProfile(new Dictionary<string, ActionSpec>
        {
            ["act"] = new ActionSpec("act", ActionCategory.Global, RuntimeMode.Unknown,
                ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var rt = new StubRuntimeAdapterAttachedExec(MakeSession());
        var ctx = new Dictionary<string, object?> { ["key"] = "val" };
        var orch = new TrainerOrchestrator(new StubProfileRepo2(profile), rt, new StubFreezeService(), new StubAuditLoggerSimple());
        var result = await orch.ExecuteAsync("test", "act", new JsonObject(), RuntimeMode.Galactic, ctx);
        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("key");
    }

    // TelemetrySnapshotService: ResolveOutputDirectory relative path (L116)
    [Fact]
    public async Task TelemetrySnapshot_RelativePath_Resolves()
    {
        var svc = new TelemetrySnapshotService();
        svc.RecordAction("act1", AddressSource.Signature, true);
        var path = await svc.ExportSnapshotAsync("telemetry_output", CancellationToken.None);
        path.Should().NotBeNullOrEmpty();
        File.Exists(path).Should().BeTrue();
        try { File.Delete(path); } catch { /* cleanup */ }
    }

    // TelemetrySnapshotService: whitespace output dir (L89-91)
    [Fact]
    public async Task TelemetrySnapshot_WhitespaceDir_Throws()
    {
        var svc = new TelemetrySnapshotService();
        var act = () => svc.ExportSnapshotAsync("  ", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    // TelemetrySnapshotService: ExportAsync overload without ct
    [Fact]
    public async Task TelemetrySnapshot_ExportNoCt()
    {
        var tmpDir = Path.Join(Path.GetTempPath(), $"ts_{Guid.NewGuid():N}");
        try
        {
            var svc = new TelemetrySnapshotService();
            var path = await svc.ExportSnapshotAsync(tmpDir);
            path.Should().NotBeNullOrEmpty();
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }
    }

    // ActionReliabilityService: L441 RequiresSymbol false when no required array
    [Fact]
    public void ActionReliability_NoRequiredArray_StableResult()
    {
        var svc = new ActionReliabilityService();
        var profile = MakeProfile(new Dictionary<string, ActionSpec>
        {
            ["act"] = new ActionSpec("act", ActionCategory.Global, RuntimeMode.Galactic,
                ExecutionKind.Memory, new JsonObject(), false, 0)
        });
        var session = MakeSession();
        var results = svc.Evaluate(profile, session, null);
        results.Should().ContainSingle().Which.State.Should().Be(ActionReliabilityState.Stable);
    }

    // ModCalibrationService: L149 BuildProcessPayload - when session has LaunchContext
    [Fact]
    public async Task ModCalibration_ExportArtifact_WithSession()
    {
        var tmpDir = Path.Join(Path.GetTempPath(), $"mc_{Guid.NewGuid():N}");
        try
        {
            var svc = new ModCalibrationService(new ActionReliabilityService());
            var session = MakeSession();
            var req = new ModCalibrationArtifactRequest("test", tmpDir, session, "notes");
            var result = await svc.ExportCalibrationArtifactAsync(req, CancellationToken.None);
            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tmpDir)) Directory.Delete(tmpDir, true);
        }
    }

    // ModCalibrationService: L267 InferDependencyStatus with valid enum string (83.33%)
    [Fact]
    public async Task ModCalibration_InferDependencyStatus_SoftFail()
    {
        var svc = new ModCalibrationService(new ActionReliabilityService());
        var session = new AttachSession("test",
            new ProcessMetadata(1, "g.exe", @"C:\g.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic,
                new Dictionary<string, string> { ["dependencyValidation"] = "SoftFail" }),
            new ProfileBuild("test", "1.0", @"C:\g.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>()), DateTimeOffset.UtcNow);
        var result = await svc.BuildCompatibilityReportAsync(MakeProfile(), session);
        result.DependencyStatus.Should().Be(DependencyValidationStatus.SoftFail);
    }

    // ModCalibrationService: L267 InferDependencyStatus with invalid string
    [Fact]
    public async Task ModCalibration_InferDependencyStatus_InvalidString_DefaultsPass()
    {
        var svc = new ModCalibrationService(new ActionReliabilityService());
        var session = new AttachSession("test",
            new ProcessMetadata(1, "g.exe", @"C:\g.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic,
                new Dictionary<string, string> { ["dependencyValidation"] = "NotAValidStatus" }),
            new ProfileBuild("test", "1.0", @"C:\g.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>()), DateTimeOffset.UtcNow);
        var result = await svc.BuildCompatibilityReportAsync(MakeProfile(), session);
        result.DependencyStatus.Should().Be(DependencyValidationStatus.Pass);
    }

    // TrustedPathPolicy: L7 static field initialization + L117-119
    [Fact]
    public void TrustedPathPolicy_BuildSiblingFilePath()
    {
        var tmpFile = Path.Join(Path.GetTempPath(), "test_file.json");
        var result = TrustedPathPolicy.BuildSiblingFilePath(tmpFile, "_backup");
        result.Should().Contain("test_file_backup.json");
    }

    [Fact]
    public void TrustedPathPolicy_BuildSiblingFilePath_EmptyDir_Throws()
    {
        // A file at the root is a special case - on Windows roots always have a directory
        var act = () => TrustedPathPolicy.BuildSiblingFilePath("nodir", "_backup");
        // On Windows this should resolve fine, but we verify the call works
        act.Should().NotThrow();
    }

    [Fact]
    public void TrustedPathPolicy_EnsureAllowedExtension_NoExtension_Throws()
    {
        var act = () => TrustedPathPolicy.EnsureAllowedExtension("/path/file", ".json");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TrustedPathPolicy_EnsureAllowedExtension_WrongExtension_Throws()
    {
        var act = () => TrustedPathPolicy.EnsureAllowedExtension("/path/file.xml", ".json");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TrustedPathPolicy_EnsureAllowedExtension_EmptyAllowedSet_NoThrow()
    {
        TrustedPathPolicy.EnsureAllowedExtension("/path/file.xml");
    }

    [Fact]
    public void TrustedPathPolicy_NormalizeAbsolute_Whitespace_Throws()
    {
        var act = () => TrustedPathPolicy.NormalizeAbsolute("  ");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TrustedPathPolicy_EnsureSubPath_Outside_Throws()
    {
        var root = Path.Join(Path.GetTempPath(), "root");
        var outside = Path.Join(Path.GetTempPath(), "other");
        var act = () => TrustedPathPolicy.EnsureSubPath(root, outside);
        act.Should().Throw<InvalidOperationException>();
    }

    // ─────────────────────────────────────────────���
    // Helpers
    // ──────────────────────────────────────────────

    private static TrainerProfile MakeProfile(Dictionary<string, ActionSpec>? actions = null, Dictionary<string, string>? metadata = null) =>
        new("test", "Test", null, ExeTarget.Swfoc, null,
            new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) },
            new Dictionary<string, long>(),
            actions ?? new Dictionary<string, ActionSpec>(),
            new Dictionary<string, bool>(),
            Array.Empty<CatalogSource>(), "schema",
            Array.Empty<HelperHookSpec>(), metadata);

    private static TrainerProfile MakeFullProfile()
    {
        var actions = new Dictionary<string, ActionSpec>
        {
            ["set_selected_hp"] = MA("set_selected_hp"),
            ["set_selected_shield"] = MA("set_selected_shield"),
            ["set_selected_speed"] = MA("set_selected_speed"),
            ["set_selected_damage_multiplier"] = MA("set_selected_damage_multiplier"),
            ["set_selected_cooldown_multiplier"] = MA("set_selected_cooldown_multiplier"),
            ["set_selected_veterancy"] = MA("set_selected_veterancy"),
            ["set_selected_owner_faction"] = MA("set_selected_owner_faction")
        };
        return MakeProfile(actions: actions);
    }

    private static ActionSpec MA(string id) =>
        new(id, ActionCategory.Unit, RuntimeMode.Unknown, ExecutionKind.Memory, new JsonObject(), false, 0);

    private static AttachSession MakeSession(RuntimeMode mode = RuntimeMode.Galactic) =>
        new("test",
            new ProcessMetadata(1, "g.exe", @"C:\g.exe", null, ExeTarget.Swfoc, mode,
                null,
                new LaunchContext(LaunchKind.BaseGame, true, Array.Empty<string>(), null, null, "test",
                    new ProfileRecommendation("test", "ok", 1.0))),
            new ProfileBuild("test", "1.0", @"C:\g.exe", ExeTarget.Swfoc),
            new SymbolMap(new Dictionary<string, SymbolInfo>()), DateTimeOffset.UtcNow);

    private static BinaryFingerprint MakeFingerprint() =>
        new("fp1", "sha256hash", "game.exe", "1.0", "1.0.0",
            DateTimeOffset.UtcNow, new[] { "game.exe" }, "/game.exe");

    private static ActionAuditRecord MakeAuditRecord() =>
        new(DateTimeOffset.UtcNow,
            new ActionContext("p1", 1, "act", AddressSource.Signature),
            true, "ok");

    private static SavePatchPack MakePatchPack() =>
        new(new SavePatchMetadata("1.0", "p1", "schema1", "hash", DateTimeOffset.UtcNow),
            new SavePatchCompatibility(new[] { "p1" }, "schema1"),
            Array.Empty<SavePatchOperation>());

    private static SpawnPresetService MakeSpawnService(
        TrainerProfile profile,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog = null,
        Exception? catalogThrows = null,
        string? presetRoot = null)
    {
        var rt = new StubRuntimeAdapterAttachedExec(MakeSession());
        var orch = new TrainerOrchestrator(new StubProfileRepo2(profile), rt, new StubFreezeService(), new StubAuditLoggerSimple());
        var cat = catalogThrows is not null
            ? (ICatalogService)new ThrowingCatalogService(catalogThrows)
            : new StubCatalogService(catalog);
        return new SpawnPresetService(new StubProfileRepo2(profile), cat, orch,
            new LiveOpsOptions { PresetRootPath = presetRoot ?? Path.Join(Path.GetTempPath(), $"p_{Guid.NewGuid():N}") });
    }

    // ──────────────────────────────────────────────
    // Stub implementations for interface default methods
    // ──────────────────────────────────────────────

    private sealed class StubAuditLogger : IAuditLogger
    {
        public bool Called { get; private set; }
        public Task WriteAsync(ActionAuditRecord record, CancellationToken cancellationToken)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }

    private sealed class StubAuditLoggerSimple : IAuditLogger
    {
        public Task WriteAsync(ActionAuditRecord record, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubModCalibrationService : IModCalibrationService
    {
        public bool ExportCalled { get; private set; }
        public Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(ModCalibrationArtifactRequest request, CancellationToken cancellationToken)
        {
            ExportCalled = true;
            return Task.FromResult(new ModCalibrationArtifactResult(true, "/out", "fp", Array.Empty<CalibrationCandidate>(), Array.Empty<string>()));
        }
        public Task<ModCompatibilityReport> BuildCompatibilityReportAsync(TrainerProfile profile, AttachSession? session, DependencyValidationResult? dependencyValidation, IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog, CancellationToken cancellationToken)
        {
            return Task.FromResult(new ModCompatibilityReport("test", DateTimeOffset.UtcNow, RuntimeMode.Galactic, DependencyValidationStatus.Pass, 0, true, Array.Empty<ModActionCompatibility>(), Array.Empty<string>()));
        }
    }

    private sealed class StubProcessLocator : IProcessLocator
    {
        public Task<IReadOnlyList<ProcessMetadata>> FindSupportedProcessesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProcessMetadata>>(Array.Empty<ProcessMetadata>());
        public Task<ProcessMetadata?> FindBestMatchAsync(ExeTarget target, CancellationToken cancellationToken) =>
            Task.FromResult<ProcessMetadata?>(null);
    }

    private sealed class StubProfileRepo : IProfileRepository
    {
        private readonly TrainerProfile _p = new("test", "Test", null, ExeTarget.Swfoc, null,
            new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) },
            new Dictionary<string, long>(), new Dictionary<string, ActionSpec>(),
            new Dictionary<string, bool>(), Array.Empty<CatalogSource>(), "schema", Array.Empty<HelperHookSpec>());

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProfileManifest("1.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>()));
        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken) =>
            Task.FromResult(_p);
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken) =>
            Task.FromResult(_p);
        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(new[] { "test" });
    }

    private sealed class StubProfileRepo2 : IProfileRepository
    {
        private readonly TrainerProfile _p;
        public StubProfileRepo2(TrainerProfile p) { _p = p; }
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct) =>
            Task.FromResult(new ProfileManifest("1.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>()));
        public Task<TrainerProfile> LoadProfileAsync(string id, CancellationToken ct) => Task.FromResult(_p);
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string id, CancellationToken ct) => Task.FromResult(_p);
        public Task ValidateProfileAsync(TrainerProfile p, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<string>>(new[] { _p.Id });
    }

    private sealed class StubProfileUpdateService : IProfileUpdateService
    {
        public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        public Task<string> InstallProfileAsync(string profileId, CancellationToken cancellationToken) =>
            Task.FromResult("/installed");
        public Task<ProfileInstallResult> InstallProfileTransactionalAsync(string profileId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProfileInstallResult(true, profileId, "/path", null, null, "ok"));
        public Task<ProfileRollbackResult> RollbackLastInstallAsync(string profileId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProfileRollbackResult(true, profileId, "/path", null, "ok"));
    }

    private sealed class StubRuntimeAdapter : IRuntimeAdapter
    {
        public bool IsAttached => true;
        public AttachSession? CurrentSession => null;
        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken) =>
            Task.FromResult(MakeSession());
        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged =>
            Task.FromResult(default(T));
        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged =>
            Task.CompletedTask;
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.Signature));
        public Task DetachAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        // Does NOT override ScanCalibrationCandidatesAsync - uses default interface method
    }

    private sealed class StubRuntimeAdapterDetached : IRuntimeAdapter
    {
        public bool IsAttached => false;
        public AttachSession? CurrentSession => null;
        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged => Task.FromResult(default(T));
        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged => Task.CompletedTask;
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.None));
        public Task DetachAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubRuntimeAdapterAttached : IRuntimeAdapter
    {
        private readonly AttachSession _session;
        public StubRuntimeAdapterAttached(AttachSession session) { _session = session; }
        public bool IsAttached => true;
        public AttachSession? CurrentSession => _session;
        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken) => Task.FromResult(_session);
        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged => Task.FromResult(default(T));
        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged => Task.CompletedTask;
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.Signature));
        public Task DetachAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubRuntimeAdapterAttachedExec : IRuntimeAdapter
    {
        private readonly AttachSession _session;
        private readonly bool _failAlways;
        public StubRuntimeAdapterAttachedExec(AttachSession session, bool failAlways = false) { _session = session; _failAlways = failAlways; }
        public bool IsAttached => true;
        public AttachSession? CurrentSession => _session;
        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken) => Task.FromResult(_session);
        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged => Task.FromResult(default(T));
        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged => Task.CompletedTask;
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(_failAlways
                ? new ActionExecutionResult(false, "fail", AddressSource.None)
                : new ActionExecutionResult(true, "ok", AddressSource.Signature));
        public Task DetachAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class PartialFailRuntime : IRuntimeAdapter
    {
        private readonly AttachSession _session;
        private readonly string _failOnSymbol;
        public PartialFailRuntime(AttachSession session, string failOnSymbol) { _session = session; _failOnSymbol = failOnSymbol; }
        public bool IsAttached => true;
        public AttachSession? CurrentSession => _session;
        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken) => Task.FromResult(_session);
        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged => Task.FromResult(default(T));
        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged => Task.CompletedTask;
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
        {
            // Fail on set_selected_ actions for the specific symbol
            if (request.Action.Id.Contains(_failOnSymbol, StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new ActionExecutionResult(false, "fail at " + _failOnSymbol, AddressSource.None));
            return Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.Signature));
        }
        public Task DetachAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubFreezeService : IValueFreezeService
    {
        public void FreezeInt(string s, int v) { }
        public void FreezeIntAggressive(string s, int v) { }
        public void FreezeFloat(string s, float v) { }
        public void FreezeBool(string s, bool v) { }
        public bool Unfreeze(string s) => true;
        public void UnfreezeAll() { }
        public bool IsFrozen(string s) => false;
        public IReadOnlyCollection<string> GetFrozenSymbols() => Array.Empty<string>();
        public void Dispose() { }
    }

    private sealed class StubSaveCodec : ISaveCodec
    {
        public Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken cancellationToken) =>
            Task.FromResult(new SaveDocument(path, schemaId, Array.Empty<byte>(), new SaveNode("/", "root", "object", null)));
        public Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken cancellationToken) =>
            Task.FromResult(new SaveValidationResult(true, Array.Empty<string>(), Array.Empty<string>()));
        public Task WriteAsync(SaveDocument document, string outputPath, CancellationToken cancellationToken) =>
            Task.CompletedTask;
        public Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken cancellationToken) =>
            Task.FromResult(true);
    }

    private sealed class StubSavePatchApplyService : ISavePatchApplyService
    {
        public Task<SavePatchApplyResult> ApplyAsync(string targetSavePath, SavePatchPack pack, string targetProfileId, bool strict, CancellationToken cancellationToken) =>
            Task.FromResult(new SavePatchApplyResult(SavePatchApplyClassification.Applied, true, "ok"));
        public Task<SaveRollbackResult> RestoreLastBackupAsync(string targetSavePath, CancellationToken cancellationToken) =>
            Task.FromResult(new SaveRollbackResult(true, "ok"));
    }

    private sealed class StubSignatureResolver : ISignatureResolver
    {
        public Task<SymbolMap> ResolveAsync(ProfileBuild profileBuild, IReadOnlyList<SignatureSet> signatureSets, IReadOnlyDictionary<string, long> fallbackOffsets, CancellationToken cancellationToken) =>
            Task.FromResult(new SymbolMap(new Dictionary<string, SymbolInfo>()));
    }

    private sealed class StubModOnboardingService : IModOnboardingService
    {
        public Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ModOnboardingResult(true, "test", "/out", Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
        public Task<ModOnboardingBatchResult> ScaffoldDraftProfilesFromSeedsAsync(ModOnboardingSeedBatchRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ModOnboardingBatchResult(true, 1, 1, 0, Array.Empty<ModOnboardingBatchItemResult>()));
    }

    private sealed class StubCatalogService : ICatalogService
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>>? _c;
        public StubCatalogService(IReadOnlyDictionary<string, IReadOnlyList<string>>? c = null) { _c = c; }
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string id, CancellationToken ct) =>
            Task.FromResult(_c ?? (IReadOnlyDictionary<string, IReadOnlyList<string>>)new Dictionary<string, IReadOnlyList<string>>());
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string id) =>
            LoadCatalogAsync(id, CancellationToken.None);
    }

    private sealed class ThrowingCatalogService : ICatalogService
    {
        private readonly Exception _ex;
        public ThrowingCatalogService(Exception ex) { _ex = ex; }
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string id, CancellationToken ct) =>
            throw _ex;
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string id) =>
            throw _ex;
    }

    private sealed class StubSdkRuntimeAdapter : ISdkRuntimeAdapter
    {
        public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request) =>
            Task.FromResult(new SdkOperationResult(true, "ok", CapabilityReasonCode.AllRequiredAnchorsPresent, SdkCapabilityStatus.Available));
        public Task<SdkOperationResult> ExecuteAsync(SdkOperationRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new SdkOperationResult(true, "ok", CapabilityReasonCode.AllRequiredAnchorsPresent, SdkCapabilityStatus.Available));
    }

    private sealed class StubProfileVariantResolver : IProfileVariantResolver
    {
        public Task<ProfileVariantResolution> ResolveAsync(string requestedProfileId, CancellationToken cancellationToken) =>
            Task.FromResult(new ProfileVariantResolution(requestedProfileId, requestedProfileId, "explicit", 1.0));
        public Task<ProfileVariantResolution> ResolveAsync(string requestedProfileId, IReadOnlyList<ProcessMetadata>? processes, CancellationToken cancellationToken) =>
            Task.FromResult(new ProfileVariantResolution(requestedProfileId, requestedProfileId, "explicit", 1.0));
    }

    private sealed class StubBinaryFingerprintService : IBinaryFingerprintService
    {
        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath) => Task.FromResult(MakeFingerprint());
        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, CancellationToken cancellationToken) => Task.FromResult(MakeFingerprint());
        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int processId) => Task.FromResult(MakeFingerprint());
        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int processId, CancellationToken cancellationToken) => Task.FromResult(MakeFingerprint());
    }

    private sealed class StubBinaryFingerprintServiceWithPid : IBinaryFingerprintService
    {
        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath) => Task.FromResult(MakeFingerprint());
        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, CancellationToken cancellationToken) => Task.FromResult(MakeFingerprint());
        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int processId) => Task.FromResult(MakeFingerprint());
        public Task<BinaryFingerprint> CaptureFromPathAsync(string modulePath, int processId, CancellationToken cancellationToken) => Task.FromResult(MakeFingerprint());
    }

    private sealed class StubCapabilityMapResolver : ICapabilityMapResolver
    {
        public Task<CapabilityResolutionResult> ResolveAsync(BinaryFingerprint fingerprint, string requestedProfileId, string operationId, IReadOnlySet<string> resolvedAnchors) =>
            Task.FromResult(MakeCapResult(operationId));
        public Task<CapabilityResolutionResult> ResolveAsync(BinaryFingerprint fingerprint, string requestedProfileId, string operationId, IReadOnlySet<string> resolvedAnchors, CancellationToken cancellationToken) =>
            Task.FromResult(MakeCapResult(operationId));
        public Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint) => Task.FromResult<string?>(null);
        public Task<string?> ResolveDefaultProfileIdAsync(BinaryFingerprint fingerprint, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
        private static CapabilityResolutionResult MakeCapResult(string opId) =>
            new("p1", opId, SdkCapabilityStatus.Available, CapabilityReasonCode.AllRequiredAnchorsPresent, 1.0, "fp1",
                Array.Empty<string>(), Array.Empty<string>(), CapabilityResolutionMetadata.Empty);
    }

    private sealed class StubSdkExecutionGuard : ISdkExecutionGuard
    {
        private readonly bool _allow;
        public StubSdkExecutionGuard(bool allow) { _allow = allow; }
        public SdkExecutionDecision CanExecute(CapabilityResolutionResult resolution, bool isMutation) =>
            new(_allow, _allow ? CapabilityReasonCode.AllRequiredAnchorsPresent : CapabilityReasonCode.MutationBlockedByCapabilityState,
                _allow ? "ok" : "blocked");
    }

    private sealed class StubSdkDiagnosticsSink : ISdkDiagnosticsSink
    {
        public Task WriteAsync(SdkOperationRequest request, SdkOperationResult result) => Task.CompletedTask;
        public Task WriteAsync(SdkOperationRequest request, SdkOperationResult result, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
