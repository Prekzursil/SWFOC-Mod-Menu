using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.Models;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelBaseOpsCoverageTests
{
    [Fact]
    public async Task RefreshActionReliability_AndSpawnPresetFlow_ShouldPopulateDiagnosticsAndRoster()
    {
        var runtime = new StubRuntimeAdapter
        {
            IsAttached = true,
            CurrentSession = BuildSession(RuntimeMode.Galactic)
        };

        var profile = BuildProfile("base_swfoc");
        var profileRepo = new StubProfileRepository(profile);
        var catalog = new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["entity_catalog"] = ["Unit|STORMTROOPER|base_swfoc|1125571106|Textures/UI/storm.dds|dep_a"]
        });
        var reliabilityService = new StubActionReliabilityService(new[]
        {
            new ActionReliabilityInfo("set_credits", ActionReliabilityState.Stable, "CAPABILITY_PROBE_PASS", 1.0d, "ok")
        });
        var spawnService = new StubSpawnPresetService();

        var vm = new SaveOpsHarness(CreateDependencies(
            runtime,
            profileRepo,
            catalog,
            reliabilityService,
            new StubSelectedUnitTransactionService(),
            spawnService,
            new StubFreezeService()));

        vm.SelectedProfileId = profile.Id;
        vm.RuntimeMode = RuntimeMode.Galactic;

        await vm.InvokeRefreshActionReliabilityAsync();
        await vm.InvokeLoadSpawnPresetsAsync();
        await vm.InvokeRunSpawnBatchAsync();

        vm.ActionReliability.Should().ContainSingle();
        vm.LiveOpsDiagnostics.Should().Contain(x => x.StartsWith("mode:"));
        vm.LiveOpsDiagnostics.Should().Contain(x => x.Contains("dependency:"));
        vm.HelperBridgeState.Should().Be("ready");
        vm.HelperBridgeReasonCode.Should().Be("CAPABILITY_PROBE_PASS");
        vm.HelperBridgeFeatures.Should().Contain("spawn_tactical_entity");
        vm.EntityRoster.Should().ContainSingle(x => x.EntityId == "STORMTROOPER");
        vm.SpawnPresets.Should().ContainSingle();
        vm.Status.Should().Contain("batch ok");
        spawnService.LastExecuteResult.Should().NotBeNull();
    }

    [Fact]
    public async Task SelectedUnitTransactionMethods_ShouldUpdateDraftAndStatuses()
    {
        var runtime = new StubRuntimeAdapter
        {
            IsAttached = true,
            CurrentSession = BuildSession(RuntimeMode.TacticalLand)
        };
        var transactions = new StubSelectedUnitTransactionService();
        var vm = new SaveOpsHarness(CreateDependencies(
            runtime,
            new StubProfileRepository(BuildProfile("base_swfoc")),
            new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)),
            new StubActionReliabilityService(Array.Empty<ActionReliabilityInfo>()),
            transactions,
            new StubSpawnPresetService(),
            new StubFreezeService()));

        vm.SelectedProfileId = "base_swfoc";
        vm.RuntimeMode = RuntimeMode.TacticalLand;
        vm.SelectedUnitHp = "100";
        vm.SelectedUnitShield = "50";
        vm.SelectedUnitSpeed = "1.5";
        vm.SelectedUnitDamageMultiplier = "2.0";
        vm.SelectedUnitCooldownMultiplier = "0.5";
        vm.SelectedUnitVeterancy = "3";
        vm.SelectedUnitOwnerFaction = "2";

        await vm.InvokeCaptureSelectedUnitBaselineAsync();
        await vm.InvokeApplySelectedUnitDraftAsync();
        await vm.InvokeRevertSelectedUnitTransactionAsync();
        await vm.InvokeRestoreSelectedUnitBaselineAsync();

        vm.SelectedUnitTransactions.Should().NotBeEmpty();
        vm.Status.Should().NotBeNullOrWhiteSpace();
        vm.SelectedUnitHp.Should().Be("100");
        vm.SelectedUnitOwnerFaction.Should().Be("2");
    }

    [Fact]
    public void SaveOpsHelpers_ShouldHandleVariantMismatch_SearchAndPatchRows()
    {
        var runtime = new StubRuntimeAdapter
        {
            IsAttached = true,
            CurrentSession = BuildSession(RuntimeMode.Galactic, "resolved_variant_profile")
        };

        var vm = new SaveOpsHarness(CreateDependencies(
            runtime,
            new StubProfileRepository(BuildProfile("base_swfoc")),
            new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)),
            new StubActionReliabilityService(Array.Empty<ActionReliabilityInfo>()),
            new StubSelectedUnitTransactionService(),
            new StubSpawnPresetService(),
            new StubFreezeService()));

        var variantError = vm.InvokeValidateSaveRuntimeVariant("base_swfoc");
        variantError.Should().Contain("save_variant_mismatch");

        var canPreview = vm.InvokePreparePatchPreview("base_swfoc");
        canPreview.Should().BeFalse();
        vm.SavePatchCompatibility.Should().ContainSingle(x => x.Code == "save_variant_mismatch");

        var compatibility = new SavePatchCompatibilityResult(
            IsCompatible: false,
            SourceHashMatches: false,
            TargetHash: "abc",
            Errors: ["schema mismatch"],
            Warnings: ["hash differs"]);
        var preview = new SavePatchPreview(
            IsCompatible: false,
            Errors: ["preview blocked"],
            Warnings: ["preview warn"],
            OperationsToApply:
            [
                new SavePatchOperation(
                    SavePatchOperationKind.SetValue,
                    "root.money",
                    "money",
                    "int",
                    100,
                    999,
                    8)
            ]);

        vm.InvokePopulatePatchPreviewOperations(preview);
        vm.InvokePopulatePatchCompatibilityRows(compatibility, preview);
        vm.InvokeAppendPatchArtifactRows("C:/tmp/backup.sav", "C:/tmp/receipt.json");

        vm.SavePatchOperations.Should().ContainSingle();
        vm.SavePatchCompatibility.Should().Contain(x => x.Code == "backup_path");
        vm.SavePatchCompatibility.Should().Contain(x => x.Code == "receipt_path");

        var root = new SaveNode(
            Path: "root",
            Name: "root",
            ValueType: "root",
            Value: null,
            Children:
            [
                new SaveNode("root.money", "money", "int", 100),
                new SaveNode("root.player.name", "name", "string", "Thrawn")
            ]);

        vm.SetLoadedSaveForCoverage(new SaveDocument("save.sav", "schema", new byte[] { 1, 2, 3 }, root), new byte[] { 1, 2, 9 });
        vm.InvokeRebuildSaveFieldRows();
        vm.SaveFields.Should().HaveCount(2);

        vm.SaveSearchQuery = "name";
        vm.FilteredSaveFields.Should().ContainSingle(x => x.Name == "name");

        vm.InvokeClearPatchPreviewState(clearLoadedPack: true);
        vm.SavePatchOperations.Should().BeEmpty();
        vm.SavePatchCompatibility.Should().BeEmpty();
    }

    [Fact]
    public async Task QuickActionHelpers_ShouldHandleDetachedAndHotkeyCollectionPaths()
    {
        var runtime = new StubRuntimeAdapter
        {
            IsAttached = false,
            CurrentSession = BuildSession(RuntimeMode.Unknown)
        };

        var vm = new SaveOpsHarness(CreateDependencies(
            runtime,
            new StubProfileRepository(BuildProfile("base_swfoc")),
            new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)),
            new StubActionReliabilityService(Array.Empty<ActionReliabilityInfo>()),
            new StubSelectedUnitTransactionService(),
            new StubSpawnPresetService(),
            new StubFreezeService()));

        vm.SelectedProfileId = "base_swfoc";

        await vm.InvokeAddHotkeyAsync();
        vm.Hotkeys.Should().ContainSingle();
        vm.SelectedHotkey = vm.Hotkeys[0];
        await vm.InvokeRemoveHotkeyAsync();
        vm.Hotkeys.Should().BeEmpty();

        var handled = await vm.ExecuteHotkeyAsync("Ctrl+1");
        handled.Should().BeFalse();

        await vm.InvokeQuickRunActionAsync("set_credits", new JsonObject
        {
            ["symbol"] = "credits",
            ["intValue"] = 1000
        });

        vm.Status.Should().Be("Ready");

        await vm.InvokeQuickUnfreezeAllAsync();
        vm.ActiveFreezes.Should().ContainSingle().Which.Should().Be("(none)");
    }

    [Fact]
    public async Task QuickActionHelpers_ShouldExerciseQuickActionAndHotkeySuccessPaths_WhenAttached()
    {
        var runtime = new StubRuntimeAdapter
        {
            IsAttached = true,
            CurrentSession = BuildSession(RuntimeMode.Galactic)
        };

        var vm = new SaveOpsHarness(CreateDependencies(
            runtime,
            new StubProfileRepository(BuildProfile("base_swfoc")),
            new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)),
            new StubActionReliabilityService(Array.Empty<ActionReliabilityInfo>()),
            new StubSelectedUnitTransactionService(),
            new StubSpawnPresetService(),
            new StubFreezeService()));

        vm.SelectedProfileId = "base_swfoc";
        vm.CreditsValue = "1500";
        vm.CreditsFreeze = true;

        await vm.InvokeQuickSetCreditsAsync();
        vm.Status.Should().Contain("Credits");

        await vm.InvokeQuickFreezeTimerAsync();
        await vm.InvokeQuickToggleFogAsync();
        await vm.InvokeQuickToggleAiAsync();
        await vm.InvokeQuickInstantBuildAsync();
        await vm.InvokeQuickUnitCapAsync();
        await vm.InvokeQuickGodModeAsync();
        await vm.InvokeQuickOneHitAsync();

        vm.Hotkeys.Add(new HotkeyBindingItem
        {
            Gesture = "Ctrl+Shift+1",
            ActionId = "set_credits",
            PayloadJson = "{\"symbol\":\"credits\",\"intValue\":2500}"
        });

        var handled = await vm.ExecuteHotkeyAsync("Ctrl+Shift+1");
        handled.Should().BeTrue();
        vm.Status.Should().Contain("Hotkey");
    }

    [Fact]
    public async Task SaveOpsMethods_ShouldLoadEditValidateWriteAndApplyPatchFlow()
    {
        var runtime = new StubRuntimeAdapter
        {
            IsAttached = true,
            CurrentSession = BuildSession(RuntimeMode.Galactic)
        };
        var saveCodec = new StubSaveCodec();
        var patchPackService = new StubSavePatchPackService();
        var patchApplyService = new StubSavePatchApplyService();
        var vm = new SaveOpsHarness(CreateDependencies(
            runtime,
            new StubProfileRepository(BuildProfile("base_swfoc")),
            new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)),
            new StubActionReliabilityService(Array.Empty<ActionReliabilityInfo>()),
            new StubSelectedUnitTransactionService(),
            new StubSpawnPresetService(),
            new StubFreezeService(),
            saveCodec: saveCodec,
            savePatchPackService: patchPackService,
            savePatchApplyService: patchApplyService));

        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-saveops-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        try
        {
            vm.SelectedProfileId = "base_swfoc";
            vm.SavePath = Path.Combine(tempRoot, "campaign.sav");
            vm.SaveNodePath = "root.money";
            vm.SaveEditValue = "999";
            vm.SavePatchPackPath = Path.Combine(tempRoot, "patch.json");

            await vm.InvokeLoadSaveAsync();
            await vm.InvokeEditSaveAsync();
            await vm.InvokeValidateSaveAsync();
            await vm.InvokeWriteSaveAsync();
            await vm.InvokeLoadPatchPackAsync();
            await vm.InvokePreviewPatchPackAsync();
            vm.SavePatchOperations.Should().ContainSingle(x => x.FieldPath == "root.money");
            await vm.InvokeApplyPatchPackAsync();
            await vm.InvokeRestoreBackupAsync();

            saveCodec.LoadCalls.Should().BeGreaterThanOrEqualTo(3);
            saveCodec.LastEditedNodePath.Should().Be("root.money");
            saveCodec.LastEditedValue.Should().Be(999);
            saveCodec.LastWritePath.Should().EndWith(".edited.sav");
            vm.SaveFields.Should().NotBeEmpty();
            vm.SavePatchCompatibility.Should().Contain(x => x.Code == "backup_path");
            vm.SavePatchApplySummary.Should().Contain("backup restored");
            vm.Status.Should().Contain("Backup restored");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CatalogHelperAndUpdateMethods_ShouldPopulateStatusAndArtifacts()
    {
        var runtime = new StubRuntimeAdapter
        {
            IsAttached = true,
            CurrentSession = BuildSession(RuntimeMode.Galactic)
        };
        var helper = new StubHelperModService();
        var updates = new StubProfileUpdateService();
        var vm = new SaveOpsHarness(CreateDependencies(
            runtime,
            new StubProfileRepository(BuildProfile("base_swfoc")),
            new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
            {
                ["entity_catalog"] = ["Unit|STORMTROOPER"],
                ["typed_entity_catalog"] = ["{\"entityId\":\"STORMTROOPER\",\"displayName\":\"Stormtrooper\",\"kind\":\"Unit\"}"]
            }),
            new StubActionReliabilityService(Array.Empty<ActionReliabilityInfo>()),
            new StubSelectedUnitTransactionService(),
            new StubSpawnPresetService(),
            new StubFreezeService(),
            helper: helper,
            updates: updates));

        vm.SelectedProfileId = "base_swfoc";

        await vm.InvokeLoadCatalogAsync();
        await vm.InvokeDeployHelperAsync();
        await vm.InvokeVerifyHelperAsync();
        await vm.InvokeCheckUpdatesAsync();
        await vm.InvokeInstallUpdateAsync();
        await vm.InvokeRollbackProfileUpdateAsync();

        vm.CatalogSummary.Should().Contain(x => x.StartsWith("entity_catalog:"));
        helper.DeployedProfileIds.Should().ContainSingle("base_swfoc");
        helper.VerifiedProfileIds.Should().ContainSingle("base_swfoc");
        vm.Updates.Should().Contain("base_swfoc");
        vm.OpsArtifactSummary.Should().Contain("rollback source:");
        vm.Status.Should().Be("rollback complete");
    }

    [Fact]
    public async Task QuickActionHelpers_ShouldSurfaceValidationFailureAndExecutionErrors()
    {
        var runtime = new StubRuntimeAdapter
        {
            IsAttached = true,
            CurrentSession = BuildSession(RuntimeMode.Galactic)
        };
        var vm = new SaveOpsHarness(CreateDependencies(
            runtime,
            new StubProfileRepository(BuildProfile("base_swfoc")),
            new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)),
            new StubActionReliabilityService(Array.Empty<ActionReliabilityInfo>()),
            new StubSelectedUnitTransactionService(),
            new StubSpawnPresetService(),
            new StubFreezeService()));

        vm.SelectedProfileId = "base_swfoc";
        vm.CreditsValue = "abc";
        await vm.InvokeQuickSetCreditsAsync();
        vm.Status.Should().Contain("Invalid credits value");

        runtime.ExecuteResult = new ActionExecutionResult(false, "boom", AddressSource.Signature);
        await vm.InvokeQuickRunActionAsync("set_credits", new JsonObject
        {
            ["symbol"] = "credits",
            ["intValue"] = 5
        });
        vm.Status.Should().Contain("boom");

        runtime.ExecuteException = new InvalidOperationException("explode");
        await vm.InvokeQuickRunActionAsync("set_credits", new JsonObject
        {
            ["symbol"] = "credits",
            ["intValue"] = 5
        });
        vm.Status.Should().Contain("explode");
    }

    [Fact]
    public async Task ProfileScaffoldAndArtifactFlows_ShouldPopulateSummariesAndStatuses()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"swfoc-app-coverage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        try
        {
            var runtime = new StubRuntimeAdapter
            {
                IsAttached = true,
                CurrentSession = BuildSession(RuntimeMode.Galactic)
            };
            var profile = BuildProfile("base_swfoc");
            var onboarding = new StubModOnboardingService();
            var calibration = new StubModCalibrationService();
            var supportBundles = new StubSupportBundleService();
            var telemetry = new StubTelemetrySnapshotService();

            var vm = new SaveOpsHarness(CreateDependencies(
                runtime,
                new StubProfileRepository(profile),
                new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)),
                new StubActionReliabilityService(Array.Empty<ActionReliabilityInfo>()),
                new StubSelectedUnitTransactionService(),
                new StubSpawnPresetService(),
                new StubFreezeService(),
                modOnboarding: onboarding,
                modCalibration: calibration,
                supportBundles: supportBundles,
                telemetry: telemetry));

            vm.SelectedProfileId = profile.Id;
            vm.OnboardingDraftProfileId = "Custom Draft";
            vm.OnboardingDisplayName = "Custom Draft";
            vm.OnboardingBaseProfileId = string.Empty;
            vm.OnboardingNamespaceRoot = "generated.mods";
            vm.OnboardingLaunchSample = "StarWarsG.exe STEAMMOD=555000111 MODPATH=Mods\\CustomDraft";
            vm.SupportBundleOutputDirectory = outputDir;
            vm.CalibrationNotes = "operator-note";

            await vm.InvokeScaffoldModProfileAsync();
            onboarding.LastRequest.Should().NotBeNull();
            onboarding.LastRequest!.BaseProfileId.Should().Be("base_swfoc");
            vm.OnboardingSummary.Should().Contain("draft=custom_generated");
            vm.OnboardingSummary.Should().Contain("555000111");
            vm.Status.Should().Be("Draft profile scaffolded: custom_generated");

            await vm.InvokeExportCalibrationArtifactAsync();
            calibration.LastArtifactRequest.Should().NotBeNull();
            calibration.LastArtifactRequest!.OutputDirectory.Should().Be(Path.Combine(outputDir, "calibration"));
            calibration.LastArtifactRequest.OperatorNotes.Should().Be("operator-note");
            vm.OpsArtifactSummary.Should().Be(calibration.ArtifactPath);
            vm.Status.Should().Be($"Calibration artifact exported: {calibration.ArtifactPath}");

            await vm.InvokeBuildCompatibilityReportAsync();
            vm.ModCompatibilityRows.Should().ContainSingle()
                .Which.Should().Contain("set_credits | Stable | CAPABILITY_PROBE_PASS");
            vm.ModCompatibilitySummary.Should().Be("promotionReady=True dependency=Pass unresolvedCritical=0");
            vm.Status.Should().Be($"Compatibility report generated for {profile.Id}");

            await vm.InvokeExportSupportBundleAsync();
            supportBundles.LastRequest.Should().NotBeNull();
            supportBundles.LastRequest!.OutputDirectory.Should().Be(outputDir);
            supportBundles.LastRequest.ProfileId.Should().Be(profile.Id);
            vm.OpsArtifactSummary.Should().Be(supportBundles.BundlePath);
            vm.Status.Should().Be($"Support bundle exported: {supportBundles.BundlePath}");

            await vm.InvokeExportTelemetrySnapshotAsync();
            telemetry.LastOutputDirectory.Should().Be(Path.Combine(outputDir, "telemetry"));
            vm.OpsArtifactSummary.Should().Be(telemetry.ExportedPath);
            vm.Status.Should().Be($"Telemetry snapshot exported: {telemetry.ExportedPath}");
        }
        finally
        {
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }
        }
    }


    private static MainViewModelDependencies CreateDependencies(
        StubRuntimeAdapter runtime,
        StubProfileRepository profiles,
        StubCatalogService catalog,
        StubActionReliabilityService reliability,
        StubSelectedUnitTransactionService selectedTransactions,
        StubSpawnPresetService spawnPresets,
        StubFreezeService freezeService,
        StubSaveCodec? saveCodec = null,
        StubSavePatchPackService? savePatchPackService = null,
        StubSavePatchApplyService? savePatchApplyService = null,
        StubHelperModService? helper = null,
        StubProfileUpdateService? updates = null,
        StubModOnboardingService? modOnboarding = null,
        StubModCalibrationService? modCalibration = null,
        StubSupportBundleService? supportBundles = null,
        ITelemetrySnapshotService? telemetry = null)
    {
        telemetry ??= new TelemetrySnapshotService();
        var orchestrator = new TrainerOrchestrator(
            profiles,
            runtime,
            freezeService,
            new StubAuditLogger(),
            telemetry);

        return new MainViewModelDependencies
        {
            Profiles = profiles,
            ProcessLocator = null!,
            LaunchContextResolver = null!,
            ProfileVariantResolver = null!,
            GameLauncher = null!,
            Runtime = runtime,
            Orchestrator = orchestrator,
            Catalog = catalog,
            SaveCodec = saveCodec ?? new StubSaveCodec(),
            SavePatchPackService = savePatchPackService ?? new StubSavePatchPackService(),
            SavePatchApplyService = savePatchApplyService ?? new StubSavePatchApplyService(),
            Helper = helper ?? new StubHelperModService(),
            Updates = updates ?? new StubProfileUpdateService(),
            ModOnboarding = modOnboarding ?? new StubModOnboardingService(),
            ModCalibration = modCalibration ?? new StubModCalibrationService(),
            SupportBundles = supportBundles ?? new StubSupportBundleService(),
            Telemetry = telemetry,
            FreezeService = freezeService,
            ActionReliability = reliability,
            SelectedUnitTransactions = selectedTransactions,
            SpawnPresets = spawnPresets
        };
    }

    private static AttachSession BuildSession(RuntimeMode mode, string resolvedVariant = "base_swfoc")
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["runtimeModeReasonCode"] = "mode_probe_ok",
            ["resolvedVariant"] = resolvedVariant,
            ["resolvedVariantReasonCode"] = "variant_match",
            ["resolvedVariantConfidence"] = "0.99",
            ["dependencyValidation"] = "Pass",
            ["dependencyValidationMessage"] = "ok",
            ["helperBridgeState"] = "ready",
            ["helperBridgeReasonCode"] = "CAPABILITY_PROBE_PASS",
            ["helperBridgeFeatures"] = "spawn_tactical_entity,set_context_allegiance"
        };

        var process = new ProcessMetadata(
            ProcessId: Environment.ProcessId,
            ProcessName: "swfoc.exe",
            ProcessPath: @"C:\Games\swfoc.exe",
            CommandLine: "STEAMMOD=1397421866",
            ExeTarget: ExeTarget.Swfoc,
            Mode: mode,
            Metadata: metadata,
            LaunchContext: new LaunchContext(
                LaunchKind.Workshop,
                CommandLineAvailable: true,
                SteamModIds: ["1397421866"],
                ModPathRaw: null,
                ModPathNormalized: null,
                DetectedVia: "cmdline",
                Recommendation: new ProfileRecommendation("base_swfoc", "workshop_match", 0.99),
                Source: "detected"));

        var symbols = new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)
        {
            ["credits"] = new SymbolInfo("credits", (nint)0x1000, SymbolValueType.Int32, AddressSource.Signature, HealthStatus: SymbolHealthStatus.Healthy),
            ["fog_reveal"] = new SymbolInfo("fog_reveal", nint.Zero, SymbolValueType.Bool, AddressSource.None, HealthStatus: SymbolHealthStatus.Unresolved),
            ["unit_cap"] = new SymbolInfo("unit_cap", (nint)0x2000, SymbolValueType.Int32, AddressSource.Fallback, HealthStatus: SymbolHealthStatus.Degraded)
        };

        return new AttachSession(
            ProfileId: "base_swfoc",
            Process: process,
            Build: new ProfileBuild("base_swfoc", "build", @"C:\Games\swfoc.exe", ExeTarget.Swfoc, ProcessId: Environment.ProcessId),
            Symbols: new SymbolMap(symbols),
            AttachedAt: DateTimeOffset.UtcNow);
    }

    private static TrainerProfile BuildProfile(string id)
    {
        return new TrainerProfile(
            Id: id,
            DisplayName: "Base",
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: "1125571106",
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["set_credits"] = new ActionSpec(
                    Id: "set_credits",
                    Category: ActionCategory.Global,
                    Mode: RuntimeMode.Galactic,
                    ExecutionKind: ExecutionKind.Sdk,
                    PayloadSchema: new JsonObject { ["required"] = new JsonArray("symbol", "intValue") },
                    VerifyReadback: true,
                    CooldownMs: 0),
                ["set_hero_respawn_timer"] = new ActionSpec(
                    Id: "set_hero_respawn_timer",
                    Category: ActionCategory.Hero,
                    Mode: RuntimeMode.Galactic,
                    ExecutionKind: ExecutionKind.Helper,
                    PayloadSchema: new JsonObject(),
                    VerifyReadback: false,
                    CooldownMs: 0)
            },
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["supports_hero_permadeath"] = "true",
                ["supports_hero_rescue"] = "false",
                ["defaultHeroRespawnTime"] = "420",
                ["duplicateHeroPolicy"] = "warn"
            });
    }

    private sealed class SaveOpsHarness : MainViewModelSaveOpsBase
    {
        public SaveOpsHarness(MainViewModelDependencies dependencies)
            : base(dependencies)
        {
            var collections = MainViewModelFactories.CreateCollections();
            Profiles = collections.Profiles;
            Actions = collections.Actions;
            CatalogSummary = collections.CatalogSummary;
            Updates = collections.Updates;
            SaveDiffPreview = collections.SaveDiffPreview;
            Hotkeys = collections.Hotkeys;
            SaveFields = collections.SaveFields;
            FilteredSaveFields = collections.FilteredSaveFields;
            SavePatchOperations = collections.SavePatchOperations;
            SavePatchCompatibility = collections.SavePatchCompatibility;
            ActionReliability = collections.ActionReliability;
            SelectedUnitTransactions = collections.SelectedUnitTransactions;
            SpawnPresets = collections.SpawnPresets;
            EntityRoster = collections.EntityRoster;
            LiveOpsDiagnostics = collections.LiveOpsDiagnostics;
            ModCompatibilityRows = collections.ModCompatibilityRows;
            ActiveFreezes = collections.ActiveFreezes;
            _freezeUiTimer = new System.Windows.Threading.DispatcherTimer();
            Status = "Ready";
        }

        protected override void ApplyPayloadTemplateForSelectedAction() { }

        protected override Task<bool> EnsureActionAvailableForCurrentSessionAsync(string actionId, string statusPrefix)
        {
            _ = actionId;
            _ = statusPrefix;
            return Task.FromResult(true);
        }

        public Task InvokeRefreshActionReliabilityAsync() => RefreshActionReliabilityAsync();
        public Task InvokeLoadSpawnPresetsAsync() => LoadSpawnPresetsAsync();
        public Task InvokeRunSpawnBatchAsync() => RunSpawnBatchAsync();
        public Task InvokeCaptureSelectedUnitBaselineAsync() => CaptureSelectedUnitBaselineAsync();
        public Task InvokeApplySelectedUnitDraftAsync() => ApplySelectedUnitDraftAsync();
        public Task InvokeRevertSelectedUnitTransactionAsync() => RevertSelectedUnitTransactionAsync();
        public Task InvokeRestoreSelectedUnitBaselineAsync() => RestoreSelectedUnitBaselineAsync();
        public string? InvokeValidateSaveRuntimeVariant(string id) => ValidateSaveRuntimeVariant(id);
        public bool InvokePreparePatchPreview(string id) => PreparePatchPreview(id);
        public void InvokePopulatePatchPreviewOperations(SavePatchPreview preview) => PopulatePatchPreviewOperations(preview);
        public void InvokePopulatePatchCompatibilityRows(SavePatchCompatibilityResult compatibility, SavePatchPreview preview) => PopulatePatchCompatibilityRows(compatibility, preview);
        public void InvokeAppendPatchArtifactRows(string? backupPath, string? receiptPath) => AppendPatchArtifactRows(backupPath, receiptPath);
        public void SetLoadedSaveForCoverage(SaveDocument save, byte[] original)
        {
            _loadedSave = save;
            _loadedSaveOriginal = original;
        }

        public Task InvokeLoadSaveAsync() => LoadSaveAsync();
        public Task InvokeEditSaveAsync() => EditSaveAsync();
        public Task InvokeValidateSaveAsync() => ValidateSaveAsync();
        public Task InvokeWriteSaveAsync() => WriteSaveAsync();
        public Task InvokeLoadPatchPackAsync() => LoadPatchPackAsync();
        public Task InvokePreviewPatchPackAsync() => PreviewPatchPackAsync();
        public Task InvokeApplyPatchPackAsync() => ApplyPatchPackAsync();
        public Task InvokeRestoreBackupAsync() => RestoreBackupAsync();
        public Task InvokeLoadCatalogAsync() => LoadCatalogAsync();
        public Task InvokeDeployHelperAsync() => DeployHelperAsync();
        public Task InvokeVerifyHelperAsync() => VerifyHelperAsync();
        public Task InvokeCheckUpdatesAsync() => CheckUpdatesAsync();
        public Task InvokeInstallUpdateAsync() => InstallUpdateAsync();
        public Task InvokeRollbackProfileUpdateAsync() => RollbackProfileUpdateAsync();
        public Task InvokeScaffoldModProfileAsync() => ScaffoldModProfileAsync();
        public Task InvokeExportCalibrationArtifactAsync() => ExportCalibrationArtifactAsync();
        public Task InvokeBuildCompatibilityReportAsync() => BuildCompatibilityReportAsync();
        public Task InvokeExportSupportBundleAsync() => ExportSupportBundleAsync();
        public Task InvokeExportTelemetrySnapshotAsync() => ExportTelemetrySnapshotAsync();
        public void InvokeRebuildSaveFieldRows() => RebuildSaveFieldRows();
        public void InvokeClearPatchPreviewState(bool clearLoadedPack) => ClearPatchPreviewState(clearLoadedPack);
        public Task InvokeAddHotkeyAsync() => AddHotkeyAsync();
        public Task InvokeRemoveHotkeyAsync() => RemoveHotkeyAsync();
        public Task InvokeQuickRunActionAsync(string actionId, JsonObject payload) => QuickRunActionAsync(actionId, payload);
        public Task InvokeQuickSetCreditsAsync() => QuickSetCreditsAsync();
        public Task InvokeQuickFreezeTimerAsync() => QuickFreezeTimerAsync();
        public Task InvokeQuickToggleFogAsync() => QuickToggleFogAsync();
        public Task InvokeQuickToggleAiAsync() => QuickToggleAiAsync();
        public Task InvokeQuickInstantBuildAsync() => QuickInstantBuildAsync();
        public Task InvokeQuickUnitCapAsync() => QuickUnitCapAsync();
        public Task InvokeQuickGodModeAsync() => QuickGodModeAsync();
        public Task InvokeQuickOneHitAsync() => QuickOneHitAsync();
        public Task InvokeQuickUnfreezeAllAsync() => QuickUnfreezeAllAsync();
    }

    private sealed class StubRuntimeAdapter : IRuntimeAdapter
    {
        public bool IsAttached { get; set; }
        public AttachSession? CurrentSession { get; set; }
        public ActionExecutionResult ExecuteResult { get; set; } = new(true, "ok", AddressSource.Signature);
        public Exception? ExecuteException { get; set; }

        public Task<AttachSession> AttachAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(CurrentSession!);
        }

        public Task<T> ReadAsync<T>(string symbol, CancellationToken cancellationToken) where T : unmanaged
        {
            _ = symbol;
            _ = cancellationToken;
            return Task.FromResult(default(T));
        }

        public Task WriteAsync<T>(string symbol, T value, CancellationToken cancellationToken) where T : unmanaged
        {
            _ = symbol;
            _ = value;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<ActionExecutionResult> ExecuteAsync(ActionExecutionRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            if (ExecuteException is not null)
            {
                throw ExecuteException;
            }

            return Task.FromResult(ExecuteResult);
        }

        public Task DetachAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            IsAttached = false;
            return Task.CompletedTask;
        }
    }

    private sealed class StubFreezeService : IValueFreezeService
    {
        private readonly HashSet<string> _symbols = new(StringComparer.OrdinalIgnoreCase);

        public void FreezeInt(string symbol, int value)
        {
            _ = value;
            _symbols.Add(symbol);
        }

        public void FreezeIntAggressive(string symbol, int value)
        {
            _ = value;
            _symbols.Add(symbol);
        }

        public void FreezeFloat(string symbol, float value)
        {
            _ = value;
            _symbols.Add(symbol);
        }

        public void FreezeBool(string symbol, bool value)
        {
            _ = value;
            _symbols.Add(symbol);
        }

        public bool Unfreeze(string symbol) => _symbols.Remove(symbol);
        public void UnfreezeAll() => _symbols.Clear();
        public bool IsFrozen(string symbol) => _symbols.Contains(symbol);
        public IReadOnlyCollection<string> GetFrozenSymbols() => _symbols.ToArray();
        public void Dispose() { }
    }

    private sealed class StubAuditLogger : IAuditLogger
    {
        public Task WriteAsync(ActionAuditRecord record, CancellationToken cancellationToken)
        {
            _ = record;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }


    private sealed class StubProfileRepository : IProfileRepository
    {
        private readonly TrainerProfile _profile;

        public StubProfileRepository(TrainerProfile profile)
        {
            _profile = profile;
        }

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            throw new NotImplementedException();
        }

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_profile);
        }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_profile);
        }

        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken)
        {
            _ = profile;
            _ = cancellationToken;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult<IReadOnlyList<string>>(new[] { _profile.Id });
        }
    }

    private sealed class StubCatalogService : ICatalogService
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _catalog;

        public StubCatalogService(IReadOnlyDictionary<string, IReadOnlyList<string>> catalog)
        {
            _catalog = catalog;
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_catalog);
        }
    }

    private sealed class StubSaveCodec : ISaveCodec
    {
        private readonly SaveDocument _saveDocument = new(
            "campaign.sav",
            "schema",
            new byte[] { 1, 2, 3, 4 },
            new SaveNode(
                "root",
                "root",
                "root",
                null,
                [
                    new SaveNode("root.money", "money", "int", 100),
                    new SaveNode("root.name", "name", "string", "Thrawn")
                ]));

        public int LoadCalls { get; private set; }
        public string? LastEditedNodePath { get; private set; }
        public object? LastEditedValue { get; private set; }
        public string? LastWritePath { get; private set; }

        public Task<SaveDocument> LoadAsync(string path, string schemaId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LoadCalls++;
            return Task.FromResult(_saveDocument with { Path = path, SchemaId = schemaId, Raw = _saveDocument.Raw.ToArray() });
        }

        public Task EditAsync(SaveDocument document, string nodePath, object? value, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastEditedNodePath = nodePath;
            LastEditedValue = value;
            document.Raw[0] = 9;
            return Task.CompletedTask;
        }

        public Task<SaveValidationResult> ValidateAsync(SaveDocument document, CancellationToken cancellationToken)
        {
            _ = document;
            _ = cancellationToken;
            return Task.FromResult(new SaveValidationResult(true, Array.Empty<string>(), ["warn"]));
        }

        public Task WriteAsync(SaveDocument document, string outputPath, CancellationToken cancellationToken)
        {
            _ = document;
            _ = cancellationToken;
            LastWritePath = outputPath;
            return Task.CompletedTask;
        }

        public Task<bool> RoundTripCheckAsync(SaveDocument document, CancellationToken cancellationToken)
        {
            _ = document;
            _ = cancellationToken;
            return Task.FromResult(true);
        }
    }

    private sealed class StubSavePatchPackService : ISavePatchPackService
    {
        private readonly SavePatchPack _pack = new(
            new SavePatchMetadata("1.0", "base_swfoc", "schema", "hash", DateTimeOffset.UtcNow),
            new SavePatchCompatibility(["base_swfoc"], "schema"),
            [
                new SavePatchOperation(SavePatchOperationKind.SetValue, "root.money", "money", "int", 100, 999, 8)
            ]);

        public Task<SavePatchPack> ExportAsync(SaveDocument originalDoc, SaveDocument editedDoc, string profileId, CancellationToken cancellationToken)
        {
            _ = originalDoc;
            _ = editedDoc;
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_pack);
        }

        public Task<SavePatchPack> LoadPackAsync(string path, CancellationToken cancellationToken)
        {
            _ = path;
            _ = cancellationToken;
            return Task.FromResult(_pack);
        }

        public Task<SavePatchCompatibilityResult> ValidateCompatibilityAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId, CancellationToken cancellationToken)
        {
            _ = pack;
            _ = targetDoc;
            _ = targetProfileId;
            _ = cancellationToken;
            return Task.FromResult(new SavePatchCompatibilityResult(true, true, "hash", Array.Empty<string>(), ["warn"]));
        }

        public Task<SavePatchPreview> PreviewApplyAsync(SavePatchPack pack, SaveDocument targetDoc, string targetProfileId, CancellationToken cancellationToken)
        {
            _ = pack;
            _ = targetDoc;
            _ = targetProfileId;
            _ = cancellationToken;
            return Task.FromResult(new SavePatchPreview(true, Array.Empty<string>(), ["preview warn"], _pack.Operations));
        }
    }

    private sealed class StubSavePatchApplyService : ISavePatchApplyService
    {
        public Task<SavePatchApplyResult> ApplyAsync(string targetSavePath, SavePatchPack pack, string targetProfileId, bool strict, CancellationToken cancellationToken)
        {
            _ = targetSavePath;
            _ = pack;
            _ = targetProfileId;
            _ = strict;
            _ = cancellationToken;
            return Task.FromResult(new SavePatchApplyResult(
                SavePatchApplyClassification.Applied,
                Applied: true,
                Message: "applied",
                OutputPath: "campaign.edited.sav",
                BackupPath: "campaign.backup.sav",
                ReceiptPath: "campaign.receipt.json"));
        }

        public Task<SaveRollbackResult> RestoreLastBackupAsync(string targetSavePath, CancellationToken cancellationToken)
        {
            _ = targetSavePath;
            _ = cancellationToken;
            return Task.FromResult(new SaveRollbackResult(
                Restored: true,
                Message: "backup restored",
                TargetPath: targetSavePath,
                BackupPath: "campaign.backup.sav"));
        }
    }

    private sealed class StubHelperModService : IHelperModService
    {
        public List<string> DeployedProfileIds { get; } = [];
        public List<string> VerifiedProfileIds { get; } = [];

        public Task<string> DeployAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            DeployedProfileIds.Add(profileId);
            return Task.FromResult($@"C:\Helpers\{profileId}");
        }

        public Task<bool> VerifyAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            VerifiedProfileIds.Add(profileId);
            return Task.FromResult(true);
        }
    }

    private sealed class StubProfileUpdateService : IProfileUpdateService
    {
        public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult<IReadOnlyList<string>>(["base_swfoc"]);
        }

        public Task<string> InstallProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(@"C:\Profiles\base_swfoc");
        }

        public Task<ProfileInstallResult> InstallProfileTransactionalAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(new ProfileInstallResult(
                Succeeded: true,
                ProfileId: profileId,
                InstalledPath: $@"C:\Profiles\{profileId}",
                BackupPath: $@"C:\Profiles\{profileId}.bak",
                ReceiptPath: $@"C:\Profiles\{profileId}.receipt.json",
                Message: "installed"));
        }

        public Task<ProfileRollbackResult> RollbackLastInstallAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(new ProfileRollbackResult(
                Restored: true,
                ProfileId: profileId,
                RestoredPath: $@"C:\Profiles\{profileId}",
                BackupPath: $@"C:\Profiles\{profileId}.bak",
                Message: "rollback complete"));
        }
    }

    private sealed class StubModOnboardingService : IModOnboardingService
    {
        public ModOnboardingRequest? LastRequest { get; private set; }

        public Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastRequest = request;
            return Task.FromResult(new ModOnboardingResult(
                Succeeded: true,
                ProfileId: "custom_generated",
                OutputPath: @"C:\Temp\custom_generated.json",
                InferredWorkshopIds: ["555000111"],
                InferredPathHints: ["customdraft"],
                InferredAliases: ["custom_generated"],
                Warnings: ["seed warning"]));
        }

        public Task<ModOnboardingBatchResult> ScaffoldDraftProfilesFromSeedsAsync(ModOnboardingSeedBatchRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new ModOnboardingBatchResult(true, 0, 0, 0, Array.Empty<ModOnboardingBatchItemResult>()));
        }
    }

    private sealed class StubModCalibrationService : IModCalibrationService
    {
        public ModCalibrationArtifactRequest? LastArtifactRequest { get; private set; }
        public string ArtifactPath { get; } = @"C:\Temp\calibration.json";

        public Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(ModCalibrationArtifactRequest request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastArtifactRequest = request;
            return Task.FromResult(new ModCalibrationArtifactResult(
                Succeeded: true,
                ArtifactPath: ArtifactPath,
                ModuleFingerprint: "fingerprint",
                Candidates: Array.Empty<CalibrationCandidate>(),
                Warnings: Array.Empty<string>()));
        }

        public Task<ModCompatibilityReport> BuildCompatibilityReportAsync(
            TrainerProfile profile,
            AttachSession? session,
            DependencyValidationResult? dependencyValidation,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog,
            CancellationToken cancellationToken)
        {
            _ = profile;
            _ = session;
            _ = dependencyValidation;
            _ = catalog;
            _ = cancellationToken;
            return Task.FromResult(new ModCompatibilityReport(
                ProfileId: "base_swfoc",
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                RuntimeMode: RuntimeMode.Galactic,
                DependencyStatus: DependencyValidationStatus.Pass,
                UnresolvedCriticalSymbols: 0,
                PromotionReady: true,
                Actions: [new ModActionCompatibility("set_credits", ActionReliabilityState.Stable, "CAPABILITY_PROBE_PASS", 1.0)],
                Notes: Array.Empty<string>()));
        }
    }

    private sealed class StubSupportBundleService : ISupportBundleService
    {
        public SupportBundleRequest? LastRequest { get; private set; }
        public string BundlePath { get; } = @"C:\Temp\support-bundle.zip";

        public Task<SupportBundleResult> ExportAsync(SupportBundleRequest request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastRequest = request;
            return Task.FromResult(new SupportBundleResult(
                Succeeded: true,
                BundlePath: BundlePath,
                ManifestPath: @"C:\Temp\support-bundle.manifest.json",
                IncludedFiles: ["runtime-snapshot.json"],
                Warnings: Array.Empty<string>()));
        }
    }

    private sealed class StubTelemetrySnapshotService : ITelemetrySnapshotService
    {
        public string? LastOutputDirectory { get; private set; }
        public string ExportedPath { get; private set; } = string.Empty;

        public void RecordAction(string actionId, AddressSource source, bool succeeded)
        {
            _ = actionId;
            _ = source;
            _ = succeeded;
        }

        public TelemetrySnapshot CreateSnapshot()
        {
            return new TelemetrySnapshot(
                DateTimeOffset.UtcNow,
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase),
                TotalActions: 0,
                FailureRate: 0,
                FallbackRate: 0,
                UnresolvedRate: 0);
        }

        public Task<string> ExportSnapshotAsync(string outputDirectory, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            Directory.CreateDirectory(outputDirectory);
            LastOutputDirectory = outputDirectory;
            ExportedPath = Path.Combine(outputDirectory, "telemetry.json");
            File.WriteAllText(ExportedPath, "{}");
            return Task.FromResult(ExportedPath);
        }

        public void Reset()
        {
        }
    }

    private sealed class StubActionReliabilityService : IActionReliabilityService
    {
        private readonly IReadOnlyList<ActionReliabilityInfo> _items;

        public StubActionReliabilityService(IReadOnlyList<ActionReliabilityInfo> items)
        {
            _items = items;
        }

        public IReadOnlyList<ActionReliabilityInfo> Evaluate(
            TrainerProfile profile,
            AttachSession session,
            IReadOnlyDictionary<string, IReadOnlyList<string>>? catalog)
        {
            _ = profile;
            _ = session;
            _ = catalog;
            return _items;
        }
    }

    private sealed class StubSelectedUnitTransactionService : ISelectedUnitTransactionService
    {
        private readonly List<SelectedUnitTransactionRecord> _history =
        [
            new SelectedUnitTransactionRecord(
                TransactionId: "tx-1",
                Timestamp: DateTimeOffset.UtcNow,
                Before: new SelectedUnitSnapshot(100, 50, 1.5f, 1.0f, 1.0f, 1, 1, DateTimeOffset.UtcNow),
                After: new SelectedUnitSnapshot(120, 60, 2.0f, 1.1f, 0.9f, 2, 2, DateTimeOffset.UtcNow),
                IsRollback: false,
                Message: "applied",
                AppliedActions: ["set_selected_hp"])
        ];

        public SelectedUnitSnapshot? Baseline => new(100, 50, 1.5f, 1.0f, 1.0f, 1, 1, DateTimeOffset.UtcNow);

        public IReadOnlyList<SelectedUnitTransactionRecord> History => _history;

        public Task<SelectedUnitSnapshot> CaptureAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(new SelectedUnitSnapshot(100, 50, 1.5f, 2.0f, 0.5f, 3, 2, DateTimeOffset.UtcNow));
        }

        public Task<SelectedUnitTransactionResult> ApplyAsync(string profileId, SelectedUnitDraft draft, RuntimeMode runtimeMode, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = draft;
            _ = runtimeMode;
            _ = cancellationToken;
            return Task.FromResult(new SelectedUnitTransactionResult(true, "applied", "tx-apply", Array.Empty<ActionExecutionResult>()));
        }

        public Task<SelectedUnitTransactionResult> RevertLastAsync(string profileId, RuntimeMode runtimeMode, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = runtimeMode;
            _ = cancellationToken;
            return Task.FromResult(new SelectedUnitTransactionResult(true, "reverted", "tx-revert", Array.Empty<ActionExecutionResult>()));
        }

        public Task<SelectedUnitTransactionResult> RestoreBaselineAsync(string profileId, RuntimeMode runtimeMode, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = runtimeMode;
            _ = cancellationToken;
            return Task.FromResult(new SelectedUnitTransactionResult(true, "restored", "tx-restore", Array.Empty<ActionExecutionResult>()));
        }
    }

    private sealed class StubSpawnPresetService : ISpawnPresetService
    {
        public SpawnBatchExecutionResult? LastExecuteResult { get; private set; }

        public Task<IReadOnlyList<SpawnPreset>> LoadPresetsAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult<IReadOnlyList<SpawnPreset>>(
            [
                new SpawnPreset("preset_1", "Storm Squad", "STORMTROOPER", "EMPIRE", "AUTO", 1, 100, "desc")
            ]);
        }

        public SpawnBatchPlan BuildBatchPlan(string profileId, SpawnPreset preset, int quantity, int delayMs, string? factionOverride, string? entryMarkerOverride, bool stopOnFailure)
        {
            var items = Enumerable.Range(1, quantity)
                .Select(i => new SpawnBatchItem(
                    Sequence: i,
                    UnitId: preset.UnitId,
                    Faction: factionOverride ?? preset.Faction,
                    EntryMarker: entryMarkerOverride ?? preset.EntryMarker,
                    DelayMs: delayMs))
                .ToArray();

            return new SpawnBatchPlan(profileId, preset.Id, stopOnFailure, items);
        }

        public Task<SpawnBatchExecutionResult> ExecuteBatchAsync(string profileId, SpawnBatchPlan plan, RuntimeMode runtimeMode, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = runtimeMode;
            _ = cancellationToken;

            LastExecuteResult = new SpawnBatchExecutionResult(
                Succeeded: true,
                Message: "batch ok",
                Attempted: plan.Items.Count,
                SucceededCount: plan.Items.Count,
                FailedCount: 0,
                StoppedEarly: false,
                Results: plan.Items.Select(i => new SpawnBatchItemResult(i.Sequence, i.UnitId, true, "ok")).ToArray());

            return Task.FromResult(LastExecuteResult);
        }
    }
}
