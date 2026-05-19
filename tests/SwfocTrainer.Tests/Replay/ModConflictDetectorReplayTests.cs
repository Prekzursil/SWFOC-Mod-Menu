using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="ModConflictDetectorService"/>.
/// </summary>
/// <remarks>
/// ModConflictDetectorService is the only v5 service that does not call the
/// Lua bridge: it scans XML files on disk to detect duplicate entity
/// definitions across mods. The structural tests therefore exercise the
/// summary builder and the duplicate-detection helper directly. The replay
/// liveness probe is included only to confirm the test class can share the
/// fixture without disturbing other test classes.
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class ModConflictDetectorReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public ModConflictDetectorReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildConflictReportSummary_no_conflicts_returns_clean_message()
    {
        var summary = ModConflictDetectorService.BuildConflictReportSummary(Array.Empty<ModConflictEntry>());
        summary.Should().Be("No conflicts detected.");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildConflictReportSummary_lists_distinct_entity_ids()
    {
        var conflicts = new[]
        {
            new ModConflictEntry("TIE_Fighter", "modA", "modB", "duplicate_entity", "..."),
            new ModConflictEntry("TIE_Fighter", "modA", "modC", "duplicate_entity", "..."),
            new ModConflictEntry("Vengeance_Frigate", "modA", "modB", "duplicate_entity", "..."),
        };

        var summary = ModConflictDetectorService.BuildConflictReportSummary(conflicts);
        summary.Should().Contain("3 conflict(s)");
        summary.Should().Contain("TIE_Fighter");
        summary.Should().Contain("Vengeance_Frigate");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void DetectDuplicateEntities_finds_overlapping_keys()
    {
        var a = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["TIE_Fighter"] = "modA/units.xml" };
        var b = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["TIE_Fighter"] = "modB/units.xml" };
        var conflicts = ModConflictDetectorService.DetectDuplicateEntities(a, b, "modA", "modB");
        conflicts.Should().HaveCount(1);
        conflicts[0].EntityId.Should().Be("TIE_Fighter");
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
