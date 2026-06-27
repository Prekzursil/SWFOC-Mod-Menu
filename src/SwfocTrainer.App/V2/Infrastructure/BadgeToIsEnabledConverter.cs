using System.Globalization;
using System.Windows.Data;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// v1.1.0 — converts a <see cref="SwfocTrainer.Core.Diagnostics.CapabilityAwareAction.Badge"/>
/// string to a Boolean for <c>Button.IsEnabled</c>. Returns <c>false</c> when
/// the badge text contains "PHASE 2 PENDING" (case-insensitive), so the button
/// is disabled and the operator can't fire a bridge call that will succeed-
/// with-no-effect.
///
/// Per v1.0.2 improvement plan CRITICAL #4: "PHASE 2 PENDING buttons remain
/// clickable — clicking fires a real bridge call that succeeds-with-no-effect,
/// polluting the activity log." This converter is the fix.
///
/// LIVE-ONLY badges remain enabled (these only work in a particular game mode
/// but DO take effect when conditions are right). MIXED composite buttons also
/// remain enabled because at least some sub-operations land cleanly.
///
/// Usage in XAML:
///     &lt;Button IsEnabled="{Binding MyAction.Badge,
///                              Converter={StaticResource BadgeToIsEnabled}}" /&gt;
/// </summary>
public sealed class BadgeToIsEnabledConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string badge) return true;
        // Case-insensitive match so any capitalization variant of the badge
        // disables the button. Operators see "PHASE 2 PENDING" in catalog;
        // tooltip strings sometimes use "Phase 2 hook pending"; both should
        // disable.
        if (badge.IndexOf("PHASE 2 PENDING", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        if (badge.IndexOf("Phase 2 hook pending", StringComparison.OrdinalIgnoreCase) >= 0) return false;
        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("BadgeToIsEnabledConverter is one-way.");
}
