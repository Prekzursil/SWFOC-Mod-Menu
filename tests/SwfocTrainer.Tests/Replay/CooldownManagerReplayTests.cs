using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="CooldownManagerService"/>
/// (the AgeControl/CooldownManager seat in the v5 service catalog).
/// </summary>
/// <remarks>
/// SelectedUnit scope emits
/// <c>Find_First_Object("X"):Reset_Ability_Counter()</c>; AllPlayerUnits emits
/// a comment-only command. Neither pattern is in the replay intercept catalog,
/// so the bridge round-trip is stubbed.
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class CooldownManagerReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public CooldownManagerReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_selected_unit_uses_Find_First_Object()
    {
        var req = new CooldownResetRequest(CooldownResetScope.SelectedUnit, "TIE_Fighter");
        var lua = CooldownManagerService.BuildCooldownResetLuaCommand(req);
        lua.Should().Be("Find_First_Object(\"TIE_Fighter\"):Reset_Ability_Counter()");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_all_player_units_emits_comment_only_placeholder()
    {
        var req = new CooldownResetRequest(CooldownResetScope.AllPlayerUnits, null);
        var lua = CooldownManagerService.BuildCooldownResetLuaCommand(req);
        lua.Should().StartWith("--");
    }

    [SkippableFact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_function_names_are_in_ledger()
    {
        if (!VerifiedLedgerLookup.LedgerAvailable)
        {
            throw new SkipException("verified_facts.json not found at expected path");
        }
        var req = new CooldownResetRequest(CooldownResetScope.SelectedUnit, "TIE_Fighter");
        var lua = CooldownManagerService.BuildCooldownResetLuaCommand(req);
        var idents = VerifiedLedgerLookup.ExtractLuaIdentifiers(lua);
        idents.Should().Contain("Find_First_Object");
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
            PreStateProbe = "return SWFOC_GetVersion()",
            LuaCommand = "return SWFOC_GetVersion()",
            PostStateProbe = "return SWFOC_GetVersion()",
            ExpectDelta = (pre, post) => pre == post && pre.Contains("(replay)"),
        };

        var result = await runner.RunAsync(assertion, CancellationToken.None);
        result.Passed.Should().BeTrue(
            because: $"FailureReason='{result.FailureReason}', stderr='{_fixture.GetStderrSnapshot()}'");
    }
}
