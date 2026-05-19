using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 117) — pins the wire format of the iter 110-113
/// engine-Lua-API helpers exposed via the UnitControl-tab "Selected
/// Unit Lua Actions" GroupBox.
/// <para>
/// Two layers of expression-splicing matter here: the inner Lua (target
/// unit expression) and the outer SWFOC_* string-arg. The bridge then
/// composes the actual <c>(unit):method(...)</c> Lua source on the engine
/// side. Testing the OUTER layer locks the dispatcher's shape so a
/// single bridge-side rename or quote-style change doesn't silently
/// mismatch.
/// </para>
/// <para>
/// Why these specific assertions:
/// <list type="bullet">
///   <item>Outer single quotes wrap the inner Lua expression so embedded
///         double-quotes (typical: <c>Find_First_Object("Empire_AT_AT")</c>)
///         survive without escapes.</item>
///   <item>Embedded single quotes inside the inner expression must escape
///         to <c>\'</c> — the dispatcher does this; tests pin it.</item>
///   <item>Bool flags emit as Lua keywords <c>true</c>/<c>false</c>
///         (NOT <c>1</c>/<c>0</c> — different from the iter 78
///         SetUnitInvuln helpers which take <c>int</c>).</item>
/// </list>
/// </para>
/// </summary>
public sealed class Iter117UnitLuaCallShapeTests
{
    [Theory]
    [InlineData("SWFOC_MakeUnitInvulnLua", "Find_First_Object(\"Empire_AT_AT\")", "true",
        "return SWFOC_MakeUnitInvulnLua('Find_First_Object(\"Empire_AT_AT\")', 'true')")]
    [InlineData("SWFOC_HideUnitLua", "Find_First_Object(\"Empire_AT_AT\")", "false",
        "return SWFOC_HideUnitLua('Find_First_Object(\"Empire_AT_AT\")', 'false')")]
    [InlineData("SWFOC_PreventAiUsageLua", "Find_First_Object(\"Rebel_Trooper_Squad\")", "true",
        "return SWFOC_PreventAiUsageLua('Find_First_Object(\"Rebel_Trooper_Squad\")', 'true')")]
    [InlineData("SWFOC_SetUnitSelectableLua", "Find_First_Object(\"Empire_AT_AT\")", "false",
        "return SWFOC_SetUnitSelectableLua('Find_First_Object(\"Empire_AT_AT\")', 'false')")]
    public void BuildUnitLuaMethodCall_PinsExpectedShape(
        string swfocFn, string unitExpr, string boolArg, string expected)
    {
        V2UnitMutationDispatcher.BuildUnitLuaMethodCall(swfocFn, unitExpr, boolArg)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("SWFOC_DespawnUnitLua", "Find_First_Object(\"Empire_AT_AT\")",
        "return SWFOC_DespawnUnitLua('Find_First_Object(\"Empire_AT_AT\")')")]
    [InlineData("SWFOC_StopUnitLua", "Find_First_Object(\"Empire_AT_AT\")",
        "return SWFOC_StopUnitLua('Find_First_Object(\"Empire_AT_AT\")')")]
    [InlineData("SWFOC_RetreatUnitLua", "Find_First_Object(\"Rebel_Trooper_Squad\")",
        "return SWFOC_RetreatUnitLua('Find_First_Object(\"Rebel_Trooper_Squad\")')")]
    public void BuildUnitLuaNoArgCall_PinsExpectedShape(
        string swfocFn, string unitExpr, string expected)
    {
        V2UnitMutationDispatcher.BuildUnitLuaNoArgCall(swfocFn, unitExpr)
            .Should().Be(expected);
    }

    [Fact]
    public void BuildUnitLuaMethodCall_EscapesEmbeddedSingleQuotes()
    {
        // Edge case: operator pastes a Lua expression containing a single
        // quote (e.g. accidental copy-paste from a Lua source file).
        // Without escaping, the outer single-quote wrapping would break
        // the SWFOC_* call string. The dispatcher must escape the inner
        // single quote to \'.
        var lua = V2UnitMutationDispatcher.BuildUnitLuaMethodCall(
            "SWFOC_MakeUnitInvulnLua",
            "Find_First_Object('Empire_AT_AT')",
            "true");

        lua.Should().Be(
            @"return SWFOC_MakeUnitInvulnLua('Find_First_Object(\'Empire_AT_AT\')', 'true')");
    }

    [Fact]
    public void BuildUnitLuaNoArgCall_EscapesEmbeddedSingleQuotes()
    {
        var lua = V2UnitMutationDispatcher.BuildUnitLuaNoArgCall(
            "SWFOC_DespawnUnitLua",
            "Find_First_Object('Empire_AT_AT')");

        lua.Should().Be(
            @"return SWFOC_DespawnUnitLua('Find_First_Object(\'Empire_AT_AT\')')");
    }

    [Fact]
    public void BuildUnitLuaMethodCall_PreservesEmbeddedDoubleQuotes()
    {
        // Critical: the bridge needs to receive the inner double-quotes
        // intact so it can pass them through to the engine. The outer
        // single-quote wrapping is exactly what makes this work without
        // backslash-escaping every double-quote.
        var lua = V2UnitMutationDispatcher.BuildUnitLuaMethodCall(
            "SWFOC_HideUnitLua",
            "Find_First_Object(\"Empire_AT_AT\")",
            "true");

        lua.Should().Contain("\"Empire_AT_AT\"")
            .And.NotContain("\\\"")
            .And.NotContain(@"\\");
    }
}
