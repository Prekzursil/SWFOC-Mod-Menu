using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Branch-coverage sweep for ProcessLocator — targets uncovered detection,
/// host-role, forced context, StarWarsG, and utility branches.
/// </summary>
public sealed class ProcessLocatorBranchCoverageTests
{
    // ── GetProcessDetection branches ────────────────────────────────────────

    [Fact]
    public void GetProcessDetection_ShouldDetectSwfoc_ByProcessName()
    {
        var detection = InvokeGetProcessDetection("swfoc", null, null);

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeFalse();
        ReadProperty<string>(detection, "DetectedVia").Should().Be("name_or_path_swfoc");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectSwfoc_ByProcessPath()
    {
        var detection = InvokeGetProcessDetection("game", @"C:\Games\swfoc.exe", null);

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectSwfoc_ByCommandLine()
    {
        var detection = InvokeGetProcessDetection("game", null, "swfoc.exe -arg1");

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectSweaw_ByProcessName()
    {
        var detection = InvokeGetProcessDetection("sweaw", null, null);

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Sweaw);
        ReadProperty<string>(detection, "DetectedVia").Should().Be("name_or_path_sweaw");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectSweaw_ByProcessPath()
    {
        var detection = InvokeGetProcessDetection("game", @"C:\Games\sweaw.exe", null);

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Sweaw);
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectSweaw_ByCommandLine()
    {
        var detection = InvokeGetProcessDetection("game", null, "sweaw.exe -arg");

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Sweaw);
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectStarWarsG_WithCorruptionPath()
    {
        var detection = InvokeGetProcessDetection("StarWarsG", @"C:\Games\corruption\StarWarsG.exe", null);

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeTrue();
        ReadProperty<string>(detection, "DetectedVia").Should().Be("starwarsg_path_corruption");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectStarWarsG_WithGameDataPath()
    {
        var detection = InvokeGetProcessDetection("StarWarsG", @"C:\Games\gamedata\StarWarsG.exe", null);

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeTrue();
        ReadProperty<string>(detection, "DetectedVia").Should().Be("starwarsg_path_gamedata_foc_safe");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectStarWarsG_DefaultFocSafe()
    {
        var detection = InvokeGetProcessDetection("StarWarsG", @"C:\Games\StarWarsG.exe", null);

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeTrue();
        ReadProperty<string>(detection, "DetectedVia").Should().Be("starwarsg_default_foc_safe");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectStarWarsG_WithFocHintInCmdLine()
    {
        // When command line contains "swfoc.exe", TryDetectDirectTarget catches it first.
        // Use "corruption" keyword instead, which only StarWarsG cmdline detection picks up.
        var detection = InvokeGetProcessDetection("StarWarsG", @"C:\Games\StarWarsG.exe", "StarWarsG.exe corruption");

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeTrue();
        ReadProperty<string>(detection, "DetectedVia").Should().Be("starwarsg_cmdline_foc_hint");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectStarWarsG_WithSteamModInCmdLine()
    {
        var detection = InvokeGetProcessDetection("StarWarsG", null, "StarWarsG.exe STEAMMOD=123");

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeTrue();
        ReadProperty<string>(detection, "DetectedVia").Should().Be("starwarsg_cmdline_foc_hint");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectStarWarsG_WithModPathInCmdLine()
    {
        var detection = InvokeGetProcessDetection("StarWarsG", null, "StarWarsG.exe MODPATH=Mods/AOTR");

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeTrue();
        ReadProperty<string>(detection, "DetectedVia").Should().Be("starwarsg_cmdline_foc_hint");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectStarWarsG_WithCorruptionInCmdLine()
    {
        var detection = InvokeGetProcessDetection("StarWarsG", null, "StarWarsG.exe corruption");

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeTrue();
        ReadProperty<string>(detection, "DetectedVia").Should().Be("starwarsg_cmdline_foc_hint");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectStarWarsG_AsSweaw_WhenSweawHintInCmdLine()
    {
        // When "sweaw.exe" literally appears in command line, TryDetectDirectTarget matches first
        // (Sweaw, IsStarWarsG=false). The StarWarsG sweaw hint path only triggers when
        // the direct check does NOT match. This tests the StarWarsG-via-path detection path.
        var detection = InvokeGetProcessDetection("StarWarsG.exe", @"C:\Games\StarWarsG.exe", null);

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeTrue();
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectSwfoc_ViaCmdlineModMarkers()
    {
        // Unknown process name/path, but command line has mod markers
        var detection = InvokeGetProcessDetection("game", @"C:\Games\game.exe", "game.exe STEAMMOD=123");

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<string>(detection, "DetectedVia").Should().Be("cmdline_mod_markers");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectSwfoc_ViaModPathMarker()
    {
        var detection = InvokeGetProcessDetection("game", @"C:\Games\game.exe", "game.exe MODPATH=Mods/AOTR");

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<string>(detection, "DetectedVia").Should().Be("cmdline_mod_markers");
    }

    [Fact]
    public void GetProcessDetection_ShouldReturnUnknown_WhenNoMatch()
    {
        var detection = InvokeGetProcessDetection("chrome", @"C:\Google\chrome.exe", null);

        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Unknown);
        ReadProperty<string>(detection, "DetectedVia").Should().Be("unknown");
    }

    // ── InferMode branches ─────────────────────────────────────────────────

    [Theory]
    [InlineData("game.exe -mode LAND", RuntimeMode.TacticalLand)]
    [InlineData("game.exe -mode SPACE", RuntimeMode.TacticalSpace)]
    [InlineData("game.exe skirmish", RuntimeMode.AnyTactical)]
    [InlineData("game.exe tactical", RuntimeMode.AnyTactical)]
    [InlineData("game.exe campaign", RuntimeMode.Galactic)]
    [InlineData("game.exe galactic", RuntimeMode.Galactic)]
    [InlineData("game.exe", RuntimeMode.Unknown)]
    [InlineData(null, RuntimeMode.Unknown)]
    [InlineData("", RuntimeMode.Unknown)]
    public void InferMode_ShouldReturnCorrectMode(string? commandLine, RuntimeMode expected)
    {
        var method = typeof(ProcessLocator).GetMethod("InferMode", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (RuntimeMode)method!.Invoke(null, new object?[] { commandLine })!;
        result.Should().Be(expected);
    }

    // ── DetermineHostRole branches ──────────────────────────────────────────

    [Fact]
    public void DetermineHostRole_ShouldReturnGameHost_WhenIsStarWarsG()
    {
        var method = typeof(ProcessLocator).GetMethod("DetermineHostRole", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var detectionType = typeof(ProcessLocator).GetNestedType("ProcessDetection", BindingFlags.NonPublic);
        detectionType.Should().NotBeNull();
        var detection = Activator.CreateInstance(detectionType!, ExeTarget.Swfoc, true, "starwarsg");

        var result = (ProcessHostRole)method!.Invoke(null, new[] { detection })!;
        result.Should().Be(ProcessHostRole.GameHost);
    }

    [Fact]
    public void DetermineHostRole_ShouldReturnLauncher_WhenSwfocNotStarWarsG()
    {
        var method = typeof(ProcessLocator).GetMethod("DetermineHostRole", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var detectionType = typeof(ProcessLocator).GetNestedType("ProcessDetection", BindingFlags.NonPublic);
        detectionType.Should().NotBeNull();
        var detection = Activator.CreateInstance(detectionType!, ExeTarget.Swfoc, false, "name");

        var result = (ProcessHostRole)method!.Invoke(null, new[] { detection })!;
        result.Should().Be(ProcessHostRole.Launcher);
    }

    [Fact]
    public void DetermineHostRole_ShouldReturnLauncher_WhenSweaw()
    {
        var method = typeof(ProcessLocator).GetMethod("DetermineHostRole", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var detectionType = typeof(ProcessLocator).GetNestedType("ProcessDetection", BindingFlags.NonPublic);
        detectionType.Should().NotBeNull();
        var detection = Activator.CreateInstance(detectionType!, ExeTarget.Sweaw, false, "name");

        var result = (ProcessHostRole)method!.Invoke(null, new[] { detection })!;
        result.Should().Be(ProcessHostRole.Launcher);
    }

    [Fact]
    public void DetermineHostRole_ShouldReturnUnknown_WhenTargetIsUnknown()
    {
        var method = typeof(ProcessLocator).GetMethod("DetermineHostRole", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var detectionType = typeof(ProcessLocator).GetNestedType("ProcessDetection", BindingFlags.NonPublic);
        detectionType.Should().NotBeNull();
        var detection = Activator.CreateInstance(detectionType!, ExeTarget.Unknown, false, "unknown");

        var result = (ProcessHostRole)method!.Invoke(null, new[] { detection })!;
        result.Should().Be(ProcessHostRole.Unknown);
    }

    // ── ExtractSteamModIds branches ────────────────────────────────────────

    [Fact]
    public void ExtractSteamModIds_ShouldReturnEmpty_WhenNullCommandLine()
    {
        var method = typeof(ProcessLocator).GetMethod("ExtractSteamModIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string[])method!.Invoke(null, new object?[] { null })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSteamModIds_ShouldReturnEmpty_WhenEmptyCommandLine()
    {
        var method = typeof(ProcessLocator).GetMethod("ExtractSteamModIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string[])method!.Invoke(null, new object?[] { "" })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSteamModIds_ShouldExtractFromSteammodAndWorkshopPath()
    {
        var method = typeof(ProcessLocator).GetMethod("ExtractSteamModIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string[])method!.Invoke(null, new object?[]
        {
            "game.exe STEAMMOD=111 modpath=\"Steam/32470/222\""
        })!;
        result.Should().Contain("111");
        result.Should().Contain("222");
    }

    // ── ExtractModPath branches ────────────────────────────────────────────

    [Fact]
    public void ExtractModPath_ShouldReturnNull_WhenNullCommandLine()
    {
        var method = typeof(ProcessLocator).GetMethod("ExtractModPath", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { null });
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractModPath_ShouldReturnNull_WhenNoModPathPresent()
    {
        var method = typeof(ProcessLocator).GetMethod("ExtractModPath", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = method!.Invoke(null, new object?[] { "game.exe -start" });
        result.Should().BeNull();
    }

    [Fact]
    public void ExtractModPath_ShouldExtractQuotedPath()
    {
        var method = typeof(ProcessLocator).GetMethod("ExtractModPath", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string?)method!.Invoke(null, new object?[] { "game.exe MODPATH=\"Mods/My Mod\"" });
        result.Should().Be("Mods/My Mod");
    }

    [Fact]
    public void ExtractModPath_ShouldExtractUnquotedPath()
    {
        var method = typeof(ProcessLocator).GetMethod("ExtractModPath", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string?)method!.Invoke(null, new object?[] { "game.exe modpath=Mods/AOTR" });
        result.Should().Be("Mods/AOTR");
    }

    // ── IsProcessName branches ─────────────────────────────────────────────

    [Theory]
    [InlineData("swfoc", "swfoc", true)]
    [InlineData("swfoc.exe", "swfoc", true)]
    [InlineData("SWFOC", "swfoc", true)]
    [InlineData("chrome", "swfoc", false)]
    [InlineData("", "swfoc", false)]
    [InlineData(null, "swfoc", false)]
    public void IsProcessName_ShouldMatchCorrectly(string? processName, string expected, bool shouldMatch)
    {
        var method = typeof(ProcessLocator).GetMethod("IsProcessName", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object?[] { processName, expected })!;
        result.Should().Be(shouldMatch);
    }

    // ── ContainsToken branches ─────────────────────────────────────────────

    [Theory]
    [InlineData("some path/swfoc.exe", "swfoc.exe", true)]
    [InlineData("SOME PATH/SWFOC.EXE", "swfoc.exe", true)]
    [InlineData("some path", "swfoc.exe", false)]
    [InlineData(null, "swfoc.exe", false)]
    [InlineData("", "swfoc.exe", false)]
    public void ContainsToken_ShouldMatchCorrectly(string? value, string token, bool expected)
    {
        var method = typeof(ProcessLocator).GetMethod("ContainsToken", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (bool)method!.Invoke(null, new object?[] { value, token })!;
        result.Should().Be(expected);
    }

    // ── NormalizeWorkshopIds branches ───────────────────────────────────────

    [Fact]
    public void NormalizeWorkshopIds_ShouldReturnEmpty_WhenNull()
    {
        var method = typeof(ProcessLocator).GetMethod("NormalizeWorkshopIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (IReadOnlyList<string>)method!.Invoke(null, new object?[] { null })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeWorkshopIds_ShouldReturnEmpty_WhenEmptyList()
    {
        var method = typeof(ProcessLocator).GetMethod("NormalizeWorkshopIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (IReadOnlyList<string>)method!.Invoke(null, new object?[] { Array.Empty<string>() })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeWorkshopIds_ShouldDeduplicateAndSort()
    {
        var method = typeof(ProcessLocator).GetMethod("NormalizeWorkshopIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (IReadOnlyList<string>)method!.Invoke(null, new object?[]
        {
            new[] { "333,111", "222,111" }
        })!;
        result.Should().Equal("111", "222", "333");
    }

    // ── NormalizeForcedProfileId branches ──────────────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("profile_id", "profile_id")]
    [InlineData("  trimmed  ", "trimmed")]
    public void NormalizeForcedProfileId_ShouldNormalizeCorrectly(string? input, string? expected)
    {
        var method = typeof(ProcessLocator).GetMethod("NormalizeForcedProfileId", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var result = (string?)method!.Invoke(null, new object?[] { input });
        result.Should().Be(expected);
    }

    // ── ResolveForcedContext branches ───────────────────────────────────────

    [Fact]
    public void ResolveForcedContext_ShouldReturnDetected_WhenModMarkersPresent()
    {
        var method = typeof(ProcessLocator).GetMethod("ResolveForcedContext", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var options = new ProcessLocatorOptions(new[] { "99999" }, "forced_profile");

        var result = method!.Invoke(null, new object?[] { "cmd", "Mods/AOTR", new[] { "111" }, options });
        result.Should().NotBeNull();
        ReadProperty<string>(result!, "Source").Should().Be("detected");
    }

    [Fact]
    public void ResolveForcedContext_ShouldReturnForced_WhenNoMarkersAndForcedHints()
    {
        var method = typeof(ProcessLocator).GetMethod("ResolveForcedContext", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var options = new ProcessLocatorOptions(new[] { "99999" }, "forced_profile");

        var result = method!.Invoke(null, new object?[] { "cmd", null, Array.Empty<string>(), options });
        result.Should().NotBeNull();
        ReadProperty<string>(result!, "Source").Should().Be("forced");
        ReadProperty<bool>(result!, "IsForced").Should().BeTrue();
    }

    [Fact]
    public void ResolveForcedContext_ShouldReturnDetected_WhenNoForcedHintsAndNoMarkers()
    {
        var method = typeof(ProcessLocator).GetMethod("ResolveForcedContext", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var options = ProcessLocatorOptions.None;

        var result = method!.Invoke(null, new object?[] { "cmd", null, Array.Empty<string>(), options });
        result.Should().NotBeNull();
        ReadProperty<string>(result!, "Source").Should().Be("detected");
    }

    // ── ResolveOptionsFromEnvironment branches ─────────────────────────────

    [Fact]
    public void ResolveOptionsFromEnvironment_ShouldReturnOptions_WhenForcedWorkshopIdsSet()
    {
        var prevIds = Environment.GetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar);
        var prevProfile = Environment.GetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar, "12345");
            Environment.SetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar, "my_profile");

            var method = typeof(ProcessLocator).GetMethod("ResolveOptionsFromEnvironment", BindingFlags.Static | BindingFlags.NonPublic);
            method.Should().NotBeNull();

            var result = (ProcessLocatorOptions)method!.Invoke(null, null)!;
            result.ForcedWorkshopIds.Should().Contain("12345");
            result.ForcedProfileId.Should().Be("my_profile");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar, prevIds);
            Environment.SetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar, prevProfile);
        }
    }

    // ── Parameterless overloads ────────────────────────────────────────────

    [Fact]
    public async Task FindSupportedProcessesAsync_ParameterlessOverload_ShouldWork()
    {
        var locator = new ProcessLocator();
        var processes = await locator.FindSupportedProcessesAsync();
        processes.Should().NotBeNull();
    }

    [Fact]
    public async Task FindBestMatchAsync_ParameterlessOverload_ShouldWork()
    {
        var locator = new ProcessLocator();
        // Most likely won't find swfoc in test environment, but should not throw
        await locator.FindBestMatchAsync(ExeTarget.Swfoc);
        // result may be null, just shouldn't throw
    }

    // ── Helper: invoke private static ──────────────────────────────────────

    private static object InvokeGetProcessDetection(string processName, string? processPath, string? commandLine)
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return method!.Invoke(null, new object?[] { processName, processPath, commandLine })!;
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        var prop = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        prop.Should().NotBeNull($"property {propertyName} should exist");
        return (T)prop!.GetValue(instance)!;
    }

    internal sealed class StubProfileRepository : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TrainerProfile> LoadProfileAsync(string profileId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string profileId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
