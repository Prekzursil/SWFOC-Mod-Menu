using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using SwfocTrainer.App.ViewModels;
using SwfocTrainer.Core.Contracts;
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

    private static void InvokeConfigureServices(IServiceCollection services)
    {
        var method = typeof(SwfocTrainer.App.Program).GetMethod("ConfigureServices", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        method!.Invoke(null, new object[] { services });
    }
}
