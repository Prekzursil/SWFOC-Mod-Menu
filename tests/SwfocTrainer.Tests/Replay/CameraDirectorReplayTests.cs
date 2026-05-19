using FluentAssertions;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="CameraDirectorService"/>.
/// </summary>
/// <remarks>
/// Camera commands resolve to engine helpers like <c>Letter_Box_On()</c>,
/// <c>Game_Set_Speed(0)</c>, etc. None of these are in the replay
/// intercept catalog, so end-to-end execution is stubbed and the structural
/// tests are the primary signal. The replay liveness probe is satisfied via
/// <c>SWFOC_GetVersion</c>.
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class CameraDirectorReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public CameraDirectorReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_freeze_yields_Game_Set_Speed_zero()
    {
        var lua = CameraDirectorService.BuildCameraLuaCommand("freeze", null);
        lua.Should().Be("Game_Set_Speed(0)");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_zoom_uses_default_when_no_parameter()
    {
        var lua = CameraDirectorService.BuildCameraLuaCommand("zoom", null);
        lua.Should().Be("Zoom_Camera(1.0)");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_returns_null_for_unknown_command()
    {
        var lua = CameraDirectorService.BuildCameraLuaCommand("explode", null);
        lua.Should().BeNull();
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
