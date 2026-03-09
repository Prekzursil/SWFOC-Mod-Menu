using System.Text.Json.Nodes;
using System.Windows.Threading;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class MainViewModelHeroArtifactCoverageTests
{
    [Fact]
    public async Task RefreshActionReliabilityAsync_ShouldPreferExplicitHeroMetadata_AndActionDrivenRespawnSupport()
    {
        var profile = BuildProfile(
            id: "base_swfoc",
            actions: new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase)
            {
                ["edit_hero_state"] = BuildHeroAction("edit_hero_state")
            },
            metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["defaultHeroRespawnTime"] = " 120 ",
                ["default_hero_respawn_time"] = " 900 ",
                ["duplicateHeroPolicy"] = " clone ",
                ["duplicate_hero_policy"] = " warn ",
                ["supports_hero_permadeath"] = " true ",
                ["supports_hero_rescue"] = "   "
            });

        var runtime = new StubRuntimeAdapter
        {
            CurrentSession = BuildSession(RuntimeMode.Galactic)
        };

        var vm = new CoverageHarness(CreateDependencies(
            runtime,
            profiles: new StubProfileRepository(profile),
            catalog: new StaticCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)),
            reliability: new StubActionReliabilityService(
            [
                new ActionReliabilityInfo("edit_hero_state", ActionReliabilityState.Stable, "HELPER_EXECUTION_APPLIED", 1.0d, "verified")
            ])));

        vm.SelectedProfileId = profile.Id;

        await vm.InvokeRefreshActionReliabilityAsync();

        vm.ActionReliability.Should().ContainSingle(x => x.ActionId == "edit_hero_state");
        vm.HeroSupportsRespawn.Should().Be("true");
        vm.HeroSupportsPermadeath.Should().Be("true");
        vm.HeroSupportsRescue.Should().Be("false");
        vm.HeroDefaultRespawnTime.Should().Be("120");
        vm.HeroDuplicatePolicy.Should().Be("clone");
    }

    [Fact]
    public async Task ScaffoldModProfileAsync_ShouldFallbackToBaseSwfoc_AndSummarizeWarnings()
    {
        var onboarding = new StubModOnboardingService(new ModOnboardingResult(
            Succeeded: true,
            ProfileId: "custom_my_mod",
            OutputPath: @"C:\Temp\custom_my_mod.json",
            InferredWorkshopIds: ["1125571106"],
            InferredPathHints: ["mods", "empire"],
            InferredAliases: ["custom_my_mod"],
            Warnings: ["warn-a", "warn-b"]));

        var vm = new CoverageHarness(CreateDependencies(
            new StubRuntimeAdapter { CurrentSession = BuildSession(RuntimeMode.Galactic) },
            profiles: new StubProfileRepository(BuildProfile("base_swfoc")),
            catalog: new StaticCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)),
            modOnboarding: onboarding));

        vm.OnboardingDraftProfileId = "custom_my_mod";
        vm.OnboardingDisplayName = "Custom My Mod";
        vm.OnboardingBaseProfileId = "   ";
        vm.OnboardingNamespaceRoot = "generated.mods";
        vm.OnboardingLaunchSample = string.Join(Environment.NewLine + Environment.NewLine, "Steam.exe STEAMMOD=1125571106", "Launcher.exe /modpath Mods\\Custom");

        await vm.InvokeScaffoldModProfileAsync();

        onboarding.LastRequest.Should().NotBeNull();
        onboarding.LastRequest!.BaseProfileId.Should().Be("base_swfoc");
        onboarding.LastRequest.LaunchSamples.Should().HaveCount(2);
        vm.OnboardingSummary.Should().Contain("warnings=warn-a; warn-b");
        vm.OnboardingSummary.Should().Contain("workshop=[1125571106]");
        vm.Status.Should().Be("Draft profile scaffolded: custom_my_mod");
    }

    [Fact]
    public async Task BuildCompatibilityReportAsync_ShouldPopulateActionRows_WhenReportContainsActions()
    {
        var profile = BuildProfile("base_swfoc");
        var calibration = new StubModCalibrationService(
            artifactResult: new ModCalibrationArtifactResult(true, @"C:\Temp\artifact.json", "fp", Array.Empty<CalibrationCandidate>(), Array.Empty<string>()),
            compatibilityReport: new ModCompatibilityReport(
                ProfileId: profile.Id,
                GeneratedAtUtc: DateTimeOffset.UtcNow,
                RuntimeMode: RuntimeMode.Galactic,
                DependencyStatus: DependencyValidationStatus.Pass,
                UnresolvedCriticalSymbols: 1,
                PromotionReady: false,
                Actions:
                [
                    new ModActionCompatibility("spawn_tactical_entity", ActionReliabilityState.Experimental, "HELPER_VERIFY_PENDING", 0.55d)
                ],
                Notes: ["pending verification"]));

        var vm = new CoverageHarness(CreateDependencies(
            new StubRuntimeAdapter { CurrentSession = BuildSession(RuntimeMode.Galactic) },
            profiles: new StubProfileRepository(profile),
            catalog: new StaticCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)),
            modCalibration: calibration));

        vm.SelectedProfileId = profile.Id;

        await vm.InvokeBuildCompatibilityReportAsync();

        vm.ModCompatibilityRows.Should().ContainSingle()
            .Which.Should().Be("spawn_tactical_entity | Experimental | HELPER_VERIFY_PENDING | 0.55");
        vm.ModCompatibilitySummary.Should().Be("promotionReady=False dependency=Pass unresolvedCritical=1");
        vm.Status.Should().Be("Compatibility report generated for base_swfoc");
    }

    [Fact]
    public async Task UpdateFailureFlows_ShouldSurfaceNoUpdates_InstallFailure_AndRollbackFailureArtifacts()
    {
        var updates = new StubProfileUpdateService(
            availableUpdates: Array.Empty<string>(),
            installResult: new ProfileInstallResult(
                Succeeded: false,
                ProfileId: "base_swfoc",
                InstalledPath: string.Empty,
                BackupPath: null,
                ReceiptPath: null,
                Message: "hash mismatch",
                ReasonCode: "sha_mismatch"),
            rollbackResult: new ProfileRollbackResult(
                Restored: false,
                ProfileId: "base_swfoc",
                RestoredPath: string.Empty,
                BackupPath: null,
                Message: "backup unavailable",
                ReasonCode: "backup_missing"));

        var vm = new CoverageHarness(CreateDependencies(
            new StubRuntimeAdapter { CurrentSession = BuildSession(RuntimeMode.Galactic) },
            profiles: new StubProfileRepository(BuildProfile("base_swfoc")),
            catalog: new StaticCatalogService(new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)),
            updates: updates));

        await vm.InvokeCheckUpdatesAsync();
        vm.Updates.Should().BeEmpty();
        vm.Status.Should().Be("No profile updates");

        vm.SelectedProfileId = "base_swfoc";

        await vm.InvokeInstallUpdateAsync();
        vm.Status.Should().Be("Profile update failed: hash mismatch");
        vm.OpsArtifactSummary.Should().Be("install failed (sha_mismatch)");

        await vm.InvokeRollbackProfileUpdateAsync();
        vm.Status.Should().Be("Rollback failed: backup unavailable");
        vm.OpsArtifactSummary.Should().Be("rollback failed (backup_missing)");
    }

    private static ActionSpec BuildHeroAction(string id)
    {
        return new ActionSpec(
            id,
            ActionCategory.Hero,
            RuntimeMode.Galactic,
            ExecutionKind.Helper,
            new JsonObject(),
            VerifyReadback: false,
            CooldownMs: 0);
    }

    private static TrainerProfile BuildProfile(
        string id,
        IReadOnlyDictionary<string, ActionSpec>? actions = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        return new TrainerProfile(
            Id: id,
            DisplayName: id,
            Inherits: null,
            ExeTarget: ExeTarget.Swfoc,
            SteamWorkshopId: null,
            SignatureSets: Array.Empty<SignatureSet>(),
            FallbackOffsets: new Dictionary<string, long>(),
            Actions: actions ?? new Dictionary<string, ActionSpec>(StringComparer.OrdinalIgnoreCase),
            FeatureFlags: new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase),
            CatalogSources: Array.Empty<CatalogSource>(),
            SaveSchemaId: "schema",
            HelperModHooks: Array.Empty<HelperHookSpec>(),
            Metadata: metadata ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    private static AttachSession BuildSession(RuntimeMode mode)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["runtimeModeReasonCode"] = "mode_probe_ok",
            ["resolvedVariant"] = "base_swfoc",
            ["resolvedVariantReasonCode"] = "variant_match",
            ["resolvedVariantConfidence"] = "0.99",
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
        IProfileRepository profiles,
        ICatalogService catalog,
        IActionReliabilityService? reliability = null,
        IModOnboardingService? modOnboarding = null,
        IModCalibrationService? modCalibration = null,
        IProfileUpdateService? updates = null)
    {
        return new MainViewModelDependencies
        {
            Profiles = profiles,
            ProcessLocator = null!,
            LaunchContextResolver = null!,
            ProfileVariantResolver = null!,
            GameLauncher = null!,
            Runtime = runtime,
            Orchestrator = null!,
            Catalog = catalog,
            SaveCodec = null!,
            SavePatchPackService = null!,
            SavePatchApplyService = null!,
            Helper = null!,
            Updates = updates ?? new StubProfileUpdateService(
                Array.Empty<string>(),
                new ProfileInstallResult(true, "base_swfoc", @"C:\Temp\installed.json", @"C:\Temp\backup.json", @"C:\Temp\receipt.json", "installed"),
                new ProfileRollbackResult(true, "base_swfoc", @"C:\Temp\restored.json", @"C:\Temp\backup.json", "rollback complete")),
            ModOnboarding = modOnboarding ?? new StubModOnboardingService(new ModOnboardingResult(
                Succeeded: true,
                ProfileId: "default_profile",
                OutputPath: @"C:\Temp\default_profile.json",
                InferredWorkshopIds: Array.Empty<string>(),
                InferredPathHints: Array.Empty<string>(),
                InferredAliases: Array.Empty<string>(),
                Warnings: Array.Empty<string>())),
            ModCalibration = modCalibration ?? new StubModCalibrationService(
                new ModCalibrationArtifactResult(true, @"C:\Temp\artifact.json", "fp", Array.Empty<CalibrationCandidate>(), Array.Empty<string>()),
                new ModCompatibilityReport("base_swfoc", DateTimeOffset.UtcNow, RuntimeMode.Galactic, DependencyValidationStatus.Pass, 0, true, Array.Empty<ModActionCompatibility>(), Array.Empty<string>())),
            SupportBundles = null!,
            Telemetry = null!,
            FreezeService = null!,
            ActionReliability = reliability ?? new StubActionReliabilityService(Array.Empty<ActionReliabilityInfo>()),
            SelectedUnitTransactions = null!,
            SpawnPresets = null!
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
            _freezeUiTimer = new DispatcherTimer();
            Status = "Ready";
        }

        protected override void ApplyPayloadTemplateForSelectedAction()
        {
        }

        protected override Task<bool> EnsureActionAvailableForCurrentSessionAsync(string actionId, string statusPrefix)
        {
            _ = actionId;
            _ = statusPrefix;
            return Task.FromResult(true);
        }

        public Task InvokeRefreshActionReliabilityAsync() => RefreshActionReliabilityAsync();
        public Task InvokeScaffoldModProfileAsync() => ScaffoldModProfileAsync();
        public Task InvokeBuildCompatibilityReportAsync() => BuildCompatibilityReportAsync();
        public Task InvokeCheckUpdatesAsync() => CheckUpdatesAsync();
        public Task InvokeInstallUpdateAsync() => InstallUpdateAsync();
        public Task InvokeRollbackProfileUpdateAsync() => RollbackProfileUpdateAsync();
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
            return Task.FromResult(new ProfileManifest("1", DateTimeOffset.UtcNow, Array.Empty<ProfileManifestEntry>()));
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

        public Task<ModCalibrationArtifactResult> ExportCalibrationArtifactAsync(ModCalibrationArtifactRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
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

    private sealed class StubProfileUpdateService : IProfileUpdateService
    {
        private readonly IReadOnlyList<string> _availableUpdates;
        private readonly ProfileInstallResult _installResult;
        private readonly ProfileRollbackResult _rollbackResult;

        public StubProfileUpdateService(
            IReadOnlyList<string> availableUpdates,
            ProfileInstallResult installResult,
            ProfileRollbackResult rollbackResult)
        {
            _availableUpdates = availableUpdates;
            _installResult = installResult;
            _rollbackResult = rollbackResult;
        }

        public Task<IReadOnlyList<string>> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            return Task.FromResult(_availableUpdates);
        }

        public Task<string> InstallProfileAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_installResult.InstalledPath);
        }

        public Task<ProfileInstallResult> InstallProfileTransactionalAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_installResult);
        }

        public Task<ProfileRollbackResult> RollbackLastInstallAsync(string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_rollbackResult);
        }
    }
}
