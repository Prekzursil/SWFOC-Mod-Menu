using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using Xunit;

namespace SwfocTrainer.Tests.App.V2.ViewModels;

/// <summary>
/// Tests for the 2026-04-27 (iter 9-14) computed properties on V2 view-models
/// that surface live UX state to the operator.
/// </summary>
/// <remarks>
/// The actual VM ctors require a real V2BridgeAdapter (which needs a pipe).
/// Each computed property was extracted into a static helper so tests can
/// pin the formatting without instantiating the full DI graph.
/// </remarks>
public sealed class ComputedPropertyTests
{
    // --- BattleControlTabViewModel.UnitCapHint ----------------------------

    [Theory]
    [InlineData(-1, "(unlimited — cap removed)")]
    [InlineData(100, "(100 units max per slot)")]
    [InlineData(500, "(500 units max per slot)")]
    [InlineData(9999, "(9999 units max per slot)")]
    public void UnitCapHint_Sane_RendersBracketedReadout(int cap, string expected)
    {
        BattleControlTabViewModel.BuildUnitCapHint(cap).Should().Be(expected);
    }

    [Fact]
    public void UnitCapHint_Zero_RendersWipeWarning()
    {
        BattleControlTabViewModel.BuildUnitCapHint(0).Should()
            .StartWith("⚠")
            .And.Contain("0 means no units");
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(-100)]
    public void UnitCapHint_NegativeOtherThanMinusOne_RendersUndefinedWarning(int cap)
    {
        BattleControlTabViewModel.BuildUnitCapHint(cap).Should()
            .StartWith("⚠")
            .And.Contain("undefined");
    }

    [Theory]
    [InlineData(10000)]
    [InlineData(50000)]
    public void UnitCapHint_HighValues_RendersEnginePerfWarning(int cap)
    {
        BattleControlTabViewModel.BuildUnitCapHint(cap).Should()
            .Contain("very high")
            .And.Contain("10k");
    }

    // --- CrossFactionRecruitmentTabViewModel.RecruitPreview ---------------

    [Fact]
    public void RecruitPreview_EmptyAddr_RendersInvalidInputBanner()
    {
        var msg = CrossFactionRecruitmentTabViewModel.BuildRecruitPreview(
            objAddrInput: "0", sourceOwnerSlot: -1, targetSlot: -1, sourceIsLocal: true);
        msg.Should().StartWith("Source obj_addr is empty");
    }

    [Fact]
    public void RecruitPreview_AddrWithoutSlot_PromptsForSlot()
    {
        var msg = CrossFactionRecruitmentTabViewModel.BuildRecruitPreview(
            objAddrInput: "1234567890", sourceOwnerSlot: -1, targetSlot: -1, sourceIsLocal: true);
        msg.Should().Contain("slot unknown");
    }

    [Fact]
    public void RecruitPreview_AddrAndSlotWithoutTarget_PromptsForTarget()
    {
        var msg = CrossFactionRecruitmentTabViewModel.BuildRecruitPreview(
            objAddrInput: "1234567890", sourceOwnerSlot: 0, targetSlot: -1, sourceIsLocal: true);
        msg.Should().Contain("target slot not set");
    }

    [Fact]
    public void RecruitPreview_SourceEqualsTarget_FlagsNoOpWarning()
    {
        var msg = CrossFactionRecruitmentTabViewModel.BuildRecruitPreview(
            objAddrInput: "1234567890", sourceOwnerSlot: 3, targetSlot: 3, sourceIsLocal: true);
        msg.Should()
            .StartWith("⚠")
            .And.Contain("no-op");
    }

    [Fact]
    public void RecruitPreview_SourceNotLocal_FlagsRejectionWarning()
    {
        var msg = CrossFactionRecruitmentTabViewModel.BuildRecruitPreview(
            objAddrInput: "1234567890", sourceOwnerSlot: 0, targetSlot: 1, sourceIsLocal: false);
        msg.Should()
            .StartWith("⚠")
            .And.Contain("NOT local");
    }

    [Fact]
    public void RecruitPreview_HappyPath_RendersTransferDescription()
    {
        var msg = CrossFactionRecruitmentTabViewModel.BuildRecruitPreview(
            objAddrInput: "1234567890", sourceOwnerSlot: 0, targetSlot: 1, sourceIsLocal: true);
        msg.Should()
            .StartWith("Will transfer")
            .And.Contain("slot 0")
            .And.Contain("slot 1");
    }

    [Fact]
    public void RecruitPreview_HexAddr_ParsesCorrectly()
    {
        // The user pastes "0x1A2B3C" — parsing should fall back to hex.
        var msg = CrossFactionRecruitmentTabViewModel.BuildRecruitPreview(
            objAddrInput: "1A2B3C", sourceOwnerSlot: 0, targetSlot: 1, sourceIsLocal: true);
        msg.Should().Contain("0x1A2B3C");
    }
}
