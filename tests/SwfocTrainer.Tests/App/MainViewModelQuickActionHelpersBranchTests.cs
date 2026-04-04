using System.Collections.ObjectModel;
using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Branch coverage for MainViewModelQuickActionHelpers.PopulateActiveFreezes.
/// </summary>
public sealed class MainViewModelQuickActionHelpersBranchTests
{
    [Fact]
    public void PopulateActiveFreezes_ShouldThrow_WhenActiveFreezesIsNull()
    {
        var act = () => MainViewModelQuickActionHelpers.PopulateActiveFreezes(
            null!, Array.Empty<string>(), Array.Empty<string>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PopulateActiveFreezes_ShouldThrow_WhenFrozenSymbolsIsNull()
    {
        var act = () => MainViewModelQuickActionHelpers.PopulateActiveFreezes(
            new ObservableCollection<string>(), null!, Array.Empty<string>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PopulateActiveFreezes_ShouldThrow_WhenActiveTogglesIsNull()
    {
        var act = () => MainViewModelQuickActionHelpers.PopulateActiveFreezes(
            new ObservableCollection<string>(), Array.Empty<string>(), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PopulateActiveFreezes_ShouldShowNone_WhenBothEmpty()
    {
        var freezes = new ObservableCollection<string>();
        MainViewModelQuickActionHelpers.PopulateActiveFreezes(
            freezes, Array.Empty<string>(), Array.Empty<string>());
        freezes.Should().ContainSingle().Which.Should().Be("(none)");
    }

    [Fact]
    public void PopulateActiveFreezes_ShouldIncludeFrozenSymbols()
    {
        var freezes = new ObservableCollection<string>();
        MainViewModelQuickActionHelpers.PopulateActiveFreezes(
            freezes, new[] { "credits", "health" }, Array.Empty<string>());
        freezes.Should().HaveCount(2);
        freezes[0].Should().Contain("credits");
        freezes[1].Should().Contain("health");
    }

    [Fact]
    public void PopulateActiveFreezes_ShouldIncludeActiveToggles()
    {
        var freezes = new ObservableCollection<string>();
        MainViewModelQuickActionHelpers.PopulateActiveFreezes(
            freezes, Array.Empty<string>(), new[] { "fog_reveal" });
        freezes.Should().ContainSingle().Which.Should().Contain("fog_reveal");
    }

    [Fact]
    public void PopulateActiveFreezes_ShouldIncludeBoth()
    {
        var freezes = new ObservableCollection<string>();
        MainViewModelQuickActionHelpers.PopulateActiveFreezes(
            freezes, new[] { "credits" }, new[] { "fog_reveal" });
        freezes.Should().HaveCount(2);
    }

    [Fact]
    public void PopulateActiveFreezes_ShouldClearExistingEntries()
    {
        var freezes = new ObservableCollection<string> { "old_entry" };
        MainViewModelQuickActionHelpers.PopulateActiveFreezes(
            freezes, new[] { "credits" }, Array.Empty<string>());
        freezes.Should().ContainSingle().Which.Should().Contain("credits");
    }
}
