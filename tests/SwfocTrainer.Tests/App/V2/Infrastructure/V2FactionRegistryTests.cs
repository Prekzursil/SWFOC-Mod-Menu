using System;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.Infrastructure;

/// <summary>
/// Tests for the 2026-04-27 <see cref="V2FactionRegistry"/> shared faction
/// list service.
/// </summary>
/// <remarks>
/// The registry is the single source of truth for the faction dropdowns in
/// PlayerState / UnitControl / WorldState / Galactic / Economy. Its merge
/// semantics are append-only and case-insensitive — these tests pin both.
/// </remarks>
public sealed class V2FactionRegistryTests
{
    [Fact]
    public void Constructor_SeedsVanilla_PlayableFactions()
    {
        var reg = new V2FactionRegistry();
        reg.Factions.Should().BeEquivalentTo(new[] { "EMPIRE", "REBEL", "UNDERWORLD" });
    }

    [Fact]
    public void MergeFactions_AddsNewEntries_AppendOnly()
    {
        var reg = new V2FactionRegistry();
        var added = reg.MergeFactions(new[] { "AOTR_REBEL", "AOTR_EMPIRE" });

        added.Should().Be(2);
        reg.Factions.Should().Contain(new[] { "EMPIRE", "REBEL", "UNDERWORLD", "AOTR_REBEL", "AOTR_EMPIRE" });
    }

    [Fact]
    public void MergeFactions_DedupsCaseInsensitively()
    {
        var reg = new V2FactionRegistry();
        var added = reg.MergeFactions(new[] { "empire", "Rebel", "underworld" });

        added.Should().Be(0, "all three are duplicates of the seed list (case-insensitive)");
        reg.Factions.Count.Should().Be(3);
    }

    [Fact]
    public void MergeFactions_PreservesExistingCasing()
    {
        var reg = new V2FactionRegistry();
        reg.MergeFactions(new[] { "empire" });

        // Seed entry is "EMPIRE"; merge of lowercase "empire" should be a
        // no-op (dedup), and the original casing must persist.
        reg.Factions[0].Should().Be("EMPIRE");
    }

    [Fact]
    public void MergeFactions_SkipsEmptyAndWhitespace()
    {
        var reg = new V2FactionRegistry();
        var added = reg.MergeFactions(new[] { "", "   ", "\t", "VONG" });

        added.Should().Be(1, "empty/whitespace entries should be skipped");
        reg.Factions.Should().Contain("VONG");
    }

    [Fact]
    public void MergeFactions_TrimsWhitespaceFromInput()
    {
        var reg = new V2FactionRegistry();
        var added = reg.MergeFactions(new[] { "  VONG  " });

        added.Should().Be(1);
        reg.Factions.Should().Contain("VONG");
        reg.Factions.Should().NotContain("  VONG  ");
    }

    [Fact]
    public void MergeFactions_IsIdempotent_AcrossMultipleCalls()
    {
        var reg = new V2FactionRegistry();
        reg.MergeFactions(new[] { "MANDALORIAN" });
        reg.MergeFactions(new[] { "MANDALORIAN", "VONG" });
        reg.MergeFactions(new[] { "vong" }); // case-insensitive duplicate

        reg.Factions.Count.Should().Be(5); // EMPIRE, REBEL, UNDERWORLD, MANDALORIAN, VONG
        reg.Factions.Should().Contain(new[] { "MANDALORIAN", "VONG" });
    }

    [Fact]
    public void MergeFactions_NullInput_Throws()
    {
        var reg = new V2FactionRegistry();
        var act = () => reg.MergeFactions(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MergeFactions_FiresCollectionChanged_PerNewEntry()
    {
        // 2026-04-27 (iter 13): observability test. The registry's
        // Factions ObservableCollection must fire CollectionChanged
        // events as merges happen — that's how WPF data binding
        // refreshes every faction-bound ComboBox in real time. We
        // count Add events to verify the wiring.
        var reg = new V2FactionRegistry();
        var addEvents = 0;
        reg.Factions.CollectionChanged += (_, e) =>
        {
            if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
            {
                addEvents++;
            }
        };

        reg.MergeFactions(new[] { "MANDALORIAN", "VONG", "EMPIRE" });

        addEvents.Should().Be(2,
            "EMPIRE is already in the seed; only MANDALORIAN + VONG should fire Add events");
    }

    [Fact]
    public void MergeFactions_NoEvents_WhenAllInputAreDuplicates()
    {
        var reg = new V2FactionRegistry();
        var changeEvents = 0;
        reg.Factions.CollectionChanged += (_, _) => changeEvents++;

        reg.MergeFactions(new[] { "EMPIRE", "REBEL", "UNDERWORLD" });

        changeEvents.Should().Be(0, "every input duplicates the seed; no event should fire");
    }
}
