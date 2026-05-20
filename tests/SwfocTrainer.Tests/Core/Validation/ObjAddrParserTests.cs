using FluentAssertions;
using SwfocTrainer.Core.Validation;
using Xunit;

namespace SwfocTrainer.Tests.Core.Validation;

/// <summary>
/// Pins the shared <see cref="ObjAddrParser"/> hoisted from
/// UnitControlTabViewModel in v1.1.0. The tests below mirror the real-world
/// inputs the parser sees across 7 V2 tabs (UnitControl, Inspector, Combat,
/// Speed, Hero Lab, Hardpoint, Cross-Faction).
/// </summary>
public sealed class ObjAddrParserTests
{
    [Theory]
    [InlineData("0x1234ABCD", 0x1234ABCDL)]
    [InlineData("0X1234ABCD", 0x1234ABCDL)]
    [InlineData("1234ABCD",   0x1234ABCDL)]
    [InlineData("1234abcd",   0x1234ABCDL)]
    [InlineData("0x0",        0L)]
    [InlineData("7FFFFFFFFFFFFFFF", long.MaxValue)]
    [InlineData("  0x1234  ", 0x1234L)]
    public void TryParse_AcceptsCanonicalForms(string input, long expected)
    {
        var (ok, addr, err) = ObjAddrParser.TryParse(input);

        ok.Should().BeTrue();
        addr.Should().Be(expected);
        err.Should().BeEmpty();
    }

    [Fact]
    public void TryParse_RejectsBeyondLongMaxValue()
    {
        // FFFFFFFFFFFFFFFF would be 0xFFFFFFFFFFFFFFFF as ulong but exceeds long.MaxValue.
        // Per Core CLS-compliance contract, we reject anything above long.MaxValue
        // even though user-space x64 pointers fit in 48 bits so this is theoretical.
        var (ok, _, err) = ObjAddrParser.TryParse("FFFFFFFFFFFFFFFF");

        ok.Should().BeFalse();
        err.Should().Contain("beyond long.MaxValue");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_RejectsEmpty(string? input)
    {
        var (ok, _, err) = ObjAddrParser.TryParse(input);

        ok.Should().BeFalse();
        err.Should().Contain("empty");
    }

    [Theory]
    [InlineData("0x")]
    [InlineData("0X")]
    public void TryParse_Rejects0xPrefixWithNoDigits(string input)
    {
        var (ok, _, err) = ObjAddrParser.TryParse(input);

        ok.Should().BeFalse();
        err.Should().Contain("no digits");
    }

    [Theory]
    [InlineData("not-a-hex")]
    [InlineData("0xZZZZ")]
    [InlineData("ABCDEFGH")] // G is not hex
    public void TryParse_RejectsInvalidHex(string input)
    {
        var (ok, _, err) = ObjAddrParser.TryParse(input);

        ok.Should().BeFalse();
        err.Should().Contain("not a valid hex");
    }

    [Fact]
    public void TryParse_OverloadWithOutParam_MatchesReturnTuple()
    {
        var success = ObjAddrParser.TryParse("0xDEADBEEF", out var addr);

        success.Should().BeTrue();
        addr.Should().Be(0xDEADBEEFL);
    }

    [Fact]
    public void TryParse_OverloadWithOutParam_FailureSetsZero()
    {
        var success = ObjAddrParser.TryParse("garbage", out var addr);

        success.Should().BeFalse();
        addr.Should().Be(0L);
    }
}
