using System.Diagnostics;
using System.IO.Pipes;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// xUnit class fixture that boots <c>swfoc_replay.exe</c> against a synthetic
/// snapshot, exposes a <see cref="NamedPipeLuaBridgeClient"/> aimed at the
/// replay pipe, and tears the harness down on dispose. Tests share one fixture
/// per test class via <see cref="IClassFixture{TFixture}"/>.
/// </summary>
/// <remarks>
/// Constraints from Phase 6 of the PRD:
/// <list type="bullet">
/// <item>The replay binary lives at <c>swfoc_lua_bridge\swfoc_replay.exe</c>
///       under the swfoc_memory repo. The fixture probes a few well-known
///       locations and skips the test class (via
///       <see cref="ReplayBinaryAvailable"/>) when the binary is not built.</item>
/// <item>The replay listens on
///       <c>\\.\pipe\swfoc_bridge_replay</c>. We poll for connectivity for up
///       to <c>StartupBudget</c> after launch.</item>
/// <item>Stdout and stderr from the child process are drained into in-memory
///       buffers so test failures can attach the binary's diagnostic log.</item>
/// </list>
/// </remarks>
public sealed class ReplayHarnessFixture : IDisposable
{
    /// <summary>Pipe name the replay harness binds to (must match REPLAY_PIPE_NAME in replay_harness.cpp).</summary>
    public const string ReplayPipeName = "swfoc_bridge_replay";

    /// <summary>Maximum time to wait for the replay binary to open its pipe before declaring startup failed.</summary>
    public static readonly TimeSpan StartupBudget = TimeSpan.FromSeconds(8);

    /// <summary>The credits value the canonical fixture writes for the local player.</summary>
    public const double FixtureLocalCredits = 12345.0;

    /// <summary>The faction string the canonical fixture writes for the local player.</summary>
    public const string FixtureLocalFaction = "UNDERWORLD";

    /// <summary>Number of TIE_Fighter instances baked into the fixture's object catalog.</summary>
    public const int FixtureTieFighterCount = 12;

    /// <summary>Number of Vengeance_Frigate instances baked into the fixture's object catalog.</summary>
    public const int FixtureVengeanceCount = 1;

    /// <summary>Mod-name metadata value baked into the fixture.</summary>
    public const string FixtureModName = "phase9_replay_fixture";

    /// <summary>Number of player slots in the fixture (REBEL, EMPIRE, UNDERWORLD).</summary>
    public const int FixturePlayerCount = 3;

    private readonly string _tempDir;
    private readonly string _snapshotPath;
    private readonly Process? _replayProcess;
    private readonly bool _binaryAvailable;
    private readonly System.Text.StringBuilder _stdoutBuffer = new();
    private readonly System.Text.StringBuilder _stderrBuffer = new();
    private bool _disposed;

    public ReplayHarnessFixture()
    {
        // Reclaim the replay pipe handle if a prior fixture's child outlived its
        // 2-second teardown budget — see KillStrayReplayProcesses XML doc.
        KillStrayReplayProcesses();

        _tempDir = Path.Combine(
            Path.GetTempPath(),
            $"swfoc_replay_fixture_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Canonical fixture snapshot: REBEL/EMPIRE/UNDERWORLD slots, UNDERWORLD local (see Fixture* constants).
        _snapshotPath = ReplaySnapshotBuilder.Create()
            .WithPlayer("REBEL", credits: 5000.0, techLevel: 1)
            .WithPlayer("EMPIRE", credits: 10000.0, techLevel: 3)
            .WithPlayer(FixtureLocalFaction, credits: FixtureLocalCredits, techLevel: 2)
            .WithLocalPlayerSlot(2)
            .WithObjects("TIE_Fighter", FixtureTieFighterCount)
            .WithObject("Vengeance_Frigate", (uint)FixtureVengeanceCount)
            .WithObjects("Nebulon_B_Frigate", 4)
            .WithObjects("Star_Destroyer", 2)
            .WithMetadata("mod_name", FixtureModName)
            .WithMetadata("mod_version", "9.0.0")
            .WithMetadata("capture_method", "phase9_test_builder")
            .Build(_tempDir);

        var binaryPath = LocateReplayBinary();
        _binaryAvailable = binaryPath is not null && File.Exists(binaryPath);
        if (!_binaryAvailable)
        {
            // Tests will skip via ReplayBinaryAvailable on machines without the harness built.
            return;
        }

        _replayProcess = LaunchReplayProcess(binaryPath!, _snapshotPath);
        if (_replayProcess is null)
        {
            _binaryAvailable = false;
            return;
        }

        if (!WaitForPipe(StartupBudget))
        {
            // Pipe never came up — kill child and skip cleanly with diagnostic output.
            TryKillProcess();
            _binaryAvailable = false;
        }
    }

    /// <summary>True when the replay binary was found and the pipe is reachable.</summary>
    public bool ReplayBinaryAvailable => _binaryAvailable;

    /// <summary>Path to the synthetic snapshot the harness loaded.</summary>
    public string SnapshotPath => _snapshotPath;

    /// <summary>
    /// Returns a fresh <see cref="NamedPipeLuaBridgeClient"/> targeted at the
    /// replay pipe. We hand back a new instance per call because the bridge
    /// closes the connection after every command, so callers should not share
    /// a long-lived client.
    /// </summary>
    public NamedPipeLuaBridgeClient Bridge => new NamedPipeLuaBridgeClient(
        pipeName: ReplayPipeName,
        connectTimeoutMs: 4000,
        readTimeoutMs: 8000);

    /// <summary>Drains the captured stdout buffer for diagnostic output on test failure.</summary>
    public string GetStdoutSnapshot()
    {
        lock (_stdoutBuffer)
        {
            return _stdoutBuffer.ToString();
        }
    }

    /// <summary>Drains the captured stderr buffer for diagnostic output on test failure.</summary>
    public string GetStderrSnapshot()
    {
        lock (_stderrBuffer)
        {
            return _stderrBuffer.ToString();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        TryKillProcess();

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup; tests should not fail because the temp file
            // could not be removed.
        }
        catch (UnauthorizedAccessException)
        {
        }

        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Walks a small list of well-known directories to find the replay binary.
    /// We bias toward the swfoc_memory repo's <c>swfoc_lua_bridge</c> folder
    /// next to this editor checkout, but allow an environment variable
    /// override for CI / split layouts.
    /// </summary>
    private static string? LocateReplayBinary()
    {
        var envOverride = Environment.GetEnvironmentVariable("SWFOC_REPLAY_BINARY");
        if (!string.IsNullOrWhiteSpace(envOverride) && File.Exists(envOverride))
        {
            return envOverride;
        }

        var candidates = new[]
        {
            @"C:\Users\Prekzursil\Downloads\swfoc_memory\swfoc_lua_bridge\swfoc_replay.exe",
            Path.Combine(Environment.CurrentDirectory, "swfoc_replay.exe"),
            Path.Combine(AppContext.BaseDirectory, "swfoc_replay.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private Process? LaunchReplayProcess(string binaryPath, string snapshotPath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = binaryPath,
                WorkingDirectory = Path.GetDirectoryName(binaryPath) ?? Environment.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.ArgumentList.Add(snapshotPath);

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                lock (_stdoutBuffer)
                {
                    _stdoutBuffer.AppendLine(e.Data);
                }
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null) return;
                lock (_stderrBuffer)
                {
                    _stderrBuffer.AppendLine(e.Data);
                }
            };

            if (!proc.Start())
            {
                return null;
            }
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            return proc;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Polls the replay pipe until it accepts a connection or the budget runs
    /// out. Returns true when the pipe is reachable.
    /// </summary>
    private bool WaitForPipe(TimeSpan budget)
    {
        var deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (_replayProcess is not null && _replayProcess.HasExited)
            {
                return false;
            }

            try
            {
                using var pipe = new NamedPipeClientStream(".", ReplayPipeName, PipeDirection.InOut);
                pipe.Connect(timeout: 250);
                if (pipe.IsConnected)
                {
                    return true;
                }
            }
            catch (TimeoutException)
            {
                // expected during startup; loop and retry
            }
            catch (IOException)
            {
                // pipe not yet created; retry
            }

            Thread.Sleep(150);
        }
        return false;
    }

    /// <summary>
    /// Defensive cleanup of any orphaned <c>swfoc_replay.exe</c> processes that
    /// still hold <c>\\.\pipe\swfoc_bridge_replay</c>. Prior fixture instances
    /// invoke <see cref="TryKillProcess"/> with a 2-second <c>WaitForExit</c>
    /// budget; if a child takes longer than that to release the pipe handle
    /// (Windows pipe-server teardown is cooperative), the next fixture's
    /// freshly-spawned binary hits ERROR_PIPE_BUSY (231) when it calls
    /// <c>CreateNamedPipe</c>. This shows up as a flake in stress testing.
    /// Killing strays first plus a brief grace pause lets the OS reclaim the
    /// handle before our launch.
    /// </summary>
    private static void KillStrayReplayProcesses()
    {
        Process[] strays;
        try
        {
            strays = Process.GetProcessesByName("swfoc_replay");
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (strays.Length == 0)
        {
            return;
        }

        foreach (var stray in strays)
        {
            try
            {
                stray.Kill(entireProcessTree: true);
                stray.WaitForExit(2000);
            }
            catch (InvalidOperationException) { /* already gone */ }
            catch (System.ComponentModel.Win32Exception) { /* access denied or race */ }
            finally
            {
                try { stray.Dispose(); } catch { /* ignore disposal during cleanup */ }
            }
        }

        // Grace pause to let Windows release the pipe handle. 250ms covers
        // typical pipe-teardown latency without slowing the green path too much.
        Thread.Sleep(250);
    }

    private void TryKillProcess()
    {
        if (_replayProcess is null)
        {
            return;
        }

        try
        {
            if (!_replayProcess.HasExited)
            {
                _replayProcess.Kill(entireProcessTree: true);
                _replayProcess.WaitForExit(2000);
            }
        }
        catch (InvalidOperationException)
        {
            // process already gone
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // permissions race during teardown; ignore
        }
        finally
        {
            try
            {
                _replayProcess.Dispose();
            }
            catch
            {
                // ignore disposal errors during teardown
            }
        }
    }
}
