using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class EnhancedSpawnServiceTests
{
    private static readonly ILogger<EnhancedSpawnService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<EnhancedSpawnService>();

    [Fact]
    public async Task ExecuteSpawnAsync_ReturnsResultWithCorrectAttemptedCount()
    {
        var service = new EnhancedSpawnService(NullLogger);
        var request = CreateRequest(quantity: 5);

        var result = await service.ExecuteSpawnAsync("test_profile", request, CancellationToken.None);

        result.Attempted.Should().Be(5);
        result.Succeeded.Should().Be(5);
        result.Failed.Should().Be(0);
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ResolveActionId_Tactical_ReturnsTacticalEntityId()
    {
        EnhancedSpawnService.ResolveActionId(SpawnMode.Tactical)
            .Should().Be("spawn_tactical_entity");
    }

    [Fact]
    public void ResolveActionId_Reinforcement_ReturnsContextEntityId()
    {
        EnhancedSpawnService.ResolveActionId(SpawnMode.Reinforcement)
            .Should().Be("spawn_context_entity");
    }

    [Fact]
    public void ResolveActionId_GalacticPersistent_ReturnsGalacticEntityId()
    {
        EnhancedSpawnService.ResolveActionId(SpawnMode.GalacticPersistent)
            .Should().Be("spawn_galactic_entity");
    }

    [Fact]
    public void ResolveActionId_UnknownMode_ThrowsArgumentOutOfRangeException()
    {
        var act = () => EnhancedSpawnService.ResolveActionId((SpawnMode)999);

        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("mode");
    }

    [Fact]
    public async Task ExecuteSpawnAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = new EnhancedSpawnService(NullLogger);
        var request = CreateRequest();

        var act = () => service.ExecuteSpawnAsync(null!, request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public async Task ExecuteSpawnAsync_NullRequest_ThrowsArgumentNullException()
    {
        var service = new EnhancedSpawnService(NullLogger);

        var act = () => service.ExecuteSpawnAsync("test_profile", null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("request");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new EnhancedSpawnService(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    private static EnhancedSpawnRequest CreateRequest(int quantity = 3)
    {
        return new EnhancedSpawnRequest(
            UnitId: "AT_AT",
            TargetFaction: "EMPIRE",
            Mode: SpawnMode.Tactical,
            Quantity: quantity,
            PositionKind: SpawnPositionKind.AtCamera,
            TargetPlanet: null,
            AllowCrossFaction: false,
            StopOnFailure: true);
    }
}
