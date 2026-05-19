using System;
using System.IO;
using System.Xml;
using FluentAssertions;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// Guards the dark/light theme dictionaries against regressing the
/// 2026-04-27 input-control contrast fix.
/// </summary>
/// <remarks>
/// <para>
/// Background: until 2026-04-27, <c>Dark.xaml</c> defined semantic theme
/// brushes (TextForeground, ChromeBackground, etc.) but contained NO
/// <c>Style TargetType="ComboBox"</c> or <c>TargetType="TextBox"</c>. Those
/// controls fell back to WPF's default light system colours, which made
/// the inner <c>SelectionBoxItem</c> of a ComboBox render as black-on-near-
/// black in dark mode. The user reported "EMPIRE barely visible" and
/// "Player slot blank" via screenshot. The fix added unkeyed implicit
/// <c>Style</c> declarations for <c>TextBox</c>, <c>ComboBox</c>, and
/// <c>ComboBoxItem</c> in both theme dictionaries, plus a new pair of
/// <c>InputBackground</c> / <c>InputForeground</c> brushes.
/// </para>
/// <para>
/// These tests parse the theme XAML directly and assert the required
/// brushes and styles exist with DynamicResource bindings.
/// </para>
/// </remarks>
public sealed class DarkThemeInputContrastRegressionTests
{
    private const string XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string XNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string LoadThemeXaml(string fileName)
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(
                dir,
                "src", "SwfocTrainer.App", "V2", "Themes", fileName);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            $"{fileName} not found by walking up from {AppContext.BaseDirectory}");
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void Theme_Defines_InputContrastBrushes(string themeFile)
    {
        var src = LoadThemeXaml(themeFile);
        src.Should().Contain("x:Key=\"InputBackground\"",
            $"{themeFile} must define InputBackground; without it ComboBox / TextBox " +
            "fall back to WPF system colours and become unreadable in dark mode " +
            "(see knowledge-base/freeze_audit_2026-04-27.md sibling: dark-mode-bug-2026-04-27).");
        src.Should().Contain("x:Key=\"InputForeground\"",
            $"{themeFile} must define InputForeground.");
        src.Should().Contain("x:Key=\"InputBorderBrush\"",
            $"{themeFile} must define InputBorderBrush.");
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void Theme_DefinesImplicitStyles_For_InputControls(string themeFile)
    {
        var src = LoadThemeXaml(themeFile);

        // Use word-boundary substring checks — keeping it simple instead of
        // pulling in a full XAML parser.
        src.Should().Contain("TargetType=\"TextBox\"",
            $"{themeFile} must declare an implicit Style for TextBox.");
        src.Should().Contain("TargetType=\"ComboBox\"",
            $"{themeFile} must declare an implicit Style for ComboBox.");
        src.Should().Contain("TargetType=\"ComboBoxItem\"",
            $"{themeFile} must declare an implicit Style for ComboBoxItem so dropdown rows " +
            "are themed in addition to the SelectionBoxItem.");
    }

    [Theory]
    [InlineData("Dark.xaml")]
    [InlineData("Light.xaml")]
    public void Theme_InputStyles_Use_DynamicResource(string themeFile)
    {
        var src = LoadThemeXaml(themeFile);
        // The brushes must be bound DynamicResource, not StaticResource — otherwise
        // ThemeService.ApplyPreference can't swap themes at runtime.
        src.Should().Contain("Value=\"{DynamicResource InputBackground}\"",
            $"{themeFile} must bind input Background via DynamicResource.");
        src.Should().Contain("Value=\"{DynamicResource InputForeground}\"",
            $"{themeFile} must bind input Foreground via DynamicResource.");
    }

    [Fact]
    public void DarkTheme_InputBackground_IsDarkEnoughForContrast()
    {
        // The dark-mode InputBackground brush must be a dark colour
        // (each RGB channel < 0x80) so light foreground text stays readable.
        var src = LoadThemeXaml("Dark.xaml");
        var marker = "x:Key=\"InputBackground\"";
        var idx = src.IndexOf(marker, StringComparison.Ordinal);
        idx.Should().BeGreaterThan(-1);
        var colorStart = src.IndexOf("Color=\"#", idx, StringComparison.Ordinal);
        colorStart.Should().BeGreaterThan(-1);
        var hex = src.Substring(colorStart + 8, 6);
        var r = Convert.ToInt32(hex.Substring(0, 2), 16);
        var g = Convert.ToInt32(hex.Substring(2, 2), 16);
        var b = Convert.ToInt32(hex.Substring(4, 2), 16);
        (r < 0x80 && g < 0x80 && b < 0x80).Should().BeTrue(
            $"dark-mode InputBackground #{hex} should have all RGB channels < 0x80; got R={r:X2} G={g:X2} B={b:X2}.");
    }

    private static string LoadMainWindowXaml()
    {
        var dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(
                dir,
                "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml");
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException(
            $"MainWindowV2.xaml not found by walking up from {AppContext.BaseDirectory}");
    }

    [Fact]
    public void MainWindow_DeclaresCustom_ComboBox_ControlTemplate()
    {
        // Background: WPF's default Aero ComboBox ControlTemplate hardcodes the
        // field background to SystemColors.WindowBrushKey (white). No Style setter
        // can override that — only a full ControlTemplate replacement works.
        // The 2026-04-27 fix replaced the implicit ComboBox style with one that
        // owns the entire visual tree via a custom Template setter. Reverting to
        // a setter-only style would re-introduce the white-field bug, so this
        // test guards the structural fix.
        var src = LoadMainWindowXaml();
        src.Should().Contain("ComboBoxToggleButtonChrome",
            "MainWindowV2.xaml must declare the private ComboBoxToggleButtonChrome style " +
            "used inside the custom ComboBox ControlTemplate.");
        src.Should().Contain("ControlTemplate TargetType=\"ComboBox\"",
            "MainWindowV2.xaml must declare a custom ControlTemplate for ComboBox so the " +
            "field background is no longer sourced from SystemColors.WindowBrushKey (white).");
        src.Should().Contain("PART_EditableTextBox",
            "Custom ComboBox template must include the PART_EditableTextBox so IsEditable=True " +
            "ComboBoxes (e.g. faction filter inputs) keep working.");
    }

    [Fact]
    public void MainWindow_CustomComboBoxTemplate_BindsForeground_ViaDynamicResource()
    {
        // The ContentPresenter inside the custom template must bind both
        // TextElement.Foreground and TextBlock.Foreground to InputForeground via
        // DynamicResource — that's what makes "Slot 6 — UNDERWORLD" actually
        // render in the high-contrast colour. Without these bindings the closed-
        // state ContentPresenter ignores the templated Foreground (Win11 quirk).
        var src = LoadMainWindowXaml();
        src.Should().Contain("TextElement.Foreground=\"{DynamicResource InputForeground}\"",
            "Custom ComboBox template must bind TextElement.Foreground via DynamicResource " +
            "so DataTemplate-rendered closed-state items pick up high contrast.");
        src.Should().Contain("TextBlock.Foreground=\"{DynamicResource InputForeground}\"",
            "Custom ComboBox template must bind TextBlock.Foreground via DynamicResource " +
            "so plain-string items render with InputForeground.");
        src.Should().Contain("Background=\"{DynamicResource InputBackground}\"",
            "Custom ComboBoxToggleButtonChrome must source its border background from " +
            "InputBackground via DynamicResource (not the WPF default WindowBrushKey).");
    }
}
