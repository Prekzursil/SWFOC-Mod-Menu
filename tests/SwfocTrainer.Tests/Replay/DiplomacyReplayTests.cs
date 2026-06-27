using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="DiplomacyService"/>.
/// </summary>
/// <remarks>
/// DiplomacyService emits the corrected PlayerWrapper instance-method form
/// (<c>p1:Make_Ally(p2)</c>), confirmed via IDA Pro string-level evidence
/// during session 2026-04-07. Method-call shape is not in the replay
/// intercept catalog, so the bridge round-trip is stubbed.
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class DiplomacyReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public DiplomacyReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_allied_uses_Make_Ally_method_form()
    {
        var lua = DiplomacyService.BuildDiplomacyLuaCommand(
            new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Allied));
        lua.Should().NotBeNull();
        lua!.Should().Contain("p1:Make_Ally(p2)");
        lua.Should().Contain("Find_Player(\"EMPIRE\")");
        lua.Should().Contain("Find_Player(\"REBEL\")");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_hostile_uses_Make_Enemy_method_form()
    {
        var lua = DiplomacyService.BuildDiplomacyLuaCommand(
            new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Hostile));
        lua.Should().NotBeNull();
        lua!.Should().Contain("p1:Make_Enemy(p2)");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_neutral_returns_null()
    {
        var lua = DiplomacyService.BuildDiplomacyLuaCommand(
            new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Neutral));
        lua.Should().BeNull();
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
        var lua = DiplomacyService.BuildDiplomacyLuaCommand(
            new DiplomacyState("EMPIRE", "REBEL", DiplomacyRelation.Allied));
        var idents = VerifiedLedgerLookup.ExtractLuaIdentifiers(lua!);
        idents.Should().Contain(new[] { "Find_Player", "Make_Ally" });
        VerifiedLedgerLookup.ContainsFunction("Make_Ally").Should().BeTrue();
        VerifiedLedgerLookup.ContainsFunction("Find_Player").Should().BeTrue();
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
