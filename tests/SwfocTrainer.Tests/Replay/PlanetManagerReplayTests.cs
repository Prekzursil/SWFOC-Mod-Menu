using FluentAssertions;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="PlanetManagerService"/>.
/// </summary>
/// <remarks>
/// SetPlanetOwner builds <c>FindPlanet("X"):Change_Owner(Find_Player("Y"))</c>,
/// which is a method-call on a found object. The replay intercept catalog
/// does not match this shape, so end-to-end execution is stubbed.
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class PlanetManagerReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public PlanetManagerReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_uses_FindPlanet_and_Change_Owner()
    {
        var lua = PlanetManagerService.BuildSetPlanetOwnerLuaCommand("TATOOINE", "REBEL");
        lua.Should().Be("FindPlanet(\"TATOOINE\"):Change_Owner(Find_Player(\"REBEL\"))");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_preserves_planet_id_casing()
    {
        var lua = PlanetManagerService.BuildSetPlanetOwnerLuaCommand("hoth_north", "EMPIRE");
        lua.Should().Contain("\"hoth_north\"");
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
        var lua = PlanetManagerService.BuildSetPlanetOwnerLuaCommand("TATOOINE", "REBEL");
        var idents = VerifiedLedgerLookup.ExtractLuaIdentifiers(lua);
        idents.Should().Contain(new[] { "FindPlanet", "Change_Owner", "Find_Player" });
        VerifiedLedgerLookup.ContainsFunction("Find_Player").Should().BeTrue();
        VerifiedLedgerLookup.ContainsFunction("Change_Owner").Should().BeTrue();
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
