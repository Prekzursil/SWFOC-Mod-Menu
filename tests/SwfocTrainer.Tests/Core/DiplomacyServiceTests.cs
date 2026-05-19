using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class DiplomacyServiceTests
{
    private static readonly ILogger<DiplomacyService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<DiplomacyService>();

    // --- LoadDiplomacyAsync ---

    [Fact]
    public async Task LoadDiplomacyAsync_ReturnsDefaultHostileStates()
    {
        var service = new DiplomacyService(NullLogger);

        var result = await service.LoadDiplomacyAsync("p1", CancellationToken.None);

        result.Should().NotBeEmpty();
        result.Should().OnlyContain(s => s.Relation == DiplomacyRelation.Hostile);
    }

    [Fact]
    public async Task LoadDiplomacyAsync_ReturnsAllUniqueFactionPairs()
    {
        var service = new DiplomacyService(NullLogger);

        var result = await service.LoadDiplomacyAsync("p1", CancellationToken.None);

        // 3 factions = 3 pairs: (E,R), (E,U), (R,U)
        result.Should().HaveCount(3);
        result.Select(s => (s.Faction1, s.Faction2)).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task LoadDiplomacyAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        IDiplomacyService service = new DiplomacyService(NullLogger);

        var result = await service.LoadDiplomacyAsync("p1");

        result.Should().NotBeEmpty();
    }

    // --- SetRelationAsync: Allied ---

    [Fact]
    public async Task SetRelationAsync_Allied_ReturnsSuccessWithMakeAlly()
    {
        var service = new DiplomacyService(NullLogger);
        var state = new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Allied);

        var result = await service.SetRelationAsync("p1", state, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be(
                "local p1 = Find_Player(\"EMPIRE\"); local p2 = Find_Player(\"REBEL\"); " +
                "if p1 and p2 then p1:Make_Ally(p2) end");
    }

    // --- SetRelationAsync: Hostile ---

    [Fact]
    public async Task SetRelationAsync_Hostile_ReturnsSuccessWithMakeEnemy()
    {
        var service = new DiplomacyService(NullLogger);
        var state = new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Hostile);

        var result = await service.SetRelationAsync("p1", state, CancellationToken.None);

        result.Succeeded.Should().BeTrue();
        result.Diagnostics.Should().ContainKey("lua_call")
            .WhoseValue.Should().Be(
                "local p1 = Find_Player(\"EMPIRE\"); local p2 = Find_Player(\"REBEL\"); " +
                "if p1 and p2 then p1:Make_Enemy(p2) end");
    }

    // --- SetRelationAsync: Neutral ---

    [Fact]
    public async Task SetRelationAsync_Neutral_ReturnsWarning()
    {
        var service = new DiplomacyService(NullLogger);
        var state = new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Neutral);

        var result = await service.SetRelationAsync("p1", state, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("Neutral");
        result.Diagnostics.Should().ContainKey("warning");
    }

    // --- Diagnostics: alliance reset warning ---

    [Fact]
    public async Task SetRelationAsync_Allied_ContainsAllianceWarning()
    {
        var service = new DiplomacyService(NullLogger);
        var state = new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Allied);

        var result = await service.SetRelationAsync("p1", state, CancellationToken.None);

        result.Diagnostics.Should().ContainKey("alliance_warning");
        result.Diagnostics!["alliance_warning"]!.ToString()
            .Should().Contain("auto-reapply");
    }

    // --- ResolveDiplomacyAction theory ---

    [Theory]
    [InlineData(DiplomacyRelation.Allied, "p1:Make_Ally(p2) [PlayerWrapper instance method]")]
    [InlineData(DiplomacyRelation.Hostile, "p1:Make_Enemy(p2) [PlayerWrapper instance method]")]
    public void ResolveDiplomacyAction_SupportedRelation_ReturnsExpectedAction(
        DiplomacyRelation relation, string expected)
    {
        DiplomacyService.ResolveDiplomacyAction(relation).Should().Be(expected);
    }

    [Theory]
    [InlineData(DiplomacyRelation.Neutral)]
    [InlineData((DiplomacyRelation)99)]
    public void ResolveDiplomacyAction_UnsupportedRelation_ReturnsNull(
        DiplomacyRelation relation)
    {
        DiplomacyService.ResolveDiplomacyAction(relation).Should().BeNull();
    }

    // --- Validation: empty factions ---

    [Fact]
    public async Task SetRelationAsync_EmptyFaction1_ReturnsFailure()
    {
        var service = new DiplomacyService(NullLogger);
        var state = new DiplomacyState("", "REBEL", DiplomacyRelation.Allied);

        var result = await service.SetRelationAsync("p1", state, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("Faction1");
    }

    [Fact]
    public async Task SetRelationAsync_WhitespaceFaction1_ReturnsFailure()
    {
        var service = new DiplomacyService(NullLogger);
        var state = new DiplomacyState("   ", "REBEL", DiplomacyRelation.Allied);

        var result = await service.SetRelationAsync("p1", state, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    [Fact]
    public async Task SetRelationAsync_EmptyFaction2_ReturnsFailure()
    {
        var service = new DiplomacyService(NullLogger);
        var state = new DiplomacyState("EMPIRE", "", DiplomacyRelation.Allied);

        var result = await service.SetRelationAsync("p1", state, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
        result.Message.Should().Contain("Faction2");
    }

    [Fact]
    public async Task SetRelationAsync_WhitespaceFaction2_ReturnsFailure()
    {
        var service = new DiplomacyService(NullLogger);
        var state = new DiplomacyState("EMPIRE", "   ", DiplomacyRelation.Allied);

        var result = await service.SetRelationAsync("p1", state, CancellationToken.None);

        result.Succeeded.Should().BeFalse();
    }

    // --- AddressSource ---

    [Fact]
    public async Task SetRelationAsync_SuccessResult_HasNoneAddressSource()
    {
        var service = new DiplomacyService(NullLogger);
        var state = new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Allied);

        var result = await service.SetRelationAsync("p1", state, CancellationToken.None);

        result.AddressSource.Should().Be(AddressSource.None);
    }

    // --- Default overload ---

    [Fact]
    public async Task SetRelationAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        IDiplomacyService service = new DiplomacyService(NullLogger);
        var state = new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Hostile);

        var result = await service.SetRelationAsync("p1", state);

        result.Succeeded.Should().BeTrue();
    }

    // --- Null guards ---

    [Fact]
    public async Task LoadDiplomacyAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = new DiplomacyService(NullLogger);

        var act = () => service.LoadDiplomacyAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public async Task SetRelationAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = new DiplomacyService(NullLogger);
        var state = new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Allied);

        var act = () => service.SetRelationAsync(null!, state, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public async Task SetRelationAsync_NullState_ThrowsArgumentNullException()
    {
        var service = new DiplomacyService(NullLogger);

        var act = () => service.SetRelationAsync("p1", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("state");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new DiplomacyService(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }
}
