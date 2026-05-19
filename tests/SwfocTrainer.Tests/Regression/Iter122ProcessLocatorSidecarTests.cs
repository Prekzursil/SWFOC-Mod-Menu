using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 122) — pins the <see cref="ProcessLocator"/> static
/// detection behavior on synthetic process tuples. Prevents a regression
/// of the iter 120/121 flake where the locator could match sidecar
/// processes (most notably <c>SwfocExtender.Host</c>) as
/// <see cref="ExeTarget.Swfoc"/>, then fail the profile-aware
/// <c>RuntimeAdapter.AttachAsync</c> with <c>ATTACH_NO_PROCESS</c>.
/// <para>
/// These tests exercise the static <c>GetProcessDetection</c> directly,
/// so they don't need a running process — they reproduce exactly the
/// inputs the production locator sees.
/// </para>
/// </summary>
public sealed class Iter122ProcessLocatorSidecarTests
{
    private const string SidecarPath = @"C:\Users\Prekzursil\Downloads\SWFOC editor\native\build-win-vs\SwfocExtender.Bridge\Release\SwfocExtender.Host.exe";

    [Fact]
    public void GetProcessDetection_SwfocExtenderHost_DoesNotMatchSwfocOrStarWarsG()
    {
        // The exact tuple that caused iter 120's flake: process name is
        // "SwfocExtender.Host" (no .exe), path ends in
        // "...\SwfocExtender.Host.exe", no command-line mod markers.
        var detection = ProcessLocator.GetProcessDetection(
            processName: "SwfocExtender.Host",
            processPath: SidecarPath,
            commandLine: $"\"{SidecarPath}\"");

        detection.ExeTarget.Should().Be(ExeTarget.Unknown,
            "the editor's sidecar binary must NOT be classified as SWFOC — " +
            "doing so causes Live tests to FAIL instead of SKIP when the actual " +
            "game isn't running. See iter 120/121 for the test-side defensive fix " +
            "and iter 122 for this root-cause regression guard.");
        detection.IsStarWarsG.Should().BeFalse();
    }

    [Theory]
    [InlineData("SwfocTrainer.App", @"C:\swfoc-editor\publish\SwfocTrainer.App.exe")]
    [InlineData("swfoc_replay", @"C:\swfoc\swfoc_replay.exe")]
    [InlineData("SwfocExtender.Host", @"C:\path\to\SwfocExtender.Host.exe")]
    public void GetProcessDetection_OurOwnTooling_DoesNotMatchSwfoc(
        string processName, string processPath)
    {
        // Editor + replay binary + extender host all live alongside the
        // game executable in a developer's workspace. None of them should
        // be classified as SWFOC by the locator.
        var detection = ProcessLocator.GetProcessDetection(
            processName, processPath, commandLine: $"\"{processPath}\"");

        detection.ExeTarget.Should().Be(ExeTarget.Unknown,
            $"{processName} is one of OUR tools, not the SWFOC game executable.");
    }

    [Fact]
    public void GetProcessDetection_RealStarWarsG_StillMatchesSwfoc()
    {
        // The legitimate detection path must still work — the regression
        // guard above must not over-tighten. A real SWFOC launch via
        // StarWarsG.exe with no mod hints is FoC by default (per the
        // FoC-safe fallback at line ~403).
        var detection = ProcessLocator.GetProcessDetection(
            processName: "StarWarsG",
            processPath: @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Empire at War Forces of Corruption\GameData\StarWarsG.exe",
            commandLine: @"""C:\Program Files (x86)\Steam\steamapps\common\Star Wars Empire at War Forces of Corruption\GameData\StarWarsG.exe""");

        detection.ExeTarget.Should().Be(ExeTarget.Swfoc,
            "a real StarWarsG.exe launch from the FoC GameData folder must still resolve as SWFOC.");
        detection.IsStarWarsG.Should().BeTrue();
    }

    [Fact]
    public void GetProcessDetection_StarWarsGWithModpath_StillMatchesSwfoc()
    {
        // Modded SWFOC sessions launch StarWarsG.exe with a modpath= command-line
        // arg. The cmdline-FoC-hint detection path (line ~422) must still match.
        var detection = ProcessLocator.GetProcessDetection(
            processName: "StarWarsG",
            processPath: @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Empire at War Forces of Corruption\GameData\StarWarsG.exe",
            commandLine: @"""StarWarsG.exe"" modpath=Mods\AwesomeMod steammod=12345");

        detection.ExeTarget.Should().Be(ExeTarget.Swfoc);
        detection.IsStarWarsG.Should().BeTrue();
    }
}
