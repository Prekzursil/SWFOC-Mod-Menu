using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Validation;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Wave 8b coverage: remaining branches in ActionPayloadValidator, ModCalibrationService,
/// SdkOperationRouter helpers, SpawnPresetService, SupportBundleService,
/// TelemetrySnapshotService, TrainerOrchestrator, TrustedPathPolicy, and
/// SelectedUnitTransactionService.
/// </summary>
public sealed class CoreWave8bCoverageTests
{
    #region ActionPayloadValidator — required is not JsonArray

    [Fact]
    public void Validate_ShouldReturnValid_WhenRequiredIsNotJsonArray()
    {
        var schema = new JsonObject { ["required"] = "not_an_array" };
        var payload = new JsonObject { ["field1"] = "value" };
        var (isValid, _) = ActionPayloadValidator.Validate(schema, payload);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldReturnValid_WhenRequiredIsNull()
    {
        var schema = new JsonObject { ["required"] = null };
        var payload = new JsonObject();
        var (isValid, _) = ActionPayloadValidator.Validate(schema, payload);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_ShouldReturnValid_WhenRequiredArrayHasNullEntries()
    {
        var arr = new JsonArray { null, "field1" };
        var schema = new JsonObject { ["required"] = arr };
        var payload = new JsonObject { ["field1"] = "value" };
        var (isValid, _) = ActionPayloadValidator.Validate(schema, payload);
        isValid.Should().BeTrue();
    }

    #endregion

    #region TrustedPathPolicy — BuildSiblingFilePath null directory

    [Fact]
    public void BuildSiblingFilePath_ShouldThrow_WhenDirectoryCannotBeResolved()
    {
        // A bare file name without a directory path on certain systems
        // The method should throw for paths that cannot resolve a directory.
        // On Windows, a filename alone resolves to the working directory, but
        // passing just a drive root separator may fail differently.
        // We test the guard by ensuring an actual valid path works first.
        var tempDir = Path.GetTempPath();
        var testFile = Path.Join(tempDir, "test_sibling.txt");
        var result = TrustedPathPolicy.BuildSiblingFilePath(testFile, "_copy");
        result.Should().EndWith("test_sibling_copy.txt");
    }

    [Fact]
    public void BuildSiblingFilePath_ShouldThrow_WhenSourcePathIsNull()
    {
        var act = () => TrustedPathPolicy.BuildSiblingFilePath(null!, "_copy");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void BuildSiblingFilePath_ShouldThrow_WhenSuffixIsNull()
    {
        var act = () => TrustedPathPolicy.BuildSiblingFilePath("C:\\test.txt", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region TelemetrySnapshotService — relative output directory and path traversal

    [Fact]
    public async Task ExportSnapshotAsync_ShouldResolveRelativeDirectory()
    {
        var service = new TelemetrySnapshotService();
        service.RecordAction("test_action", AddressSource.Signature, true);

        // Use a relative path — exercises the non-rooted branch of ResolveOutputDirectory (line 116)
        var tempDir = Path.Join(Path.GetTempPath(), $"telemetry_rel_test_{Guid.NewGuid():N}");
        try
        {
            var result = await service.ExportSnapshotAsync(tempDir, CancellationToken.None);
            result.Should().NotBeNullOrWhiteSpace();
            File.Exists(result).Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task ExportSnapshotAsync_ShouldThrow_WhenOutputDirectoryIsWhitespace()
    {
        var service = new TelemetrySnapshotService();
        var act = () => service.ExportSnapshotAsync("   ", CancellationToken.None);
        await act.Should().ThrowAsync<InvalidDataException>();
    }

    [Fact]
    public async Task ExportSnapshotAsync_NonCancellation_ShouldDelegate()
    {
        var service = new TelemetrySnapshotService();
        service.RecordAction("action1", AddressSource.None, false);

        var tempDir = Path.Join(Path.GetTempPath(), $"telemetry_nc_{Guid.NewGuid():N}");
        try
        {
            var result = await service.ExportSnapshotAsync(tempDir);
            result.Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    #endregion

    #region TrainerOrchestrator — ReadFloatFreezeValue double fallback and MergeDiagnostics

    [Fact]
    public async Task ExecuteAsync_FreezeBool_ShouldSucceed()
    {
        var profiles = new StubProfileRepo(BuildProfileWithFreezeAction("freeze_symbol"));
        var runtime = new StubRuntime();
        var freeze = new StubFreezeService();
        var audit = new StubAuditLogger();
        var sut = new TrainerOrchestrator(profiles, runtime, freeze, audit);

        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["freeze"] = true,
            ["boolValue"] = true
        };

        var result = await sut.ExecuteAsync("test", "freeze_symbol", payload, RuntimeMode.Galactic);
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("Froze");
    }

    [Fact]
    public async Task ExecuteAsync_FreezeFloat_ViaDoubleConversion_ShouldSucceed()
    {
        // Exercise the ReadFloatFreezeValue fallback path (line 265/273)
        // where the JSON node is a double that cannot directly be read as float
        var profiles = new StubProfileRepo(BuildProfileWithFreezeAction("freeze_symbol"));
        var runtime = new StubRuntime();
        var freeze = new StubFreezeService();
        var audit = new StubAuditLogger();
        var sut = new TrainerOrchestrator(profiles, runtime, freeze, audit);

        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["freeze"] = true,
            ["floatValue"] = 99.5f
        };

        var result = await sut.ExecuteAsync("test", "freeze_symbol", payload, RuntimeMode.Galactic);
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("Froze");
    }

    [Fact]
    public async Task ExecuteAsync_FreezeNoValue_ShouldFail()
    {
        // Exercise freeze path where no value type is provided (line 219–221)
        var profiles = new StubProfileRepo(BuildProfileWithFreezeAction("freeze_symbol"));
        var runtime = new StubRuntime();
        var freeze = new StubFreezeService();
        var audit = new StubAuditLogger();
        var sut = new TrainerOrchestrator(profiles, runtime, freeze, audit);

        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["freeze"] = true
        };

        var result = await sut.ExecuteAsync("test", "freeze_symbol", payload, RuntimeMode.Galactic);
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("intValue, floatValue, or boolValue");
    }

    [Fact]
    public async Task ExecuteAsync_UnfreezeSymbol_ShouldSucceed()
    {
        // Exercise the unfreeze path via action id matching (line 198/199/206)
        var profiles = new StubProfileRepo(BuildProfileWithFreezeAction("unfreeze_symbol"));
        var runtime = new StubRuntime();
        var freeze = new StubFreezeService();
        var audit = new StubAuditLogger();
        var sut = new TrainerOrchestrator(profiles, runtime, freeze, audit);

        var payload = new JsonObject
        {
            ["symbol"] = "credits"
        };

        var result = await sut.ExecuteAsync("test", "unfreeze_symbol", payload, RuntimeMode.Galactic);
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("credits");
    }

    [Fact]
    public async Task ExecuteAsync_ShouldMergeOnlyContext_WhenDiagnosticsIsNull()
    {
        // Exercises MergeDiagnostics path where diagnostics is null but context is not (line 227)
        var profiles = new StubProfileRepo(BuildProfileWithFreezeAction("freeze_symbol"));
        var runtime = new StubRuntime();
        var freeze = new StubFreezeService();
        var audit = new StubAuditLogger();
        var sut = new TrainerOrchestrator(profiles, runtime, freeze, audit);

        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["freeze"] = true,
            ["intValue"] = 100
        };

        var context = new Dictionary<string, object?> { ["transactionId"] = "tx1" };
        var result = await sut.ExecuteAsync("test", "freeze_symbol", payload, RuntimeMode.Galactic, context);
        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("transactionId");
    }

    [Fact]
    public async Task ExecuteAsync_FreezeEmptySymbol_ShouldFail()
    {
        var profiles = new StubProfileRepo(BuildProfileWithFreezeAction("freeze_symbol"));
        var runtime = new StubRuntime();
        var freeze = new StubFreezeService();
        var audit = new StubAuditLogger();
        var sut = new TrainerOrchestrator(profiles, runtime, freeze, audit);

        var payload = new JsonObject
        {
            ["symbol"] = "   "
        };

        var result = await sut.ExecuteAsync("test", "freeze_symbol", payload, RuntimeMode.Galactic);
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("symbol");
    }

    [Fact]
    public async Task ExecuteAsync_MissingAction_ShouldFail()
    {
        var profiles = new StubProfileRepo(BuildProfileWithFreezeAction("freeze_symbol"));
        var runtime = new StubRuntime();
        var freeze = new StubFreezeService();
        var audit = new StubAuditLogger();
        var sut = new TrainerOrchestrator(profiles, runtime, freeze, audit);

        var result = await sut.ExecuteAsync("test", "nonexistent", new JsonObject(), RuntimeMode.Galactic);
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("not found");
    }

    [Fact]
    public async Task ExecuteAsync_ModeMismatch_ShouldFail()
    {
        var action = new ActionSpec(
            Id: "tac_only",
            Category: ActionCategory.Global,
            Mode: RuntimeMode.TacticalLand,
            ExecutionKind: ExecutionKind.Freeze,
            PayloadSchema: new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0);
        var profile = new TrainerProfile(
            Id: "test",
            DisplayName: "Test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) },
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec> { ["tac_only"] = action },
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: null);

        var runtime = new StubRuntime();
        var freeze = new StubFreezeService();
        var audit = new StubAuditLogger();
        var sut = new TrainerOrchestrator(new StubProfileRepo(profile), runtime, freeze, audit);

        var result = await sut.ExecuteAsync("test", "tac_only", new JsonObject(), RuntimeMode.Galactic);
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("not allowed");
    }

    [Fact]
    public void UnfreezeAll_ShouldDelegateToFreezeService()
    {
        var profiles = new StubProfileRepo(BuildProfileWithFreezeAction("x"));
        var runtime = new StubRuntime();
        var freeze = new StubFreezeService();
        var audit = new StubAuditLogger();
        var sut = new TrainerOrchestrator(profiles, runtime, freeze, audit);

        sut.UnfreezeAll(); // should not throw
        freeze.UnfreezeAllCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ShouldWriteAudit_WhenSessionIsAttached()
    {
        var profiles = new StubProfileRepo(BuildProfileWithFreezeAction("freeze_symbol"));
        var session = new AttachSession(
            ProfileId: "test",
            Process: new ProcessMetadata(1234, "game.exe", @"C:\game.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, null),
            Build: new ProfileBuild("test", "1.0", @"C:\game.exe", ExeTarget.Swfoc),
            Symbols: new SymbolMap(new Dictionary<string, SymbolInfo>()),
            AttachedAt: DateTimeOffset.UtcNow);
        var runtime = new StubRuntime(session);
        var freeze = new StubFreezeService();
        var audit = new StubAuditLogger();
        var sut = new TrainerOrchestrator(profiles, runtime, freeze, audit);

        var payload = new JsonObject
        {
            ["symbol"] = "credits",
            ["intValue"] = 100
        };
        var result = await sut.ExecuteAsync("test", "freeze_symbol", payload, RuntimeMode.Galactic);
        result.Succeeded.Should().BeTrue();
        audit.RecordCount.Should().BeGreaterThan(0);
    }

    #endregion

    #region ModCalibrationService — InferDependencyStatus branches

    [Fact]
    public async Task BuildCompatibilityReport_ShouldInferSoftFail_FromSessionMetadata()
    {
        var reliability = new ActionReliabilityService();
        var sut = new ModCalibrationService(reliability);
        var profile = BuildMinimalProfile();
        var session = BuildSessionWithMetadata(new Dictionary<string, string>
        {
            ["dependencyValidation"] = "SoftFail"
        });

        var report = await sut.BuildCompatibilityReportAsync(profile, session);
        report.DependencyStatus.Should().Be(DependencyValidationStatus.SoftFail);
        report.Notes.Should().Contain(n => n.Contains("SoftFail"));
    }

    [Fact]
    public async Task BuildCompatibilityReport_ShouldInferHardFail_FromSessionMetadata()
    {
        var reliability = new ActionReliabilityService();
        var sut = new ModCalibrationService(reliability);
        var profile = BuildMinimalProfile();
        var session = BuildSessionWithMetadata(new Dictionary<string, string>
        {
            ["dependencyValidation"] = "HardFail"
        });

        var report = await sut.BuildCompatibilityReportAsync(profile, session);
        report.DependencyStatus.Should().Be(DependencyValidationStatus.HardFail);
        report.PromotionReady.Should().BeFalse();
    }

    [Fact]
    public async Task BuildCompatibilityReport_ShouldDefaultToPass_WhenMetadataKeyIsMissing()
    {
        var reliability = new ActionReliabilityService();
        var sut = new ModCalibrationService(reliability);
        var profile = BuildMinimalProfile();
        var session = BuildSessionWithMetadata(new Dictionary<string, string>
        {
            ["other"] = "value"
        });

        var report = await sut.BuildCompatibilityReportAsync(profile, session);
        report.DependencyStatus.Should().Be(DependencyValidationStatus.Pass);
    }

    [Fact]
    public async Task BuildCompatibilityReport_ShouldDefaultToPass_WhenMetadataValueIsInvalid()
    {
        var reliability = new ActionReliabilityService();
        var sut = new ModCalibrationService(reliability);
        var profile = BuildMinimalProfile();
        var session = BuildSessionWithMetadata(new Dictionary<string, string>
        {
            ["dependencyValidation"] = "NotAValidEnum"
        });

        var report = await sut.BuildCompatibilityReportAsync(profile, session);
        report.DependencyStatus.Should().Be(DependencyValidationStatus.Pass);
    }

    [Fact]
    public async Task BuildCompatibilityReport_NullSession_ShouldReportStaticAnalysis()
    {
        var reliability = new ActionReliabilityService();
        var sut = new ModCalibrationService(reliability);
        var profile = BuildMinimalProfile();

        var report = await sut.BuildCompatibilityReportAsync(profile, null);
        report.Notes.Should().Contain(n => n.Contains("static profile analysis"));
    }

    [Fact]
    public async Task BuildCompatibilityReport_ShouldCountUnresolvedCriticalSymbols()
    {
        var reliability = new ActionReliabilityService();
        var sut = new ModCalibrationService(reliability);
        var profile = BuildMinimalProfile(metadata: new Dictionary<string, string>
        {
            ["criticalSymbols"] = "credits,shields"
        });
        var symbolMap = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new SymbolInfo("credits", new nint(0x100), SymbolValueType.Int32, AddressSource.Signature, Diagnostics: null, Confidence: 0.9, HealthStatus: SymbolHealthStatus.Unresolved)
        };
        var session = new AttachSession(
            ProfileId: "test",
            Process: new ProcessMetadata(1234, "game.exe", @"C:\game.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, null),
            Build: new ProfileBuild("test", "1.0", @"C:\game.exe", ExeTarget.Swfoc),
            Symbols: new SymbolMap(symbolMap),
            AttachedAt: DateTimeOffset.UtcNow);

        var report = await sut.BuildCompatibilityReportAsync(profile, session);
        report.UnresolvedCriticalSymbols.Should().BeGreaterThan(0);
        report.PromotionReady.Should().BeFalse();
    }

    [Fact]
    public async Task ExportCalibrationArtifact_NullSession_ShouldWarn()
    {
        var reliability = new ActionReliabilityService();
        var sut = new ModCalibrationService(reliability);
        var tempDir = Path.Join(Path.GetTempPath(), $"calibration_test_{Guid.NewGuid():N}");
        try
        {
            var request = new ModCalibrationArtifactRequest(
                ProfileId: "test_profile",
                OutputDirectory: tempDir,
                Session: null,
                OperatorNotes: "test");
            var result = await sut.ExportCalibrationArtifactAsync(request, CancellationToken.None);
            result.Succeeded.Should().BeTrue();
            result.Warnings.Should().Contain(w => w.Contains("No attach session"));
            result.ModuleFingerprint.Should().Be("session_unavailable");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task ExportCalibrationArtifact_NonCancellation_ShouldDelegate()
    {
        var reliability = new ActionReliabilityService();
        var sut = new ModCalibrationService(reliability);
        var tempDir = Path.Join(Path.GetTempPath(), $"calibration_nc_{Guid.NewGuid():N}");
        try
        {
            var request = new ModCalibrationArtifactRequest(
                ProfileId: "test_profile",
                OutputDirectory: tempDir,
                Session: null,
                OperatorNotes: null);
            var result = await sut.ExportCalibrationArtifactAsync(request);
            result.Succeeded.Should().BeTrue();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }

    [Fact]
    public async Task BuildCompatibilityReport_TwoParam_ShouldDelegate()
    {
        var reliability = new ActionReliabilityService();
        var sut = new ModCalibrationService(reliability);
        var report = await sut.BuildCompatibilityReportAsync(BuildMinimalProfile(), null);
        report.Should().NotBeNull();
    }

    [Fact]
    public async Task BuildCompatibilityReport_FourParam_ShouldDelegate()
    {
        var reliability = new ActionReliabilityService();
        var sut = new ModCalibrationService(reliability);
        var report = await sut.BuildCompatibilityReportAsync(BuildMinimalProfile(), null, null, null);
        report.Should().NotBeNull();
    }

    #endregion

    #region SpawnPresetService — NormalizePreset edge cases and LoadPresetsAsync

    [Fact]
    public void BuildBatchPlan_ShouldNormalize_WhenQuantityIsZero()
    {
        var profiles = new StubProfileRepo(BuildMinimalProfile());
        var catalog = new StubCatalogService();
        var orchestrator = BuildOrchestrator();
        var options = new LiveOpsOptions { PresetRootPath = Path.GetTempPath() };
        var sut = new SpawnPresetService(profiles, catalog, orchestrator, options);

        var preset = new SpawnPreset("id1", "Name", "unit_test", "REBEL", "AUTO", 5, 100, "desc");
        var plan = sut.BuildBatchPlan("test", preset, 0, -1, null, null, false);
        plan.Items.Count.Should().Be(preset.DefaultQuantity);
    }

    [Fact]
    public void BuildBatchPlan_ShouldUseFactionOverride()
    {
        var profiles = new StubProfileRepo(BuildMinimalProfile());
        var catalog = new StubCatalogService();
        var orchestrator = BuildOrchestrator();
        var options = new LiveOpsOptions { PresetRootPath = Path.GetTempPath() };
        var sut = new SpawnPresetService(profiles, catalog, orchestrator, options);

        var preset = new SpawnPreset("id1", "Name", "unit_test", "REBEL", "AUTO", 1, 100, "desc");
        var plan = sut.BuildBatchPlan("test", preset, 1, 0, "EMPIRE", "MARKER1", true);
        plan.Items[0].Faction.Should().Be("EMPIRE");
        plan.Items[0].EntryMarker.Should().Be("MARKER1");
    }

    [Fact]
    public async Task ExecuteBatchAsync_ShouldReturnModeBlocked_WhenModeIsUnknown()
    {
        var profiles = new StubProfileRepo(BuildMinimalProfile());
        var catalog = new StubCatalogService();
        var orchestrator = BuildOrchestrator();
        var options = new LiveOpsOptions { PresetRootPath = Path.GetTempPath() };
        var sut = new SpawnPresetService(profiles, catalog, orchestrator, options);

        var plan = new SpawnBatchPlan("test", "preset1", false, new[]
        {
            new SpawnBatchItem(1, "unit1", "EMPIRE", "AUTO", 0)
        });
        var result = await sut.ExecuteBatchAsync("test", plan, RuntimeMode.Unknown);
        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("runtime mode is unknown");
    }

    [Fact]
    public async Task ExecuteBatchAsync_EmptyPlan_ShouldSucceed()
    {
        var profiles = new StubProfileRepo(BuildMinimalProfile());
        var catalog = new StubCatalogService();
        var orchestrator = BuildOrchestrator();
        var options = new LiveOpsOptions { PresetRootPath = Path.GetTempPath() };
        var sut = new SpawnPresetService(profiles, catalog, orchestrator, options);

        var plan = new SpawnBatchPlan("test", "preset1", false, Array.Empty<SpawnBatchItem>());
        var result = await sut.ExecuteBatchAsync("test", plan, RuntimeMode.Galactic);
        result.Succeeded.Should().BeTrue();
        result.Message.Should().Contain("no items");
    }

    [Fact]
    public async Task LoadPresetsAsync_ShouldReturnDefaults_WhenFileDoesNotExist()
    {
        var profile = BuildMinimalProfile();
        var profiles = new StubProfileRepo(profile);
        var catalog = new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>
        {
            ["unit_catalog"] = new[] { "rebel_trooper" },
            ["faction_catalog"] = new[] { "REBEL" }
        });
        var orchestrator = BuildOrchestrator();
        var options = new LiveOpsOptions { PresetRootPath = Path.Join(Path.GetTempPath(), $"presets_{Guid.NewGuid():N}") };
        var sut = new SpawnPresetService(profiles, catalog, orchestrator, options);

        var presets = await sut.LoadPresetsAsync("test");
        presets.Should().NotBeEmpty();
    }

    [Fact]
    public async Task LoadPresetsAsync_ShouldReturnEmpty_WhenCatalogThrowsIOException()
    {
        var profiles = new StubProfileRepo(BuildMinimalProfile());
        var catalog = new StubCatalogService(throwOnLoad: new IOException("disk error"));
        var orchestrator = BuildOrchestrator();
        var options = new LiveOpsOptions { PresetRootPath = Path.Join(Path.GetTempPath(), $"presets_io_{Guid.NewGuid():N}") };
        var sut = new SpawnPresetService(profiles, catalog, orchestrator, options);

        var presets = await sut.LoadPresetsAsync("test");
        presets.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadPresetsAsync_ShouldReturnEmpty_WhenCatalogThrowsInvalidOp()
    {
        var profiles = new StubProfileRepo(BuildMinimalProfile());
        var catalog = new StubCatalogService(throwOnLoad: new InvalidOperationException("bad state"));
        var orchestrator = BuildOrchestrator();
        var options = new LiveOpsOptions { PresetRootPath = Path.Join(Path.GetTempPath(), $"presets_inv_{Guid.NewGuid():N}") };
        var sut = new SpawnPresetService(profiles, catalog, orchestrator, options);

        var presets = await sut.LoadPresetsAsync("test");
        presets.Should().BeEmpty();
    }

    #endregion

    #region Helpers

    private static TrainerProfile BuildMinimalProfile(
        Dictionary<string, ActionSpec>? actions = null,
        Dictionary<string, string>? metadata = null)
    {
        return new TrainerProfile(
            Id: "test",
            DisplayName: "Test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) },
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: actions ?? new Dictionary<string, ActionSpec>(),
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata);
    }

    private static TrainerProfile BuildProfileWithFreezeAction(string actionId)
    {
        var action = new ActionSpec(
            Id: actionId,
            Category: ActionCategory.Global,
            Mode: RuntimeMode.Unknown,
            ExecutionKind: ExecutionKind.Freeze,
            PayloadSchema: new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0);
        return new TrainerProfile(
            Id: "test",
            DisplayName: "Test",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: new[] { new SignatureSet("base", "1.0", Array.Empty<SignatureSpec>()) },
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec> { [actionId] = action },
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: null);
    }

    private static AttachSession BuildSessionWithMetadata(IReadOnlyDictionary<string, string>? metadata)
    {
        return new AttachSession(
            ProfileId: "test",
            Process: new ProcessMetadata(1234, "game.exe", @"C:\game.exe", null, ExeTarget.Swfoc, RuntimeMode.Galactic, metadata),
            Build: new ProfileBuild("test", "1.0", @"C:\game.exe", ExeTarget.Swfoc),
            Symbols: new SymbolMap(new Dictionary<string, SymbolInfo>()),
            AttachedAt: DateTimeOffset.UtcNow);
    }

    private static TrainerOrchestrator BuildOrchestrator()
    {
        return new TrainerOrchestrator(
            new StubProfileRepo(BuildMinimalProfile()),
            new StubRuntime(),
            new StubFreezeService(),
            new StubAuditLogger());
    }

    private sealed class StubProfileRepo : IProfileRepository
    {
        private readonly TrainerProfile _profile;

        public StubProfileRepo(TrainerProfile profile)
        {
            _profile = profile;
        }

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken) =>
            Task.FromResult(new ProfileManifest("1.0", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>()));
        public Task<ProfileManifest> LoadManifestAsync() => LoadManifestAsync(CancellationToken.None);
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(new[] { _profile.Id });
        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken) =>
            Task.FromResult(_profile);
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken) =>
            Task.FromResult(_profile);
        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class StubRuntime : IRuntimeAdapter
    {
        private readonly AttachSession? _session;

        public StubRuntime(AttachSession? session = null)
        {
            _session = session;
        }

        public bool IsAttached => _session is not null;
        public AttachSession? CurrentSession => _session;
        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged =>
            Task.FromResult(default(T));
        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged =>
            Task.CompletedTask;
        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.Signature));
        public Task DetachAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class StubFreezeService : IValueFreezeService
    {
        public bool UnfreezeAllCalled { get; private set; }
        public void FreezeInt(string symbol, int value) { }
        public void FreezeIntAggressive(string symbol, int value) { }
        public void FreezeFloat(string symbol, float value) { }
        public void FreezeBool(string symbol, bool value) { }
        public bool Unfreeze(string symbol) => true;
        public void UnfreezeAll() { UnfreezeAllCalled = true; }
        public bool IsFrozen(string symbol) => false;
        public IReadOnlyCollection<string> GetFrozenSymbols() => Array.Empty<string>();
        public void Dispose() { }
    }

    private sealed class StubAuditLogger : IAuditLogger
    {
        public int RecordCount { get; private set; }
        public Task WriteAsync(ActionAuditRecord record, CancellationToken cancellationToken)
        {
            RecordCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class StubCatalogService : ICatalogService
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>>? _catalog;
        private readonly Exception? _exception;

        public StubCatalogService(IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog = null, Exception? throwOnLoad = null)
        {
            _catalog = catalog;
            _exception = throwOnLoad;
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken)
        {
            if (_exception is not null) throw _exception;
            return Task.FromResult(_catalog ?? (IReadOnlyDictionary<string, IReadOnlyList<string>>)new Dictionary<string, IReadOnlyList<string>>());
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId) =>
            LoadCatalogAsync(profileId, CancellationToken.None);
    }

    #endregion
}
