using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// 2026-04-28 (iter 70) — maps the operator-facing
/// <see cref="BridgeActivityStats.HealthCategory"/>
/// (<c>"Healthy" / "Degraded" / "Failing"</c>) to a theme-aware brush
/// from the application's resource dictionary. Used by the bottom
/// status bar dot to surface bridge health at a glance regardless of
/// which tab the operator is on.
///
/// Theme keys: <c>BridgeHealthHealthyBrush</c> /
/// <c>BridgeHealthDegradedBrush</c> / <c>BridgeHealthFailingBrush</c>
/// — defined in both <c>Dark.xaml</c> and <c>Light.xaml</c>.
/// </summary>
public sealed class HealthCategoryToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var category = value as string ?? "Healthy";
        var resourceKey = category switch
        {
            "Failing" => "BridgeHealthFailingBrush",
            "Degraded" => "BridgeHealthDegradedBrush",
            _ => "BridgeHealthHealthyBrush",
        };
        if (Application.Current?.TryFindResource(resourceKey) is Brush brush)
        {
            return brush;
        }
        // Fallback for design-time / test contexts without the resource dictionary.
        return Application.Current?.TryFindResource("AccentForeground") as Brush
            ?? Brushes.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
