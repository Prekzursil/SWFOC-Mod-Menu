using System.Reflection;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

/// <summary>
/// Wave 8 branch-coverage tests for ProcessLocator — targets remaining uncovered
/// branches in InferMode, ExtractModPath, ExtractSteamModIds, NormalizeWorkshopIds,
/// DetermineHostRole, IsProcessName, ContainsToken, TryDetectStarWarsG sub-paths,
/// ResolveForcedContext, and NormalizeForcedProfileId.
/// </summary>
public sealed class ProcessLocatorWave8BranchCoverageTests
{
    private static readonly BindingFlags NonPublicStatic =
        BindingFlags.Static | BindingFlags.NonPublic;

    // ── InferMode ────────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, RuntimeMode.Unknown)]
    [InlineData("", RuntimeMode.Unknown)]
    [InlineData("   ", RuntimeMode.Unknown)]
    [InlineData("some land battle", RuntimeMode.TacticalLand)]
    [InlineData("entering space mode", RuntimeMode.TacticalSpace)]
    [InlineData("skirmish game", RuntimeMode.AnyTactical)]
    [InlineData("tactical view", RuntimeMode.AnyTactical)]
    [InlineData("campaign mode", RuntimeMode.Galactic)]
    [InlineData("galactic conquest", RuntimeMode.Galactic)]
    [InlineData("no match here", RuntimeMode.Unknown)]
    public void InferMode_AllBranches(string? commandLine, RuntimeMode expected)
    {
        var method = typeof(ProcessLocator).GetMethod("InferMode", NonPublicStatic)!;
        var result = (RuntimeMode)method.Invoke(null, new object?[] { commandLine })!;
        result.Should().Be(expected);
    }

    // ── ExtractModPath ───────────────────────────────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("no mod path here", null)]
    [InlineData("modpath=C:\\Mods\\MyMod", "C:\\Mods\\MyMod")]
    [InlineData("modpath = C:\\Mods\\MyMod", "C:\\Mods\\MyMod")]
    [InlineData("modpath=\"C:\\Mods\\My Mod\"", "C:\\Mods\\My Mod")]
    public void ExtractModPath_AllBranches(string? commandLine, string? expected)
    {
        var method = typeof(ProcessLocator).GetMethod("ExtractModPath", NonPublicStatic)!;
        var result = (string?)method.Invoke(null, new object?[] { commandLine });
        result.Should().Be(expected);
    }

    // ── ExtractSteamModIds ───────────────────────────────────────────────

    [Fact]
    public void ExtractSteamModIds_NullCommandLine_ReturnsEmpty()
    {
        var method = typeof(ProcessLocator).GetMethod("ExtractSteamModIds", NonPublicStatic)!;
        var result = (string[])method.Invoke(null, new object?[] { null })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSteamModIds_EmptyCommandLine_ReturnsEmpty()
    {
        var method = typeof(ProcessLocator).GetMethod("ExtractSteamModIds", NonPublicStatic)!;
        var result = (string[])method.Invoke(null, new object?[] { "" })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void ExtractSteamModIds_SteamModMarker_ExtractsIds()
    {
        var method = typeof(ProcessLocator).GetMethod("ExtractSteamModIds", NonPublicStatic)!;
        var result = (string[])method.Invoke(null, new object?[] { "steammod=12345 steammod=67890" })!;
        result.Should().Contain("12345");
        result.Should().Contain("67890");
    }

    [Fact]
    public void ExtractSteamModIds_WorkshopPath_ExtractsIds()
    {
        var method = typeof(ProcessLocator).GetMethod("ExtractSteamModIds", NonPublicStatic)!;
        var result = (string[])method.Invoke(null, new object?[] { "C:\\Steam\\steamapps\\workshop\\content\\32470\\99999\\mod.xml" })!;
        result.Should().Contain("99999");
    }

    [Fact]
    public void ExtractSteamModIds_DeduplicatesIds()
    {
        var method = typeof(ProcessLocator).GetMethod("ExtractSteamModIds", NonPublicStatic)!;
        var result = (string[])method.Invoke(null, new object?[] { "steammod=12345 steammod=12345" })!;
        result.Should().HaveCount(1);
    }

    // ── NormalizeWorkshopIds ─────────────────────────────────────────────

    [Fact]
    public void NormalizeWorkshopIds_Null_ReturnsEmpty()
    {
        var method = typeof(ProcessLocator).GetMethod("NormalizeWorkshopIds", NonPublicStatic)!;
        var result = (IReadOnlyList<string>)method.Invoke(null, new object?[] { null })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeWorkshopIds_EmptyList_ReturnsEmpty()
    {
        var method = typeof(ProcessLocator).GetMethod("NormalizeWorkshopIds", NonPublicStatic)!;
        var result = (IReadOnlyList<string>)method.Invoke(null, new object?[] { (IReadOnlyList<string>)Array.Empty<string>() })!;
        result.Should().BeEmpty();
    }

    [Fact]
    public void NormalizeWorkshopIds_CsvSplitAndDedup()
    {
        var method = typeof(ProcessLocator).GetMethod("NormalizeWorkshopIds", NonPublicStatic)!;
        var input = new List<string> { "111,222", "222,333" } as IReadOnlyList<string>;
        var result = (IReadOnlyList<string>)method.Invoke(null, new object?[] { input })!;
        result.Should().HaveCount(3);
        result.Should().Contain("111");
        result.Should().Contain("222");
        result.Should().Contain("333");
    }

    [Fact]
    public void NormalizeWorkshopIds_WhitespaceEntries_Skipped()
    {
        var method = typeof(ProcessLocator).GetMethod("NormalizeWorkshopIds", NonPublicStatic)!;
        var input = new List<string> { "  ", "", "111" } as IReadOnlyList<string>;
        var result = (IReadOnlyList<string>)method.Invoke(null, new object?[] { input })!;
        result.Should().HaveCount(1);
        result.Should().Contain("111");
    }

    // ── DetermineHostRole ────────────────────────────────────────────────

    [Fact]
    public void DetermineHostRole_StarWarsG_ReturnsGameHost()
    {
        var method = typeof(ProcessLocator).GetMethod("DetermineHostRole", NonPublicStatic)!;
        var detectionType = typeof(ProcessLocator).GetNestedType("ProcessDetection", BindingFlags.NonPublic)!;
        var detection = Activator.CreateInstance(detectionType, ExeTarget.Swfoc, true, "starwarsg_default")!;
        var result = (ProcessHostRole)method.Invoke(null, new[] { detection })!;
        result.Should().Be(ProcessHostRole.GameHost);
    }

    [Fact]
    public void DetermineHostRole_Swfoc_ReturnsLauncher()
    {
        var method = typeof(ProcessLocator).GetMethod("DetermineHostRole", NonPublicStatic)!;
        var detectionType = typeof(ProcessLocator).GetNestedType("ProcessDetection", BindingFlags.NonPublic)!;
        var detection = Activator.CreateInstance(detectionType, ExeTarget.Swfoc, false, "name_or_path_swfoc")!;
        var result = (ProcessHostRole)method.Invoke(null, new[] { detection })!;
        result.Should().Be(ProcessHostRole.Launcher);
    }

    [Fact]
    public void DetermineHostRole_Sweaw_ReturnsLauncher()
    {
        var method = typeof(ProcessLocator).GetMethod("DetermineHostRole", NonPublicStatic)!;
        var detectionType = typeof(ProcessLocator).GetNestedType("ProcessDetection", BindingFlags.NonPublic)!;
        var detection = Activator.CreateInstance(detectionType, ExeTarget.Sweaw, false, "name_or_path_sweaw")!;
        var result = (ProcessHostRole)method.Invoke(null, new[] { detection })!;
        result.Should().Be(ProcessHostRole.Launcher);
    }

    [Fact]
    public void DetermineHostRole_Unknown_ReturnsUnknown()
    {
        var method = typeof(ProcessLocator).GetMethod("DetermineHostRole", NonPublicStatic)!;
        var detectionType = typeof(ProcessLocator).GetNestedType("ProcessDetection", BindingFlags.NonPublic)!;
        var detection = Activator.CreateInstance(detectionType, ExeTarget.Unknown, false, "unknown")!;
        var result = (ProcessHostRole)method.Invoke(null, new[] { detection })!;
        result.Should().Be(ProcessHostRole.Unknown);
    }

    // ── IsProcessName ────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "swfoc", false)]
    [InlineData("", "swfoc", false)]
    [InlineData("   ", "swfoc", false)]
    [InlineData("swfoc", "swfoc", true)]
    [InlineData("SWFOC", "swfoc", true)]
    [InlineData("swfoc.exe", "swfoc", true)]
    [InlineData("sweaw", "swfoc", false)]
    public void IsProcessName_AllBranches(string? processName, string expected, bool expectedResult)
    {
        var method = typeof(ProcessLocator).GetMethod("IsProcessName", NonPublicStatic)!;
        var result = (bool)method.Invoke(null, new object?[] { processName, expected })!;
        result.Should().Be(expectedResult);
    }

    // ── ContainsToken ────────────────────────────────────────────────────

    [Theory]
    [InlineData(null, "test", false)]
    [InlineData("hello world", "world", true)]
    [InlineData("hello world", "WORLD", true)]
    [InlineData("hello world", "xyz", false)]
    public void ContainsToken_AllBranches(string? value, string token, bool expected)
    {
        var method = typeof(ProcessLocator).GetMethod("ContainsToken", NonPublicStatic)!;
        var result = (bool)method.Invoke(null, new object?[] { value, token })!;
        result.Should().Be(expected);
    }

    // ── TryDetectStarWarsG sub-paths ─────────────────────────────────────

    [Fact]
    public void GetProcessDetection_StarWarsGProcess_CorruptionPath_DetectsSwfoc()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "starwarsg", "C:\\Games\\corruption\\starwarsg.exe", null });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_StarWarsGProcess_GameDataPath_DetectsSwfoc()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "starwarsg", "C:\\Games\\GameData\\starwarsg.exe", null });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_StarWarsGProcess_DefaultFallback_DetectsSwfoc()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "starwarsg", "C:\\Games\\other\\starwarsg.exe", null });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_StarWarsGProcess_SweawHint_DetectsSweaw()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "starwarsg", "C:\\Games\\starwarsg.exe", "sweaw.exe" });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Sweaw);
    }

    [Fact]
    public void GetProcessDetection_StarWarsGProcess_CmdlineFocHint_DetectsSwfoc()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "starwarsg", "C:\\Games\\starwarsg.exe", "swfoc.exe steammod=123" });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_DirectSweaw_DetectsSweaw()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "sweaw", null, null });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Sweaw);
    }

    [Fact]
    public void GetProcessDetection_DirectSwfoc_DetectsSwfoc()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "swfoc", null, null });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_UnknownProcess_ReturnsUnknown()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "notepad", null, null });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Unknown);
    }

    [Fact]
    public void GetProcessDetection_CmdlineModMarkers_DetectsSwfoc()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "gameprocess", null, "steammod=123" });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_CmdlineModPath_DetectsSwfoc()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "gameprocess", null, "modpath=C:\\mods\\test" });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Swfoc);
    }

    // ── ResolveForcedContext ─────────────────────────────────────────────

    [Fact]
    public void ResolveForcedContext_HasModMarkers_ReturnsDetected()
    {
        var method = typeof(ProcessLocator).GetMethod("ResolveForcedContext", NonPublicStatic)!;
        var steamModIds = new[] { "12345" } as IReadOnlyList<string>;
        var options = new ProcessLocatorOptions(new[] { "99999" }, "forced_profile");
        var result = method.Invoke(null, new object?[] { "steammod=12345", null, steamModIds, options });
        var source = (string)result!.GetType().GetProperty("Source")!.GetValue(result)!;
        source.Should().Be("detected");
    }

    [Fact]
    public void ResolveForcedContext_NoMarkers_NoForcedHints_ReturnsDetected()
    {
        var method = typeof(ProcessLocator).GetMethod("ResolveForcedContext", NonPublicStatic)!;
        var steamModIds = Array.Empty<string>() as IReadOnlyList<string>;
        var options = ProcessLocatorOptions.None;
        var result = method.Invoke(null, new object?[] { null, null, steamModIds, options });
        var source = (string)result!.GetType().GetProperty("Source")!.GetValue(result)!;
        source.Should().Be("detected");
    }

    [Fact]
    public void ResolveForcedContext_NoMarkers_WithForcedWorkshopIds_ReturnsForced()
    {
        var method = typeof(ProcessLocator).GetMethod("ResolveForcedContext", NonPublicStatic)!;
        var steamModIds = Array.Empty<string>() as IReadOnlyList<string>;
        var options = new ProcessLocatorOptions(new[] { "99999" }, null);
        var result = method.Invoke(null, new object?[] { null, null, steamModIds, options });
        var source = (string)result!.GetType().GetProperty("Source")!.GetValue(result)!;
        source.Should().Be("forced");
    }

    [Fact]
    public void ResolveForcedContext_NoMarkers_WithForcedProfileId_ReturnsForced()
    {
        var method = typeof(ProcessLocator).GetMethod("ResolveForcedContext", NonPublicStatic)!;
        var steamModIds = Array.Empty<string>() as IReadOnlyList<string>;
        var options = new ProcessLocatorOptions(null, "forced_profile");
        var result = method.Invoke(null, new object?[] { null, null, steamModIds, options });
        var source = (string)result!.GetType().GetProperty("Source")!.GetValue(result)!;
        source.Should().Be("forced");
    }

    // ── NormalizeForcedProfileId ─────────────────────────────────────────

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("  profile  ", "profile")]
    [InlineData("vanilla_swfoc", "vanilla_swfoc")]
    public void NormalizeForcedProfileId_AllBranches(string? input, string? expected)
    {
        var method = typeof(ProcessLocator).GetMethod("NormalizeForcedProfileId", NonPublicStatic)!;
        var result = (string?)method.Invoke(null, new object?[] { input });
        result.Should().Be(expected);
    }

    // ── StarWarsG with corruption in forward-slash path ──────────────────

    [Fact]
    public void GetProcessDetection_StarWarsG_ForwardSlashCorruption_DetectsSwfoc()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "starwarsg", "/opt/games/corruption/starwarsg.exe", null });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_StarWarsG_ForwardSlashGamedata_DetectsSwfoc()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "starwarsg", "/opt/games/gamedata/starwarsg.exe", null });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Swfoc);
    }

    // ── Constructor variations ────────────────────────────────────────────

    [Fact]
    public void Constructor_Default_DoesNotThrow()
    {
        var locator = new ProcessLocator();
        locator.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_WithNullLaunchContextResolver_DoesNotThrow()
    {
        var locator = new ProcessLocator(launchContextResolver: null, profileRepository: null);
        locator.Should().NotBeNull();
    }

    // ── StarWarsG cmdline with sweaw.exe + steammod (not sweaw detection) ─

    [Fact]
    public void GetProcessDetection_StarWarsG_SweawInCmdline_DetectedAsDirectSweaw()
    {
        // When sweaw.exe appears in the command line, TryDetectDirectTarget catches it first
        // regardless of StarWarsG process name or steammod markers.
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "starwarsg", "C:\\starwarsg.exe", "sweaw.exe steammod=12345" });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Sweaw);
    }

    [Fact]
    public void GetProcessDetection_StarWarsG_CorruptionInCmdline_DetectsSwfoc()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "starwarsg", "C:\\starwarsg.exe", "corruption" });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Swfoc);
    }

    // ── Sweaw detection via path ─────────────────────────────────────────

    [Fact]
    public void GetProcessDetection_SweawPath_DetectsSweaw()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "game", "C:\\Games\\sweaw.exe", null });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Sweaw);
    }

    [Fact]
    public void GetProcessDetection_SwfocPath_DetectsSwfoc()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "game", "C:\\Games\\swfoc.exe", null });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Swfoc);
    }

    [Fact]
    public void GetProcessDetection_SweawInCmdline_DetectsSweaw()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "game", null, "sweaw.exe" });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Sweaw);
    }

    [Fact]
    public void GetProcessDetection_SwfocInCmdline_DetectsSwfoc()
    {
        var method = typeof(ProcessLocator).GetMethod("GetProcessDetection", NonPublicStatic)!;
        var result = method.Invoke(null, new object?[] { "game", null, "swfoc.exe" });
        var exeTarget = (ExeTarget)result!.GetType().GetProperty("ExeTarget")!.GetValue(result)!;
        exeTarget.Should().Be(ExeTarget.Swfoc);
    }
}
