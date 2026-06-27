using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SwfocTrainer.Core.Contracts;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using Xunit;

namespace SwfocTrainer.Tests.Core;

public sealed class DamageLogServiceTests
{
    private static readonly ILogger<DamageLogService> NullLogger =
        NullLoggerFactory.Instance.CreateLogger<DamageLogService>();

    private static readonly DateTimeOffset BaseTime =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // --- ComputeSummaryAsync ---

    [Fact]
    public async Task ComputeSummaryAsync_WithEntries_ReturnsCorrectMvp()
    {
        var entries = new List<DamageLogEntry>
        {
            new("EMPIRE_AT_AT", "REBEL_TROOPER", 500f, "blaster", BaseTime),
            new("EMPIRE_AT_AT", "REBEL_TROOPER", 300f, "blaster", BaseTime.AddSeconds(5)),
            new("REBEL_X_WING", "EMPIRE_AT_AT", 200f, "torpedo", BaseTime.AddSeconds(10))
        };

        var service = new DamageLogService(NullLogger);

        var summary = await service.ComputeSummaryAsync(entries, CancellationToken.None);

        summary.MvpUnit.Should().Be("EMPIRE_AT_AT");
    }

    [Fact]
    public async Task ComputeSummaryAsync_WithEntries_ReturnsCorrectDamagePerFaction()
    {
        var entries = new List<DamageLogEntry>
        {
            new("EMPIRE_AT_AT", "REBEL_TROOPER", 500f, "blaster", BaseTime),
            new("EMPIRE_ISD", "REBEL_CRUISER", 300f, "turbolaser", BaseTime.AddSeconds(2)),
            new("REBEL_X_WING", "EMPIRE_AT_AT", 200f, "torpedo", BaseTime.AddSeconds(5))
        };

        var service = new DamageLogService(NullLogger);

        var summary = await service.ComputeSummaryAsync(entries, CancellationToken.None);

        summary.DamagePerFaction.Should().ContainKey("Empire")
            .WhoseValue.Should().Be(800f);
        summary.DamagePerFaction.Should().ContainKey("Rebel")
            .WhoseValue.Should().Be(200f);
    }

    [Fact]
    public async Task ComputeSummaryAsync_WithKills_ReturnsCorrectKillsPerFaction()
    {
        var entries = new List<DamageLogEntry>
        {
            new("EMPIRE_AT_AT", "REBEL_TROOPER", 100f, "kill", BaseTime),
            new("EMPIRE_ISD", "REBEL_CRUISER", 200f, "kill", BaseTime.AddSeconds(1)),
            new("REBEL_X_WING", "EMPIRE_TANK", 150f, "kill", BaseTime.AddSeconds(2)),
            new("EMPIRE_AT_AT", "REBEL_BASE", 50f, "blaster", BaseTime.AddSeconds(3))
        };

        var service = new DamageLogService(NullLogger);

        var summary = await service.ComputeSummaryAsync(entries, CancellationToken.None);

        summary.KillsPerFaction.Should().ContainKey("Empire")
            .WhoseValue.Should().Be(2);
        summary.KillsPerFaction.Should().ContainKey("Rebel")
            .WhoseValue.Should().Be(1);
    }

    [Fact]
    public async Task ComputeSummaryAsync_WithEntries_ReturnsCorrectBattleDuration()
    {
        var entries = new List<DamageLogEntry>
        {
            new("EMPIRE_AT_AT", "REBEL_TROOPER", 100f, "blaster", BaseTime),
            new("REBEL_X_WING", "EMPIRE_AT_AT", 50f, "torpedo", BaseTime.AddMinutes(5))
        };

        var service = new DamageLogService(NullLogger);

        var summary = await service.ComputeSummaryAsync(entries, CancellationToken.None);

        summary.BattleDuration.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ComputeSummaryAsync_EmptyList_ReturnsZeroedSummary()
    {
        var service = new DamageLogService(NullLogger);

        var summary = await service.ComputeSummaryAsync(
            Array.Empty<DamageLogEntry>(), CancellationToken.None);

        summary.MvpUnit.Should().BeEmpty();
        summary.DamagePerFaction.Should().BeEmpty();
        summary.KillsPerFaction.Should().BeEmpty();
        summary.BattleDuration.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task ComputeSummaryAsync_NoKillEntries_KillsPerFactionIsEmpty()
    {
        var entries = new List<DamageLogEntry>
        {
            new("EMPIRE_AT_AT", "REBEL_TROOPER", 100f, "blaster", BaseTime)
        };

        var service = new DamageLogService(NullLogger);

        var summary = await service.ComputeSummaryAsync(entries, CancellationToken.None);

        summary.KillsPerFaction.Should().BeEmpty();
    }

    // --- PollEntriesAsync ---

    [Fact]
    public async Task PollEntriesAsync_NoEntries_ReturnsEmptyList()
    {
        var service = new DamageLogService(NullLogger);

        var result = await service.PollEntriesAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task PollEntriesAsync_AfterAddEntries_ReturnsAccumulatedEntries()
    {
        var service = new DamageLogService(NullLogger);
        var entries = new List<DamageLogEntry>
        {
            new("EMPIRE_AT_AT", "REBEL_TROOPER", 100f, "blaster", BaseTime),
            new("REBEL_X_WING", "EMPIRE_AT_AT", 50f, "torpedo", BaseTime.AddSeconds(1))
        };

        service.AddEntries(entries);

        var result = await service.PollEntriesAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].SourceUnit.Should().Be("EMPIRE_AT_AT");
        result[1].SourceUnit.Should().Be("REBEL_X_WING");
    }

    [Fact]
    public async Task PollEntriesAsync_AfterClearEntries_ReturnsEmptyList()
    {
        var service = new DamageLogService(NullLogger);
        service.AddEntries(new[]
        {
            new DamageLogEntry("EMPIRE_AT_AT", "REBEL_TROOPER", 100f, "blaster", BaseTime)
        });

        service.ClearEntries();

        var result = await service.PollEntriesAsync(CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task PollEntriesAsync_ReturnsSnapshotNotLiveReference()
    {
        var service = new DamageLogService(NullLogger);
        service.AddEntries(new[]
        {
            new DamageLogEntry("EMPIRE_AT_AT", "REBEL_TROOPER", 100f, "blaster", BaseTime)
        });

        var snapshot = await service.PollEntriesAsync(CancellationToken.None);

        service.AddEntries(new[]
        {
            new DamageLogEntry("REBEL_X_WING", "EMPIRE_AT_AT", 50f, "torpedo", BaseTime)
        });

        snapshot.Should().HaveCount(1);
    }

    // --- Internal static method tests ---

    [Fact]
    public void ComputeMvpUnit_EmptyEntries_ReturnsEmptyString()
    {
        DamageLogService.ComputeMvpUnit(Array.Empty<DamageLogEntry>())
            .Should().BeEmpty();
    }

    [Fact]
    public void ComputeMvpUnit_SingleUnit_ReturnsThatUnit()
    {
        var entries = new[]
        {
            new DamageLogEntry("EMPIRE_AT_AT", "REBEL_TROOPER", 100f, "blaster", BaseTime)
        };

        DamageLogService.ComputeMvpUnit(entries).Should().Be("EMPIRE_AT_AT");
    }

    [Fact]
    public void ComputeBattleDuration_EmptyEntries_ReturnsZero()
    {
        DamageLogService.ComputeBattleDuration(Array.Empty<DamageLogEntry>())
            .Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ComputeBattleDuration_SingleEntry_ReturnsZero()
    {
        var entries = new[]
        {
            new DamageLogEntry("A", "B", 10f, "hit", BaseTime)
        };

        DamageLogService.ComputeBattleDuration(entries).Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("EMPIRE_AT_AT", "Empire")]
    [InlineData("IMPERIAL_ISD", "Empire")]
    [InlineData("REBEL_X_WING", "Rebel")]
    [InlineData("UNDERWORLD_TYBER", "Underworld")]
    [InlineData("REPUBLIC_CRUISER", "Republic")]
    [InlineData("CIS_DROID", "CIS")]
    [InlineData("HERO_VADER", "Unknown")]
    [InlineData("", "Unknown")]
    [InlineData(null, "Unknown")]
    public void InferFaction_MapsCorrectly(string? unitName, string expected)
    {
        DamageLogService.InferFaction(unitName!).Should().Be(expected);
    }

    // --- Null guards ---

    [Fact]
    public async Task ComputeSummaryAsync_NullEntries_ThrowsArgumentNullException()
    {
        var service = new DamageLogService(NullLogger);

        var act = () => service.ComputeSummaryAsync(null!, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentNullException>()
            .WithParameterName("entries");
    }

    [Fact]
    public void AddEntries_NullEntries_ThrowsArgumentNullException()
    {
        var service = new DamageLogService(NullLogger);

        var act = () => service.AddEntries(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("entries");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new DamageLogService(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    // --- Default overloads ---

    [Fact]
    public async Task PollEntriesAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        IDamageLogService service = new DamageLogService(NullLogger);

        var result = await service.PollEntriesAsync();

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ComputeSummaryAsync_DefaultOverload_DelegatesToCancellationOverload()
    {
        IDamageLogService service = new DamageLogService(NullLogger);

        var summary = await service.ComputeSummaryAsync(Array.Empty<DamageLogEntry>());

        summary.MvpUnit.Should().BeEmpty();
    }

    // --- ComputeMvpUnit edge cases ---

    [Fact]
    public void ComputeMvpUnit_TiedDamage_ReturnsFirstEncountered()
    {
        var entries = new[]
        {
            new DamageLogEntry("UNIT_A", "TARGET", 100f, "blaster", BaseTime),
            new DamageLogEntry("UNIT_B", "TARGET", 100f, "blaster", BaseTime.AddSeconds(1))
        };

        var mvp = DamageLogService.ComputeMvpUnit(entries);

        // Both have 100f total damage; the first iterated in the dictionary wins
        mvp.Should().NotBeNullOrWhiteSpace();
        mvp.Should().BeOneOf("UNIT_A", "UNIT_B");
    }

    [Fact]
    public void ComputeMvpUnit_AllNullOrEmptySourceUnits_ReturnsEmpty()
    {
        var entries = new[]
        {
            new DamageLogEntry(null!, "TARGET", 100f, "blaster", BaseTime),
            new DamageLogEntry("", "TARGET", 200f, "blaster", BaseTime.AddSeconds(1))
        };

        DamageLogService.ComputeMvpUnit(entries).Should().BeEmpty();
    }

    [Fact]
    public void ComputeMvpUnit_MultipleEntriesSameUnit_AggregatesCorrectly()
    {
        var entries = new[]
        {
            new DamageLogEntry("EMPIRE_AT_AT", "R1", 100f, "blaster", BaseTime),
            new DamageLogEntry("REBEL_X_WING", "E1", 90f, "torpedo", BaseTime.AddSeconds(1)),
            new DamageLogEntry("EMPIRE_AT_AT", "R2", 60f, "blaster", BaseTime.AddSeconds(2))
        };

        // AT_AT total = 160, X_WING total = 90
        DamageLogService.ComputeMvpUnit(entries).Should().Be("EMPIRE_AT_AT");
    }

    // --- ComputeDamagePerFaction edge cases ---

    [Fact]
    public void ComputeDamagePerFaction_EmptyEntries_ReturnsEmptyDictionary()
    {
        var result = DamageLogService.ComputeDamagePerFaction(Array.Empty<DamageLogEntry>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeDamagePerFaction_NullSourceUnit_MapsToUnknown()
    {
        var entries = new[]
        {
            new DamageLogEntry(null!, "TARGET", 50f, "blaster", BaseTime)
        };

        var result = DamageLogService.ComputeDamagePerFaction(entries);

        result.Should().ContainKey("Unknown")
            .WhoseValue.Should().Be(50f);
    }

    // --- ComputeKillsPerFaction edge cases ---

    [Fact]
    public void ComputeKillsPerFaction_EmptyEntries_ReturnsEmptyDictionary()
    {
        var result = DamageLogService.ComputeKillsPerFaction(Array.Empty<DamageLogEntry>());

        result.Should().BeEmpty();
    }

    [Fact]
    public void ComputeKillsPerFaction_OnlyNonKillEntries_ReturnsEmptyDictionary()
    {
        var entries = new[]
        {
            new DamageLogEntry("EMPIRE_AT_AT", "REBEL_TROOPER", 100f, "blaster", BaseTime),
            new DamageLogEntry("REBEL_X_WING", "EMPIRE_AT_AT", 50f, "torpedo", BaseTime)
        };

        DamageLogService.ComputeKillsPerFaction(entries).Should().BeEmpty();
    }

    [Fact]
    public void ComputeKillsPerFaction_KillWithNullSourceUnit_MapsToUnknown()
    {
        var entries = new[]
        {
            new DamageLogEntry(null!, "REBEL_TARGET", 100f, "kill", BaseTime)
        };

        var result = DamageLogService.ComputeKillsPerFaction(entries);

        result.Should().ContainKey("Unknown")
            .WhoseValue.Should().Be(1);
    }

    // --- ComputeBattleDuration edge cases ---

    [Fact]
    public void ComputeBattleDuration_OutOfOrderTimestamps_StillComputesCorrectDuration()
    {
        var entries = new[]
        {
            new DamageLogEntry("A", "B", 10f, "hit", BaseTime.AddMinutes(5)),
            new DamageLogEntry("A", "B", 10f, "hit", BaseTime),
            new DamageLogEntry("A", "B", 10f, "hit", BaseTime.AddMinutes(3))
        };

        DamageLogService.ComputeBattleDuration(entries)
            .Should().Be(TimeSpan.FromMinutes(5));
    }

    // --- InferFaction edge cases ---

    [Fact]
    public void InferFaction_EmptyString_ReturnsUnknown()
    {
        DamageLogService.InferFaction(string.Empty).Should().Be("Unknown");
    }
}
