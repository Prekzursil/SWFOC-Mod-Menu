using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace SwfocTrainer.UiTests;

/// <summary>
/// xUnit collection fixture that launches the SWFOC Trainer WPF app in a
/// safe-for-testing mode (no auto-connect to the bridge, no live game
/// required) once per test assembly, and tears it down at the end.
/// </summary>
/// <remarks>
/// <para>
/// The editor's <c>V2Settings.AutoConnectOnStartup</c> defaults to <c>true</c>,
/// which would make the editor probe the bridge pipe at window-loaded time.
/// During UI tests we don't want that — we just want the UI to render so we
/// can audit visibility/positioning/clickability per tab. The fixture writes
/// a one-shot <c>v2_settings.json</c> with <c>autoConnect: false</c> in a
/// dedicated AppData directory, then launches the app pointing at it via
/// the <c>APPDATA</c> env var override.
/// </para>
/// <para>
/// This fixture also captures the FlaUI <see cref="UIA3Automation"/> handle
/// and the main-window <see cref="Window"/> reference so individual tests
/// can navigate quickly without re-launching the app each time.
/// </para>
/// </remarks>
public sealed class EditorAppFixture : IDisposable
{
    private const string MainWindowTitleSubstring = "SWFOC";
    private static readonly TimeSpan WindowLaunchTimeout = TimeSpan.FromSeconds(30);

    private readonly string _tempAppData;
    private string? _previousAppData;
    private Application? _app;

    public UIA3Automation Automation { get; private set; } = null!;
    public Window MainWindow { get; private set; } = null!;
    public string EditorExePath { get; }

    /// <summary>
    /// In-process stand-in for the bridge DLL pipe server. Started before the
    /// editor launches so the V2 Diagnostics tab's auto-probe succeeds and
    /// the rich tab surface renders. <see cref="FakeBridgeServer.CommandsServed"/>
    /// can be inspected from tests to assert that probes actually round-tripped.
    /// </summary>
    public FakeBridgeServer FakeBridge { get; private set; } = null!;

    public EditorAppFixture()
    {
        EditorExePath = ResolveEditorExe();
        _tempAppData = Path.Combine(
            Path.GetTempPath(),
            $"swfoc-uitest-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempAppData);

        // Settings: enable autoConnect so the Diagnostics tab probes the
        // bridge on Loaded — those probes hit our FakeBridgeServer, not a
        // real DLL or game.
        WriteSettings(autoConnect: true);

        // Start the fake bridge BEFORE launching the editor so the editor's
        // first connect attempt (1500 ms timeout) succeeds.
        FakeBridge = new FakeBridgeServer();
        FakeBridge.Start();

        _previousAppData = Environment.GetEnvironmentVariable("APPDATA");
        Environment.SetEnvironmentVariable("APPDATA", _tempAppData);

        Automation = new UIA3Automation();
        _app = Application.Launch(new ProcessStartInfo
        {
            FileName = EditorExePath,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(EditorExePath)!,
            EnvironmentVariables =
            {
                ["APPDATA"] = _tempAppData,
            },
        });

        MainWindow = WaitForMainWindow();

        // Give the Diagnostics tab a moment to issue its initial probe set —
        // SafeProbeAsync is fire-and-forget on Loaded, so we briefly wait for
        // the FakeBridgeServer to have served at least the four diagnostics
        // probes before tests start walking the UI.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(8);
        while (FakeBridge.CommandsServed < 4 && DateTime.UtcNow < deadline)
        {
            Thread.Sleep(150);
        }
    }

    private static string ResolveEditorExe()
    {
        // Walk up from this assembly's bin/Debug or bin/Release to the editor solution root,
        // then descend to src/SwfocTrainer.App/bin/<config>/net8.0-windows/SwfocTrainer.App.exe.
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            // Prefer the same configuration we are running under (Debug/Release).
            var thisCfg = AppContext.BaseDirectory.Contains("\\Release\\", StringComparison.OrdinalIgnoreCase)
                ? "Release" : "Debug";
            var candidate = Path.Combine(
                dir,
                "src", "SwfocTrainer.App", "bin", thisCfg, "net8.0-windows",
                "SwfocTrainer.App.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            "SwfocTrainer.App.exe not found. Build the editor (Debug or Release) " +
            "before running UI tests.");
    }

    private void WriteSettings(bool autoConnect)
    {
        // %APPDATA%/SwfocTrainer/v2_settings.json
        var appDir = Path.Combine(_tempAppData, "SwfocTrainer");
        Directory.CreateDirectory(appDir);
        var settingsPath = Path.Combine(appDir, "v2_settings.json");
        var json = "{\n  \"autoConnect\": " + (autoConnect ? "true" : "false")
                 + ",\n  \"theme\": \"light\"\n}";
        File.WriteAllText(settingsPath, json);
    }

    private Window WaitForMainWindow()
    {
        var deadline = DateTime.UtcNow + WindowLaunchTimeout;
        Window? found = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var wnd = _app!.GetMainWindow(Automation, TimeSpan.FromSeconds(2));
                if (wnd is not null && (wnd.Title ?? string.Empty)
                    .Contains(MainWindowTitleSubstring, StringComparison.OrdinalIgnoreCase))
                {
                    found = wnd;
                    break;
                }
            }
            catch (TimeoutException)
            {
                // Still loading — retry.
            }
            Thread.Sleep(250);
        }
        if (found is null)
        {
            throw new InvalidOperationException(
                $"Main window with title containing '{MainWindowTitleSubstring}' did not appear " +
                $"within {WindowLaunchTimeout}. Editor exe: {EditorExePath}");
        }
        return found;
    }

    public void Dispose()
    {
        try
        {
            if (_app is not null && !_app.HasExited)
            {
                _app.Close();
                _app.WaitWhileMainHandleIsMissing(TimeSpan.FromSeconds(2));
                if (!_app.HasExited)
                {
                    _app.Kill();
                }
            }
        }
        catch (Exception)
        {
            // Best-effort cleanup.
        }

        Automation?.Dispose();

        try
        {
            FakeBridge?.Dispose();
        }
        catch (Exception)
        {
            // Best-effort.
        }

        try
        {
            Environment.SetEnvironmentVariable("APPDATA", _previousAppData);
            if (Directory.Exists(_tempAppData))
            {
                Directory.Delete(_tempAppData, recursive: true);
            }
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }
}

[CollectionDefinition("Editor UI", DisableParallelization = true)]
public sealed class EditorUiCollection : ICollectionFixture<EditorAppFixture>
{
    // Parallelization disabled: only one editor instance can attach to the
    // accessibility tree at a time, and tests share window state.
}
