using System.Diagnostics;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;

namespace SwfocTrainer.Runtime.Services;

public sealed class GameLaunchService : IGameLaunchService
{
    private const string GameRootOverrideEnvVar = "SWFOC_GAME_ROOT";
    private const string DiagnosticLaunchState = "launchState";
    private const string DiagnosticResolvedRoot = "resolvedRoot";
    private const string DiagnosticTarget = "target";
    private const string DiagnosticOverlayModPath = "overlayModPath";
    private const string DiagnosticLaunchHost = "launchHost";
    private const string LaunchStateRootMissing = "root_missing";
    private const string LaunchStateExeMissing = "exe_missing";
    private const string LaunchStateStartFailed = "start_failed";
    private const string LaunchStateStarted = "started";
    private const string LaunchHostDirectGameHost = "direct_game_host";
    private const string LaunchHostLauncherStubFallback = "launcher_stub_fallback";

    private static readonly string[] DefaultRoots =
    [
        @"D:\SteamLibrary\steamapps\common\Star Wars Empire at War",
        @"C:\Program Files (x86)\Steam\steamapps\common\Star Wars Empire at War"
    ];

    public Task<GameLaunchResult> LaunchAsync(GameLaunchRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.TerminateExistingTargets)
        {
            TerminateKnownTargets();
        }

        var rootResolution = TryResolveLaunchRoot();
        if (rootResolution.Failure is not null)
        {
            return Task.FromResult(rootResolution.Failure);
        }

        var executableResolution = TryResolveExecutable(rootResolution.Root, request.Target);
        if (executableResolution.Failure is not null)
        {
            return Task.FromResult(executableResolution.Failure);
        }

        var arguments = BuildArguments(request);
        Process process;
        try
        {
            process = StartProcess(executableResolution.ExecutablePath, rootResolution.Root, arguments);
        }
        catch (Exception ex)
        {
            return Task.FromResult(CreateFailureResult(
                message: $"Process start failed: {ex.Message}",
                state: LaunchStateStartFailed,
                executablePath: executableResolution.ExecutablePath,
                arguments: arguments,
                diagnostics: new Dictionary<string, object?>
                {
                    ["exceptionType"] = ex.GetType().Name
                }));
        }

        return Task.FromResult(CreateStartedResult(request, process.Id, executableResolution.ExecutablePath, rootResolution.Root, arguments));
    }

    private static void TerminateKnownTargets()
    {
        foreach (var processName in new[] { "sweaw", "swfoc", "StarWarsG" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                TryKillProcess(process);
            }
        }
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Ignore termination failures for stale processes.
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

    private static (string Root, GameLaunchResult? Failure) TryResolveLaunchRoot()
    {
        var root = ResolveRoot();
        if (!string.IsNullOrWhiteSpace(root))
        {
            return (root, null);
        }

        var failure = CreateFailureResult(
            message: "Unable to resolve game root path. Set SWFOC_GAME_ROOT to override.",
            state: LaunchStateRootMissing,
            executablePath: string.Empty,
            arguments: string.Empty,
            diagnostics: new Dictionary<string, object?>
            {
                ["rootOverrideEnv"] = Environment.GetEnvironmentVariable(GameRootOverrideEnvVar) ?? string.Empty
            });
        return (string.Empty, failure);
    }

    private static (string ExecutablePath, GameLaunchResult? Failure) TryResolveExecutable(string root, GameLaunchTarget target)
    {
        foreach (var executablePath in ResolveExecutableCandidates(root, target))
        {
            if (File.Exists(executablePath))
            {
                return (executablePath, null);
            }
        }

        var preferredExecutablePath = ResolveExecutableCandidates(root, target).FirstOrDefault() ?? ResolveExecutablePath(root, target);
        var failure = CreateFailureResult(
            message: $"Executable not found: {preferredExecutablePath}",
            state: LaunchStateExeMissing,
            executablePath: preferredExecutablePath,
            arguments: string.Empty,
            diagnostics: new Dictionary<string, object?>
            {
                [DiagnosticResolvedRoot] = root,
                [DiagnosticTarget] = target.ToString(),
                ["attemptedExecutables"] = ResolveExecutableCandidates(root, target).ToArray()
            });
        return (string.Empty, failure);
    }

    private static GameLaunchResult CreateStartedResult(
        GameLaunchRequest request,
        int processId,
        string executablePath,
        string root,
        string arguments)
    {
        return new GameLaunchResult(
            Succeeded: true,
            Message: "Launch command dispatched.",
            ProcessId: processId,
            ExecutablePath: executablePath,
            Arguments: arguments,
            Diagnostics: new Dictionary<string, object?>
            {
                [DiagnosticLaunchState] = LaunchStateStarted,
                [DiagnosticTarget] = request.Target.ToString(),
                ["mode"] = request.Mode.ToString(),
                ["profileIdHint"] = request.ProfileIdHint ?? string.Empty,
                [DiagnosticResolvedRoot] = root,
                ["workshopIds"] = NormalizeWorkshopIds(request.WorkshopIds),
                [DiagnosticOverlayModPath] = request.OverlayModPath ?? string.Empty,
                [DiagnosticLaunchHost] = ClassifyLaunchHost(executablePath, request.Target)
            });
    }

    private static IReadOnlyList<string> ResolveExecutableCandidates(string root, GameLaunchTarget target)
    {
        return target switch
        {
            GameLaunchTarget.Sweaw =>
            [
                Path.Combine(root, "GameData", "sweaw.exe")
            ],
            GameLaunchTarget.Swfoc =>
            [
                Path.Combine(root, "corruption", "StarWarsG.exe"),
                Path.Combine(root, "corruption", "swfoc.exe")
            ],
            _ =>
            [
                Path.Combine(root, "corruption", "StarWarsG.exe"),
                Path.Combine(root, "corruption", "swfoc.exe")
            ]
        };
    }

    private static string ClassifyLaunchHost(string executablePath, GameLaunchTarget target)
    {
        if (target == GameLaunchTarget.Swfoc &&
            Path.GetFileName(executablePath).Equals("StarWarsG.exe", StringComparison.OrdinalIgnoreCase))
        {
            return LaunchHostDirectGameHost;
        }

        if (target == GameLaunchTarget.Swfoc)
        {
            return LaunchHostLauncherStubFallback;
        }

        return LaunchHostDirectGameHost;
    }

    private static string BuildArguments(GameLaunchRequest request)
    {
        var overlayArgument = BuildModPathArgument(request.OverlayModPath);
        return request.Mode switch
        {
            GameLaunchMode.SteamMod => JoinArguments(overlayArgument, BuildSteamModArguments(request.WorkshopIds)),
            GameLaunchMode.ModPath => BuildModPathArgument(request.ModPath),
            _ => overlayArgument
        };
    }

    private static Process StartProcess(string executablePath, string root, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            Arguments = arguments,
            WorkingDirectory = Path.GetDirectoryName(executablePath) ?? root,
            UseShellExecute = false
        };

        return Process.Start(startInfo) ?? throw new InvalidOperationException("Process start returned null.");
    }

    private static GameLaunchResult CreateFailureResult(
        string message,
        string state,
        string executablePath,
        string arguments,
        IReadOnlyDictionary<string, object?>? diagnostics)
    {
        var fullDiagnostics = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            [DiagnosticLaunchState] = state
        };
        if (diagnostics is not null)
        {
            foreach (var entry in diagnostics)
            {
                fullDiagnostics[entry.Key] = entry.Value;
            }
        }

        return new GameLaunchResult(
            Succeeded: false,
            Message: message,
            ProcessId: 0,
            ExecutablePath: executablePath,
            Arguments: arguments,
            Diagnostics: fullDiagnostics);
    }

    private static string BuildSteamModArguments(IReadOnlyList<string>? workshopIds)
    {
        var normalized = NormalizeWorkshopIds(workshopIds);
        if (normalized.Count == 0)
        {
            return string.Empty;
        }

        return string.Join(" ", normalized.Select(static id => $"STEAMMOD={id}"));
    }

    private static string BuildModPathArgument(string? modPath)
    {
        return string.IsNullOrWhiteSpace(modPath)
            ? string.Empty
            : $"MODPATH=\"{modPath}\"";
    }

    private static string JoinArguments(params string?[] values)
    {
        return string.Join(" ", values.Where(static value => !string.IsNullOrWhiteSpace(value)));
    }

    private static IReadOnlyList<string> NormalizeWorkshopIds(IReadOnlyList<string>? workshopIds)
    {
        if (workshopIds is null || workshopIds.Count == 0)
        {
            return Array.Empty<string>();
        }

        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in workshopIds)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            foreach (var token in raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            {
                var id = token.Trim();
                if (id.Length == 0 || !seen.Add(id))
                {
                    continue;
                }

                values.Add(id);
            }
        }

        return values;
    }
}
