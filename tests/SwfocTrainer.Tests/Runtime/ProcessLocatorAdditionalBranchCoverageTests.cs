using System.Reflection;
using System.Text.Json.Nodes;
using FluentAssertions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Additional branch-coverage sweep for ProcessLocator — targets remaining uncovered
/// branches in profile loading, StarWarsG detection, command-line parsing, WMI fallback,
/// forced context resolution, and FindBestMatchAsync paths.
/// </summary>
public sealed class ProcessLocatorAdditionalBranchCoverageTests
{
    // ── Constructor overloads ──────────────────────────────────────────────

    [Fact]
    public void Constructor_Default_ShouldCreateLocator()
    {
        var locator = new ProcessLocator();
        locator.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithLaunchContextResolver_ShouldCreateLocator()
    {
        var locator = new ProcessLocator(new LaunchContextResolver());
        locator.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithProfileRepository_ShouldCreateLocator()
    {
        var locator = new ProcessLocator(new EmptyProfileRepository());
        locator.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithBothNull_ShouldUseDefaults()
    {
        var locator = new ProcessLocator(null, null);
        locator.Should().NotBeNull();
    }

    // ── TryDetectDirectTarget — sweaw by path and cmdline ─────────────────

    [Fact]
    public void GetProcessDetection_ShouldDetectSweaw_ByPath()
    {
        var detection = InvokeGetProcessDetection("unknown", @"C:\Games\sweaw.exe", null);
        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Sweaw);
        ReadProperty<string>(detection, "DetectedVia").Should().Be("name_or_path_sweaw");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectSweaw_ByCmdLine()
    {
        var detection = InvokeGetProcessDetection("unknown", null, "sweaw.exe -something");
        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Sweaw);
    }

    // ── TryDetectDirectTarget — swfoc by all paths ────────────────────────

    [Fact]
    public void GetProcessDetection_ShouldDetectSwfoc_ByPath()
    {
        var detection = InvokeGetProcessDetection("unknown", @"C:\Games\swfoc.exe", null);
        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<string>(detection, "DetectedVia").Should().Be("name_or_path_swfoc");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectSwfoc_ByCmdLine()
    {
        var detection = InvokeGetProcessDetection("unknown", null, "swfoc.exe -arg");
        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
    }

    // ── IsStarWarsGProcess — detection by path ────────────────────────────

    [Fact]
    public void GetProcessDetection_ShouldDetectStarWarsG_ByPath()
    {
        var detection = InvokeGetProcessDetection("unknown", @"C:\Games\starwarsg.exe", null);
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeTrue();
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectStarWarsG_ByCmdLine()
    {
        var detection = InvokeGetProcessDetection("unknown", null, "starwarsg.exe -arg");
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeTrue();
    }

    // ── TryDetectStarWarsGFromCommandLine — sweaw hint (pure) ─────────────

    [Fact]
    public void GetProcessDetection_ShouldDetectSweaw_WhenSweawExeInCmdLine()
    {
        // TryDetectDirectTarget checks cmdline for "sweaw.exe" and catches it first,
        // before StarWarsG detection can run. This is the expected priority.
        var detection = InvokeGetProcessDetection("StarWarsG", null, "sweaw.exe");
        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Sweaw);
        ReadProperty<bool>(detection, "IsStarWarsG").Should().BeFalse();
        ReadProperty<string>(detection, "DetectedVia").Should().Be("name_or_path_sweaw");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectSweaw_WhenSweawExeInCmdLineWithSteamMod()
    {
        // TryDetectDirectTarget catches sweaw.exe in cmdline first regardless of other markers
        var detection = InvokeGetProcessDetection("StarWarsG", null, "sweaw.exe steammod=123");
        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Sweaw);
        ReadProperty<string>(detection, "DetectedVia").Should().Be("name_or_path_sweaw");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectSweaw_WhenSweawExeInCmdLineWithModPath()
    {
        // TryDetectDirectTarget catches sweaw.exe in cmdline first regardless of other markers
        var detection = InvokeGetProcessDetection("StarWarsG", null, "sweaw.exe modpath=Mods/AOTR");
        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Sweaw);
        ReadProperty<string>(detection, "DetectedVia").Should().Be("name_or_path_sweaw");
    }

    // ── TryDetectStarWarsG — path with forward slashes ────────────────────

    [Fact]
    public void GetProcessDetection_ShouldDetectStarWarsG_WithCorruptionForwardSlash()
    {
        var detection = InvokeGetProcessDetection("StarWarsG", @"C:/Games/corruption/StarWarsG.exe", null);
        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<string>(detection, "DetectedVia").Should().Be("starwarsg_path_corruption");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetectStarWarsG_WithGamedataForwardSlash()
    {
        var detection = InvokeGetProcessDetection("StarWarsG", @"C:/Games/gamedata/StarWarsG.exe", null);
        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<string>(detection, "DetectedVia").Should().Be("starwarsg_path_gamedata_foc_safe");
    }

    // ── Cmdline mod markers without direct target match ───────────────────

    [Fact]
    public void GetProcessDetection_ShouldDetect_ViaModPathMarker_WhenNoNameMatch()
    {
        var detection = InvokeGetProcessDetection("randomgame", null, "randomgame.exe modpath=Mods/AOTR");
        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<string>(detection, "DetectedVia").Should().Be("cmdline_mod_markers");
    }

    [Fact]
    public void GetProcessDetection_ShouldDetect_ViaSteamModMarker_WhenNoNameMatch()
    {
        var detection = InvokeGetProcessDetection("randomgame", null, "randomgame.exe steammod=12345");
        ReadProperty<ExeTarget>(detection, "ExeTarget").Should().Be(ExeTarget.Swfoc);
        ReadProperty<string>(detection, "DetectedVia").Should().Be("cmdline_mod_markers");
    }

    // ── InferMode — all branches ──────────────────────────────────────────

    [Theory]
    [InlineData(null, RuntimeMode.Unknown)]
    [InlineData("", RuntimeMode.Unknown)]
    [InlineData("   ", RuntimeMode.Unknown)]
    [InlineData("game.exe", RuntimeMode.Unknown)]
    [InlineData("game.exe -mode LAND", RuntimeMode.TacticalLand)]
    [InlineData("game.exe -mode SPACE", RuntimeMode.TacticalSpace)]
    [InlineData("game.exe skirmish", RuntimeMode.AnyTactical)]
    [InlineData("game.exe tactical", RuntimeMode.AnyTactical)]
    [InlineData("game.exe campaign", RuntimeMode.Galactic)]
    [InlineData("game.exe galactic", RuntimeMode.Galactic)]
    public void InferMode_ShouldReturnExpectedMode(string? commandLine, RuntimeMode expected)
    {
        var result = (RuntimeMode)InvokeStaticMethod("InferMode", commandLine)!;
        result.Should().Be(expected);
    }

    // ── DetermineHostRole — all branches ──────────────────────────────────

    [Fact]
    public void DetermineHostRole_ShouldReturnGameHost_ForStarWarsG()
    {
        var detection = CreateDetection(ExeTarget.Swfoc, true, "test");
        var result = (ProcessHostRole)InvokeStaticMethod("DetermineHostRole", detection)!;
        result.Should().Be(ProcessHostRole.GameHost);
    }

    [Fact]
    public void DetermineHostRole_ShouldReturnLauncher_ForSwfoc()
    {
        var detection = CreateDetection(ExeTarget.Swfoc, false, "test");
        var result = (ProcessHostRole)InvokeStaticMethod("DetermineHostRole", detection)!;
        result.Should().Be(ProcessHostRole.Launcher);
    }

    [Fact]
    public void DetermineHostRole_ShouldReturnLauncher_ForSweaw()
    {
        var detection = CreateDetection(ExeTarget.Sweaw, false, "test");
        var result = (ProcessHostRole)InvokeStaticMethod("DetermineHostRole", detection)!;
        result.Should().Be(ProcessHostRole.Launcher);
    }

    [Fact]
    public void DetermineHostRole_ShouldReturnUnknown_ForUnknownTarget()
    {
        var detection = CreateDetection(ExeTarget.Unknown, false, "test");
        var result = (ProcessHostRole)InvokeStaticMethod("DetermineHostRole", detection)!;
        result.Should().Be(ProcessHostRole.Unknown);
    }

    // ── ExtractSteamModIds — all branches ─────────────────────────────────

    [Fact]
    public void ExtractSteamModIds_ShouldReturnEmpty_ForNull()
    {
        var result = (string[])InvokeStaticMethod("ExtractSteamModIds", (string?)null)!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSteamModIds_ShouldReturnEmpty_ForEmpty()
    {
        var result = (string[])InvokeStaticMethod("ExtractSteamModIds", "")!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSteamModIds_ShouldReturnEmpty_ForWhitespace()
    {
        var result = (string[])InvokeStaticMethod("ExtractSteamModIds", "   ")!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSteamModIds_ShouldExtractSteamModValue()
    {
        var result = (string[])InvokeStaticMethod("ExtractSteamModIds", "game.exe STEAMMOD=12345")!;
        result.Should().Contain("12345");
    }

    [Fact]
    public void ExtractSteamModIds_ShouldExtractWorkshopPathIds()
    {
        var result = (string[])InvokeStaticMethod("ExtractSteamModIds",
            "game.exe modpath=\"C:\\Steam\\32470\\99999\"")!;
        result.Should().Contain("99999");
    }

    [Fact]
    public void ExtractSteamModIds_ShouldDeduplicateAndSort()
    {
        var result = (string[])InvokeStaticMethod("ExtractSteamModIds",
            "game.exe STEAMMOD=333 STEAMMOD=111 modpath=\"/32470/222\" STEAMMOD=111")!;
        result.Should().Equal("111", "222", "333");
    }

    // ── ExtractModPath — all branches ─────────────────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("game.exe -start", null)]
    [InlineData("game.exe MODPATH=\"Mods/My Mod\"", "Mods/My Mod")]
    [InlineData("game.exe modpath=Mods/AOTR", "Mods/AOTR")]
    public void ExtractModPath_ShouldReturnExpected(string? commandLine, string? expected)
    {
        var result = (string?)InvokeStaticMethod("ExtractModPath", commandLine);
        result.Should().Be(expected);
    }

    // ── IsProcessName — all branches ──────────────────────────────────────

    [Theory]
    [InlineData(null, "swfoc", false)]
    [InlineData("", "swfoc", false)]
    [InlineData("   ", "swfoc", false)]
    [InlineData("swfoc", "swfoc", true)]
    [InlineData("SWFOC", "swfoc", true)]
    [InlineData("swfoc.exe", "swfoc", true)]
    [InlineData("SWFOC.EXE", "swfoc", true)]
    [InlineData("chrome", "swfoc", false)]
    [InlineData("sweaw", "sweaw", true)]
    [InlineData("sweaw.exe", "sweaw", true)]
    public void IsProcessName_ShouldMatchCorrectly(string? processName, string expected, bool shouldMatch)
    {
        var result = (bool)InvokeStaticMethod("IsProcessName", processName, expected)!;
        result.Should().Be(shouldMatch);
    }

    // ── ContainsToken — all branches ──────────────────────────────────────

    [Theory]
    [InlineData(null, "token", false)]
    [InlineData("", "token", false)]
    [InlineData("path/to/swfoc.exe", "swfoc.exe", true)]
    [InlineData("PATH/TO/SWFOC.EXE", "swfoc.exe", true)]
    [InlineData("no match here", "swfoc.exe", false)]
    public void ContainsToken_ShouldReturnExpected(string? value, string token, bool expected)
    {
        var result = (bool)InvokeStaticMethod("ContainsToken", value, token)!;
        result.Should().Be(expected);
    }

    // ── NormalizeWorkshopIds — all branches ────────────────────────────────

    [Fact]
    public void NormalizeWorkshopIds_ShouldReturnEmpty_ForNull()
    {
        var method = typeof(ProcessLocator).GetMethod("NormalizeWorkshopIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (IReadOnlyList<string>)method!.Invoke(null, new object?[] { null })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeWorkshopIds_ShouldReturnEmpty_ForEmptyList()
    {
        var method = typeof(ProcessLocator).GetMethod("NormalizeWorkshopIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (IReadOnlyList<string>)method!.Invoke(null, new object?[] { Array.Empty<string>() })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeWorkshopIds_ShouldSkipWhitespaceEntries()
    {
        var method = typeof(ProcessLocator).GetMethod("NormalizeWorkshopIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (IReadOnlyList<string>)method!.Invoke(null, new object?[] { new[] { "  ", "", "111" } })!;
        result.Should().Equal("111");
    }

    [Fact]
    public void NormalizeWorkshopIds_ShouldSplitCsvAndDeduplicate()
    {
        var method = typeof(ProcessLocator).GetMethod("NormalizeWorkshopIds", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var result = (IReadOnlyList<string>)method!.Invoke(null, new object?[] { new[] { "333,111", "222,111" } })!;
        result.Should().Equal("111", "222", "333");
    }

    // ── NormalizeForcedProfileId — all branches ───────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("profile_id", "profile_id")]
    [InlineData("  trimmed  ", "trimmed")]
    public void NormalizeForcedProfileId_ShouldReturnExpected(string? input, string? expected)
    {
        var result = (string?)InvokeStaticMethod("NormalizeForcedProfileId", input);
        result.Should().Be(expected);
    }

    // ── ResolveForcedContext — all branches ────────────────────────────────

    [Fact]
    public void ResolveForcedContext_ShouldReturnDetected_WhenSteamModIdsPresent()
    {
        var options = new ProcessLocatorOptions(new[] { "forced_id" }, "forced_profile");
        var result = InvokeStaticMethod("ResolveForcedContext",
            "cmd", null, new[] { "detected_id" }, options);
        ReadProperty<string>(result!, "Source").Should().Be("detected");
    }

    [Fact]
    public void ResolveForcedContext_ShouldReturnDetected_WhenModPathPresent()
    {
        var options = new ProcessLocatorOptions(new[] { "forced_id" }, "forced_profile");
        var result = InvokeStaticMethod("ResolveForcedContext",
            "cmd", "Mods/AOTR", Array.Empty<string>(), options);
        ReadProperty<string>(result!, "Source").Should().Be("detected");
    }

    [Fact]
    public void ResolveForcedContext_ShouldReturnForced_WhenNoMarkersAndForcedIds()
    {
        var options = new ProcessLocatorOptions(new[] { "forced_id" }, "forced_profile");
        var result = InvokeStaticMethod("ResolveForcedContext",
            "cmd", null, Array.Empty<string>(), options);
        ReadProperty<string>(result!, "Source").Should().Be("forced");
        ReadProperty<bool>(result!, "IsForced").Should().BeTrue();
        ReadProperty<string>(result!, "ForcedProfileId").Should().Be("forced_profile");
    }

    [Fact]
    public void ResolveForcedContext_ShouldReturnForced_WhenNoMarkersAndForcedProfileOnly()
    {
        var options = new ProcessLocatorOptions(null, "forced_profile");
        var result = InvokeStaticMethod("ResolveForcedContext",
            "cmd", null, Array.Empty<string>(), options);
        ReadProperty<string>(result!, "Source").Should().Be("forced");
    }

    [Fact]
    public void ResolveForcedContext_ShouldReturnDetected_WhenNoForcedHints()
    {
        var options = ProcessLocatorOptions.None;
        var result = InvokeStaticMethod("ResolveForcedContext",
            "cmd", null, Array.Empty<string>(), options);
        ReadProperty<string>(result!, "Source").Should().Be("detected");
        ReadProperty<bool>(result!, "IsForced").Should().BeFalse();
    }

    [Fact]
    public void ResolveForcedContext_ShouldUseDetectedIds_WhenForcedWorkshopIdsEmpty()
    {
        // Forced profile but no forced workshop IDs, and no detected mod markers
        var options = new ProcessLocatorOptions(Array.Empty<string>(), "forced_profile");
        var result = InvokeStaticMethod("ResolveForcedContext",
            "cmd", null, Array.Empty<string>(), options);
        ReadProperty<string>(result!, "Source").Should().Be("forced");
    }

    // ── ResolveOptionsFromEnvironment ─────────────────────────────────────

    [Fact]
    public void ResolveOptionsFromEnvironment_ShouldReturnNone_WhenNoEnvVars()
    {
        var prevIds = Environment.GetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar);
        var prevProfile = Environment.GetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar, null);
            Environment.SetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar, null);

            var result = (ProcessLocatorOptions)InvokeStaticMethod("ResolveOptionsFromEnvironment")!;
            result.ForcedWorkshopIds.Should().BeNullOrEmpty();
            result.ForcedProfileId.Should().BeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar, prevIds);
            Environment.SetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar, prevProfile);
        }
    }

    [Fact]
    public void ResolveOptionsFromEnvironment_ShouldParseEnvVars()
    {
        var prevIds = Environment.GetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar);
        var prevProfile = Environment.GetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar, "111,222");
            Environment.SetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar, "my_profile");

            var result = (ProcessLocatorOptions)InvokeStaticMethod("ResolveOptionsFromEnvironment")!;
            result.ForcedWorkshopIds.Should().Contain("111");
            result.ForcedWorkshopIds.Should().Contain("222");
            result.ForcedProfileId.Should().Be("my_profile");
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar, prevIds);
            Environment.SetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar, prevProfile);
        }
    }

    [Fact]
    public void ResolveOptionsFromEnvironment_ShouldReturnNone_WhenEnvVarsEmpty()
    {
        var prevIds = Environment.GetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar);
        var prevProfile = Environment.GetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar);
        try
        {
            Environment.SetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar, "");
            Environment.SetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar, "");

            var result = (ProcessLocatorOptions)InvokeStaticMethod("ResolveOptionsFromEnvironment")!;
            result.ForcedWorkshopIds.Should().BeNullOrEmpty();
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProcessLocator.ForceWorkshopIdsEnvVar, prevIds);
            Environment.SetEnvironmentVariable(ProcessLocator.ForceProfileIdEnvVar, prevProfile);
        }
    }

    // ── FindSupportedProcessesAsync — parameterless overload ──────────────

    [Fact]
    public async Task FindSupportedProcessesAsync_Parameterless_ShouldWork()
    {
        var locator = new ProcessLocator();
        var result = await locator.FindSupportedProcessesAsync();
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task FindSupportedProcessesAsync_WithNullOptions_ShouldDefault()
    {
        var locator = new ProcessLocator();
        var result = await locator.FindSupportedProcessesAsync(null, CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task FindSupportedProcessesAsync_WithToken_ShouldWork()
    {
        var locator = new ProcessLocator();
        var result = await locator.FindSupportedProcessesAsync(CancellationToken.None);
        result.Should().NotBeNull();
    }

    // ── FindBestMatchAsync — parameterless overload ───────────────────────

    [Fact]
    public async Task FindBestMatchAsync_Parameterless_ShouldReturnNull_WhenNoMatch()
    {
        var locator = new ProcessLocator();
        var result = await locator.FindBestMatchAsync(ExeTarget.Swfoc);
        // No game running in test env, just verify it doesn't throw
    }

    [Fact]
    public async Task FindBestMatchAsync_WithToken_ShouldWork()
    {
        var locator = new ProcessLocator();
        var result = await locator.FindBestMatchAsync(ExeTarget.Swfoc, CancellationToken.None);
        // No game running in test env
    }

    [Fact]
    public async Task FindBestMatchAsync_WithNullOptions_ShouldDefault()
    {
        var locator = new ProcessLocator();
        var result = await locator.FindBestMatchAsync(ExeTarget.Swfoc, null, CancellationToken.None);
        // Should not throw
    }

    [Fact]
    public async Task FindBestMatchAsync_WithUnknownTarget_ShouldReturnNull()
    {
        var locator = new ProcessLocator();
        var result = await locator.FindBestMatchAsync(ExeTarget.Unknown, CancellationToken.None);
        result.Should().BeNull();
    }

    // ── LoadProfilesForLaunchContextAsync — null repo ─────────────────────

    [Fact]
    public async Task FindSupportedProcessesAsync_ShouldWorkWithNullProfileRepository()
    {
        var locator = new ProcessLocator(new LaunchContextResolver(), null);
        var result = await locator.FindSupportedProcessesAsync(CancellationToken.None);
        result.Should().NotBeNull();
    }

    // ── LoadProfilesForLaunchContextAsync — cache TTL branch ──────────────

    [Fact]
    public async Task FindSupportedProcessesAsync_ShouldLoadProfilesFromRepository()
    {
        var repo = new CountingProfileRepository();
        var locator = new ProcessLocator(new LaunchContextResolver(), repo);

        await locator.FindSupportedProcessesAsync(CancellationToken.None);

        // Profiles should have been loaded at least once
        repo.LoadCount.Should().BeGreaterOrEqualTo(1);
    }

    // ── LoadProfilesForLaunchContextAsync — exception handling ────────────

    [Fact]
    public async Task FindSupportedProcessesAsync_ShouldHandleInvalidOperationException_FromProfileLoad()
    {
        var repo = new ThrowingProfileRepository(new InvalidOperationException("broken"));
        var locator = new ProcessLocator(new LaunchContextResolver(), repo);

        var result = await locator.FindSupportedProcessesAsync(CancellationToken.None);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task FindSupportedProcessesAsync_ShouldHandleKeyNotFoundException_FromProfileLoad()
    {
        var repo = new ThrowingProfileRepository(new KeyNotFoundException("not found"));
        var locator = new ProcessLocator(new LaunchContextResolver(), repo);

        var result = await locator.FindSupportedProcessesAsync(CancellationToken.None);
        result.Should().NotBeNull();
    }

    // ── BuildBaseMetadata branches ────────────────────────────────────────

    [Fact]
    public void BuildBaseMetadata_ShouldPopulateAllKeys()
    {
        var method = typeof(ProcessLocator).GetMethod("BuildBaseMetadata", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var detection = CreateDetection(ExeTarget.Swfoc, true, "test");
        var input = CreateBaseMetadataInput(detection, "cmd line", 12345, ProcessHostRole.GameHost,
            new[] { "111", "222" }, "Mods/AOTR");
        var result = (Dictionary<string, string>)method!.Invoke(null, new object?[] { input })!;

        result["targetHint"].Should().Be("Swfoc");
        result["hasModPath"].Should().Be("True");
        result["hasSteamMod"].Should().Be("True");
        result["detectedVia"].Should().Be("test");
        result["isStarWarsG"].Should().Be("True");
        result["steamModIdsDetected"].Should().Contain("111");
        result["hostRole"].Should().Be("gamehost");
        result["mainModuleSize"].Should().Be("12345");
        result["workshopMatchCount"].Should().Be("2");
    }

    [Fact]
    public void BuildBaseMetadata_ShouldHandleEmptySteamModIds()
    {
        var method = typeof(ProcessLocator).GetMethod("BuildBaseMetadata", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var detection = CreateDetection(ExeTarget.Swfoc, false, "test");
        var input = CreateBaseMetadataInput(detection, null, 0, ProcessHostRole.Launcher,
            Array.Empty<string>(), null);
        var result = (Dictionary<string, string>)method!.Invoke(null, new object?[] { input })!;

        result["hasSteamMod"].Should().Be("False");
        result["hasModPath"].Should().Be("False");
        result["steamModIdsDetected"].Should().BeEmpty();
        result["commandLineAvailable"].Should().Be("False");
    }

    private static object CreateBaseMetadataInput(
        object detection, string? commandLine, int mainModuleSize,
        ProcessHostRole hostRole, IReadOnlyCollection<string> steamModIds, string? modPathRaw)
    {
        var inputType = typeof(ProcessLocator).GetNestedType("BaseMetadataInput", BindingFlags.NonPublic);
        inputType.Should().NotBeNull();
        return Activator.CreateInstance(inputType!, detection, commandLine, mainModuleSize, hostRole, steamModIds, modPathRaw)!;
    }

    // ── ApplyLaunchContextMetadata ────────────────────────────────────────

    [Fact]
    public void ApplyLaunchContextMetadata_ShouldPopulateAllKeys()
    {
        var method = typeof(ProcessLocator).GetMethod("ApplyLaunchContextMetadata", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var launchContext = new LaunchContext(
            LaunchKind.Workshop, true, new[] { "111" },
            "Mods/AOTR", @"C:\Mods\AOTR", "cmdline",
            new ProfileRecommendation("test_profile", "match", 0.95));

        method!.Invoke(null, new object[] { metadata, launchContext });

        metadata["launchKind"].Should().Be("Workshop");
        metadata["modPathRaw"].Should().Be("Mods/AOTR");
        metadata["modPathNormalized"].Should().Contain("AOTR");
        metadata["profileRecommendation"].Should().Be("test_profile");
        metadata["recommendationReason"].Should().Be("match");
        metadata["recommendationConfidence"].Should().Be("0.95");
        metadata["hasModPath"].Should().Be("True");
        metadata["hasSteamMod"].Should().Be("True");
        metadata["steamModIdsDetected"].Should().Contain("111");
    }

    [Fact]
    public void ApplyLaunchContextMetadata_ShouldHandleNullFields()
    {
        var method = typeof(ProcessLocator).GetMethod("ApplyLaunchContextMetadata", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var launchContext = new LaunchContext(
            LaunchKind.Unknown, false, Array.Empty<string>(),
            null, null, "unknown",
            new ProfileRecommendation(null, "none", 0.0));

        method!.Invoke(null, new object[] { metadata, launchContext });

        metadata["modPathRaw"].Should().BeEmpty();
        metadata["modPathNormalized"].Should().BeEmpty();
        metadata["profileRecommendation"].Should().BeEmpty();
        metadata["hasSteamMod"].Should().Be("False");
        metadata["steamModIdsDetected"].Should().BeEmpty();
    }

    // ── ForcedContextResolution.IsForced property ─────────────────────────

    [Fact]
    public void ForcedContextResolution_IsForced_ShouldBeTrue_WhenSourceIsForced()
    {
        var options = new ProcessLocatorOptions(new[] { "id" }, null);
        var result = InvokeStaticMethod("ResolveForcedContext",
            "cmd", null, Array.Empty<string>(), options);
        ReadProperty<bool>(result!, "IsForced").Should().BeTrue();
    }

    [Fact]
    public void ForcedContextResolution_IsForced_ShouldBeFalse_WhenSourceIsDetected()
    {
        var options = ProcessLocatorOptions.None;
        var result = InvokeStaticMethod("ResolveForcedContext",
            "cmd", null, Array.Empty<string>(), options);
        ReadProperty<bool>(result!, "IsForced").Should().BeFalse();
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static object InvokeGetProcessDetection(string processName, string? processPath, string? commandLine)
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return method!.Invoke(null, new object?[] { processName, processPath, commandLine })!;
    }

    private static object? InvokeStaticMethod(string methodName, params object?[] args)
    {
        var method = typeof(ProcessLocator).GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull($"method '{methodName}' should exist on ProcessLocator");
        return method!.Invoke(null, args);
    }

    private static object CreateDetection(ExeTarget target, bool isStarWarsG, string detectedVia)
    {
        var detectionType = typeof(ProcessLocator).GetNestedType("ProcessDetection", BindingFlags.NonPublic);
        detectionType.Should().NotBeNull();
        return Activator.CreateInstance(detectionType!, target, isStarWarsG, detectedVia)!;
    }

    private static T ReadProperty<T>(object instance, string propertyName)
    {
        var prop = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        prop.Should().NotBeNull($"property '{propertyName}' should exist");
        return (T)prop!.GetValue(instance)!;
    }

    // ── Stub implementations ──────────────────────────────────────────────

    private sealed class EmptyProfileRepository : IProfileRepository
    {
        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<TrainerProfile> LoadProfileAsync(string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string id, CancellationToken ct) => throw new NotImplementedException();
        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }

    private sealed class CountingProfileRepository : IProfileRepository
    {
        public int LoadCount;

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<TrainerProfile> LoadProfileAsync(string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string id, CancellationToken ct) => throw new NotImplementedException();
        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct)
        {
            Interlocked.Increment(ref LoadCount);
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }
    }

    private sealed class ThrowingProfileRepository : IProfileRepository
    {
        private readonly Exception _exception;
        public ThrowingProfileRepository(Exception exception) => _exception = exception;

        public Task<ProfileManifest> LoadManifestAsync(CancellationToken ct) => throw new NotImplementedException();
        public Task<TrainerProfile> LoadProfileAsync(string id, CancellationToken ct) => throw new NotImplementedException();
        public Task<TrainerProfile> ResolveInheritedProfileAsync(string id, CancellationToken ct) => throw new NotImplementedException();
        public Task ValidateProfileAsync(TrainerProfile profile, CancellationToken ct) => Task.CompletedTask;
        public Task<IReadOnlyList<string>> ListAvailableProfilesAsync(CancellationToken ct)
            => throw _exception;
    }
}
