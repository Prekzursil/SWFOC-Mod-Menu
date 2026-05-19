using FluentAssertions;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="FactionSwitchService"/>.
/// </summary>
/// <remarks>
/// 2026-04-11 update: The 2026-04-10 B4 resolution pointed at
/// <c>PlayerListClass::Switch_Sides</c> (RVA 0x297E80) as the canonical
/// setter, but a live-game galactic test on 2026-04-10 revealed that
/// Switch_Sides is silently guarded out in game mode 3 by sub_14028AF60.
/// The bridge now exposes <c>SWFOC_SetHumanPlayer_v3(slot)</c>, which
/// does a manual +0x62 sweep and calls the subsystem refresh path
/// unconditionally — mode-agnostic. FactionSwitchService now emits
/// <c>return SWFOC_SetHumanPlayer_v3(slot)</c>. See
/// <c>knowledge-base/faction_switch_full_anatomy_2026-04-11.md</c>.
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class FactionSwitchReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public FactionSwitchReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_emits_SWFOC_SetHumanPlayer_for_known_target()
    {
        var lua = FactionSwitchService.BuildFactionSwitchLuaCommand("REBEL");
        lua.Should().StartWith("return SWFOC_SetHumanPlayer_v3(");
        lua.Should().Contain("SWFOC_SetHumanPlayer_v3(0)");
        lua.Should().NotContain("BLOCKED-NEEDS-MEMORY");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_throws_for_whitespace_target()
    {
        FluentActions.Invoking(() =>
            FactionSwitchService.BuildFactionSwitchLuaCommand("   "))
            .Should().Throw<ArgumentException>();
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_maps_EMPIRE_to_slot_1()
    {
        var lua = FactionSwitchService.BuildFactionSwitchLuaCommand("EMPIRE");
        lua.Should().Contain("SWFOC_SetHumanPlayer_v3(1)");
    }

    [SkippableFact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Stubbed")]
    public async Task ReplayBridge_alive_probe_succeeds()
    {
        if (!_fixture.ReplayBinaryAvailable)
        {
            throw new SkipException("swfoc_replay.exe not available; replay tests skipped");
        }

        var runner = new BridgeAssertionRunner(_fixture.Bridge);
        var assertion = new BridgeAssertion
        {
            PreStateProbe = "return SWFOC_GetLocalPlayer()",
            LuaCommand = "return SWFOC_GetLocalPlayer()",
            PostStateProbe = "return SWFOC_GetLocalPlayer()",
            ExpectDelta = (pre, post) =>
                pre == post && pre.Contains(ReplayHarnessFixture.FixtureLocalFaction, StringComparison.Ordinal),
        };

        var result = await runner.RunAsync(assertion, CancellationToken.None);
        result.Passed.Should().BeTrue(
            because: $"FailureReason='{result.FailureReason}', stderr='{_fixture.GetStderrSnapshot()}'");
    }
}
