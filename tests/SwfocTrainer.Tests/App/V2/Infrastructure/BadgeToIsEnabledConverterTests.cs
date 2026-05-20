using System.Globalization;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.Infrastructure;

/// <summary>
/// Pins the v1.1.0 <see cref="BadgeToIsEnabledConverter"/> behavior:
/// PHASE 2 PENDING / "Phase 2 hook pending" disable the button;
/// everything else (LIVE / MIXED / LIVE ONLY) keeps it enabled.
/// </summary>
public sealed class BadgeToIsEnabledConverterTests
{
    private static readonly BadgeToIsEnabledConverter _conv = new();

    [Theory]
    [InlineData("LIVE")]
    [InlineData("LIVE ONLY")]
    [InlineData("MIXED (3/5 LIVE)")]
    [InlineData("")]
    [InlineData("something custom")]
    public void Convert_EnablesNonPhase2Badges(string badge)
    {
        var result = _conv.Convert(badge, typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(true);
    }

    [Theory]
    [InlineData("PHASE 2 PENDING")]
    [InlineData("phase 2 pending")] // case-insensitive
    [InlineData("Phase 2 hook pending")]
    [InlineData("OK: AI unfreeze recorded (Phase 2 hook pending)")] // bridge response style
    public void Convert_DisablesPhase2Badges(string badge)
    {
        var result = _conv.Convert(badge, typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(false);
    }

    [Fact]
    public void Convert_NonStringIsEnabled()
    {
        // Defensive: if the binding source isn't a string, default to enabled
        // so we don't accidentally lock out the operator on a type mismatch.
        var result = _conv.Convert(42, typeof(bool), null, CultureInfo.InvariantCulture);
        result.Should().Be(true);
    }

    [Fact]
    public void ConvertBack_ThrowsNotSupported()
    {
        Action act = () => _conv.ConvertBack(true, typeof(string), null, CultureInfo.InvariantCulture);
        act.Should().Throw<NotSupportedException>();
    }
}
