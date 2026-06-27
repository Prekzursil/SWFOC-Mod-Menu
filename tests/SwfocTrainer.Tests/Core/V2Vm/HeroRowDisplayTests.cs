using FluentAssertions;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.Core.V2Vm;

/// <summary>
/// Tests for the 2026-04-27 <see cref="HeroRow.RespawnRemainingDisplay"/>
/// human-readable formatter.
/// </summary>
/// <remarks>
/// The DataGrid was previously showing raw int milliseconds, forcing the
/// operator to mentally divide by 1000 every glance. The display property
/// renders short timers as "X.X sec", long ones as "M min S sec", and
/// disabled-respawn rows as "—".
/// </remarks>
public sealed class HeroRowDisplayTests
{
    private static HeroRow Row(int ms, bool enabled = true) =>
        new(ObjAddr: 0x1000, TypeName: "REBEL_HERO", OwnerSlot: 0,
            Alive: true, RespawnRemainingMs: ms, RespawnEnabled: enabled);

    [Fact]
    public void Disabled_Respawn_RendersDash()
    {
        Row(5000, enabled: false).RespawnRemainingDisplay.Should().Be("—");
    }

    [Fact]
    public void Zero_Ms_Renders_AsZero()
    {
        Row(0).RespawnRemainingDisplay.Should().Be("0 ms");
    }

    [Theory]
    [InlineData(1, "1 ms")]
    [InlineData(500, "500 ms")]
    [InlineData(999, "999 ms")]
    public void Sub_Second_Renders_AsMs(int ms, string expected)
    {
        Row(ms).RespawnRemainingDisplay.Should().Be(expected);
    }

    [Theory]
    [InlineData(1000, "1.0 sec")]
    [InlineData(5000, "5.0 sec")]
    [InlineData(5500, "5.5 sec")]
    [InlineData(59_999, "60.0 sec")]
    public void Seconds_Range_RendersWithOneDecimal(int ms, string expected)
    {
        Row(ms).RespawnRemainingDisplay.Should().Be(expected);
    }

    [Theory]
    [InlineData(60_000, "1 min")]
    [InlineData(90_000, "1 min 30 sec")]
    [InlineData(125_000, "2 min 5 sec")]
    [InlineData(600_000, "10 min")]
    public void Minutes_Range_RendersWithMinSec(int ms, string expected)
    {
        Row(ms).RespawnRemainingDisplay.Should().Be(expected);
    }
}
