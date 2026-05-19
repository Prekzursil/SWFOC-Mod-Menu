using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// Guards <see cref="UnitControlTabViewModel"/>'s inline Lua command builders
/// against a revert to hex-literal formatting.
/// </summary>
/// <remarks>
/// <para>
/// The V2 UnitControl tab used to emit commands like
/// <c>return SWFOC_SetUnitHull(0x0, 99999)</c> via a <c>$"0x{addr:X}"</c>
/// format string. The game's embedded Lua is 5.0.2, which does NOT accept
/// <c>0x</c>-prefixed hex literals (hex literal syntax was added in Lua 5.1).
/// As a result, every button on the Unit Control tab reported
/// <c>pipe:1: ')' expected near 'x0'</c> when the Obj addr textbox was left
/// at its default <c>0x0</c>, and the feature was effectively unusable.
/// </para>
/// <para>
/// Fix (2026-04-11): the inline builders were split out into
/// <c>internal static</c> helpers and rewritten to emit decimal integers via
/// <c>CultureInfo.InvariantCulture</c>, matching the existing decimal
/// formatting already used by <c>HardpointService</c> and
/// <c>UnitInspectorService</c>. This file pins that fix: every assertion
/// here fails on the old broken form AND passes on the new form, so a
/// future "simplification" that reintroduces hex literals will fire the
/// guard instead of silently reverting to a broken tab.
/// </para>
/// </remarks>
public sealed class UnitControlLuaFormatRegressionTests
{
    // ----- SetUnitHull -----

    [Fact]
    public void Regression_SetUnitHull_EmitsDecimalAddress()
    {
        var lua = V2UnitMutationDispatcher.BuildSetUnitHullLuaCommand(
            addr: 0x1234ABCDUL,
            hp: 99999);

        // Decimal literal, not hex. 0x1234ABCD = 305441741 in decimal.
        lua.Should().Contain("305441741");
        lua.Should().NotContain("0x1234ABCD");
        lua.Should().NotContain("0x");
    }

    [Fact]
    public void Regression_SetUnitHull_ZeroAddressDoesNotProduceInvalidLua()
    {
        // The original bug: $"0x{0:X}" -> "0x0", which Lua 5.0 parses as the
        // number 0 followed by identifier 'x0' (parse error).
        var lua = V2UnitMutationDispatcher.BuildSetUnitHullLuaCommand(
            addr: 0UL,
            hp: 1234);

        // The new shape uses a bare decimal 0.
        lua.Should().Be("return SWFOC_SetUnitHull(0, 1234)");
        lua.Should().NotContain("0x0");
    }

    [Fact]
    public void Regression_SetUnitHull_PreservesHpArgument()
    {
        var lua = V2UnitMutationDispatcher.BuildSetUnitHullLuaCommand(
            addr: 42UL,
            hp: 500.5);

        lua.Should().Contain("return SWFOC_SetUnitHull(42,");
        lua.Should().Contain("500.5");
    }

    // ----- SetUnitInvuln -----

    [Fact]
    public void Regression_SetUnitInvuln_EnableEmitsDecimalAddrAnd1()
    {
        var lua = V2UnitMutationDispatcher.BuildSetUnitInvulnLuaCommand(
            addr: 0x7F1234ABUL,
            enable: true);

        // 0x7F1234AB = 2131899563 decimal.
        lua.Should().Be("return SWFOC_SetUnitInvuln(2131899563, 1)");
        lua.Should().NotContain("0x");
    }

    [Fact]
    public void Regression_SetUnitInvuln_DisableEmitsDecimalAddrAnd0()
    {
        var lua = V2UnitMutationDispatcher.BuildSetUnitInvulnLuaCommand(
            addr: 100UL,
            enable: false);

        lua.Should().Be("return SWFOC_SetUnitInvuln(100, 0)");
    }

    [Fact]
    public void Regression_SetUnitInvuln_ZeroAddressDoesNotProduceInvalidLua()
    {
        var lua = V2UnitMutationDispatcher.BuildSetUnitInvulnLuaCommand(
            addr: 0UL,
            enable: true);

        // Old broken form: "return SWFOC_SetUnitInvuln(0x0, 1)".
        // New form: "return SWFOC_SetUnitInvuln(0, 1)".
        lua.Should().Be("return SWFOC_SetUnitInvuln(0, 1)");
        lua.Should().NotContain("0x0");
    }

    // ----- PreventUnitDeath -----

    [Fact]
    public void Regression_PreventUnitDeath_EnableEmitsDecimalAddrAnd1()
    {
        var lua = V2UnitMutationDispatcher.BuildPreventUnitDeathLuaCommand(
            addr: 0xDEADBEEFUL,
            enable: true);

        // 0xDEADBEEF = 3735928559 decimal.
        lua.Should().Be("return SWFOC_PreventUnitDeath(3735928559, 1)");
        lua.Should().NotContain("0x");
        lua.Should().NotContain("DEADBEEF");
    }

    [Fact]
    public void Regression_PreventUnitDeath_ZeroAddressDoesNotProduceInvalidLua()
    {
        var lua = V2UnitMutationDispatcher.BuildPreventUnitDeathLuaCommand(
            addr: 0UL,
            enable: false);

        lua.Should().Be("return SWFOC_PreventUnitDeath(0, 0)");
        lua.Should().NotContain("0x0");
    }

    // ----- Cross-cutting -----
    //
    // Enumerates every inline builder to prove none of them emit the old
    // "0x" prefix for the address. If a new inline builder is added later
    // without being covered by this test, the developer is expected to add
    // an entry here.

    [Theory]
    [InlineData(0UL)]
    [InlineData(1UL)]
    [InlineData(0x1000UL)]
    [InlineData(0xFFFFFFFFUL)]
    [InlineData(0x7FFFFFFFFFFFFFFFUL)]
    public void AllInlineBuilders_NeverEmitZeroXLiteral(ulong addr)
    {
        var setHull = V2UnitMutationDispatcher.BuildSetUnitHullLuaCommand(addr, 1.0);
        var setInvuln = V2UnitMutationDispatcher.BuildSetUnitInvulnLuaCommand(addr, true);
        var prevent = V2UnitMutationDispatcher.BuildPreventUnitDeathLuaCommand(addr, true);

        // No command should contain a "0x" literal -- Lua 5.0 parse error.
        setHull.Should().NotContain("0x");
        setInvuln.Should().NotContain("0x");
        prevent.Should().NotContain("0x");
    }

    // ----- AI brain builders (NullAiBrain / AttachAiBrain) -------------
    // 2026-04-27: extracted from PlayerStateTabViewModel inline Lua into
    // the shared V2UnitMutationDispatcher. Slot is an int, not a ulong,
    // so the 0x-literal hazard isn't there — but a future "switch the
    // bridge to take a ulong slot id" change would silently break, so
    // we pin the wire format anyway.

    [Theory]
    [InlineData(0, "return SWFOC_NullAiBrain(0)")]
    [InlineData(1, "return SWFOC_NullAiBrain(1)")]
    [InlineData(7, "return SWFOC_NullAiBrain(7)")]
    public void BuildNullAiBrain_EmitsExpectedShape(int slot, string expected)
    {
        V2UnitMutationDispatcher.BuildNullAiBrainLuaCommand(slot)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(0, "return SWFOC_AttachAiBrain(0)")]
    [InlineData(1, "return SWFOC_AttachAiBrain(1)")]
    [InlineData(7, "return SWFOC_AttachAiBrain(7)")]
    public void BuildAttachAiBrain_EmitsExpectedShape(int slot, string expected)
    {
        V2UnitMutationDispatcher.BuildAttachAiBrainLuaCommand(slot)
            .Should().Be(expected);
    }

    // ----- ParseSelectedUnitResponse -----
    //
    // SWFOC_GetSelectedUnit returns a Lua number which the bridge stringifies
    // to decimal (possibly with fractional zero due to Lua 5.0 tostring).
    // The parser must tolerate both integer and scientific formats and
    // reject garbage without throwing.

    [Fact]
    public void ParseSelectedUnitResponse_IntegerAddressRoundTrips()
    {
        UnitControlTabViewModel
            .ParseSelectedUnitResponse("140283945472")
            .Should().Be(140283945472UL);
    }

    [Fact]
    public void ParseSelectedUnitResponse_ScientificNotationRoundTrips()
    {
        // Lua 5.0's default tostring emits scientific for large ints.
        UnitControlTabViewModel
            .ParseSelectedUnitResponse("1.4028e+11")
            .Should().BeInRange(140279000000UL, 140281000000UL);
    }

    [Fact]
    public void ParseSelectedUnitResponse_ZeroMeansNoSelection()
    {
        UnitControlTabViewModel.ParseSelectedUnitResponse("0").Should().Be(0UL);
    }

    [Fact]
    public void ParseSelectedUnitResponse_EmptyStringReturnsZero()
    {
        UnitControlTabViewModel.ParseSelectedUnitResponse(string.Empty).Should().Be(0UL);
    }

    [Fact]
    public void ParseSelectedUnitResponse_NullReturnsZero()
    {
        UnitControlTabViewModel.ParseSelectedUnitResponse(null).Should().Be(0UL);
    }

    [Fact]
    public void ParseSelectedUnitResponse_GarbageReturnsZeroDoesNotThrow()
    {
        UnitControlTabViewModel.ParseSelectedUnitResponse("not a number").Should().Be(0UL);
        UnitControlTabViewModel.ParseSelectedUnitResponse("ERR: ...").Should().Be(0UL);
    }
}
