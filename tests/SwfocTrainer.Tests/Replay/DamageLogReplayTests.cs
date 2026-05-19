using FluentAssertions;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="DamageLogService"/>.
/// </summary>
/// <remarks>
/// DamageLog emits <c>SWFOC_EventControl(1)</c> / <c>SWFOC_EventControl(0)</c>
/// commands. The replay registers a real <c>SWFOC_EventControl</c> stub but
/// the <c>ReplayLoad</c> intercept catalog only matches the four
/// <c>SWFOC_*</c> commands listed in <c>replay_harness.cpp</c>; everything
/// else falls through to the no-op fake. The bridge round-trip is therefore
/// stubbed at the assertion layer.
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class DamageLogReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public DamageLogReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildEventControlLuaCommand_enable_emits_one()
    {
        DamageLogService.BuildEventControlLuaCommand(true).Should().Be("SWFOC_EventControl(1)");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildEventControlLuaCommand_disable_emits_zero()
    {
        DamageLogService.BuildEventControlLuaCommand(false).Should().Be("SWFOC_EventControl(0)");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildEventControlLuaCommand_emits_a_known_bridge_helper()
    {
        // SWFOC_EventControl is a bridge-side helper (registered by
        // lua_bridge.cpp), not a verified_facts.json entry — those track
        // engine-side bindings only. We instead assert the helper name appears
        // in the structural identifier set the regex extracts.
        var lua = DamageLogService.BuildEventControlLuaCommand(true);
        var idents = VerifiedLedgerLookup.ExtractLuaIdentifiers(lua);
        idents.Should().Contain("SWFOC_EventControl");
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
