using FluentAssertions;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="StoryEventService"/>.
/// </summary>
/// <remarks>
/// The service emits <c>Story_Event("eventId")</c> commands. The replay
/// harness's intercept catalog only matches <c>return SWFOC_*(...)</c>
/// commands, so end-to-end execution falls through to the no-op stub. This
/// suite therefore relies on structural assertions for the command shape and
/// uses an unrelated read-only probe (<c>SWFOC_GetVersion</c>) to confirm the
/// pipe is healthy. See <c>knowledge-base/replay_stub_gaps.md</c> entry
/// "StoryEventService" for the suggested
/// <c>SWFOC_ReplayLastStoryEvent()</c> helper that would unblock direct
/// observation of fired events.
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class StoryEventReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public StoryEventReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_emits_Story_Event_call_with_quoted_id()
    {
        var lua = StoryEventService.BuildStoryEventLuaCommand("INTRO_REBEL");
        lua.Should().StartWith("Story_Event(");
        lua.Should().Contain("\"INTRO_REBEL\"");
        lua.Should().EndWith(")");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_does_not_alter_event_id_casing()
    {
        var lua = StoryEventService.BuildStoryEventLuaCommand("missionAccept_Tatooine");
        lua.Should().Contain("\"missionAccept_Tatooine\"");
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
        var lua = StoryEventService.BuildStoryEventLuaCommand("INTRO_REBEL");
        var idents = VerifiedLedgerLookup.ExtractLuaIdentifiers(lua);
        idents.Should().Contain("Story_Event");
        VerifiedLedgerLookup.ContainsFunction("Story_Event").Should().BeTrue();
    }

    [SkippableFact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Stubbed")]
    public async Task ReplayBridge_is_alive_via_GetVersion_probe()
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
            ExpectDelta = (pre, post) =>
                pre == post && pre.Contains("(replay)", StringComparison.Ordinal),
            Description = "replay bridge identifies itself as the replay variant",
        };

        var result = await runner.RunAsync(assertion, CancellationToken.None);
        result.Passed.Should().BeTrue(
            because: $"FailureReason='{result.FailureReason}', stderr='{_fixture.GetStderrSnapshot()}'");
    }
}
