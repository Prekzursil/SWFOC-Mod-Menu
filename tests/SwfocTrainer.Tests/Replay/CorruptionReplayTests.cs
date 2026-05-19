using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="CorruptionService"/>.
/// </summary>
/// <remarks>
/// Corruption emits <c>Story_Event("CORRUPTION_RACKETEERING_PLANET")</c> /
/// <c>Story_Event("REMOVE_CORRUPTION_PLANET")</c>. Story_Event is not in the
/// replay intercept catalog. Bridge round-trip is stubbed.
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class CorruptionReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public CorruptionReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildCorruptionLuaCommand_uses_Story_Event_pattern()
    {
        var entry = new CorruptionEntry("tatooine", CorruptionType.Racketeering, 1);
        var lua = CorruptionService.BuildCorruptionLuaCommand(entry);
        lua.Should().Be("Story_Event(\"CORRUPTION_RACKETEERING_TATOOINE\")");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildRemoveCorruptionLuaCommand_uses_REMOVE_prefix()
    {
        var lua = CorruptionService.BuildRemoveCorruptionLuaCommand("hoth");
        lua.Should().Be("Story_Event(\"REMOVE_CORRUPTION_HOTH\")");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void ValidateCorruptionType_rejects_None_and_accepts_real_types()
    {
        CorruptionService.ValidateCorruptionType(CorruptionType.None).Should().BeFalse();
        CorruptionService.ValidateCorruptionType(CorruptionType.Racketeering).Should().BeTrue();
        CorruptionService.ValidateCorruptionType(CorruptionType.Bribery).Should().BeTrue();
        CorruptionService.ValidateCorruptionType(CorruptionType.Piracy).Should().BeTrue();
        CorruptionService.ValidateCorruptionType(CorruptionType.Kidnapping).Should().BeTrue();
        CorruptionService.ValidateCorruptionType(CorruptionType.Sabotage).Should().BeTrue();
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
        var lua = CorruptionService.BuildCorruptionLuaCommand(
            new CorruptionEntry("tatooine", CorruptionType.Bribery, 1));
        var idents = VerifiedLedgerLookup.ExtractLuaIdentifiers(lua);
        idents.Should().Contain("Story_Event");
        VerifiedLedgerLookup.ContainsFunction("Story_Event").Should().BeTrue();
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
