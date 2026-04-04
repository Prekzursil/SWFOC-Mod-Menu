using FluentAssertions;
using SwfocTrainer.App.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App;

/// <summary>
/// Wave 5 branch coverage for MainViewModelSelectedUnitParsingHelpers
/// and MainViewModelSelectedUnitDraftHelpers: direct unit-level coverage
/// of TryParseSelectedUnitFloat and TryParseSelectedUnitInt for all branches
/// (null input, whitespace, valid, invalid), plus DraftHelpers error paths
/// for HP, Shield, Speed, Cooldown failures.
/// </summary>
public sealed class MainViewModelParsingHelpersWave5Tests
{
    // --- TryParseSelectedUnitFloat ---

    [Fact]
    public void TryParseSelectedUnitFloat_NullInput_ShouldThrow()
    {
        var act = () => MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(
            null!, "error", out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryParseSelectedUnitFloat_NullErrorMessage_ShouldThrow()
    {
        var act = () => MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(
            "10", null!, out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryParseSelectedUnitFloat_EmptyInput_ShouldReturnNullValue()
    {
        var ok = MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(
            "", "error", out var value, out var error);
        ok.Should().BeTrue();
        value.Should().BeNull();
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryParseSelectedUnitFloat_WhitespaceInput_ShouldReturnNullValue()
    {
        var ok = MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(
            "   ", "error", out var value, out var error);
        ok.Should().BeTrue();
        value.Should().BeNull();
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryParseSelectedUnitFloat_ValidFloat_ShouldReturnParsedValue()
    {
        var ok = MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(
            "3.14", "error", out var value, out var error);
        ok.Should().BeTrue();
        value.Should().BeApproximately(3.14f, 0.01f);
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryParseSelectedUnitFloat_ValidInteger_ShouldReturnParsedValue()
    {
        var ok = MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(
            "100", "error", out var value, out var error);
        ok.Should().BeTrue();
        value.Should().Be(100f);
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryParseSelectedUnitFloat_InvalidInput_ShouldReturnError()
    {
        var ok = MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitFloat(
            "abc", "HP must be a number.", out var value, out var error);
        ok.Should().BeFalse();
        value.Should().BeNull();
        error.Should().Be("HP must be a number.");
    }

    // --- TryParseSelectedUnitInt ---

    [Fact]
    public void TryParseSelectedUnitInt_NullInput_ShouldThrow()
    {
        var act = () => MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt(
            null!, "error", out _, out _);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void TryParseSelectedUnitInt_EmptyInput_ShouldReturnNullValue()
    {
        var ok = MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt(
            "", "error", out var value, out var error);
        ok.Should().BeTrue();
        value.Should().BeNull();
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryParseSelectedUnitInt_WhitespaceInput_ShouldReturnNullValue()
    {
        var ok = MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt(
            "  ", "error", out var value, out var error);
        ok.Should().BeTrue();
        value.Should().BeNull();
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryParseSelectedUnitInt_ValidInt_ShouldReturnParsedValue()
    {
        var ok = MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt(
            "42", "error", out var value, out var error);
        ok.Should().BeTrue();
        value.Should().Be(42);
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryParseSelectedUnitInt_InvalidInput_ShouldReturnError()
    {
        var ok = MainViewModelSelectedUnitParsingHelpers.TryParseSelectedUnitInt(
            "xyz", "Veterancy must be an integer.", out var value, out var error);
        ok.Should().BeFalse();
        value.Should().BeNull();
        error.Should().Be("Veterancy must be an integer.");
    }

    // --- TryParseSelectedUnitFloatValues (all 5 failure paths) ---

    [Fact]
    public void TryParseSelectedUnitFloatValues_InvalidHp_ShouldFail()
    {
        var ok = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(
            new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs(
                "invalid", "100", "1", "1", "1"),
            out var values, out var error);
        ok.Should().BeFalse();
        error.Should().Be("HP must be a number.");
        values.Hp.Should().BeNull();
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_InvalidShield_ShouldFail()
    {
        var ok = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(
            new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs(
                "100", "invalid", "1", "1", "1"),
            out var values, out var error);
        ok.Should().BeFalse();
        error.Should().Be("Shield must be a number.");
        values.Shield.Should().BeNull();
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_InvalidSpeed_ShouldFail()
    {
        var ok = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(
            new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs(
                "100", "100", "invalid", "1", "1"),
            out var values, out var error);
        ok.Should().BeFalse();
        error.Should().Be("Speed must be a number.");
        values.Speed.Should().BeNull();
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_InvalidCooldown_ShouldFail()
    {
        var ok = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(
            new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs(
                "100", "100", "1", "1", "invalid"),
            out var values, out var error);
        ok.Should().BeFalse();
        error.Should().Be("Cooldown multiplier must be a number.");
        values.Cooldown.Should().BeNull();
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_AllValid_ShouldSucceed()
    {
        var ok = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(
            new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs(
                "100", "50", "2.5", "3", "1.5"),
            out var values, out var error);
        ok.Should().BeTrue();
        error.Should().BeEmpty();
        values.Hp.Should().Be(100f);
        values.Shield.Should().Be(50f);
        values.Speed.Should().Be(2.5f);
        values.Damage.Should().Be(3f);
        values.Cooldown.Should().Be(1.5f);
    }

    [Fact]
    public void TryParseSelectedUnitFloatValues_AllBlank_ShouldSucceedWithNulls()
    {
        var ok = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitFloatValues(
            new MainViewModelSelectedUnitDraftHelpers.SelectedUnitFloatInputs(
                "", "", "", "", ""),
            out var values, out var error);
        ok.Should().BeTrue();
        error.Should().BeEmpty();
        values.Hp.Should().BeNull();
        values.Shield.Should().BeNull();
        values.Speed.Should().BeNull();
        values.Damage.Should().BeNull();
        values.Cooldown.Should().BeNull();
    }

    // --- TryParseSelectedUnitIntValues ---

    [Fact]
    public void TryParseSelectedUnitIntValues_InvalidVeterancy_ShouldFail()
    {
        var ok = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitIntValues(
            "invalid", "1", out var veterancy, out var ownerFaction, out var error);
        ok.Should().BeFalse();
        veterancy.Should().BeNull();
        ownerFaction.Should().BeNull();
        error.Should().Be("Veterancy must be an integer.");
    }

    [Fact]
    public void TryParseSelectedUnitIntValues_BothValid_ShouldSucceed()
    {
        var ok = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitIntValues(
            "3", "2", out var veterancy, out var ownerFaction, out var error);
        ok.Should().BeTrue();
        veterancy.Should().Be(3);
        ownerFaction.Should().Be(2);
        error.Should().BeEmpty();
    }

    [Fact]
    public void TryParseSelectedUnitIntValues_BothBlank_ShouldSucceedWithNulls()
    {
        var ok = MainViewModelSelectedUnitDraftHelpers.TryParseSelectedUnitIntValues(
            "", "", out var veterancy, out var ownerFaction, out var error);
        ok.Should().BeTrue();
        veterancy.Should().BeNull();
        ownerFaction.Should().BeNull();
        error.Should().BeEmpty();
    }
}
