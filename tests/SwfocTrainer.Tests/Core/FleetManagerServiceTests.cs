using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class FleetManagerServiceTests
{
    private static readonly ILogger<FleetManagerService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<FleetManagerService>();

    [Fact]
    public async Task LoadFleetsAsync_ReturnsEmptyList()
    {
        var service = new FleetManagerService(NullLogger);

        var result = await service.LoadFleetsAsync("test_profile", CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadFleetsAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var service = new FleetManagerService(NullLogger);

        var act = () => service.LoadFleetsAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new FleetManagerService(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    [Fact]
    public async Task LoadFleetsAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        IFleetManagerService service = new FleetManagerService(NullLogger);

        var result = await service.LoadFleetsAsync("test_profile");

        result.Should().BeEmpty();
    }
}
