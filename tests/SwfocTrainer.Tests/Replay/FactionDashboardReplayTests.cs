using FluentAssertions;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="FactionDashboardService"/>.
/// </summary>
/// <remarks>
/// The service builds a method-call style command:
/// <c>local p = Find_Player("EMPIRE"); if p then return tostring(p:Get_Credits()) else return "0" end</c>.
/// The replay harness's <c>ReplayLoad</c> intercept pattern-matches a small
/// catalog of <c>return SWFOC_*(...)</c> commands, so this command does NOT
/// run end-to-end against the fake VM. End-to-end coverage is therefore
/// stubbed at the assertion layer (we use SWFOC_GetCredits as a liveness
/// probe instead) and the structural tests verify the BuildLuaCommand output
/// shape directly. See <c>knowledge-base/replay_stub_gaps.md</c> for the gap
/// description and the proposed <c>SWFOC_ReplayPlayerCredits(faction)</c>
/// helper that would unblock direct execution.
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class FactionDashboardReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public FactionDashboardReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_produces_parseable_lua()
    {
        var lua = FactionDashboardService.BuildFactionQueryLuaCommand("EMPIRE");
        lua.Should().Contain("Find_Player");
        lua.Should().Contain("Get_Credits");
        lua.Should().Contain("\"EMPIRE\"");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_escapes_faction_name_inside_double_quotes()
    {
        var lua = FactionDashboardService.BuildFactionQueryLuaCommand("UNDERWORLD");
        lua.Should().Contain("\"UNDERWORLD\"");
        // The build command should not interpolate the value before the
        // double quotes — verifies our string template format.
        lua.IndexOf("\"UNDERWORLD\"").Should().BeGreaterThan(lua.IndexOf("Find_Player"));
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
        var lua = FactionDashboardService.BuildFactionQueryLuaCommand("REBEL");
        var idents = VerifiedLedgerLookup.ExtractLuaIdentifiers(lua);
        idents.Should().Contain("Find_Player");
        idents.Should().Contain("Get_Credits");
        VerifiedLedgerLookup.ContainsFunction("Find_Player").Should().BeTrue();
        VerifiedLedgerLookup.ContainsFunction("Get_Credits").Should().BeTrue();
    }

    [SkippableFact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Stubbed")]
    public async Task ReplayBridge_returns_known_credits_for_local_player()
    {
        if (!_fixture.ReplayBinaryAvailable)
        {
            throw new SkipException("swfoc_replay.exe not available; replay tests skipped");
        }

        // SWFOC_GetCredits is in the replay's intercept catalog and returns the
        // credits of the local player (slot index 0 in player_array). Our
        // fixture places UNDERWORLD (12345) at slot 0 via WithLocalPlayerSlot.
        var runner = new BridgeAssertionRunner(_fixture.Bridge);
        var assertion = new BridgeAssertion
        {
            PreStateProbe = "return SWFOC_GetCredits()",
            LuaCommand = "return SWFOC_GetCredits()",
            PostStateProbe = "return SWFOC_GetCredits()",
            // The replay harness formats numbers via printf("%.14g", n), which
            // collapses integral doubles like 12345.0 to "12345".
            ExpectDelta = (pre, post) => pre == post && pre == "12345",
            Description = "fixture local player credits == 12345 (read-only)",
        };

        var result = await runner.RunAsync(assertion, CancellationToken.None);
        result.Passed.Should().BeTrue(
            because: $"FailureReason='{result.FailureReason}', stderr='{_fixture.GetStderrSnapshot()}'");
    }
}
