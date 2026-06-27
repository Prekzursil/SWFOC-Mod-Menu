using System.Globalization;
using System.Windows.Data;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.Infrastructure;

public sealed class StringEqualsMultiConverterTests
{
    [Fact]
    public void Convert_ReturnsTrueForMatchingPathsWithDifferentCaseAndTrailingSlash()
    {
        var converter = new StringEqualsMultiConverter();

        var result = converter.Convert(
            new object[] { @"C:\Games\Saves\Save1.sav\", @"c:\games\saves\save1.sav" },
            typeof(bool),
            null,
            CultureInfo.InvariantCulture);

        result.Should().Be(true);
    }

    [Fact]
    public void Convert_ReturnsFalseWhenEitherSideIsMissing()
    {
        var converter = new StringEqualsMultiConverter();

        var result = converter.Convert(
            new object[] { @"C:\Games\Saves\Save1.sav", string.Empty },
            typeof(bool),
            null,
            CultureInfo.InvariantCulture);

        result.Should().Be(false);
    }

    [Fact]
    public void ConvertBack_ReturnsDoNothingForEveryTarget()
    {
        var converter = new StringEqualsMultiConverter();

        var result = converter.ConvertBack(
            true,
            new[] { typeof(string), typeof(string) },
            null,
            CultureInfo.InvariantCulture);

        result.Should().Equal(Binding.DoNothing, Binding.DoNothing);
    }
}
