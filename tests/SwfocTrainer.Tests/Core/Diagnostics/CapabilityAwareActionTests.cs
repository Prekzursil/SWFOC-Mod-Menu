using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;

namespace SwfocTrainer.Tests.Core.Diagnostics;

/// <summary>
/// 2026-04-27 (iter 55) — pins the shared <see cref="CapabilityAwareAction"/>
/// helper that backs per-button capability badges across Quick Actions,
/// Battle Control, and any future tab that needs them.
/// </summary>
public sealed class CapabilityAwareActionTests
{
    [Fact]
    public void Constructor_AllLiveHelpers_BadgeIsLive()
    {
        var action = new CapabilityAwareAction("Heal", "SWFOC_HealAllLocal");
        action.Badge.Should().Be("LIVE");
        action.IsAllLive.Should().BeTrue();
        action.IsMixed.Should().BeFalse();
    }

    [Fact]
    public void Constructor_AllPhase2PendingHelpers_BadgeIsPhase2Pending()
    {
        var action = new CapabilityAwareAction("Cap", "SWFOC_SetUnitCapOverride");
        action.Badge.Should().Be("PHASE 2 PENDING");
        action.IsAllLive.Should().BeFalse();
        action.IsMixed.Should().BeFalse(
            "uniformly Phase-2-pending is a single status — not mixed");
    }

    [Fact]
    public void Constructor_MixedLiveAndPhase2_BadgeUsesMixedFormat()
    {
        var action = new CapabilityAwareAction("Composite",
            "SWFOC_GodMode",            // LIVE
            "SWFOC_SetUnitCapOverride", // PHASE 2 PENDING
            "SWFOC_HealAllLocal");      // LIVE
        action.Badge.Should().Be("MIXED (2/3 LIVE)");
        action.IsMixed.Should().BeTrue();
        action.IsAllLive.Should().BeFalse();
    }

    [Fact]
    public void Constructor_UnknownHelper_BadgeIsUnavailable()
    {
        var action = new CapabilityAwareAction("Unknown", "SWFOC_NotInCatalog_Iter55");
        action.Badge.Should().Be("UNAVAILABLE");
        action.IsAllLive.Should().BeFalse();
    }

    [Fact]
    public void FromLuaCommands_ParsesHelperNamesAndComputesBadge()
    {
        var action = CapabilityAwareAction.FromLuaCommands("Reveal", new[]
        {
            "return SWFOC_RevealAll(1)",      // LIVE
            "return SWFOC_GetPlanets()",       // LIVE
            "return SWFOC_GetAllPlayers()",    // LIVE
        });
        action.HelperNames.Should().Equal("SWFOC_RevealAll", "SWFOC_GetPlanets", "SWFOC_GetAllPlayers");
        action.Badge.Should().Be("LIVE");
        action.IsMixed.Should().BeFalse();
        action.IsAllLive.Should().BeTrue();
    }

    [Fact]
    public void FromLuaCommands_NullThrows()
    {
        var act = () => CapabilityAwareAction.FromLuaCommands("X", null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("return SWFOC_GodMode(1)", "SWFOC_GodMode")]
    [InlineData("return SWFOC_HealAllLocal()", "SWFOC_HealAllLocal")]
    [InlineData("return SWFOC_BatchTypeExists('a|b|c')", "SWFOC_BatchTypeExists")]
    [InlineData("", "")]
    // No-space input is parsed as if the helper begins at index 0; the
    // arg-list paren still terminates the name, so "SWFOC_NoSpace()"
    // yields just "SWFOC_NoSpace". This is the documented robustness
    // behaviour — a malformed input still gives a usable helper name.
    [InlineData("SWFOC_NoSpace()", "SWFOC_NoSpace")]
    public void ExtractHelperName_HandlesShapeVariations(string lua, string expected)
    {
        CapabilityAwareAction.ExtractHelperName(lua).Should().Be(expected);
    }

    [Fact]
    public void Constructor_PreservesNameAndHelpersOrder()
    {
        var action = new CapabilityAwareAction("MyAction", "A", "B", "C");
        action.Name.Should().Be("MyAction");
        action.HelperNames.Should().Equal("A", "B", "C");
    }
}
