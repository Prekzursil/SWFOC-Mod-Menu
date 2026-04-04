using System.Collections.ObjectModel;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 5 branch coverage for MainViewModelQuickActionHelpers:
/// PopulateActiveFreezes with frozen symbols, active toggles, empty, combined.
/// </summary>
public sealed class MainViewModelQuickActionHelpersWave5Tests
{
    [Fact]
    public void PopulateActiveFreezes_WithFrozenSymbolsOnly_ShouldShowFreezeEntries()
    {
        var activeFreezes = new ObservableCollection<string>();
        var frozen = new[] { "credits", "game_timer_freeze" };
        var toggles = Array.Empty<string>();

        MainViewModelQuickActionHelpers.PopulateActiveFreezes(activeFreezes, frozen, toggles);

        activeFreezes.Should().HaveCount(2);
        activeFreezes[0].Should().Contain("credits");
        activeFreezes[1].Should().Contain("game_timer_freeze");
    }

    [Fact]
    public void PopulateActiveFreezes_WithActiveTogglesOnly_ShouldShowToggleEntries()
    {
        var activeFreezes = new ObservableCollection<string>();
        var frozen = Array.Empty<string>();
        var toggles = new[] { "fog_reveal", "ai_enabled" };

        MainViewModelQuickActionHelpers.PopulateActiveFreezes(activeFreezes, frozen, toggles);

        activeFreezes.Should().HaveCount(2);
        activeFreezes[0].Should().Contain("fog_reveal");
        activeFreezes[1].Should().Contain("ai_enabled");
    }

    [Fact]
    public void PopulateActiveFreezes_Empty_ShouldShowNone()
    {
        var activeFreezes = new ObservableCollection<string>();
        var frozen = Array.Empty<string>();
        var toggles = Array.Empty<string>();

        MainViewModelQuickActionHelpers.PopulateActiveFreezes(activeFreezes, frozen, toggles);

        activeFreezes.Should().ContainSingle().Which.Should().Contain("(none)");
    }

    [Fact]
    public void PopulateActiveFreezes_Combined_ShouldShowBoth()
    {
        var activeFreezes = new ObservableCollection<string>();
        var frozen = new[] { "credits" };
        var toggles = new[] { "fog_reveal" };

        MainViewModelQuickActionHelpers.PopulateActiveFreezes(activeFreezes, frozen, toggles);

        activeFreezes.Should().HaveCount(2);
    }

    [Fact]
    public void PopulateActiveFreezes_ShouldClearExistingEntries()
    {
        var activeFreezes = new ObservableCollection<string> { "old_entry" };
        var frozen = new[] { "credits" };
        var toggles = Array.Empty<string>();

        MainViewModelQuickActionHelpers.PopulateActiveFreezes(activeFreezes, frozen, toggles);

        activeFreezes.Should().NotContain("old_entry");
        activeFreezes.Should().ContainSingle();
    }
}
