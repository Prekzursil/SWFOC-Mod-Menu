using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 118) — pins the wire format of the iter 108
/// SWFOC_ChangeUnitOwner LIVE wire as exposed via the UnitControl tab.
/// Two-Lua-expr signature (unit, player) is the differentiating factor
/// from the iter 117 single-expr helpers.
/// </summary>
public sealed class Iter118ChangeUnitOwnerShapeTests
{
    [Theory]
    [InlineData("Find_First_Object(\"Empire_AT_AT\")", "Find_Player(\"REBEL\")",
        "return SWFOC_ChangeUnitOwner('Find_First_Object(\"Empire_AT_AT\")', 'Find_Player(\"REBEL\")')")]
    [InlineData("Find_First_Object(\"Rebel_Trooper_Squad\")", "Find_Player(\"EMPIRE\")",
        "return SWFOC_ChangeUnitOwner('Find_First_Object(\"Rebel_Trooper_Squad\")', 'Find_Player(\"EMPIRE\")')")]
    [InlineData("Find_Hint(\"hero\")", "Find_Player(\"Hostile_Garrison\")",
        "return SWFOC_ChangeUnitOwner('Find_Hint(\"hero\")', 'Find_Player(\"Hostile_Garrison\")')")]
    public void BuildChangeUnitOwner_PinsExpectedShape(
        string unitExpr, string playerExpr, string expected)
    {
        V2UnitMutationDispatcher.BuildChangeUnitOwnerLuaCommand(unitExpr, playerExpr)
            .Should().Be(expected);
    }

    [Fact]
    public void BuildChangeUnitOwner_EscapesSingleQuotesInBothArgs()
    {
        // Both args must escape independently — different from the iter 117
        // single-arg helpers which only have one expression to escape.
        var lua = V2UnitMutationDispatcher.BuildChangeUnitOwnerLuaCommand(
            "Find_First_Object('Empire_AT_AT')",
            "Find_Player('REBEL')");

        lua.Should().Be(
            @"return SWFOC_ChangeUnitOwner('Find_First_Object(\'Empire_AT_AT\')', 'Find_Player(\'REBEL\')')");
    }

    [Fact]
    public void BuildChangeUnitOwner_PreservesEmbeddedDoubleQuotes()
    {
        var lua = V2UnitMutationDispatcher.BuildChangeUnitOwnerLuaCommand(
            "Find_First_Object(\"Empire_AT_AT\")",
            "Find_Player(\"REBEL\")");

        lua.Should().Contain("\"Empire_AT_AT\"")
            .And.Contain("\"REBEL\"")
            .And.NotContain("\\\"")
            .And.NotContain(@"\\");
    }

    [Fact]
    public void BuildChangeUnitOwner_HandlesMixedQuoteEdgeCase()
    {
        // Operator pastes a unit expr with single quotes and a player expr
        // with double quotes — the dispatcher must escape independently.
        var lua = V2UnitMutationDispatcher.BuildChangeUnitOwnerLuaCommand(
            "Find_First_Object('Empire_AT_AT')",
            "Find_Player(\"REBEL\")");

        lua.Should().Contain(@"\'Empire_AT_AT\'")
            .And.Contain("\"REBEL\"")
            .And.NotContain(@"\\""");
    }
}
