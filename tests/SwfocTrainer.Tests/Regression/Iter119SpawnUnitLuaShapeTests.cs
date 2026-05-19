using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-04-29 (iter 119) — pins the wire format of the iter 109
/// SWFOC_SpawnUnitLua LIVE wire as exposed via the Spawning tab's
/// "Spawn unit via Lua" GroupBox. Three-Lua-expr signature
/// (player, type, position) — the most expressive of the iter 100-113
/// wires.
/// </summary>
public sealed class Iter119SpawnUnitLuaShapeTests
{
    [Theory]
    [InlineData(
        "Find_Player(\"REBEL\")",
        "Find_Object_Type(\"Rebel_Trooper_Squad\")",
        "Create_Position(0, 0, 0)",
        "return SWFOC_SpawnUnitLua('Find_Player(\"REBEL\")', 'Find_Object_Type(\"Rebel_Trooper_Squad\")', 'Create_Position(0, 0, 0)')")]
    [InlineData(
        "Find_Player(\"EMPIRE\")",
        "Find_Object_Type(\"Empire_AT_AT\")",
        "Create_Position(1500, -2200, 0)",
        "return SWFOC_SpawnUnitLua('Find_Player(\"EMPIRE\")', 'Find_Object_Type(\"Empire_AT_AT\")', 'Create_Position(1500, -2200, 0)')")]
    [InlineData(
        "Find_Player(\"Hostile_Garrison\")",
        "Find_Object_Type(\"Pirate_Frigate\")",
        "Create_Position(-3000, 4500, 1000)",
        "return SWFOC_SpawnUnitLua('Find_Player(\"Hostile_Garrison\")', 'Find_Object_Type(\"Pirate_Frigate\")', 'Create_Position(-3000, 4500, 1000)')")]
    public void BuildSpawnUnitLua_PinsExpectedShape(
        string playerExpr, string typeExpr, string posExpr, string expected)
    {
        V2UnitMutationDispatcher.BuildSpawnUnitLuaCommand(playerExpr, typeExpr, posExpr)
            .Should().Be(expected);
    }

    [Fact]
    public void BuildSpawnUnitLua_EscapesSingleQuotesInAllThreeArgs()
    {
        // All three args independently escape — no shared escape state, so
        // a single quote in one arg doesn't bleed into another.
        var lua = V2UnitMutationDispatcher.BuildSpawnUnitLuaCommand(
            "Find_Player('REBEL')",
            "Find_Object_Type('Rebel_Trooper_Squad')",
            "Create_Position('0', '0', '0')");

        lua.Should().Be(
            @"return SWFOC_SpawnUnitLua('Find_Player(\'REBEL\')', 'Find_Object_Type(\'Rebel_Trooper_Squad\')', 'Create_Position(\'0\', \'0\', \'0\')')");
    }

    [Fact]
    public void BuildSpawnUnitLua_PreservesEmbeddedDoubleQuotes()
    {
        var lua = V2UnitMutationDispatcher.BuildSpawnUnitLuaCommand(
            "Find_Player(\"REBEL\")",
            "Find_Object_Type(\"Rebel_Trooper_Squad\")",
            "Create_Position(0, 0, 0)");

        lua.Should().Contain("\"REBEL\"")
            .And.Contain("\"Rebel_Trooper_Squad\"")
            .And.NotContain("\\\"")
            .And.NotContain(@"\\");
    }

    [Fact]
    public void BuildSpawnUnitLua_PositionAcceptsArbitraryLuaExpression()
    {
        // The position arg isn't restricted to literal Create_Position calls —
        // the operator might pass a planet's spawn point, or a hint. Verify
        // the dispatcher doesn't re-parse the expression.
        var lua = V2UnitMutationDispatcher.BuildSpawnUnitLuaCommand(
            "Find_Player(\"REBEL\")",
            "Find_Object_Type(\"Rebel_Trooper_Squad\")",
            "Find_Hint(\"hero\")");

        lua.Should().Contain("'Find_Hint(\"hero\")'");
    }
}
