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

        services.AddSingleton<IAuditLogger>(_ => new FileAuditLogger(Path.Combine(appData, "logs")));

        services.AddSingleton<IProfileRepository, FileSystemProfileRepository>();
        services.AddSingleton<ILaunchContextResolver, LaunchContextResolver>();
        services.AddSingleton<IModDependencyValidator, ModDependencyValidator>();
        services.AddSingleton<IActionReliabilityService, ActionReliabilityService>();
        services.AddSingleton<ISymbolHealthService, SymbolHealthService>();
        services.AddSingleton<IProcessLocator, ProcessLocator>();
        services.AddSingleton<ISignatureResolver, SignatureResolver>();
        services.AddSingleton<IRuntimeAdapter, RuntimeAdapter>();
        services.AddSingleton<IValueFreezeService, ValueFreezeService>();
        services.AddSingleton<ISelectedUnitTransactionService, SelectedUnitTransactionService>();
        services.AddSingleton<ISpawnPresetService, SpawnPresetService>();
        services.AddSingleton<ICatalogService, CatalogService>();
        services.AddSingleton<IHelperModService, HelperModService>();
        services.AddSingleton<ISaveCodec, BinarySaveCodec>();
        services.AddSingleton<ISavePatchPackService, SavePatchPackService>();
        services.AddSingleton<ISavePatchApplyService, SavePatchApplyService>();

        services.AddSingleton<HttpClient>();
        services.AddSingleton<IProfileUpdateService, GitHubProfileUpdateService>();

        services.AddSingleton<TrainerOrchestrator>();
        services.AddSingleton<MainViewModel>();

        services.AddSingleton<MainWindow>();
    }
}
