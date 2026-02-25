using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Profiles.Services;
using SwfocTrainer.Saves.Services;

namespace SwfocTrainer.App;

public partial class App
{
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
