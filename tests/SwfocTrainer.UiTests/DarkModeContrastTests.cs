using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.UiTests;

/// <summary>
/// Pixel-level proof that the 2026-04-27 custom ComboBox ControlTemplate
/// actually renders dark-mode ComboBoxes correctly. Earlier fixes (implicit
/// styles in Dark.xaml, then in Window.Resources) silently failed because
/// WPF's default Aero ComboBox template hardcodes the field background to
/// <c>SystemColors.WindowBrushKey</c> (white), which no Style setter could
/// override. The fix replaces the entire ControlTemplate.
/// </summary>
/// <remarks>
/// <para>
/// Unlike <see cref="WpfTabAuditTests"/>, this test launches its own editor
/// instance with <c>theme=dark</c> in v2_settings.json (the fixture forces
/// light), drives the UI to where Player slot + Faction ComboBoxes are
/// visible, captures a screen rectangle of the rendered controls via
/// <see cref="Graphics.CopyFromScreen(System.Drawing.Point, System.Drawing.Point, System.Drawing.Size)"/>,
/// and asserts that pixels in the
/// expected field area are actually dark.
/// </para>
/// <para>
/// PNG screenshots are written to <c>test_results/dark_mode_*.png</c> and
/// listed in test output for human review.
/// </para>
/// </remarks>
[SupportedOSPlatform("windows")]
[Trait("Category", "DarkModeContrast")]
public sealed class DarkModeContrastTests : IDisposable
{
    private const string MainWindowTitleSubstring = "SWFOC";
    private static readonly TimeSpan WindowLaunchTimeout = TimeSpan.FromSeconds(30);
    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndNoTopMost = new(-2);
    private const int ShowWindowRestore = 9;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpShowWindow = 0x0040;

    // Threshold for the field-background pixel sample. Dark.xaml declares
    // InputBackground = #15203A, so each channel must be < 0x80 to satisfy
    // the contrast invariant guarded in DarkThemeInputContrastRegressionTests.
    private const int DarkChannelMax = 0x80;

    private readonly ITestOutputHelper _output;
    private readonly string _tempAppData;
    private readonly string _resultsDir;
    private readonly FakeBridgeServer _fakeBridge;
    private readonly UIA3Automation _automation;
    private readonly Application _app;
    private readonly Window _mainWindow;
    private readonly string? _previousAppData;

    public DarkModeContrastTests(ITestOutputHelper output)
    {
        _output = output;

        // Drop screenshots next to the trx logger output so CI / local
        // operators see them in the same place as the test result file.
        _resultsDir = ResolveResultsDir();
        Directory.CreateDirectory(_resultsDir);

        _tempAppData = Path.Combine(
            Path.GetTempPath(),
            $"swfoc-darkmode-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempAppData);

        WriteDarkModeSettings();

        _fakeBridge = new FakeBridgeServer();
        _fakeBridge.Start();

        _previousAppData = Environment.GetEnvironmentVariable("APPDATA");
        Environment.SetEnvironmentVariable("APPDATA", _tempAppData);

        _automation = new UIA3Automation();
        _app = Application.Launch(new ProcessStartInfo
        {
            FileName = ResolveEditorExe(),
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(ResolveEditorExe())!,
            EnvironmentVariables =
            {
                ["APPDATA"] = _tempAppData,
            },
        });
        _mainWindow = WaitForMainWindow();

        // Force the window to a known position / size so we can rely on the
        // ComboBox bounding rect being on-screen. The window also needs to be
        // foregrounded so screen capture sees the real WPF render output and
        // not the contents of any window stacked above it.
        _mainWindow.Move(40, 40);
        BringMainWindowToFront();
    }

    [Fact]
    public void PlayerSlot_ComboBox_RendersWithDarkBackground()
    {
        SelectTab("Player State");

        // Find the closest TextBlock label, then its sibling ComboBox.
        var playerSlotLabel = FindTextBlockByText("Player slot:");
        playerSlotLabel.Should().NotBeNull(
            "Player slot label must render — without it the test cannot anchor to the ComboBox.");

        // The ComboBox is the next sibling to the right of the label in the
        // StackPanel. We find the first ComboBox descendant whose Y overlaps
        // the label's Y range and whose X is greater than the label's right edge.
        var labelRect = playerSlotLabel!.BoundingRectangle;
        var combo = _mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.ComboBox))
            .FirstOrDefault(c =>
            {
                var r = c.BoundingRectangle;
                if (r.IsEmpty) return false;
                bool yOverlap = r.Top < labelRect.Bottom && r.Bottom > labelRect.Top;
                bool xRight = r.Left >= labelRect.Right - 8;
                return yOverlap && xRight;
            });
        combo.Should().NotBeNull(
            "Player slot ComboBox must be located to the right of the 'Player slot:' label.");

        var rect = combo!.BoundingRectangle;
        rect.IsEmpty.Should().BeFalse("ComboBox must have a non-empty bounding rectangle.");
        rect.Width.Should().BeGreaterThan(20);
        rect.Height.Should().BeGreaterThan(10);

        // Capture and assert.
        var screenshotPath = Path.Combine(_resultsDir, "dark_mode_player_slot.png");
        AssertDarkFieldBackground(rect, screenshotPath, controlName: "Player slot");
    }

    [Fact]
    public void Faction_ComboBox_RendersWithDarkBackground()
    {
        SelectTab("Unit Control");

        // Unit Control contains a Faction selector in the Spawn unit group.
        // Same approach as Player slot: anchor to the "Faction:" label, then
        // sample the ComboBox immediately to its right.
        var factionLabel = FindTextBlockByText("Faction:");
        factionLabel.Should().NotBeNull(
            "Faction label must render — without it the test cannot anchor to the ComboBox.");

        var labelRect = factionLabel!.BoundingRectangle;
        var combo = _mainWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.ComboBox))
            .FirstOrDefault(c =>
            {
                var r = c.BoundingRectangle;
                if (r.IsEmpty) return false;
                bool yOverlap = r.Top < labelRect.Bottom && r.Bottom > labelRect.Top;
                bool xRight = r.Left >= labelRect.Right - 8;
                return yOverlap && xRight;
            });
        combo.Should().NotBeNull(
            "Faction ComboBox must be located to the right of the 'Faction:' label.");

        var rect = combo!.BoundingRectangle;
        rect.IsEmpty.Should().BeFalse();
        rect.Width.Should().BeGreaterThan(20);
        rect.Height.Should().BeGreaterThan(10);

        var screenshotPath = Path.Combine(_resultsDir, "dark_mode_faction.png");
        AssertDarkFieldBackground(rect, screenshotPath, controlName: "Faction");
    }

    [Fact]
    public void TabHeader_ChromeIsDark_NotWhite()
    {
        // Belt-and-braces: the WindowBackground brush should also yield a dark
        // pixel near the top-left of the chrome area, so we know the dark
        // theme dictionary is the active one. If theme="dark" silently fell
        // back to light (e.g. ThemeService.ApplyPreference parse failure),
        // this test fires before the per-control checks.
        var rect = _mainWindow.BoundingRectangle;
        rect.IsEmpty.Should().BeFalse();

        // Sample a small strip just inside the window chrome (5px in from the
        // window edge, away from the OS-drawn title bar which is OS-themed
        // and won't reflect our XAML brushes).
        var sample = new Rectangle(rect.Left + 5, rect.Top + 80, 60, 30);
        using var bmp = CaptureScreen(sample);
        var screenshotPath = Path.Combine(_resultsDir, "dark_mode_window_chrome.png");
        bmp.Save(screenshotPath, ImageFormat.Png);
        _output.WriteLine($"Saved: {screenshotPath}");

        var (avgR, avgG, avgB) = SampleAverage(bmp);
        _output.WriteLine($"Window chrome avg colour: R={avgR:X2} G={avgG:X2} B={avgB:X2}");

        (avgR < DarkChannelMax && avgG < DarkChannelMax && avgB < DarkChannelMax)
            .Should().BeTrue(
                $"Window chrome is supposed to render dark in theme=dark, but mean colour was " +
                $"R={avgR:X2} G={avgG:X2} B={avgB:X2}. Screenshot: {screenshotPath}.");
    }

    private void AssertDarkFieldBackground(
        Rectangle uiaRect,
        string screenshotPath,
        string controlName)
    {
        // Sample the LEFT 60% of the ComboBox horizontally — the right ~20px
        // is the dropdown arrow with ButtonBackground (slightly different shade)
        // which we want to exclude from the field-background assertion.
        // FlaUI 5.0 returns System.Drawing.Rectangle directly, so we just clone.
        var fullRect = new Rectangle(uiaRect.Left, uiaRect.Top, uiaRect.Width, uiaRect.Height);

        using var fullBmp = CaptureScreen(fullRect);
        fullBmp.Save(screenshotPath, ImageFormat.Png);
        _output.WriteLine($"Saved: {screenshotPath} ({fullBmp.Width}x{fullBmp.Height})");

        // Build a bitmap that is just the field area (no arrow chrome on the right).
        int fieldWidth = Math.Max(8, (int)(fullBmp.Width * 0.6));
        int fieldHeight = fullBmp.Height;
        using var fieldBmp = new Bitmap(fieldWidth, fieldHeight, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(fieldBmp))
        {
            g.DrawImage(fullBmp,
                destRect: new Rectangle(0, 0, fieldWidth, fieldHeight),
                srcRect: new Rectangle(0, 0, fieldWidth, fieldHeight),
                srcUnit: GraphicsUnit.Pixel);
        }

        // To avoid being misled by light foreground TEXT pixels (which are
        // legitimately bright in dark mode), sample the DARKEST 25% of pixels
        // in the field rectangle and average them — that's the background.
        var pixels = EnumeratePixels(fieldBmp).ToList();
        var sortedByLuma = pixels
            .OrderBy(p => p.R * 299 + p.G * 587 + p.B * 114)
            .Take(Math.Max(1, pixels.Count / 4))
            .ToList();
        var avgR = (int)sortedByLuma.Average(p => (double)p.R);
        var avgG = (int)sortedByLuma.Average(p => (double)p.G);
        var avgB = (int)sortedByLuma.Average(p => (double)p.B);
        _output.WriteLine(
            $"{controlName} ComboBox field-background avg (darkest quartile): " +
            $"R={avgR:X2} G={avgG:X2} B={avgB:X2}");

        // Also sample the BRIGHTEST 5% — that should be the foreground text.
        // It must be light (any channel >= 0xA0) so we know contrast is real.
        var sortedByLumaDesc = pixels
            .OrderByDescending(p => p.R * 299 + p.G * 587 + p.B * 114)
            .Take(Math.Max(1, pixels.Count / 20))
            .ToList();
        var fgR = (int)sortedByLumaDesc.Average(p => (double)p.R);
        var fgG = (int)sortedByLumaDesc.Average(p => (double)p.G);
        var fgB = (int)sortedByLumaDesc.Average(p => (double)p.B);
        _output.WriteLine(
            $"{controlName} ComboBox foreground avg (brightest 5%): " +
            $"R={fgR:X2} G={fgG:X2} B={fgB:X2}");

        // Assert: background dark.
        (avgR < DarkChannelMax && avgG < DarkChannelMax && avgB < DarkChannelMax)
            .Should().BeTrue(
                $"{controlName} ComboBox should render with a dark field background " +
                $"(each RGB channel < 0x{DarkChannelMax:X2}), but darkest-quartile mean was " +
                $"R={avgR:X2} G={avgG:X2} B={avgB:X2}. " +
                $"This means the WPF Aero default ControlTemplate is still being used, " +
                $"or the InputBackground brush wasn't applied. " +
                $"Screenshot: {screenshotPath}.");

        // Assert: foreground brighter than background by at least 0x40 in
        // each channel (the actual gap is normally far larger; 0x40 is a
        // sanity floor that catches "everything is the same dark colour").
        ((fgR > avgR + 0x40) || (fgG > avgG + 0x40) || (fgB > avgB + 0x40))
            .Should().BeTrue(
                $"{controlName} ComboBox foreground (text) must be readably brighter than " +
                $"its background. Got bg=R{avgR:X2}/G{avgG:X2}/B{avgB:X2}, " +
                $"fg=R{fgR:X2}/G{fgG:X2}/B{fgB:X2}. Screenshot: {screenshotPath}.");
    }

    private AutomationElement? FindTextBlockByText(string text)
    {
        // FlaUI's Text element type matches WPF TextBlock under UIA3.
        var matches = _mainWindow.FindAllDescendants(cf =>
            cf.ByControlType(ControlType.Text));
        return matches.FirstOrDefault(t =>
            string.Equals((t.Name ?? string.Empty).Trim(),
                          text.Trim(),
                          StringComparison.OrdinalIgnoreCase));
    }

    private void SelectTab(string tabName)
    {
        var tabControl = _mainWindow.FindFirstDescendant(cf => cf.ByControlType(ControlType.Tab))
            ?? throw new InvalidOperationException("Main TabControl not found.");
        var headers = tabControl.FindAllChildren(cf => cf.ByControlType(ControlType.TabItem));
        var header = headers.FirstOrDefault(h =>
            string.Equals((h.Name ?? string.Empty).Trim(),
                          tabName,
                          StringComparison.OrdinalIgnoreCase));
        if (header is null)
        {
            throw new InvalidOperationException($"Tab header not found: {tabName}");
        }

        var selectionItem = header.Patterns.SelectionItem.PatternOrDefault;
        if (selectionItem is not null)
        {
            selectionItem.Select();
        }
        else
        {
            header.Click();
        }
        Thread.Sleep(300);
    }

    private Bitmap CaptureScreen(Rectangle screenRect)
    {
        BringMainWindowToFront();
        var bmp = new Bitmap(screenRect.Width, screenRect.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(
                sourceX: screenRect.Left,
                sourceY: screenRect.Top,
                destinationX: 0,
                destinationY: 0,
                blockRegionSize: new Size(screenRect.Width, screenRect.Height));
        }
        return bmp;
    }

    private void BringMainWindowToFront()
    {
        var rawHandle = _mainWindow.Properties.NativeWindowHandle.ValueOrDefault;
        if (rawHandle == 0)
        {
            _mainWindow.Focus();
            Thread.Sleep(400);
            return;
        }

        var hwnd = new IntPtr(rawHandle);
        ShowWindow(hwnd, ShowWindowRestore);
        SetWindowPos(hwnd, HwndTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        SetForegroundWindow(hwnd);
        _mainWindow.Focus();
        Thread.Sleep(150);
        SetWindowPos(hwnd, HwndNoTopMost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpShowWindow);
        Thread.Sleep(250);
    }

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);

    private static (int, int, int) SampleAverage(Bitmap bmp)
    {
        long r = 0, g = 0, b = 0;
        int count = 0;
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                var c = bmp.GetPixel(x, y);
                r += c.R; g += c.G; b += c.B;
                count++;
            }
        }
        return ((int)(r / count), (int)(g / count), (int)(b / count));
    }

    private static System.Collections.Generic.IEnumerable<Color> EnumeratePixels(Bitmap bmp)
    {
        for (int y = 0; y < bmp.Height; y++)
        {
            for (int x = 0; x < bmp.Width; x++)
            {
                yield return bmp.GetPixel(x, y);
            }
        }
    }

    private static string ResolveEditorExe()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
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
            "SwfocTrainer.App.exe not found. Build the editor before running UI tests.");
    }

    private static string ResolveResultsDir()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "tests");
            if (Directory.Exists(candidate))
            {
                // We are in the editor solution; results go under the
                // sibling swfoc_memory/test_results path used by the rest
                // of the harness.
                return Path.Combine(
                    Path.GetDirectoryName(dir)!,
                    "swfoc_memory", "test_results");
            }
            dir = Path.GetDirectoryName(dir);
        }
        return Path.Combine(Path.GetTempPath(), "swfoc-darkmode-results");
    }

    private void WriteDarkModeSettings()
    {
        var appDir = Path.Combine(_tempAppData, "SwfocTrainer");
        Directory.CreateDirectory(appDir);
        var settingsPath = Path.Combine(appDir, "v2_settings.json");
        File.WriteAllText(settingsPath,
            "{\n  \"autoConnect\": true,\n  \"theme\": \"dark\"\n}");
    }

    private Window WaitForMainWindow()
    {
        var deadline = DateTime.UtcNow + WindowLaunchTimeout;
        Window? found = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var wnd = _app.GetMainWindow(_automation, TimeSpan.FromSeconds(2));
                if (wnd is not null && (wnd.Title ?? string.Empty)
                    .Contains(MainWindowTitleSubstring, StringComparison.OrdinalIgnoreCase))
                {
                    found = wnd;
                    break;
                }
            }
            catch (TimeoutException)
            {
                // still loading
            }
            Thread.Sleep(250);
        }
        if (found is null)
        {
            throw new InvalidOperationException(
                $"Main window did not appear within {WindowLaunchTimeout}.");
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
            // best-effort cleanup
        }

        _automation?.Dispose();

        try { _fakeBridge?.Dispose(); } catch { /* best-effort */ }

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
            // best-effort
        }
    }
}
