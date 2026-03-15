using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SwfocTrainer.App;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Catalog.Config;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.IO;
using SwfocTrainer.Core.Logging;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Helper.Config;
using SwfocTrainer.Profiles.Config;
using SwfocTrainer.Runtime.Services;
using SwfocTrainer.Saves.Config;
using Xunit;

namespace SwfocTrainer.Tests.App;

public sealed class ProgramServiceRegistrationTests
{
    [Fact]
    public void ConfigureServices_ShouldRegisterCoreLaunchAndMechanicServices()
    {
        var services = new ServiceCollection();

        InvokeConfigureServices(services);

        services.Any(x => x.ServiceType == typeof(IGameLaunchService)).Should().BeTrue();
        services.Any(x => x.ServiceType == typeof(IWorkshopInventoryService)).Should().BeTrue();
        services.Any(x => x.ServiceType == typeof(IHelperBridgeBackend)).Should().BeTrue();
        services.Any(x => x.ServiceType == typeof(ITransplantCompatibilityService)).Should().BeTrue();
        services.Any(x => x.ServiceType == typeof(IContentTransplantService)).Should().BeTrue();
        services.Any(x => x.ServiceType == typeof(IModMechanicDetectionService)).Should().BeTrue();
        services.Any(x => x.ServiceType == typeof(MainViewModelDependencies)).Should().BeTrue();
        services.Any(x => x.ServiceType == typeof(MainViewModel)).Should().BeTrue();
    }

    [Fact]
    public void ConfigureServices_ShouldRespectRemoteManifestEnvironmentOverride()
    {
        var previous = Environment.GetEnvironmentVariable("SWFOC_PROFILE_MANIFEST_URL");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_PROFILE_MANIFEST_URL", "https://example.invalid/manifest.json");
            var services = new ServiceCollection();

            InvokeConfigureServices(services);

            var optionsDescriptor = services.Single(x => x.ServiceType.FullName == "SwfocTrainer.Profiles.Config.ProfileRepositoryOptions");
            optionsDescriptor.ImplementationInstance.Should().NotBeNull();
            var remoteManifestUrl = optionsDescriptor.ImplementationInstance!
                .GetType()
                .GetProperty("RemoteManifestUrl", BindingFlags.Instance | BindingFlags.Public)?
                .GetValue(optionsDescriptor.ImplementationInstance) as string;
            remoteManifestUrl.Should().Be("https://example.invalid/manifest.json");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_PROFILE_MANIFEST_URL", previous);
        }
    }

    [Fact]
    public void ConfigureServices_ShouldRegisterExpectedOptionsAndUiServices()
    {
        var services = new ServiceCollection();

        InvokeConfigureServices(services);

        services.Any(x => x.ServiceType == typeof(ProfileRepositoryOptions)).Should().BeTrue();
        services.Any(x => x.ServiceType == typeof(CatalogOptions)).Should().BeTrue();
        services.Any(x => x.ServiceType == typeof(HelperModOptions)).Should().BeTrue();
        services.Any(x => x.ServiceType == typeof(SaveOptions)).Should().BeTrue();
        services.Any(x => x.ServiceType == typeof(TrainerOrchestrator)).Should().BeTrue();
        services.Any(x => x.ServiceType == typeof(MainWindow)).Should().BeTrue();
    }

    [Fact]
    public void PrivateRegistrationSteps_ShouldPopulateServiceCollection()
    {
        var services = new ServiceCollection();
        var appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SwfocTrainer",
            $"test-{Guid.NewGuid():N}");
        var profilesRoot = Path.Combine(appData, "profiles");

        try
        {
            InvokePrivateStatic("ConfigureLogging", services);
            InvokePrivateStatic("RegisterOptions", services, appData, profilesRoot, "https://example.invalid/manifest.json");
            InvokePrivateStatic("RegisterCoreServices", services, appData, Path.Combine(profilesRoot, "sdk", "maps"));
            InvokePrivateStatic("RegisterProfileUpdateServices", services);
            InvokePrivateStatic("RegisterUiServices", services);

            using var provider = services.BuildServiceProvider();

            provider.GetRequiredService<ILoggerFactory>().Should().NotBeNull();
            provider.GetRequiredService<ProfileRepositoryOptions>().RemoteManifestUrl.Should().Be("https://example.invalid/manifest.json");
            provider.GetRequiredService<CatalogOptions>().CatalogRootPath.Should().Contain("profiles");
            provider.GetRequiredService<HelperModOptions>().InstallRoot.Should().Contain("helper_mod");
            provider.GetRequiredService<SaveOptions>().SchemaRootPath.Should().Contain("schemas");
            provider.GetRequiredService<IAuditLogger>().Should().BeOfType<FileAuditLogger>();
            provider.GetRequiredService<IGameLaunchService>().Should().BeOfType<GameLaunchService>();
            provider.GetRequiredService<IProcessLocator>().Should().BeOfType<ProcessLocator>();
            provider.GetRequiredService<MainViewModelDependencies>().Should().NotBeNull();
        }
        finally
        {
            if (Directory.Exists(appData))
            {
                Directory.Delete(appData, recursive: true);
            }
        }
    }

    private static void InvokeConfigureServices(IServiceCollection services)
    {
        var method = typeof(SwfocTrainer.App.Program).GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        method!.Invoke(null, new object[] { services });
    }

    private static void InvokePrivateStatic(string methodName, params object?[] args)
    {
        var method = typeof(SwfocTrainer.App.Program).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        method!.Invoke(null, args);
    }
}
