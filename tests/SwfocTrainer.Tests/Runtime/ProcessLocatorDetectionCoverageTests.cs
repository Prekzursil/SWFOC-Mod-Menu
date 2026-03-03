using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ProcessLocatorDetectionCoverageTests
{
    [Fact]
    public void Constructors_ShouldCreateLocatorInstances_WithAllOverloads()
    {
        var defaultLocator = new ProcessLocator();
        var resolverLocator = new ProcessLocator(new LaunchContextResolver());
        var repositoryLocator = new ProcessLocator(new StubProfileRepository());

        defaultLocator.Should().NotBeNull();
        resolverLocator.Should().NotBeNull();
        repositoryLocator.Should().NotBeNull();
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectSweaw_ByExecutableName()
    {
        var detection = InvokePrivateStatic(
            methodName: "GetProcessDetection",
            "sweaw.exe",
            null,
            null);

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Sweaw);
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeFalse();
        ReadProperty<string>(detection, "DetectedVia").Should().Be("name_or_path_sweaw");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectSwfoc_ForStarWarsGWithFoCMarkers()
    {
        var detection = InvokePrivateStatic(
            methodName: "GetProcessDetection",
            "StarWarsG.exe",
            @"C:\Games\corruption\StarWarsG.exe",
            "\"StarWarsG.exe\" STEAMMOD=1397421866");

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeTrue();
        ReadProperty<string>(detection, "DetectedVia").Should().Contain("starwarsg");
    }

    [Theory]
    [InlineData("mode=LAND", RuntimeMode.TacticalLand)]
    [InlineData("mode=SPACE", RuntimeMode.TacticalSpace)]
    [InlineData("mode=TACTICAL", RuntimeMode.AnyTactical)]
    [InlineData("state=campaign", RuntimeMode.Galactic)]
    [InlineData("mode=unknown", RuntimeMode.Unknown)]
    [InlineData("", RuntimeMode.Unknown)]
    public void InferMode_ShouldMapTelemetryHints(string commandLine, RuntimeMode expected)
    {
        var inferred = (RuntimeMode)InvokePrivateStatic(
            methodName: "InferMode",
            commandLine);

        inferred.Should().Be(expected);
    }

    [Fact]
    public void ExtractSteamModIds_ShouldParseAndSortDistinctIds()
    {
        var ids = (string[])InvokePrivateStatic(
            methodName: "ExtractSteamModIds",
            "\"StarWarsG.exe\" STEAMMOD=3447786229 modpath=\"x/32470/1397421866\" STEAMMOD=3447786229");

        ids.Should().Equal("1397421866", "3447786229");
    }

    [Theory]
    [InlineData("modpath=\"Mods\\\\AOTR\"", "Mods\\\\AOTR")]
    [InlineData("MODPATH=Mods/ROE", "Mods/ROE")]
    [InlineData("something else", null)]
    public void ExtractModPath_ShouldSupportQuotedAndUnquotedTokens(string commandLine, string? expected)
    {
        var resolved = (string?)InvokePrivateStatic(
            methodName: "ExtractModPath",
            commandLine);

        resolved.Should().Be(expected);
    }

    [Fact]
    public void ResolveOptionsFromEnvironment_ShouldReturnNone_WhenNoForcedHints()
    {
        var previousIds = Environment.GetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar);
        var previousProfile = Environment.GetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar, null);
            Environment.SetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar, null);

            var options = (ProcessLocatorOptions)InvokePrivateStatic(methodName: "ResolveOptionsFromEnvironment");

            options.ForcedWorkshopIds.Should().BeNullOrEmpty();
            options.ForcedProfileId.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar, previousIds);
            Environment.SetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar, previousProfile);
        }
    }

    private static object InvokePrivateStatic(string methodName, params object?[] arguments)
    {
        var method = typeof(ProcessLocator).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"private static method '{methodName}' should exist.");
        return method!.Invoke(null, arguments)!;
    }

    private static T ReadProperty<T>(object value, string propertyName)
    {
        var property = value.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        property.Should().NotBeNull($"property '{propertyName}' should exist.");
        return (T)property!.GetValue(value)!;
    }

    private sealed class StubProfileRepository : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken)
            {
                _ = cancellationToken;
                throw new NotImplementedException();
            }

        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken)
            {
                _ = profileId;
                _ = cancellationToken;
                throw new NotImplementedException();
            }

        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken)
            {
                _ = profileId;
                _ = cancellationToken;
                throw new NotImplementedException();
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
                return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
            }
    }
}


