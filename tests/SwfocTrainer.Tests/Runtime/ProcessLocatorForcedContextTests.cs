using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ProcessLocatorForcedContextTests
{
    [Fact]
    public void ResolveForcedContext_Should_Return_Forced_When_NoMarkers_And_ForcedHintsPresent()
    {
        var resolution = InvokeResolveForcedContext(
            commandLine: "StarWarsG.exe NOARTPROCESS IGNOREASSERTS",
            modPathRaw: null,
            detectedSteamModIds: Array.Empty<string>(),
            options: new ProcessLocatorOptions(
                ForcedWorkshopIds: new[] { "3447786229", "1397421866" },
                ForcedProfileId: "roe_3447786229_swfoc"));

        ReadStringProperty(resolution, "Source").Should().Be("forced");
        ReadStringProperty(resolution, "ForcedWorkshopIdsCsv").Should().Be("1397421866,3447786229");
        ReadStringProperty(resolution, "ForcedProfileId").Should().Be("roe_3447786229_swfoc");
        ReadStringSequenceProperty(resolution, "EffectiveSteamModIds")
            .Should()
            .Equal("1397421866", "3447786229");
    }

    [Fact]
    public void ResolveForcedContext_Should_Stay_Detected_When_ModMarkers_Are_Present()
    {
        var resolution = InvokeResolveForcedContext(
            commandLine: "\"C:\\Game\\corruption\\StarWarsG.exe\" STEAMMOD=1397421866",
            modPathRaw: null,
            detectedSteamModIds: new[] { "1397421866" },
            options: new ProcessLocatorOptions(
                ForcedWorkshopIds: new[] { "3447786229" },
                ForcedProfileId: "roe_3447786229_swfoc"));

        ReadStringProperty(resolution, "Source").Should().Be("detected");
        ReadStringProperty(resolution, "ForcedWorkshopIdsCsv").Should().BeEmpty();
        ReadStringProperty(resolution, "ForcedProfileId").Should().BeNull();
        ReadStringSequenceProperty(resolution, "EffectiveSteamModIds")
            .Should()
            .Equal("1397421866");
    }

    [Fact]
    public void ResolveOptionsFromEnvironment_Should_Parse_Forced_Ids_And_Profile()
    {
        var previousWorkshopIds = Environment.GetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar);
        var previousProfileId = Environment.GetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar, "3447786229,1397421866,3447786229");
            Environment.SetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar, " roe_3447786229_swfoc ");

            var options = InvokeResolveOptionsFromEnvironment();

            options.ForcedWorkshopIds.Should().Equal("1397421866", "3447786229");
            options.ForcedProfileId.Should().Be("roe_3447786229_swfoc");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar, previousWorkshopIds);
            Environment.SetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar, previousProfileId);
        }
    }

    private static object InvokeResolveForcedContext(
        string? commandLine,
        string? modPathRaw,
        IReadOnlyList<string> detectedSteamModIds,
        ProcessLocatorOptions options)
    {
        var method = typeof(ProcessLocator).GetMethod("ResolveForcedContext", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var result = method!.Invoke(null, new object?[] { commandLine, modPathRaw, detectedSteamModIds, options });
        result.Should().NotBeNull();
        return result!;
    }

    private static ProcessLocatorOptions InvokeResolveOptionsFromEnvironment()
    {
        var method = typeof(ProcessLocator).GetMethod("ResolveOptionsFromEnvironment", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        var result = method!.Invoke(null, Array.Empty<object>());
        result.Should().NotBeNull();
        return result!.Should().BeAssignableTo<ProcessLocatorOptions>().Subject;
    }

    private static string? ReadStringProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        property.Should().NotBeNull($"property '{propertyName}' should exist on forced context resolution payload.");
        return property!.GetValue(instance) as string;
    }

    private static IReadOnlyList<string> ReadStringSequenceProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        property.Should().NotBeNull($"property '{propertyName}' should exist on forced context resolution payload.");
        var value = property!.GetValue(instance);
        value.Should().BeAssignableTo<IEnumerable<string>>();
        return value!.Should().BeAssignableTo<IEnumerable<string>>().Subject.ToArray();
    }
}
