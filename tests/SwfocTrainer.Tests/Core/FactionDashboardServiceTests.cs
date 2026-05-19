using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class FactionDashboardServiceTests
{
    private static readonly ILogger<FactionDashboardService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<FactionDashboardService>();

    [Fact]
    public async Task CaptureSnapshotsAsync_WithFactions_ReturnsSnapshotPerFaction()
    {
        var catalog = new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>
        {
            ["faction_catalog"] = new[] { "EMPIRE", "REBEL", "UNDERWORLD" }
        });
        var service = new FactionDashboardService(catalog, NullLogger);

        var snapshots = await service.CaptureSnapshotsAsync("test_profile", CancellationToken.None);

        snapshots.Should().HaveCount(3);
        snapshots[0].FactionName.Should().Be("EMPIRE");
        snapshots[1].FactionName.Should().Be("REBEL");
        snapshots[2].FactionName.Should().Be("UNDERWORLD");
        snapshots.Should().AllSatisfy(s =>
        {
            s.Credits.Should().Be(0);
            s.UnitCount.Should().Be(0);
            s.PlanetCount.Should().Be(0);
            s.TechLevel.Should().Be(0);
            s.CapturedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        });
    }

    [Fact]
    public async Task CaptureSnapshotsAsync_WithNoFactions_ReturnsEmptyList()
    {
        var catalog = new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>());
        var service = new FactionDashboardService(catalog, NullLogger);

        var snapshots = await service.CaptureSnapshotsAsync("test_profile", CancellationToken.None);

        snapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureSnapshotsAsync_WithEmptyFactionList_ReturnsEmptyList()
    {
        var catalog = new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>
        {
            ["faction_catalog"] = Array.Empty<string>()
        });
        var service = new FactionDashboardService(catalog, NullLogger);

        var snapshots = await service.CaptureSnapshotsAsync("test_profile", CancellationToken.None);

        snapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureSnapshotsAsync_NullProfileId_ThrowsArgumentNullException()
    {
        var catalog = new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>());
        var service = new FactionDashboardService(catalog, NullLogger);

        var act = () => service.CaptureSnapshotsAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("profileId");
    }

    [Fact]
    public void Constructor_NullCatalog_ThrowsArgumentNullException()
    {
        var act = () => new FactionDashboardService(null!, NullLogger);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("catalog");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var catalog = new StubCatalogService(new Dictionary<string, IReadOnlyList<string>>());

        var act = () => new FactionDashboardService(catalog, null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    private sealed class StubCatalogService : ICatalogService
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _catalog;

        public StubCatalogService(IReadOnlyDictionary<string, IReadOnlyList<string>> catalog)
        {
            _catalog = catalog;
        }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> LoadCatalogAsync(
            string profileId, CancellationToken cancellationToken)
        {
            _ = profileId;
            _ = cancellationToken;
            return Task.FromResult(_catalog);
        }
    }
}
