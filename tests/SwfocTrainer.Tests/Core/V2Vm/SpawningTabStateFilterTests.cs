using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.Core.Ux;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.Core.V2Vm;

/// <summary>
/// Tests for the 2026-04-27 faceted-filter additions to
/// <see cref="SpawningTabState"/>: FactionFilter + DomainFilter
/// composition, and the <see cref="SpawningTabState.ClassifyDomain"/>
/// heuristic.
/// </summary>
/// <remarks>
/// Background: the operator wants to narrow a 500+ unit catalogue to
/// "Empire ground units" without scrolling. We added two filters that
/// compose with the existing SearchQuery, and a static heuristic that
/// classifies a type id as Space / Ground / Unknown by substring
/// matching against universal English unit-type words (FRIGATE,
/// INFANTRY, WALKER, …).
/// </remarks>
public sealed class SpawningTabStateFilterTests
{
    private static SpawningTabState NewState()
    {
        return new SpawningTabState(new NoopDispatcher(), new NoopFeedbackSink());
    }

    [Fact]
    public void FilteredTypes_NoFilters_ReturnsAllTypes()
    {
        var state = NewState();
        state.SetAvailableTypes(new[] { "REBEL_INFANTRY", "EMPIRE_AT_AT", "UNDERWORLD_FRIGATE" });

        var result = state.FilteredTypes();

        result.Should().HaveCount(3);
    }

    [Fact]
    public void FilteredTypes_SearchQuery_NarrowsByCaseInsensitiveSubstring()
    {
        var state = NewState();
        state.SetAvailableTypes(new[] { "REBEL_INFANTRY", "EMPIRE_INFANTRY", "REBEL_TANK" });
        state.SearchQuery = "infantry";

        var result = state.FilteredTypes();

        result.Should().BeEquivalentTo("REBEL_INFANTRY", "EMPIRE_INFANTRY");
    }

    [Fact]
    public void FilteredTypes_FactionFilter_NarrowsByPrefixSubstring()
    {
        var state = NewState();
        state.SetAvailableTypes(new[]
        {
            "REBEL_INFANTRY", "EMPIRE_INFANTRY",
            "REBEL_FRIGATE", "EMPIRE_FRIGATE",
        });
        state.FactionFilter = "REBEL";

        var result = state.FilteredTypes();

        result.Should().BeEquivalentTo("REBEL_INFANTRY", "REBEL_FRIGATE");
    }

    [Fact]
    public void FilteredTypes_DomainFilter_NarrowsBy_ClassifyDomain()
    {
        var state = NewState();
        state.SetAvailableTypes(new[]
        {
            "REBEL_INFANTRY",       // Ground
            "REBEL_FRIGATE",        // Space
            "EMPIRE_AT_AT",         // Ground
            "EMPIRE_DESTROYER",     // Space
            "MOD_VONG_WARRIOR",     // Unknown
        });
        state.DomainFilter = "Space";

        var result = state.FilteredTypes();

        result.Should().BeEquivalentTo("REBEL_FRIGATE", "EMPIRE_DESTROYER");
    }

    [Fact]
    public void FilteredTypes_AllThreeFilters_Compose_AsAnd()
    {
        var state = NewState();
        state.SetAvailableTypes(new[]
        {
            "REBEL_INFANTRY", "REBEL_TANK", "REBEL_FRIGATE",
            "EMPIRE_INFANTRY", "EMPIRE_TANK",
        });
        state.SearchQuery = "TANK";
        state.FactionFilter = "REBEL";
        state.DomainFilter = "Ground";

        var result = state.FilteredTypes();

        result.Should().BeEquivalentTo("REBEL_TANK");
    }

    [Fact]
    public void FilteredTypes_EmptyFilterStrings_AreNoOps()
    {
        var state = NewState();
        state.SetAvailableTypes(new[] { "REBEL_INFANTRY", "EMPIRE_INFANTRY" });
        state.FactionFilter = "";
        state.DomainFilter = "   ";

        var result = state.FilteredTypes();

        result.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("REBEL_FRIGATE_NEBULON_B", "Space")]
    [InlineData("EMPIRE_DESTROYER_VICTORY_CLASS", "Space")]
    [InlineData("UNDERWORLD_CORVETTE", "Space")]
    [InlineData("REBEL_INFANTRY_SOLDIER", "Ground")]
    [InlineData("EMPIRE_AT_AT", "Ground")]
    [InlineData("EMPIRE_AT_ST", "Ground")]
    [InlineData("REBEL_TANK_T2B", "Ground")]
    [InlineData("REBEL_ARTILLERY_MPTL", "Ground")]
    [InlineData("EMPIRE_BARRACKS", "Ground")]
    [InlineData("EMPIRE_FACTORY", "Ground")]
    [InlineData("EMPIRE_TIE_FIGHTER", "Space")]
    [InlineData("REBEL_X_WING", "Space")]
    [InlineData("MOD_VONG_PYRAMID", "Unknown")]
    [InlineData("UNKNOWN_THING", "Unknown")]
    [InlineData("", "Unknown")]
    public void ClassifyDomain_KnownNames_ReturnsExpected(string typeId, string expected)
    {
        SpawningTabState.ClassifyDomain(typeId).Should().Be(expected);
    }

    private sealed class NoopDispatcher : ISpawningDispatcher
    {
        public Task<bool> SpawnUnitAsync(
            string typeId, int slot, float x, float y, float z, int count, CancellationToken ct)
            => Task.FromResult(true);
    }

    private sealed class NoopFeedbackSink : IUxFeedbackSink
    {
        public void Emit(UxFeedback feedback) { }
    }
}
