using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwfocTrainer.Core.IO;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Catalog.Config;
using SwfocTrainer.Catalog.Services;
using SwfocTrainer.Core.Contracts;
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

namespace SwfocTrainer.App;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();

        var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_serviceProvider is not null)
        {
            _serviceProvider.Dispose();
        }

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var appData = TrustedPathPolicy.GetOrCreateAppDataRoot();

        var profilesRoot = Path.Combine(AppContext.BaseDirectory, "profiles", "default");
        var remoteManifest = Environment.GetEnvironmentVariable("SWFOC_PROFILE_MANIFEST_URL");

        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        services.AddSingleton(new ProfileRepositoryOptions
        {
            ProfilesRootPath = profilesRoot,
            ManifestFileName = "manifest.json",
            DownloadCachePath = Path.Combine(appData, "cache"),
            RemoteManifestUrl = remoteManifest
        });

        services.AddSingleton(new CatalogOptions
        {
            CatalogRootPath = Path.Combine(profilesRoot, "catalog")
        });

        services.AddSingleton(new HelperModOptions
        {
            SourceRoot = Path.Combine(profilesRoot, "helper"),
            InstallRoot = Path.Combine(appData, "helper_mod")
        });

        services.AddSingleton(new SaveOptions
        {
            SchemaRootPath = Path.Combine(profilesRoot, "schemas")
        });

        services.AddSingleton(new LiveOpsOptions
        {
            PresetRootPath = Path.Combine(profilesRoot, "presets")
        });

        var capabilityMapsRoot = Path.Combine(profilesRoot, "sdk", "maps");

        services.AddSingleton<IAuditLogger>(_ => new FileAuditLogger(Path.Combine(appData, "logs")));

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
        services.AddSingleton<IActionReliabilityService, ActionReliabilityService>();
        services.AddSingleton<IModCalibrationService, ModCalibrationService>();
        services.AddSingleton<ISymbolHealthService, SymbolHealthService>();
        services.AddSingleton<ITelemetrySnapshotService, TelemetrySnapshotService>();
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

        services.AddSingleton<HttpClient>();
        services.AddSingleton<IProfileUpdateService, GitHubProfileUpdateService>();

        services.AddSingleton<TrainerOrchestrator>();
        services.AddSingleton(provider => new MainViewModelDependencies
        {
            Profiles = provider.GetRequiredService<IProfileRepository>(),
            ProcessLocator = provider.GetRequiredService<IProcessLocator>(),
            LaunchContextResolver = provider.GetRequiredService<ILaunchContextResolver>(),
            ProfileVariantResolver = provider.GetRequiredService<IProfileVariantResolver>(),
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
            SpawnPresets = provider.GetRequiredService<ISpawnPresetService>()
        });
        services.AddSingleton<MainViewModel>();

        services.AddSingleton<MainWindow>();
    }
}
