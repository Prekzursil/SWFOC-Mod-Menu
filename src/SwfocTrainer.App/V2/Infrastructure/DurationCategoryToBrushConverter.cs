using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-27 (iter 65) — maps the operator-facing duration bucket
/// (<c>"Fast" / "Normal" / "Slow" / "VerySlow"</c>) to a theme-aware
/// brush from the application's resource dictionary. Used by the
/// Diagnostics activity DataGrid to color-code the ms column so slow
/// calls pop visually.
///
/// The converter looks up
/// <c>DurationFastBrush / DurationNormalBrush / DurationSlowBrush /
/// DurationVerySlowBrush</c> in <see cref="Application.Resources"/> so
/// the colors live with the rest of the theme dictionary and swap on
/// dark/light toggle. Falls back to the default text foreground if the
/// resource is missing (CI without the App resource dictionary).
/// </summary>
public sealed class DurationCategoryToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var category = value as string ?? "Normal";
        var resourceKey = category switch
        {
            "Fast" => "DurationFastBrush",
            "Slow" => "DurationSlowBrush",
            "VerySlow" => "DurationVerySlowBrush",
            _ => "DurationNormalBrush",
        };
        if (Application.Current?.TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }
        // Fallback: built-in foreground if the App-level resource isn't loaded
        // (e.g., theme not yet swapped, or running in a context without the
        // theme dictionary like a unit test or a designer surface).
        return Application.Current?.TryFindResource("AccentForeground") as Brush
            ?? Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
