using FluentAssertions;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="RosterBrowserService"/>.
/// </summary>
/// <remarks>
/// The discover-types command is a complex multi-line Lua block that walks
/// <c>Find_All_Objects_Of_Type</c> and calls back into <c>SWFOC_Log</c>.
/// The replay intercept catalog does not match this shape, so end-to-end
/// execution is stubbed and we use <c>SWFOC_ReplayPlayerCount()</c> as the
/// liveness probe (it returns the canonical fixture's player slot count).
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class RosterBrowserReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public RosterBrowserReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_emits_object_walk()
    {
        var lua = RosterBrowserService.BuildDiscoverTypesLuaCommand("HEROES");
        lua.Should().Contain("Find_Object_Type");
        lua.Should().Contain("Find_All_Objects_Of_Type");
        lua.Should().Contain("SWFOC_Log");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_passes_category_quoted()
    {
        var lua = RosterBrowserService.BuildDiscoverTypesLuaCommand("UNIT_CATALOG");
        lua.Should().Contain("\"UNIT_CATALOG\"");
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
        var lua = RosterBrowserService.BuildDiscoverTypesLuaCommand("HEROES");
        var idents = VerifiedLedgerLookup.ExtractLuaIdentifiers(lua);
        idents.Should().Contain("Find_All_Objects_Of_Type");
        VerifiedLedgerLookup.ContainsFunction("Find_All_Objects_Of_Type").Should().BeTrue();
    }

    [SkippableFact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Stubbed")]
    public async Task ReplayBridge_returns_known_player_count()
    {
        if (!_fixture.ReplayBinaryAvailable)
        {
            throw new SkipException("swfoc_replay.exe not available; replay tests skipped");
        }

        var runner = new BridgeAssertionRunner(_fixture.Bridge);
        var assertion = new BridgeAssertion
        {
            PreStateProbe = "return SWFOC_ReplayPlayerCount()",
            LuaCommand = "return SWFOC_ReplayPlayerCount()",
            PostStateProbe = "return SWFOC_ReplayPlayerCount()",
            ExpectDelta = (pre, post) =>
                pre == post && pre == ReplayHarnessFixture.FixturePlayerCount.ToString(),
        };

        var result = await runner.RunAsync(assertion, CancellationToken.None);
        result.Passed.Should().BeTrue(
            because: $"FailureReason='{result.FailureReason}', stderr='{_fixture.GetStderrSnapshot()}'");
    }
}
