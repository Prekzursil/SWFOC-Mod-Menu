using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Saves.Services;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelArtifactCoverageTests
{
    [Fact]
    public async Task ArtifactOperations_ShouldCoverFailureBranches_AndDraftFallbacks()
    {
        var outputDir = Path.Combine(Path.GetTempPath(), $"artifact-coverage-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputDir);

        try
        {
            var profile = BuildProfile(
                id: "draft_profile",
                actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
                metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
            var runtime = new StubRuntimeAdapter { CurrentSession = BuildSession(RuntimeMode.Galactic) };
            var onboarding = new StubModOnboardingService(new ModOnboardingResult(
                Succeeded: true,
                ProfileId: "draft_profile",
                OutputPath: @"C:\Temp\draft_profile.json",
                InferredWorkshopIds: Array.Empty<string>(),
                InferredPathHints: Array.Empty<string>(),
                InferredAliases: ["draft_profile"],
                Warnings: Array.Empty<string>()));
            var calibration = new StubModCalibrationService(
                artifactResult: new ModCalibrationArtifactResult(
                    Succeeded: false,
                    ArtifactPath: @"C:\Temp\artifact.json",
                    ModuleFingerprint: "fp",
                    Candidates: Array.Empty<CalibrationCandidate>(),
                    Warnings: Array.Empty<string>()),
                compatibilityReport: new ModCompatibilityReport(
                    ProfileId: profile.Id,
                    GeneratedAtUtc: DateTimeOffset.UtcNow,
                    RuntimeMode: RuntimeMode.Galactic,
                    DependencyStatus: DependencyValidationStatus.HardFail,
                    UnresolvedCriticalSymbols: 2,
                    PromotionReady: false,
                    Actions: Array.Empty<ModActionCompatibility>(),
                    Notes: ["blocked"]));
            var supportBundles = new StubSupportBundleService(new SupportBundleResult(
                Succeeded: false,
                BundlePath: @"C:\Temp\bundle.zip",
                ManifestPath: @"C:\Temp\bundle.manifest.json",
                IncludedFiles: Array.Empty<string>(),
                Warnings: ["bundle warning"]));
            var telemetry = new StubTelemetrySnapshotService();

            var vm = new CoverageHarness(CreateDependencies(
                runtime,
                profiles: new StubProfileRepository(profile),
                modOnboarding: onboarding,
                modCalibration: calibration,
                supportBundles: supportBundles,
                telemetry: telemetry));

            vm.SelectedProfileId = null;
            vm.OnboardingDraftProfileId = "draft_profile";
            vm.OnboardingDisplayName = "Draft Profile";
            vm.OnboardingBaseProfileId = "custom_base";
            vm.OnboardingNamespaceRoot = "generated.mods";
            vm.OnboardingLaunchSample = string.Join(Environment.NewLine + Environment.NewLine, "Steam.exe STEAMMOD=1", "Launcher.exe STEAMMOD=2");
            vm.CalibrationNotes = "note-a";
            vm.SupportBundleOutputDirectory = outputDir;

            await vm.InvokeScaffoldModProfileAsync();
            onboarding.LastRequest.Should().NotBeNull();
            onboarding.LastRequest!.BaseProfileId.Should().Be("custom_base");
            onboarding.LastRequest.LaunchSamples.Should().HaveCount(2);
            vm.OnboardingSummary.Should().Contain("warnings=none");
            vm.Status.Should().Be("Draft profile scaffolded: draft_profile");

            await vm.InvokeExportCalibrationArtifactAsync();
            calibration.LastArtifactRequest.Should().NotBeNull();
            calibration.LastArtifactRequest!.ProfileId.Should().Be("draft_profile");
            calibration.LastArtifactRequest.OutputDirectory.Should().Be(Path.Combine(outputDir, "calibration"));
            vm.OpsArtifactSummary.Should().Be(@"C:\Temp\artifact.json");
            vm.Status.Should().Be("Calibration artifact export failed.");

            await vm.InvokeBuildCompatibilityReportAsync();
            vm.ModCompatibilityRows.Should().BeEmpty();
            vm.ModCompatibilitySummary.Should().Be("promotionReady=False dependency=HardFail unresolvedCritical=2");
            vm.Status.Should().Be("Compatibility report generated for draft_profile");

            await vm.InvokeExportSupportBundleAsync();
            supportBundles.LastRequest.Should().NotBeNull();
            supportBundles.LastRequest!.OutputDirectory.Should().Be(outputDir);
            supportBundles.LastRequest.ProfileId.Should().BeNull();
            vm.OpsArtifactSummary.Should().Be(@"C:\Temp\bundle.zip");
            vm.Status.Should().Be("Support bundle export failed.");

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

    [Fact]
    public void RefreshLiveOpsDiagnostics_ShouldResetSurfaceWithoutSession_AndNormalizeMetadataValues()
    {
        var runtime = new StubRuntimeAdapter { CurrentSession = null };
        var vm = new CoverageHarness(CreateDependencies(runtime));

        vm.InvokeRefreshLiveOpsDiagnostics();

        vm.LiveOpsDiagnostics.Should().BeEmpty();
        vm.AttachState.Should().Be("detached");
        vm.RuntimeVariantSummary.Should().Be("unknown");
        vm.HelperBridgeState.Should().Be("unknown");
        vm.HelperBridgeFeatures.Should().Be("none");
        vm.HelperBridgeExecutionPath.Should().Be("unknown");
        vm.HelperBridgeBlockingReason.Should().Be("unknown");
        vm.HelperAutoloadState.Should().Be("unknown");
        vm.HelperLastOperationToken.Should().Be("unknown");

        runtime.CurrentSession = BuildSession(RuntimeMode.Galactic, metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["runtimeModeReasonCode"] = "mode_probe_ok",
            ["resolvedVariant"] = "base_swfoc",
            ["resolvedVariantReasonCode"] = "variant_match",
            ["resolvedVariantConfidence"] = "0.90",
            ["dependencyValidation"] = "Pass",
            ["dependencyValidationMessage"] = "ready",
            ["helperBridgeState"] = " ready ",
            ["helperBridgeReasonCode"] = " CAPABILITY_PROBE_PASS ",
            ["helperBridgeFeatures"] = " spawn_tactical_entity ; ; place_planet_building, set_context_allegiance ",
            ["helperExecutionPath"] = " native_dispatch_unavailable ",
            ["helperBridgeBlockingReason"] = " native_dispatch_unavailable ",
            ["helperAutoloadState"] = " pending_story_mode_load ",
            ["helperAutoloadReasonCode"] = " story_wrapper_waiting_for_story_load ",
            ["helperAutoloadStrategy"] = " story_wrapper_chain ",
            ["helperAutoloadScript"] = " Library/PGStoryMode.lua ",
            ["helperLastOperationToken"] = " token-ops-001 ",
            ["helperLastOperationKind"] = " SpawnTacticalEntity ",
            ["helperLastVerifyState"] = " applied ",
            ["helperLastEntryPoint"] = " SWFOC_Trainer_Spawn_Context ",
            ["helperLastAppliedEntityId"] = " EMP_STORM_SQUAD "
        });

        vm.InvokeRefreshLiveOpsDiagnostics();

        vm.AttachState.Should().Be("attached");
        vm.AttachedProcessSummary.Should().StartWith("swfoc.exe:");
        vm.RuntimeResolvedVariant.Should().Be("base_swfoc");
        vm.RuntimeResolvedVariantReasonCode.Should().Be("variant_match");
        vm.RuntimeResolvedVariantConfidence.Should().Be("0.90");
        vm.RuntimeVariantSummary.Should().Be("base_swfoc (variant_match, conf=0.90)");
        vm.HelperBridgeState.Should().Be("ready");
        vm.HelperBridgeReasonCode.Should().Be("CAPABILITY_PROBE_PASS");
        vm.HelperBridgeFeatures.Should().Be("spawn_tactical_entity, place_planet_building, set_context_allegiance");
        vm.HelperBridgeExecutionPath.Should().Be("native_dispatch_unavailable");
        vm.HelperBridgeBlockingReason.Should().Be("native_dispatch_unavailable");
        vm.HelperBridgeBlockSummary.Should().Be("native_dispatch_unavailable");
        vm.HelperAutoloadState.Should().Be("pending_story_mode_load");
        vm.HelperAutoloadReasonCode.Should().Be("story_wrapper_waiting_for_story_load");
        vm.HelperAutoloadStrategy.Should().Be("story_wrapper_chain");
        vm.HelperAutoloadScript.Should().Be("Library/PGStoryMode.lua");
        vm.HelperLastOperationToken.Should().Be("token-ops-001");
        vm.HelperLastOperationKind.Should().Be("SpawnTacticalEntity");
        vm.HelperLastVerifyState.Should().Be("applied");
        vm.HelperLastEntryPoint.Should().Be("SWFOC_Trainer_Spawn_Context");
        vm.HelperLastAppliedEntityId.Should().Be("EMP_STORM_SQUAD");
        vm.LiveOpsDiagnostics.Should().Contain(x => x.StartsWith("mode:"));
        vm.LiveOpsDiagnostics.Should().Contain(x => x.StartsWith("attach: attached (swfoc.exe:"));
        vm.LiveOpsDiagnostics.Should().Contain(x => x.StartsWith("launch:"));
        vm.LiveOpsDiagnostics.Should().Contain("helper_features: spawn_tactical_entity, place_planet_building, set_context_allegiance");
        vm.LiveOpsDiagnostics.Should().Contain("helper_execution_path: native_dispatch_unavailable");
        vm.LiveOpsDiagnostics.Should().Contain("helper_blocking_reason: native_dispatch_unavailable");
        vm.LiveOpsDiagnostics.Should().Contain("helper_autoload: pending_story_mode_load (story_wrapper_waiting_for_story_load)");
        vm.LiveOpsDiagnostics.Should().Contain("helper_autoload_target: story_wrapper_chain -> Library/PGStoryMode.lua");
        vm.LiveOpsDiagnostics.Should().Contain(x => x.Contains("dependency:"));
    }

    [Fact]
    public async Task LiveOpsRefreshers_ShouldHandleCatalogFailure_MissingProfiles_AndHeroFallbacks()
    {
        var profile = BuildProfile(
            id: "base_swfoc",
            actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["supports_hero_permadeath"] = "   ",
                ["supports_hero_rescue"] = "1",
                ["hero_respawn_time"] = " 900 ",
                ["duplicate_hero_policy"] = " clone_warn "
            });

        var runtime = new StubRuntimeAdapter { CurrentSession = BuildSession(RuntimeMode.TacticalLand) };
        var vm = new CoverageHarness(CreateDependencies(
            runtime,
            profiles: new StubProfileRepository(profile),
            catalog: new ThrowingCatalogService(),
            reliability: new StubActionReliabilityService([
                new ActionReliabilityInfo("spawn_tactical_entity", ActionReliabilityState.Experimental, "HELPER_VERIFY_PENDING", 0.55, "pending")
            ])));

        vm.SelectedProfileId = profile.Id;
        await vm.InvokeRefreshActionReliabilityAsync();

        vm.ActionReliability.Should().ContainSingle(x => x.ActionId == "spawn_tactical_entity");
        vm.EntityRoster.Should().BeEmpty();
        vm.HeroSupportsRespawn.Should().Be("false");
        vm.HeroSupportsPermadeath.Should().Be("false");
        vm.HeroSupportsRescue.Should().Be("true");
        vm.HeroDefaultRespawnTime.Should().Be("900");
        vm.HeroDuplicatePolicy.Should().Be("clone_warn");

        var missingProfileVm = new CoverageHarness(CreateDependencies(
            runtime,
            profiles: new StubProfileRepository(null),
            catalog: new StaticCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase))));

        missingProfileVm.SelectedProfileId = "missing_profile";
        await missingProfileVm.InvokeRefreshActionReliabilityAsync();

        missingProfileVm.EntityRoster.Should().BeEmpty();
        missingProfileVm.HeroSupportsRespawn.Should().Be("false");
        missingProfileVm.HeroDefaultRespawnTime.Should().Be("unknown");

        var spawnVm = new CoverageHarness(CreateDependencies(
            runtime,
            profiles: null,
            catalog: new StaticCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)),
            spawnPresets: new StubSpawnPresetService([
                new SpawnPreset("preset-1", "Storm Squad", "EMP_STORM_SQUAD", "EMPIRE", "AUTO")
            ])));

        spawnVm.SelectedProfileId = profile.Id;
        await spawnVm.InvokeLoadSpawnPresetsAsync();

        spawnVm.SpawnPresets.Should().ContainSingle(x => x.Id == "preset-1");
        spawnVm.EntityRoster.Should().BeEmpty();
        spawnVm.HeroSupportsRespawn.Should().Be("false");
        spawnVm.HeroSupportsPermadeath.Should().Be("false");
        spawnVm.HeroSupportsRescue.Should().Be("false");
        spawnVm.HeroDefaultRespawnTime.Should().Be("unknown");
        spawnVm.HeroDuplicatePolicy.Should().Be("unknown");
        spawnVm.Status.Should().Be("Loaded 1 spawn preset(s); roster=0.");
    }

    private static TrainerProfile BuildProfile(
        string id,
        IReadOnlyDictionary<string, ActionSpec> actions,
        IReadOnlyDictionary<string, string> metadata)
    {
        return new TrainerProfile(
            Id: id,
            DisplayName: id,
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: actions,
            FeatureFlags: new Dictionary<string, bool>(),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata);
    }

    private static AttachSession BuildSession(RuntimeMode mode, IReadOnlyDictionary<string, string>? metadata = null)
    {
        metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["runtimeModeReasonCode"] = "mode_probe_ok",
            ["resolvedVariant"] = "base_swfoc",
            ["resolvedVariantReasonCode"] = "variant_match",
            ["resolvedVariantConfidence"] = "0.99",
            ["dependencyValidation"] = "Pass",
            ["dependencyValidationMessage"] = "ok",
            ["helperBridgeState"] = "ready",
            ["helperBridgeReasonCode"] = "CAPABILITY_PROBE_PASS",
            ["helperBridgeFeatures"] = "spawn_tactical_entity"
        };

        return new AttachSession(
            ProfileId: "base_swfoc",
            Process: new ProcessMetadata(
                ProcessId: Environment.ProcessId,
                ProcessName: "swfoc.exe",
                ProcessPath: @"C:\Games\swfoc.exe",
                CommandLine: "STEAMMOD=1125571106",
                ExeTarget: ExeTarget.Swfoc,
                Mode: mode,
                Metadata: metadata,
                LaunchContext: new LaunchContext(
                    LaunchKind.Workshop,
                    CommandLineAvailable: true,
                    SteamModIds: ["1125571106"],
                    ModPathRaw: null,
                    ModPathNormalized: null,
                    DetectedVia: "command_line",
                    Recommendation: new ProfileRecommendation("base_swfoc", "variant_match", 0.99))),
            Build: new ProfileBuild("base_swfoc", "build", @"C:\Games\swfoc.exe", ExeTarget.Swfoc, ProcessId: Environment.ProcessId),
            Symbols: new SymbolMap(new Dictionary<string, SymbolInfo>(StringComparer.OrdinalIgnoreCase)),
            AttachedAt: DateTimeOffset.UtcNow);
    }

    private static MainViewModelDependencies CreateDependencies(
        IRuntimeAdapter runtime,
        IProfileRepository? profiles = null,
        ICatalogService? catalog = null,
        IActionReliabilityService? reliability = null,
        IModOnboardingService? modOnboarding = null,
        IModCalibrationService? modCalibration = null,
        ISupportBundleService? supportBundles = null,
        ITelemetrySnapshotService? telemetry = null,
        ISpawnPresetService? spawnPresets = null)
    {
        var orchestratorProfiles = new StubProfileRepository(BuildProfile(
            id: "base_swfoc",
            actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)));
        telemetry ??= new StubTelemetrySnapshotService();

        return new MainViewModelDependencies
        {
            Profiles = profiles!,
            ProcessLocator = null!,
            LaunchContextResolver = null!,
            ProfileVariantResolver = null!,
            GameLauncher = null!,
            Runtime = runtime,
            Orchestrator = new TrainerOrchestrator(orchestratorProfiles, runtime, new NullFreezeService(), new NullAuditLogger(), telemetry),
            Catalog = catalog!,
            SaveCodec = null!,
            SavePatchPackService = null!,
            SavePatchApplyService = null!,
            Helper = null!,
            Updates = null!,
            ModOnboarding = modOnboarding ?? new StubModOnboardingService(new ModOnboardingResult(
                Succeeded: true,
                ProfileId: "default_profile",
                OutputPath: @"C:\Temp\default_profile.json",
                InferredWorkshopIds: Array.Empty<string>(),
                InferredPathHints: Array.Empty<string>(),
                InferredAliases: Array.Empty<string>(),
                Warnings: Array.Empty<string>())),
            ModCalibration = modCalibration ?? new StubModCalibrationService(
                artifactResult: new ModCalibrationArtifactResult(true, @"C:\Temp\artifact.json", "fp", Array.Empty<CalibrationCandidate>(), Array.Empty<string>()),
                compatibilityReport: new ModCompatibilityReport("base_swfoc", DateTimeOffset.UtcNow, RuntimeMode.Galactic, DependencyValidationStatus.Pass, 0, true, Array.Empty<ModActionCompatibility>(), Array.Empty<string>())),
            SupportBundles = supportBundles ?? new StubSupportBundleService(new SupportBundleResult(true, @"C:\Temp\bundle.zip", @"C:\Temp\bundle.manifest.json", Array.Empty<string>(), Array.Empty<string>())),
            Telemetry = telemetry,
            FreezeService = new NullFreezeService(),
            ActionReliability = reliability ?? new StubActionReliabilityService(Array.Empty<ActionReliabilityInfo>()),
            SelectedUnitTransactions = null!,
            SpawnPresets = spawnPresets ?? new StubSpawnPresetService(Array.Empty<SpawnPreset>())
        };
    }

    private sealed class CoverageHarness : MainViewModelSaveOpsBase
    {
        public CoverageHarness(MainViewModelDependencies dependencies)
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

        public Task InvokeScaffoldModProfileAsync() => ScaffoldModProfileAsync();
        public Task InvokeExportCalibrationArtifactAsync() => ExportCalibrationArtifactAsync();
        public Task InvokeBuildCompatibilityReportAsync() => BuildCompatibilityReportAsync();
        public Task InvokeExportSupportBundleAsync() => ExportSupportBundleAsync();
        public Task InvokeExportTelemetrySnapshotAsync() => ExportTelemetrySnapshotAsync();
        public void InvokeRefreshLiveOpsDiagnostics() => RefreshLiveOpsDiagnostics();
        public Task InvokeRefreshActionReliabilityAsync() => RefreshActionReliabilityAsync();
        public Task InvokeLoadSpawnPresetsAsync() => LoadSpawnPresetsAsync();
    }

    private sealed class StubRuntimeAdapter : IRuntimeAdapter
    {
        public bool IsAttached { get; set; } = true;
        public AttachSession? CurrentSession { get; set; }

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
            return Task.FromResult(new ActionExecutionResult(true, "ok", AddressSource.Signature));
        }

        public Task DetachAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }

    private sealed class StubProfileRepository : IProfileRepository
    {
        private readonly TrainerProfile? _profile;

        public StubProfileRepository(TrainerProfile? profile)
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
            return Task.FromResult(_profile!);
        }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_profile!);
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
            return Task.FromResult<IReadOnlyList<string>>(_profile is null ? Array.Empty<string>() : [_profile.Id]);
        }
    }

    private sealed class StaticCatalogService : ICatalogService
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _catalog;

        public StaticCatalogService(IReadOnlyDictionary<string, IReadOnlyList<string>> catalog)
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

    private sealed class ThrowingCatalogService : ICatalogService
    {
        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            throw new InvalidOperationException("catalog unavailable");
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

    private sealed class StubModOnboardingService : IModOnboardingService
    {
        private readonly ModOnboardingResult _result;

        public StubModOnboardingService(ModOnboardingResult result)
        {
            _result = result;
        }

        public ModOnboardingRequest? LastRequest { get; private set; }

        public Task<ModOnboardingResult> ScaffoldDraftProfileAsync(ModOnboardingRequest request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastRequest = request;
            return Task.FromResult(_result);
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
        private readonly ModCalibrationArtifactResult _artifactResult;
        private readonly ModCompatibilityReport _compatibilityReport;

        public StubModCalibrationService(
            ModCalibrationArtifactResult artifactResult,
            ModCompatibilityReport compatibilityReport)
        {
            _artifactResult = artifactResult;
            _compatibilityReport = compatibilityReport;
        }

        public ModCalibrationArtifactRequest? LastArtifactRequest { get; private set; }

        public Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(ModCalibrationArtifactRequest request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastArtifactRequest = request;
            return Task.FromResult(_artifactResult);
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
            return Task.FromResult(_compatibilityReport);
        }
    }

    private sealed class StubSupportBundleService : ISupportBundleService
    {
        private readonly SupportBundleResult _result;

        public StubSupportBundleService(SupportBundleResult result)
        {
            _result = result;
        }

        public SupportBundleRequest? LastRequest { get; private set; }

        public Task<SupportBundleResult> ExportAsync(SupportBundleRequest request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            LastRequest = request;
            return Task.FromResult(_result);
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

    private sealed class StubSpawnPresetService : ISpawnPresetService
    {
        private readonly IReadOnlyList<SpawnPreset> _presets;

        public StubSpawnPresetService(IReadOnlyList<SpawnPreset> presets)
        {
            _presets = presets;
        }

        public Task<IReadOnlyList<SpawnPreset>> LoadPresetsAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_presets);
        }

        public SpawnBatchPlan BuildBatchPlan(
            string profileId,
            SpawnPreset preset,
            int quantity,
            int delayMs,
            string? factionOverride,
            string? entryMarkerOverride,
            bool stopOnFailure)
        {
            _ = quantity;
            return new SpawnBatchPlan(
                profileId,
                preset.Id,
                stopOnFailure,
                [new SpawnBatchItem(1, preset.UnitId, factionOverride ?? preset.Faction, entryMarkerOverride ?? preset.EntryMarker, delayMs)]);
        }

        public Task<SpawnBatchExecutionResult> ExecuteBatchAsync(
            string profileId,
            SpawnBatchPlan plan,
            RuntimeMode runtimeMode,
            CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = plan;
            _ = runtimeMode;
            _ = cancellationToken;
            return Task.FromResult(new SpawnBatchExecutionResult(true, "ok", 0, 0, 0, false, Array.Empty<SpawnBatchItemResult>()));
        }
    }

    private sealed class NullFreezeService : IValueFreezeService
    {
        public void FreezeInt(string symbol, int value)
        {
            _ = symbol;
            _ = value;
        }

        public void FreezeIntAggressive(string symbol, int value)
        {
            _ = symbol;
            _ = value;
        }

        public void FreezeFloat(string symbol, float value)
        {
            _ = symbol;
            _ = value;
        }

        public void FreezeBool(string symbol, bool value)
        {
            _ = symbol;
            _ = value;
        }

        public bool Unfreeze(string symbol)
        {
            _ = symbol;
            return false;
        }

        public void UnfreezeAll()
        {
        }

        public bool IsFrozen(string symbol)
        {
            _ = symbol;
            return false;
        }

        public IReadOnlyCollection<string> GetFrozenSymbols()
        {
            return Array.Empty<string>();
        }

        public void Dispose()
        {
        }
    }

    private sealed class NullAuditLogger : IAuditLogger
    {
        public Task WriteAsync(ActionAuditRecord record, CancellationToken cancellationToken)
        {
            _ = record;
            _ = cancellationToken;
            return Task.CompletedTask;
        }
    }
}
