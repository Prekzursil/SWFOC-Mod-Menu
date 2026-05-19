using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="OwnershipTransferService"/>.
/// </summary>
/// <remarks>
/// OwnershipTransfer emits
/// <c>Find_First_Object("X"):Change_Owner(Find_Player("Y"))</c>. The replay
/// intercept catalog does not match this method-call shape, so the bridge
/// round-trip is stubbed.
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class OwnershipTransferReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public OwnershipTransferReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_chains_Find_First_Object_with_Change_Owner()
    {
        var lua = OwnershipTransferService.BuildOwnershipLuaCommand("TIE_Fighter", "REBEL");
        lua.Should().Be("Find_First_Object(\"TIE_Fighter\"):Change_Owner(Find_Player(\"REBEL\"))");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void ResolveScopeLabel_returns_lowercase_kebab()
    {
        OwnershipTransferService.ResolveScopeLabel(OwnershipTransferScope.SelectedUnit).Should().Be("selected_unit");
        OwnershipTransferService.ResolveScopeLabel(OwnershipTransferScope.AllOfType).Should().Be("all_of_type");
        OwnershipTransferService.ResolveScopeLabel(OwnershipTransferScope.AllVisible).Should().Be("all_visible");
        OwnershipTransferService.ResolveScopeLabel(OwnershipTransferScope.Planet).Should().Be("planet");
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
        var lua = OwnershipTransferService.BuildOwnershipLuaCommand("X_Wing", "REBEL");
        var idents = VerifiedLedgerLookup.ExtractLuaIdentifiers(lua);
        idents.Should().Contain(new[] { "Find_First_Object", "Change_Owner", "Find_Player" });
        VerifiedLedgerLookup.ContainsFunction("Change_Owner").Should().BeTrue();
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
