using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

/// <summary>
/// Tests for the internal static Build* methods added in Group B:
/// RosterBrowserService, FactionDashboardService, and ModConflictDetectorService.
/// </summary>
public sealed class V5ServiceLuaCommandBuilderGroupBTests
{
    // ========== RosterBrowserService.BuildDiscoverTypesLuaCommand ==========

    [Theory]
    [InlineData("UNITS")]
    [InlineData("HEROES")]
    [InlineData("BUILDINGS")]
    public void BuildDiscoverTypesLuaCommand_HappyPath_ContainsCategoryAndFindObjectType(
        string category)
    {
        var lua = RosterBrowserService.BuildDiscoverTypesLuaCommand(category);

        lua.Should().Contain("Find_Object_Type(");
        lua.Should().Contain($"\"{category}\"");
    }

    [Fact]
    public void BuildDiscoverTypesLuaCommand_ContainsFindAllObjectsOfType()
    {
        var lua = RosterBrowserService.BuildDiscoverTypesLuaCommand("UNITS");

        lua.Should().Contain("Find_All_Objects_Of_Type(");
    }

    [Fact]
    public void BuildDiscoverTypesLuaCommand_ContainsTestValidAndGetName()
    {
        var lua = RosterBrowserService.BuildDiscoverTypesLuaCommand("HEROES");

        lua.Should().Contain("TestValid(u)");
        lua.Should().Contain("Get_Name()");
    }

    [Fact]
    public void BuildDiscoverTypesLuaCommand_NullCategory_ThrowsArgumentNull()
    {
        var act = () => RosterBrowserService.BuildDiscoverTypesLuaCommand(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ========== FactionDashboardService.BuildFactionQueryLuaCommand ==========

    [Theory]
    [InlineData("EMPIRE")]
    [InlineData("REBEL")]
    [InlineData("UNDERWORLD")]
    public void BuildFactionQueryLuaCommand_HappyPath_ContainsFactionAndFindPlayer(
        string factionName)
    {
        var lua = FactionDashboardService.BuildFactionQueryLuaCommand(factionName);

        lua.Should().Contain("Find_Player(");
        lua.Should().Contain($"\"{factionName}\"");
    }

    [Fact]
    public void BuildFactionQueryLuaCommand_ContainsGetCredits()
    {
        var lua = FactionDashboardService.BuildFactionQueryLuaCommand("EMPIRE");

        lua.Should().Contain("Get_Credits()");
    }

    [Fact]
    public void BuildFactionQueryLuaCommand_ContainsFallbackZero()
    {
        var lua = FactionDashboardService.BuildFactionQueryLuaCommand("REBEL");

        lua.Should().Contain("return \"0\"");
    }

    [Fact]
    public void BuildFactionQueryLuaCommand_NullFaction_ThrowsArgumentNull()
    {
        var act = () => FactionDashboardService.BuildFactionQueryLuaCommand(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // ========== ModConflictDetectorService.BuildConflictReportSummary ==========

    [Fact]
    public void BuildConflictReportSummary_EmptyList_ReturnsNoConflicts()
    {
        var conflicts = Array.Empty<ModConflictEntry>();

        var summary = ModConflictDetectorService.BuildConflictReportSummary(conflicts);

        summary.Should().Be("No conflicts detected.");
    }

    [Fact]
    public void BuildConflictReportSummary_SingleConflict_ReturnsCountAndEntityId()
    {
        var conflicts = new[]
        {
            new ModConflictEntry("AT_AT", "modA", "modB", "duplicate_entity", "details")
        };

        var summary = ModConflictDetectorService.BuildConflictReportSummary(conflicts);

        summary.Should().StartWith("1 conflict(s):");
        summary.Should().Contain("AT_AT");
    }

    [Fact]
    public void BuildConflictReportSummary_MultipleConflicts_ReturnsCorrectCount()
    {
        var conflicts = new[]
        {
            new ModConflictEntry("AT_AT", "modA", "modB", "duplicate_entity", "d1"),
            new ModConflictEntry("X_WING", "modA", "modC", "duplicate_entity", "d2"),
            new ModConflictEntry("RANCOR", "modB", "modC", "duplicate_entity", "d3")
        };

        var summary = ModConflictDetectorService.BuildConflictReportSummary(conflicts);

        summary.Should().StartWith("3 conflict(s):");
        summary.Should().Contain("AT_AT");
        summary.Should().Contain("X_WING");
        summary.Should().Contain("RANCOR");
    }

    [Fact]
    public void BuildConflictReportSummary_DuplicateEntityIds_DeduplicatesInSummary()
    {
        var conflicts = new[]
        {
            new ModConflictEntry("AT_AT", "modA", "modB", "duplicate_entity", "d1"),
            new ModConflictEntry("AT_AT", "modA", "modC", "duplicate_entity", "d2")
        };

        var summary = ModConflictDetectorService.BuildConflictReportSummary(conflicts);

        summary.Should().StartWith("2 conflict(s):");
        // Distinct() deduplicates, so AT_AT should appear only once in the entity list
        var entityPart = summary.Substring(summary.IndexOf(':') + 2);
        entityPart.Split(", ").Should().HaveCount(1);
    }

    [Fact]
    public void BuildConflictReportSummary_NullList_ThrowsArgumentNull()
    {
        var act = () => ModConflictDetectorService.BuildConflictReportSummary(null!);

        act.Should().Throw<ArgumentNullException>();
    }
}
