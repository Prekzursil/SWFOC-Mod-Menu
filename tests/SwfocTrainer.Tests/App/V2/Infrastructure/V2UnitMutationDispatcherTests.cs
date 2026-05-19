using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.Infrastructure;

/// <summary>
/// Pins the wire format of the 2026-04-27 <see cref="V2UnitMutationDispatcher"/>
/// internal Build*LuaCommand helpers. The 3 SetUnit* helpers already have
/// hex-literal regression coverage in
/// <see cref="Regression.UnitControlLuaFormatRegressionTests"/>; these
/// tests cover the AI brain helpers + provide CLS-compliant boundary
/// coverage for slot indices.
/// </summary>
public sealed class V2UnitMutationDispatcherTests
{
    [Theory]
    [InlineData(0, "return SWFOC_NullAiBrain(0)")]
    [InlineData(1, "return SWFOC_NullAiBrain(1)")]
    [InlineData(7, "return SWFOC_NullAiBrain(7)")]
    [InlineData(15, "return SWFOC_NullAiBrain(15)")]
    public void BuildNullAiBrain_PinsExpectedShape(int slot, string expected)
    {
        V2UnitMutationDispatcher.BuildNullAiBrainLuaCommand(slot)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "return SWFOC_AttachAiBrain(0)")]
    [InlineData(1, "return SWFOC_AttachAiBrain(1)")]
    [InlineData(7, "return SWFOC_AttachAiBrain(7)")]
    [InlineData(15, "return SWFOC_AttachAiBrain(15)")]
    public void BuildAttachAiBrain_PinsExpectedShape(int slot, string expected)
    {
        V2UnitMutationDispatcher.BuildAttachAiBrainLuaCommand(slot)
            .Should().Be(expected);
    }

    [Fact]
    public void BuildNullAiBrain_DoesNotEmitHexLiteral()
    {
        // Lua 5.0 doesn't accept 0x prefix; the helpers must always go
        // decimal regardless of the value the operator picks.
        V2UnitMutationDispatcher.BuildNullAiBrainLuaCommand(0xFF)
            .Should().NotContain("0x")
            .And.Contain("255");
    }

    [Fact]
    public void BuildAttachAiBrain_DoesNotEmitHexLiteral()
    {
        V2UnitMutationDispatcher.BuildAttachAiBrainLuaCommand(0xFF)
            .Should().NotContain("0x")
            .And.Contain("255");
    }
}
