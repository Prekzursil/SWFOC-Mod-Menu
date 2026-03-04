#pragma warning disable CA1014
using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class ProcessLocatorAdditionalCoverageTests
{
    [Fact]
    public void GetProcessDetection_ShouldUseCmdlineModMarkersHeuristic()
    {
        var detection = InvokePrivateStatic("GetProcessDetection", "custom.exe", @"C:\Games\custom.exe", "custom.exe MODPATH=Mods/AOTR");

        detection.Should().NotBeNull();
        ReadProperty<ExeTarget>(detection!, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<string>(detection!, "DetectedVia").Should().Be("cmdline_mod_markers");
    }

    [Fact]
    public void GetProcessDetection_ShouldReturnUnknown_WhenNoHintsPresent()
    {
        var detection = InvokePrivateStatic("GetProcessDetection", "custom.exe", @"C:\Games\custom.exe", "");

        detection.Should().NotBeNull();
        ReadProperty<ExeTarget>(detection!, "ExeTarget").Should().Be(ExeTarget.Unknown);
        ReadProperty<string>(detection!, "DetectedVia").Should().Be("unknown");
    }

    [Theory]
    [InlineData("starwarsg.exe sweaw.exe", ExeTarget.Sweaw, "starwarsg_cmdline_sweaw_hint")]
    [InlineData("starwarsg.exe steammod=123", ExeTarget.Swfoc, "starwarsg_cmdline_foc_hint")]
    [InlineData("starwarsg.exe modpath=Mods/abc", ExeTarget.Swfoc, "starwarsg_cmdline_foc_hint")]
    [InlineData("starwarsg.exe corruption", ExeTarget.Swfoc, "starwarsg_cmdline_foc_hint")]
    public void TryDetectStarWarsGFromCommandLine_ShouldMapHints(string commandLine, ExeTarget expectedTarget, string expectedVia)
    {
        var detection = InvokePrivateStatic("TryDetectStarWarsGFromCommandLine", commandLine);

        detection.Should().NotBeNull();
        ReadProperty<ExeTarget>(detection!, "ExeTarget").Should().Be(expectedTarget);
        ReadProperty<string>(detection!, "DetectedVia").Should().Be(expectedVia);
    }

    [Fact]
    public void TryDetectStarWarsGFromCommandLine_ShouldReturnNull_WhenNoMatchingHints()
    {
        var detection = InvokePrivateStatic("TryDetectStarWarsGFromCommandLine", "starwarsg.exe --windowed");

        detection.Should().BeNull();
    }

    [Theory]
    [InlineData(@"C:\Games\Corruption\StarWarsG.exe", "", "starwarsg_path_corruption")]
    [InlineData(@"C:\Games\GameData\StarWarsG.exe", "", "starwarsg_path_gamedata_foc_safe")]
    [InlineData(@"C:\Games\Unknown\StarWarsG.exe", "", "starwarsg_default_foc_safe")]
    public void TryDetectStarWarsG_ShouldUsePathFallbacks(string processPath, string commandLine, string expectedVia)
    {
        var detection = InvokePrivateStatic("TryDetectStarWarsG", "StarWarsG.exe", processPath, commandLine);

        detection.Should().NotBeNull();
        ReadProperty<ExeTarget>(detection!, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<string>(detection!, "DetectedVia").Should().Be(expectedVia);
    }

    [Fact]
    public void TryDetectStarWarsG_ShouldReturnNull_WhenProcessIsNotStarWarsG()
    {
        var detection = InvokePrivateStatic("TryDetectStarWarsG", "other.exe", @"C:\Games\other.exe", "");

        detection.Should().BeNull();
    }

    [Theory]
    [InlineData("sweaw.exe", @"C:\Game\sweaw.exe", null, true)]
    [InlineData("swfoc", @"C:\Game\swfoc.exe", null, true)]
    [InlineData("unknown", @"C:\Game\abc.exe", "contains swfoc.exe", true)]
    [InlineData("unknown", @"C:\Game\abc.exe", null, false)]
    public void TryDetectDirectTarget_ShouldMatchKnownTargets(string processName, string processPath, string? commandLine, bool expectedFound)
    {
        var method = typeof(ProcessLocator).GetMethod("TryDetectDirectTarget", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var invokeArgs = new object?[] { processName, processPath, commandLine, null };
        var found = (bool)method!.Invoke(null, invokeArgs)!;

        found.Should().Be(expectedFound);
    }

    [Fact]
    public void UtilityMethods_ShouldCoverNameAndTokenBranches()
    {
        ((bool)InvokePrivateStatic("ContainsToken", "abc DEF", "def")!).Should().BeTrue();
        ((bool)InvokePrivateStatic("ContainsToken", null, "def")!).Should().BeFalse();

        ((bool)InvokePrivateStatic("IsProcessName", "swfoc.exe", "swfoc")!).Should().BeTrue();
        ((bool)InvokePrivateStatic("IsProcessName", " SWFOC ", "swfoc")!).Should().BeFalse();
        ((bool)InvokePrivateStatic("IsProcessName", null, "swfoc")!).Should().BeFalse();
    }

    [Fact]
    public void NormalizeWorkshopIds_ShouldDeduplicateSortAndIgnoreWhitespace()
    {
        var ids = (IReadOnlyList<string>)InvokePrivateStatic(
            "NormalizeWorkshopIds",
            (IReadOnlyList<string>)new[] { " 3447786229,1397421866 ", "", "1397421866", "  " })!;

        ids.Should().Equal("1397421866", "3447786229");
    }

    [Fact]
    public void DetermineHostRole_ShouldMapStarWarsGAndLauncherCases()
    {
        var starWarsGDetection = InvokePrivateStatic("GetProcessDetection", "StarWarsG.exe", @"C:\Games\Corruption\StarWarsG.exe", "");
        var sweawDetection = InvokePrivateStatic("GetProcessDetection", "sweaw.exe", @"C:\Games\sweaw.exe", "");
        var unknownDetection = InvokePrivateStatic("GetProcessDetection", "random.exe", @"C:\Games\random.exe", "");

        ((ProcessHostRole)InvokePrivateStatic("DetermineHostRole", starWarsGDetection)!).Should().Be(ProcessHostRole.GameHost);
        ((ProcessHostRole)InvokePrivateStatic("DetermineHostRole", sweawDetection)!).Should().Be(ProcessHostRole.Launcher);
        ((ProcessHostRole)InvokePrivateStatic("DetermineHostRole", unknownDetection)!).Should().Be(ProcessHostRole.Unknown);
    }

    [Fact]
    public void ResolveForcedContext_ShouldReturnDetected_WhenNoForcedHints()
    {
        var resolution = InvokePrivateStatic(
            "ResolveForcedContext",
            "StarWarsG.exe",
            null,
            new[] { "1397421866" },
            ProcessLocatorOptions.None);

        resolution.Should().NotBeNull();
        ReadProperty<string>(resolution!, "Source").Should().Be("detected");
        ReadStringSequenceProperty(resolution!, "EffectiveSteamModIds").Should().Equal("1397421866");
    }

    private static object? InvokePrivateStatic(string methodName, params object?[] args)
    {
        var method = typeof(ProcessLocator).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull($"Expected private static method '{methodName}'");
        return method!.Invoke(null, args);
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        property.Should().NotBeNull();
        return (T)property!.GetValue(instance)!;
    }

    private static IReadOnlyList<string> ReadStringSequenceProperty(object instance, string propertyName)
    {
        var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        property.Should().NotBeNull();
        return ((IEnumerable<string>)property!.GetValue(instance)!).ToArray();
    }
}

#pragma warning restore CA1014

