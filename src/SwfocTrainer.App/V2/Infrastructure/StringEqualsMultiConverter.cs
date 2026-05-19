using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace SwfocTrainer.App.V2.Infrastructure;

/// <summary>
/// Compares two bound strings for WPF triggers that need binding-to-binding equality.
/// </summary>
public sealed class StringEqualsMultiConverter : IMultiValueConverter
{
    public object Convert(object[]? values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 2)
        {
            return false;
        }

        var left = values[0] as string;
        var right = values[1] as string;
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            Normalize(left),
            Normalize(right),
            StringComparison.OrdinalIgnoreCase);
    }

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(targetTypes);
        var results = new object[targetTypes.Length];
        Array.Fill(results, Binding.DoNothing);
        return results;
    }

    private static string Normalize(string value) =>
        value.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
