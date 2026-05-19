using FluentAssertions;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="FleetManagerService"/>.
/// </summary>
/// <remarks>
/// Fleet assembly emits <c>Assemble_Fleet(Find_Player(...), FindPlanet(...))</c>,
/// which is not in the replay intercept catalog. Bridge round-trip is stubbed.
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class FleetManagerReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public FleetManagerReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_emits_Assemble_Fleet_call()
    {
        var lua = FleetManagerService.BuildAssembleFleetLuaCommand("REBEL", "TATOOINE");
        lua.Should().Be("Assemble_Fleet(Find_Player(\"REBEL\"), FindPlanet(\"TATOOINE\"))");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_quotes_both_arguments()
    {
        var lua = FleetManagerService.BuildAssembleFleetLuaCommand("EMPIRE", "HOTH");
        lua.Should().Contain("\"EMPIRE\"");
        lua.Should().Contain("\"HOTH\"");
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
        var lua = FleetManagerService.BuildAssembleFleetLuaCommand("EMPIRE", "HOTH");
        var idents = VerifiedLedgerLookup.ExtractLuaIdentifiers(lua);
        idents.Should().Contain("Find_Player");
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
