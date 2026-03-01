using System.Diagnostics;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class GameLaunchService : IGameLaunchService
{
    private const string GameRootOverrideEnvVar = "SWFOC_GAME_ROOT";

    private static readonly string[] DefaultRoots =
    [
        @"D:\SteamLibrary\steamapps\common\Star Wars Empire at War",
        @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Empire at War"
    ];

    public Task<GameLaunchResult> LaunchAsync(GameLaunchRequest request, CancellationToken cancellationToken)
    {
        if (request.TerminateExistingTargets)
        {
            TerminateKnownTargets();
        }

        var root = ResolveRoot();
        if (string.IsNullOrWhiteSpace(root))
        {
            return Task.FromResult(new GameLaunchResult(
                Succeeded: false,
                Message: "Unable to resolve game root path. Set SWFOC_GAME_ROOT to override.",
                ProcessId: 0,
                ExecutablePath: string.Empty,
                Arguments: string.Empty,
                Diagnostics: new Dictionary<string, object?>
                {
                    ["launchState"] = "root_missing",
                    ["rootOverrideEnv"] = Environment.GetEnvironmentVariable(GameRootOverrideEnvVar) ?? string.Empty
                }));
        }

        var executablePath = ResolveExecutablePath(root, request.Target);
        if (!File.Exists(executablePath))
        {
            return Task.FromResult(new GameLaunchResult(
                Succeeded: false,
                Message: $"Executable not found: {executablePath}",
                ProcessId: 0,
                ExecutablePath: executablePath,
                Arguments: string.Empty,
                Diagnostics: new Dictionary<string, object?>
                {
                    ["launchState"] = "exe_missing",
                    ["resolvedRoot"] = root,
                    ["target"] = request.Target.ToString()
                }));
        }

        var arguments = BuildArguments(request);
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? root,
            UseShellExecute = false
        };

        var process = Process.Start(startInfo);
        if (process is null)
        {
            return Task.FromResult(new GameLaunchResult(
                Succeeded: false,
                Message: "Process start returned null.",
                ProcessId: 0,
                ExecutablePath: executablePath,
                Arguments: arguments,
                Diagnostics: new Dictionary<string, object?>
                {
                    ["launchState"] = "start_failed"
                }));
        }

        return Task.FromResult(new GameLaunchResult(
            Succeeded: true,
            Message: "Launch command dispatched.",
            ProcessId: process.Id,
            ExecutablePath: executablePath,
            Arguments: arguments,
            Diagnostics: new Dictionary<string, object?>
            {
                ["launchState"] = "started",
                ["target"] = request.Target.ToString(),
                ["mode"] = request.Mode.ToString(),
                ["profileIdHint"] = request.ProfileIdHint ?? string.Empty,
                ["resolvedRoot"] = root
            }));
    }

    private static void TerminateKnownTargets()
    {
        foreach (var processName in new[] { "sweaw", "swfoc", "StarWarsG" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Best-effort cleanup only.
                }
            }
        }
    }

    private static string ResolveRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(GameRootOverrideEnvVar);
        if (!string.IsNullOrWhiteSpace(overrideRoot) && Directory.Exists(overrideRoot))
        {
            return Path.GetFullPath(overrideRoot.Trim());
        }

        return DefaultRoots.FirstOrDefault(Directory.Exists) ?? string.Empty;
    }

    private static string ResolveExecutablePath(string root, GameLaunchTarget target)
    {
        return target switch
        {
            GameLaunchTarget.Sweaw => Path.Combine(root, "GameData", "sweaw.exe"),
            GameLaunchTarget.Swfoc => Path.Combine(root, "corruption", "swfoc.exe"),
            _ => Path.Combine(root, "corruption", "swfoc.exe")
        };
    }

    private static string BuildArguments(GameLaunchRequest request)
    {
        return request.Mode switch
        {
            GameLaunchMode.SteamMod => string.IsNullOrWhiteSpace(request.WorkshopId)
                ? string.Empty
                : $"STEAMMOD={request.WorkshopId}",
            GameLaunchMode.ModPath => string.IsNullOrWhiteSpace(request.ModPath)
                ? string.Empty
                : $"MODPATH=\"{request.ModPath}\"",
            _ => string.Empty
        };
    }
}
