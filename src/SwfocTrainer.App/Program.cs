using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwfocTrainer.App.V2;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Catalog.Config;
using SwfocTrainer.Catalog.Services;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Helper.Config;
using SwfocTrainer.Helper.Services;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Saves.Config;
using SwfocTrainer.Saves.Services;
using SwfocTrainer.Transplant.Services;

namespace SwfocTrainer.App;

internal static class Program
{
    /// <summary>
    /// Command-line flag. Presence forces the legacy <see cref="MainWindow"/>;
    /// absence launches the V2 direct-to-bridge UI (<see cref="MainWindowV2"/>).
    /// </summary>
    internal const string LegacyUiFlag = "--legacy-ui";

    [STAThread]
    private static void Main(string[] args)
    {
        var useLegacy = args is not null && args.Any(
            a => string.Equals(a, LegacyUiFlag, StringComparison.OrdinalIgnoreCase));

        var services = new ServiceCollection();
        ConfigureServices(services);
        RegisterV2Services(services);

        using var serviceProvider = services.BuildServiceProvider();
        var app = new Application();

        // 2026-04-25: apply persisted theme preference (default: follow Windows)
        // before the first window is created so the initial paint already
        // shows the right palette.
        var v2Settings = serviceProvider.GetRequiredService<V2Settings>();
        ThemeService.ApplyPreference(ThemeService.ParsePreference(v2Settings.Theme));

        if (useLegacy)
        {
            var legacy = serviceProvider.GetRequiredService<MainWindow>();
            app.Run(legacy);
        }
        else
        {
            var v2 = serviceProvider.GetRequiredService<MainWindowV2>();
            app.Run(v2);
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var appData = TrustedPathPolicy.GetOrCreateAppDataRoot();
        var profilesRoot = Path.Join(AppContext.BaseDirectory, "profiles", "default");
        var remoteManifest = Environment.GetEnvironmentVariable("SWFOC_PROFILE_MANIFEST_URL");
        var capabilityMapsRoot = Path.Join(profilesRoot, "sdk", "maps");

        ConfigureLogging(services);
        RegisterOptions(services, appData, profilesRoot, remoteManifest);
        RegisterCoreServices(services, appData, capabilityMapsRoot);
        RegisterProfileUpdateServices(services);
        RegisterUiServices(services);
    }

    private static void ConfigureLogging(IServiceCollection services)
    {
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });
    }

    private static void RegisterOptions(
        IServiceCollection services,
        string appData,
        string profilesRoot,
        string? remoteManifest)
    {
        services.AddSingleton(new ProfileRepositoryOptions
        {
            ProfilesRootPath = profilesRoot,
            ManifestFileName = "manifest.json",
            DownloadCachePath = Path.Join(appData, "cache"),
            RemoteManifestUrl = remoteManifest
        });

        services.AddSingleton(new CatalogOptions
        {
            CatalogRootPath = Path.Join(profilesRoot, "catalog")
        });

        services.AddSingleton(new HelperModOptions
        {
            SourceRoot = Path.Join(profilesRoot, "helper"),
            InstallRoot = Path.Join(appData, "helper_mod")
        });

        services.AddSingleton(new SaveOptions
        {
            SchemaRootPath = Path.Join(profilesRoot, "schemas")
        });

        services.AddSingleton(new LiveOpsOptions
        {
            PresetRootPath = Path.Join(profilesRoot, "presets")
        });
    }

    private static void RegisterCoreServices(
        IServiceCollection services,
        string appData,
        string capabilityMapsRoot)
    {
        services.AddSingleton<IAuditLogger>(_ => new FileAuditLogger(Path.Join(appData, "logs")));

        services.AddSingleton<IProfileRepository, FileSystemProfileRepository>();
        services.AddSingleton<ILaunchContextResolver, LaunchContextResolver>();
        services.AddSingleton<IModDependencyValidator, ModDependencyValidator>();
        services.AddSingleton<IBinaryFingerprintService, BinaryFingerprintService>();
        services.AddSingleton<ICapabilityMapResolver>(provider =>
            new CapabilityMapResolver(
                capabilityMapsRoot,
                provider.GetRequiredService<ILogger<CapabilityMapResolver>>()));
        services.AddSingleton<ISdkExecutionGuard, SdkExecutionGuard>();
        services.AddSingleton<IProfileVariantResolver, ProfileVariantResolver>();
        services.AddSingleton<ISdkRuntimeAdapter, NoopSdkRuntimeAdapter>();
        services.AddSingleton<ISdkDiagnosticsSink, NullSdkDiagnosticsSink>();
        services.AddSingleton<ISdkOperationRouter, SdkOperationRouter>();
        services.AddSingleton<IBackendRouter, BackendRouter>();
        services.AddSingleton<IExecutionBackend, NamedPipeExtenderBackend>();
        services.AddSingleton<IHelperBridgeBackend>(provider =>
            new NamedPipeHelperBridgeBackend(provider.GetRequiredService<IExecutionBackend>()));
        services.AddSingleton<IActionReliabilityService, ActionReliabilityService>();
        services.AddSingleton<ITransplantCompatibilityService, TransplantCompatibilityService>();
        services.AddSingleton<IContentTransplantService, ContentTransplantService>();
        services.AddSingleton<IModMechanicDetectionService, ModMechanicDetectionService>();
        services.AddSingleton<IModCalibrationService, ModCalibrationService>();
        services.AddSingleton<IWorkshopInventoryService, WorkshopInventoryService>();
        services.AddSingleton<ISymbolHealthService, SymbolHealthService>();
        services.AddSingleton<ITelemetrySnapshotService, TelemetrySnapshotService>();
        services.AddSingleton<IGameLaunchService, GameLaunchService>();
        services.AddSingleton<IProcessLocator, ProcessLocator>();
        services.AddSingleton<ISignatureResolver, SignatureResolver>();
        services.AddSingleton<IRuntimeAdapter, RuntimeAdapter>();
        services.AddSingleton<ISupportBundleService, SupportBundleService>();
        services.AddSingleton<IValueFreezeService, ValueFreezeService>();
        services.AddSingleton<ISelectedUnitTransactionService, SelectedUnitTransactionService>();
        services.AddSingleton<ISpawnPresetService, SpawnPresetService>();
        services.AddSingleton<ICatalogService, CatalogService>();
        services.AddSingleton<IHelperModService, HelperModService>();
        services.AddSingleton<IModOnboardingService, ModOnboardingService>();
        services.AddSingleton<ISaveCodec, BinarySaveCodec>();
        services.AddSingleton<ISavePatchPackService, SavePatchPackService>();
        services.AddSingleton<ISavePatchApplyService, SavePatchApplyService>();
        services.AddSingleton<NamedPipeLuaBridgeClient>(_ => new NamedPipeLuaBridgeClient());
        services.AddSingleton<ILuaBridgeExecutor>(provider => new LuaBridgeExecutor(
            provider.GetRequiredService<TrainerOrchestrator>(),
            provider.GetRequiredService<IProfileRepository>(),
            provider.GetRequiredService<NamedPipeLuaBridgeClient>()));
        services.AddSingleton<IRosterBrowserService, RosterBrowserService>();
        services.AddSingleton<IFactionDashboardService, FactionDashboardService>();
        services.AddSingleton<IEnhancedSpawnService, EnhancedSpawnService>();

        // Wave 2
        services.AddSingleton<IOwnershipTransferService, OwnershipTransferService>();
        services.AddSingleton<IPlanetManagerService, PlanetManagerService>();
        services.AddSingleton<IFleetManagerService, FleetManagerService>();
        services.AddSingleton<IFactionSwitchService, FactionSwitchService>();

        // Wave 3
        services.AddSingleton<IAiControlService, AiControlService>();
        services.AddSingleton<ICooldownManagerService, CooldownManagerService>();

        // Wave 4
        services.AddSingleton<ICameraDirectorService, CameraDirectorService>();
        services.AddSingleton<IStoryEventService, StoryEventService>();

        // Wave 5
        services.AddSingleton<IModConflictDetectorService, ModConflictDetectorService>();
        services.AddSingleton<IDamageLogService, DamageLogService>();

        // Wave 6
        services.AddSingleton<IDiplomacyService, DiplomacyService>();
        services.AddSingleton<ICorruptionService, CorruptionService>();
    }

    private static void RegisterProfileUpdateServices(IServiceCollection services)
    {
        services.AddSingleton<HttpClient>();
        services.AddSingleton<IProfileUpdateService, GitHubProfileUpdateService>();
    }

    private static void RegisterUiServices(IServiceCollection services)
    {
        services.AddSingleton<TrainerOrchestrator>();
        services.AddSingleton(provider => new MainViewModelDependencies
        {
            Profiles = provider.GetRequiredService<IProfileRepository>(),
            ProcessLocator = provider.GetRequiredService<IProcessLocator>(),
            LaunchContextResolver = provider.GetRequiredService<ILaunchContextResolver>(),
            ProfileVariantResolver = provider.GetRequiredService<IProfileVariantResolver>(),
            GameLauncher = provider.GetRequiredService<IGameLaunchService>(),
            Runtime = provider.GetRequiredService<IRuntimeAdapter>(),
            Orchestrator = provider.GetRequiredService<TrainerOrchestrator>(),
            Catalog = provider.GetRequiredService<ICatalogService>(),
            SaveCodec = provider.GetRequiredService<ISaveCodec>(),
            SavePatchPackService = provider.GetRequiredService<ISavePatchPackService>(),
            SavePatchApplyService = provider.GetRequiredService<ISavePatchApplyService>(),
            Helper = provider.GetRequiredService<IHelperModService>(),
            Updates = provider.GetRequiredService<IProfileUpdateService>(),
            ModOnboarding = provider.GetRequiredService<IModOnboardingService>(),
            ModCalibration = provider.GetRequiredService<IModCalibrationService>(),
            SupportBundles = provider.GetRequiredService<ISupportBundleService>(),
            Telemetry = provider.GetRequiredService<ITelemetrySnapshotService>(),
            FreezeService = provider.GetRequiredService<IValueFreezeService>(),
            ActionReliability = provider.GetRequiredService<IActionReliabilityService>(),
            SelectedUnitTransactions = provider.GetRequiredService<ISelectedUnitTransactionService>(),
            SpawnPresets = provider.GetRequiredService<ISpawnPresetService>(),
            RosterBrowser = provider.GetRequiredService<IRosterBrowserService>(),
            FactionDashboard = provider.GetRequiredService<IFactionDashboardService>(),
            EnhancedSpawn = provider.GetRequiredService<IEnhancedSpawnService>(),
            OwnershipTransfer = provider.GetRequiredService<IOwnershipTransferService>(),
            PlanetManager = provider.GetRequiredService<IPlanetManagerService>(),
            FleetManager = provider.GetRequiredService<IFleetManagerService>(),
            FactionSwitch = provider.GetRequiredService<IFactionSwitchService>(),
            AiControl = provider.GetRequiredService<IAiControlService>(),
            CooldownManager = provider.GetRequiredService<ICooldownManagerService>(),
            CameraDirector = provider.GetRequiredService<ICameraDirectorService>(),
            StoryEvents = provider.GetRequiredService<IStoryEventService>(),
            ModConflicts = provider.GetRequiredService<IModConflictDetectorService>(),
            DamageLog = provider.GetRequiredService<IDamageLogService>(),
            Diplomacy = provider.GetRequiredService<IDiplomacyService>(),
            Corruption = provider.GetRequiredService<ICorruptionService>()
        });
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    // =========================================================================
    // V2 registration — direct-to-bridge pipeline.
    //
    // V2 intentionally does NOT depend on TrainerOrchestrator, IProfileRepository,
    // SdkOperationRouter, ActionSymbolRegistry, or any MainViewModel* partial
    // class. It builds its own ILuaBridgeExecutor implementation (V2BridgeAdapter)
    // that only talks to NamedPipeLuaBridgeClient, then hands that to the 8
    // bridge-helper services that the legacy DI container never registered.
    // =========================================================================
    private static void RegisterV2Services(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(_ => V2Settings.Load());
        services.AddSingleton(provider => new V2BridgeAdapter(
            provider.GetRequiredService<NamedPipeLuaBridgeClient>()));

        // The 8 bridge-helper services were missing from the legacy DI graph.
        // We register them against V2BridgeAdapter (which implements
        // ILuaBridgeExecutor) so they work for V2 even if the legacy LuaBridgeExecutor
        // is also in the container for backwards compat.
        services.AddSingleton<IGodModeService>(provider => new GodModeService(
            provider.GetRequiredService<V2BridgeAdapter>(),
            provider.GetRequiredService<ILogger<GodModeService>>()));
        services.AddSingleton<IOneHitKillService>(provider => new OneHitKillService(
            provider.GetRequiredService<V2BridgeAdapter>(),
            provider.GetRequiredService<ILogger<OneHitKillService>>()));
        services.AddSingleton<IEconomyService>(provider => new EconomyService(
            provider.GetRequiredService<V2BridgeAdapter>(),
            provider.GetRequiredService<ILogger<EconomyService>>()));
        services.AddSingleton<IHeroRespawnService>(provider => new HeroRespawnService(
            provider.GetRequiredService<V2BridgeAdapter>(),
            provider.GetRequiredService<ILogger<HeroRespawnService>>()));
        services.AddSingleton<IUnitInspectorService>(provider => new UnitInspectorService(
            provider.GetRequiredService<V2BridgeAdapter>(),
            provider.GetRequiredService<ILogger<UnitInspectorService>>()));
        services.AddSingleton<IHardpointService>(provider => new HardpointService(
            provider.GetRequiredService<V2BridgeAdapter>(),
            provider.GetRequiredService<ILogger<HardpointService>>()));
        services.AddSingleton<ICrashAnalyzerService>(provider => new CrashAnalyzerService(
            provider.GetRequiredService<V2BridgeAdapter>(),
            provider.GetRequiredService<ILogger<CrashAnalyzerService>>()));
        services.AddSingleton<IMaphackService>(provider => new MaphackService(
            provider.GetRequiredService<V2BridgeAdapter>(),
            provider.GetRequiredService<ILogger<MaphackService>>()));

        // 2026-04-27: thin dispatcher that wraps the bridge's raw helpers
        // (SetUnitHull / SetUnitInvuln / PreventUnitDeath / NullAiBrain /
        // AttachAiBrain) so a Lua signature change in powrprof.dll fails
        // visibly at one place instead of silently across two ViewModels.
        services.AddSingleton(provider => new V2UnitMutationDispatcher(
            provider.GetRequiredService<V2BridgeAdapter>()));

        // 2026-04-27: shared faction registry. PlayerStateTabViewModel
        // pumps it from SWFOC_GetAllPlayers; UnitControl / WorldState /
        // Galactic / Economy tabs bind their faction ComboBoxes to its
        // Factions collection so vanilla / mod / submod each surface
        // their own faction strings without per-tab code changes.
        services.AddSingleton<V2FactionRegistry>();

        services.AddSingleton<MainViewModelV2>();
        services.AddSingleton<MainWindowV2>();
    }
}
