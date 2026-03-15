using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Runtime.Services;
using Xunit;

namespace SwfocTrainer.Tests.Runtime;

public sealed class GameLaunchServiceTests
{
    [Fact]
    public void BuildArguments_ShouldEmitChainedSteamModArguments_WhenMultipleWorkshopIdsProvided()
    {
        var request = new GameLaunchRequest(
            Target: GameLaunchTarget.Swfoc,
            Mode: GameLaunchMode.SteamMod,
            WorkshopIds: new[] { "1397421866", "3447786229" });

        var args = InvokeBuildArguments(request);

        args.Should().Be("STEAMMOD=1397421866 STEAMMOD=3447786229");
    }

    [Fact]
    public void BuildArguments_ShouldEmitOverlayModPathBeforeSteamMods_WhenOverlayPathProvided()
    {
        var request = new GameLaunchRequest(
            Target: GameLaunchTarget.Swfoc,
            Mode: GameLaunchMode.SteamMod,
            WorkshopIds: new[] { "1397421866", "3447786229" },
            OverlayModPath: @"C:\Users\tester\AppData\Local\SwfocTrainer\helper_mod\base_swfoc");

        var args = InvokeBuildArguments(request);

        args.Should().Be("MODPATH=\"C:\\Users\\tester\\AppData\\Local\\SwfocTrainer\\helper_mod\\base_swfoc\" STEAMMOD=1397421866 STEAMMOD=3447786229");
    }

    [Fact]
    public void BuildArguments_ShouldNormalizeCsvAndPreserveInputOrder()
    {
        var request = new GameLaunchRequest(
            Target: GameLaunchTarget.Swfoc,
            Mode: GameLaunchMode.SteamMod,
            WorkshopIds: new[] { "1397421866,3447786229", "3447786229", "3287776766" });

        var args = InvokeBuildArguments(request);

        args.Should().Be("STEAMMOD=1397421866 STEAMMOD=3447786229 STEAMMOD=3287776766");
    }

    [Fact]
    public void BuildArguments_ShouldReturnEmpty_ForVanillaMode()
    {
        var request = new GameLaunchRequest(
            Target: GameLaunchTarget.Swfoc,
            Mode: GameLaunchMode.Vanilla,
            WorkshopIds: new[] { "1397421866" });

        var args = InvokeBuildArguments(request);

        args.Should().BeEmpty();
    }

    [Fact]
    public void BuildArguments_ShouldEmitOverlayModPath_ForVanillaMode_WhenProvided()
    {
        var request = new GameLaunchRequest(
            Target: GameLaunchTarget.Swfoc,
            Mode: GameLaunchMode.Vanilla,
            OverlayModPath: @"C:\Users\tester\AppData\Local\SwfocTrainer\helper_mod\base_swfoc");

        var args = InvokeBuildArguments(request);

        args.Should().Be("MODPATH=\"C:\\Users\\tester\\AppData\\Local\\SwfocTrainer\\helper_mod\\base_swfoc\"");
    }

    [Fact]
    public void BuildArguments_ShouldEmitQuotedModPath_ForModPathMode()
    {
        var request = new GameLaunchRequest(
            Target: GameLaunchTarget.Swfoc,
            Mode: GameLaunchMode.ModPath,
            ModPath: @"Mods\AOTR");

        var args = InvokeBuildArguments(request);

        args.Should().Be("MODPATH=\"Mods\\AOTR\"");
    }

    [Fact]
    public void BuildArguments_ShouldReturnEmpty_ForModPathMode_WhenPathIsBlank()
    {
        var request = new GameLaunchRequest(
            Target: GameLaunchTarget.Swfoc,
            Mode: GameLaunchMode.ModPath,
            ModPath: "   ");

        var args = InvokeBuildArguments(request);

        args.Should().BeEmpty();
    }

    [Fact]
    public async Task LaunchAsync_ShouldReturnExeMissing_WhenOverrideRootDoesNotContainTargetExe()
    {
        var service = new GameLaunchService();
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-launch-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        var previous = Environment.GetEnvironmentVariable("SWFOC_GAME_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", root);

            var result = await service.LaunchAsync(
                new GameLaunchRequest(
                    Target: GameLaunchTarget.Swfoc,
                    Mode: GameLaunchMode.Vanilla),
                CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Diagnostics.Should().NotBeNull();
            result.Diagnostics!["launchState"]!.ToString().Should().Be("exe_missing");
            result.Diagnostics["resolvedRoot"]!.ToString().Should().Be(Path.GetFullPath(root));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", previous);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task LaunchAsync_ShouldHonorCancellationToken()
    {
        var service = new GameLaunchService();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            service.LaunchAsync(
                new GameLaunchRequest(GameLaunchTarget.Swfoc, GameLaunchMode.Vanilla),
                cts.Token));
    }

    [Theory]
    [InlineData(GameLaunchTarget.Sweaw, @"C:\Games\Root\GameData\sweaw.exe")]
    [InlineData(GameLaunchTarget.Swfoc, @"C:\Games\Root\corruption\swfoc.exe")]
    [InlineData((GameLaunchTarget)999, @"C:\Games\Root\corruption\swfoc.exe")]
    public void ResolveExecutablePath_ShouldRouteByTarget(GameLaunchTarget target, string expected)
    {
        var method = typeof(GameLaunchService).GetMethod(
            "ResolveExecutablePath",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var resolved = method!.Invoke(null, new object?[] { @"C:\Games\Root", target });
        resolved.Should().Be(expected);
    }

    [Fact]
    public void BuildSteamModArguments_ShouldReturnEmpty_ForNullWorkshopIds()
    {
        var method = typeof(GameLaunchService).GetMethod(
            "BuildSteamModArguments",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var args = method!.Invoke(null, new object?[] { null });
        args.Should().Be(string.Empty);
    }

    [Fact]
    public void CreateFailureResult_ShouldMergeLaunchStateAndAdditionalDiagnostics()
    {
        var method = typeof(GameLaunchService).GetMethod(
            "CreateFailureResult",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var diagnostics = new Dictionary<string, object?>
        {
            ["resolvedRoot"] = @"C:\Games\Root",
            ["target"] = "Swfoc"
        };
        var result = (GameLaunchResult)method!.Invoke(null, new object?[]
        {
            "failed",
            "exe_missing",
            @"C:\Games\Root\corruption\swfoc.exe",
            string.Empty,
            diagnostics
        })!;

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainKey("launchState");
        result.Diagnostics!["launchState"]!.ToString().Should().Be("exe_missing");
        result.Diagnostics["resolvedRoot"]!.ToString().Should().Be(@"C:\Games\Root");
    }

    [Fact]
    public void CreateFailureResult_ShouldKeepOnlyLaunchState_WhenNoAdditionalDiagnosticsProvided()
    {
        var method = typeof(GameLaunchService).GetMethod(
            "CreateFailureResult",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var result = (GameLaunchResult)method!.Invoke(null, new object?[]
        {
            "failed",
            "start_failed",
            @"C:\Games\Root\corruption\swfoc.exe",
            string.Empty,
            null
        })!;

        result.Succeeded.Should().BeFalse();
        result.Diagnostics.Should().ContainSingle();
        result.Diagnostics.Should().ContainKey("launchState");
        result.Diagnostics!["launchState"]!.ToString().Should().Be("start_failed");
    }

    [Fact]
    public void ResolveRoot_ShouldNormalizeOverride_WhenDirectoryExists()
    {
        var method = typeof(GameLaunchService).GetMethod(
            "ResolveRoot",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var root = Path.Combine(Path.GetTempPath(), $"swfoc-launch-root-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var previous = Environment.GetEnvironmentVariable("SWFOC_GAME_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", root);
            var resolved = (string)method!.Invoke(null, Array.Empty<object?>())!;
            resolved.Should().Be(Path.GetFullPath(root));
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", previous);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveRoot_ShouldUseFallbackDefaultRoots_WhenOverrideIsMissing()
    {
        var method = typeof(GameLaunchService).GetMethod(
            "ResolveRoot",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var previous = Environment.GetEnvironmentVariable("SWFOC_GAME_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", null);
            var root = (string)method!.Invoke(null, Array.Empty<object?>())!;
            root.Should().NotBeNull();
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", previous);
        }
    }

    [Fact]
    public void TryResolveExecutable_ShouldReturnSuccessTuple_WhenExecutableExists()
    {
        var method = typeof(GameLaunchService).GetMethod(
            "TryResolveExecutable",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var root = Path.Combine(Path.GetTempPath(), $"swfoc-launch-exe-{Guid.NewGuid():N}");
        var corruption = Path.Combine(root, "corruption");
        Directory.CreateDirectory(corruption);
        var executablePath = Path.Combine(corruption, "swfoc.exe");
        File.WriteAllBytes(executablePath, new byte[] { 0x4D, 0x5A });

        try
        {
            var tuple = method!.Invoke(null, new object?[] { root, GameLaunchTarget.Swfoc });
            tuple.Should().NotBeNull();
            var path = (string?)tuple!.GetType().GetField("Item1", BindingFlags.Public | BindingFlags.Instance)?.GetValue(tuple);
            var failure = tuple.GetType().GetField("Item2", BindingFlags.Public | BindingFlags.Instance)?.GetValue(tuple);

            path.Should().Be(executablePath);
            failure.Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateStartedResult_ShouldIncludeRouteDiagnostics()
    {
        var method = typeof(GameLaunchService).GetMethod(
            "CreateStartedResult",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var request = new GameLaunchRequest(
            Target: GameLaunchTarget.Swfoc,
            Mode: GameLaunchMode.SteamMod,
            WorkshopIds: new[] { "1397421866", "3447786229" },
            ProfileIdHint: "profile");
        var result = (GameLaunchResult)method!.Invoke(null, new object?[]
        {
            request,
            9001,
            @"C:\Games\Root\corruption\swfoc.exe",
            @"C:\Games\Root",
            "STEAMMOD=1397421866 STEAMMOD=3447786229"
        })!;

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("launchState");
        result.Diagnostics!["launchState"]!.ToString().Should().Be("started");
        result.Diagnostics["workshopIds"].Should().BeAssignableTo<IReadOnlyList<string>>();
    }

    [Fact]
    public void StartProcess_ShouldReturnProcess_WhenInvokedWithKnownExecutable()
    {
        var method = typeof(GameLaunchService).GetMethod(
            "StartProcess",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        var executable = Environment.ProcessPath;
        executable.Should().NotBeNullOrWhiteSpace();

        var process = (Process?)method!.Invoke(null, new object?[]
        {
            executable!,
            Path.GetDirectoryName(executable!)!,
            "--version"
        });

        process.Should().NotBeNull();
        process!.WaitForExit(5000).Should().BeTrue();
    }

    [Fact]
    public async Task LaunchAsync_ShouldReturnStartFailed_WhenExecutableCannotStart()
    {
        var service = new GameLaunchService();
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-launch-start-failed-{Guid.NewGuid():N}");
        var executableDirectory = Path.Combine(root, "corruption");
        Directory.CreateDirectory(executableDirectory);
        var executablePath = Path.Combine(executableDirectory, "swfoc.exe");
        await File.WriteAllTextAsync(executablePath, "not-an-executable");

        var previousOverride = Environment.GetEnvironmentVariable("SWFOC_GAME_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", root);
            var result = await service.LaunchAsync(
                new GameLaunchRequest(GameLaunchTarget.Swfoc, GameLaunchMode.Vanilla),
                CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Diagnostics.Should().ContainKey("launchState");
            result.Diagnostics!["launchState"]!.ToString().Should().Be("start_failed");
            result.Diagnostics.Should().ContainKey("exceptionType");
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", previousOverride);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void TerminateKnownTargets_ShouldTerminateSwfocNamedProcess_WhenAvailable()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && !RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return;
        }

        var tempRoot = Path.Combine(Path.GetTempPath(), $"swfoc-launch-kill-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempRoot);
        var executablePath = Path.Combine(tempRoot, RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "swfoc.exe" : "swfoc");

        try
        {
            Process process;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                File.Copy(cmdExe, executablePath, overwrite: true);
                process = Process.Start(new ProcessStartInfo(executablePath, "/c timeout /t 30 /nobreak >nul")
                {
                    UseShellExecute = false
                })!;
            }
            else
            {
                var sleepBinary = "/bin/sleep";
                if (!File.Exists(sleepBinary))
                {
                    return;
                }

                File.Copy(sleepBinary, executablePath, overwrite: true);
                var chmod = Process.Start(new ProcessStartInfo("chmod", $"+x \"{executablePath}\"")
                {
                    UseShellExecute = false
                });
                chmod.Should().NotBeNull();
                chmod!.WaitForExit(5000).Should().BeTrue();

                process = Process.Start(new ProcessStartInfo(executablePath, "30")
                {
                    UseShellExecute = false
                })!;
            }

            process.Should().NotBeNull();
            process.HasExited.Should().BeFalse();

            var terminateMethod = typeof(GameLaunchService).GetMethod(
                "TerminateKnownTargets",
                BindingFlags.NonPublic | BindingFlags.Static);
            terminateMethod.Should().NotBeNull();
            terminateMethod!.Invoke(null, Array.Empty<object?>());

            process.WaitForExit(5000).Should().BeTrue();
            process.Dispose();
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void TryKillProcess_ShouldNotThrow_WhenKillThrows()
    {
        var method = typeof(GameLaunchService).GetMethod(
            "TryKillProcess",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        using var process = new Process();

        var invocation = () => method!.Invoke(null, new object?[] { process });
        invocation.Should().NotThrow();
    }

    [Fact]
    public async Task LaunchAsync_ShouldReturnRootMissing_WhenNoRootCanBeResolved()
    {
        var service = new GameLaunchService();
        var overrideRoot = Path.Combine(Path.GetTempPath(), $"swfoc-launch-root-missing-{Guid.NewGuid():N}");
        var defaultRoots = GetMutableDefaultRoots();
        var originalRoots = defaultRoots.ToArray();
        var previousOverride = Environment.GetEnvironmentVariable("SWFOC_GAME_ROOT");

        try
        {
            for (var index = 0; index < defaultRoots.Length; index++)
            {
                defaultRoots[index] = Path.Combine(Path.GetTempPath(), $"swfoc-default-root-missing-{Guid.NewGuid():N}", index.ToString());
            }

            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", overrideRoot);
            var result = await service.LaunchAsync(
                new GameLaunchRequest(GameLaunchTarget.Swfoc, GameLaunchMode.Vanilla),
                CancellationToken.None);

            result.Succeeded.Should().BeFalse();
            result.Diagnostics.Should().NotBeNull();
            result.Diagnostics!["launchState"]?.ToString().Should().Be("root_missing");
            result.Diagnostics["rootOverrideEnv"]?.ToString().Should().Be(overrideRoot);
        }
        finally
        {
            RestoreDefaultRoots(defaultRoots, originalRoots);
            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", previousOverride);
        }
    }

    [Fact]
    public async Task LaunchAsync_ShouldReturnStarted_WithNormalizedSteamModArguments()
    {
        var service = new GameLaunchService();
        var root = Path.Combine(Path.GetTempPath(), $"swfoc-launch-started-{Guid.NewGuid():N}");
        var executableDirectory = Path.Combine(root, "corruption");
        Directory.CreateDirectory(executableDirectory);
        var executablePath = Path.Combine(executableDirectory, "swfoc.exe");
        var sourceExecutable = Environment.ProcessPath;
        sourceExecutable.Should().NotBeNullOrWhiteSpace();
        File.Copy(sourceExecutable!, executablePath, overwrite: true);

        var previousOverride = Environment.GetEnvironmentVariable("SWFOC_GAME_ROOT");
        try
        {
            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", root);
            var request = new GameLaunchRequest(
                Target: GameLaunchTarget.Swfoc,
                Mode: GameLaunchMode.SteamMod,
                WorkshopIds: new[] { " 1397421866 ,3447786229 ", "", "3447786229", "3287776766" },
                TerminateExistingTargets: true);

            var result = await service.LaunchAsync(request, CancellationToken.None);

            result.Succeeded.Should().BeTrue();
            result.Diagnostics.Should().NotBeNull();
            result.Diagnostics!["launchState"]?.ToString().Should().Be("started");
            result.Arguments.Should().Be("STEAMMOD=1397421866 STEAMMOD=3447786229 STEAMMOD=3287776766");
            result.ExecutablePath.Should().Be(executablePath);
            result.Diagnostics["workshopIds"].Should().BeAssignableTo<IReadOnlyList<string>>();
            var workshopIds = (IReadOnlyList<string>)result.Diagnostics["workshopIds"]!;
            workshopIds.Should().Equal("1397421866", "3447786229", "3287776766");

            TryTerminateProcess(result.ProcessId);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SWFOC_GAME_ROOT", previousOverride);
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void BuildArguments_ShouldSkipBlankWorkshopEntries_WhenNormalizingSteamModInputs()
    {
        var request = new GameLaunchRequest(
            Target: GameLaunchTarget.Swfoc,
            Mode: GameLaunchMode.SteamMod,
            WorkshopIds: new[] { " ", "1397421866, ,3447786229", "3447786229", "  ", "3287776766" });

        var args = InvokeBuildArguments(request);

        args.Should().Be("STEAMMOD=1397421866 STEAMMOD=3447786229 STEAMMOD=3287776766");
    }

    private static string[] GetMutableDefaultRoots()
    {
        var field = typeof(GameLaunchService).GetField("DefaultRoots", BindingFlags.NonPublic | BindingFlags.Static);
        field.Should().NotBeNull();
        var value = field!.GetValue(null);
        value.Should().BeAssignableTo<string[]>();
        return (string[])value!;
    }

    private static void RestoreDefaultRoots(string[] target, IReadOnlyList<string> source)
    {
        for (var index = 0; index < target.Length && index < source.Count; index++)
        {
            target[index] = source[index];
        }
    }

    private static void TryTerminateProcess(int processId)
    {
        if (processId <= 0)
        {
            return;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch
        {
            // Best effort cleanup for process launched during tests.
        }
    }

    private static string InvokeBuildArguments(GameLaunchRequest request)
    {
        var method = typeof(GameLaunchService).GetMethod(
            "BuildArguments",
            BindingFlags.NonPublic | BindingFlags.Static);

        method.Should().NotBeNull();
        var result = method!.Invoke(null, new object?[] { request });
        result.Should().BeOfType<string>();
        return (string)result!;
    }
}
