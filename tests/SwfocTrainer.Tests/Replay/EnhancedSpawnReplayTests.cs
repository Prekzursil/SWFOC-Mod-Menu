using FluentAssertions;
using SwfocTrainer.Core.Models;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Core.Verification;
using Xunit;

namespace SwfocTrainer.Tests.Replay;

/// <summary>
/// Phase 9 replay coverage for <see cref="EnhancedSpawnService"/>.
/// </summary>
/// <remarks>
/// Spawn commands compose <c>Find_Player</c>, <c>Find_Object_Type</c>, and
/// <c>Spawn_Unit</c>/<c>Reinforce_Unit</c>/<c>Galactic_Spawn_Unit</c>. None of
/// these are in the replay intercept catalog, so the bridge round-trip is
/// stubbed and we use a deterministic <c>SWFOC_ReplayObjectCount("TIE_Fighter")</c>
/// probe to confirm the replay is loading the fixture's object catalog.
/// </remarks>
[Collection(ReplayHarnessCollection.Name)]
public sealed class EnhancedSpawnReplayTests
{
    private readonly ReplayHarnessFixture _fixture;

    public EnhancedSpawnReplayTests(ReplayHarnessFixture fixture) => _fixture = fixture;

    private static EnhancedSpawnRequest MakeRequest(string unitId, string faction, SpawnMode mode, string? planet = null) =>
        new(
            UnitId: unitId,
            TargetFaction: faction,
            Mode: mode,
            Quantity: 1,
            PositionKind: SpawnPositionKind.AtCamera,
            TargetPlanet: planet,
            AllowCrossFaction: false,
            StopOnFailure: false);

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_tactical_uses_Spawn_Unit()
    {
        var req = MakeRequest("TIE_Fighter", "EMPIRE", SpawnMode.Tactical);
        var lua = EnhancedSpawnService.BuildSpawnLuaCommand(req);
        lua.Should().Contain("Spawn_Unit");
        lua.Should().Contain("Find_Player(\"EMPIRE\")");
        lua.Should().Contain("Find_Object_Type(\"TIE_Fighter\")");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_reinforcement_uses_Reinforce_Unit()
    {
        var req = MakeRequest("X_Wing", "REBEL", SpawnMode.Reinforcement);
        var lua = EnhancedSpawnService.BuildSpawnLuaCommand(req);
        lua.Should().Contain("Reinforce_Unit");
        lua.Should().NotContain("Spawn_Unit(");
    }

    [Fact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Structural")]
    public void BuildLuaCommand_galactic_persistent_uses_default_planet_when_missing()
    {
        var req = MakeRequest("Star_Destroyer", "EMPIRE", SpawnMode.GalacticPersistent);
        var lua = EnhancedSpawnService.BuildSpawnLuaCommand(req);
        lua.Should().Contain("Galactic_Spawn_Unit");
        lua.Should().Contain("CORUSCANT");
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
        var req = MakeRequest("TIE_Fighter", "EMPIRE", SpawnMode.Tactical);
        var lua = EnhancedSpawnService.BuildSpawnLuaCommand(req);
        var idents = VerifiedLedgerLookup.ExtractLuaIdentifiers(lua);
        idents.Should().Contain(new[] { "Find_Player", "Find_Object_Type", "Spawn_Unit", "Create_Position" });
        VerifiedLedgerLookup.ContainsFunction("Find_Player").Should().BeTrue();
        VerifiedLedgerLookup.ContainsFunction("Find_Object_Type").Should().BeTrue();
    }

    [SkippableFact]
    [Trait("Category", "Replay")]
    [Trait("Replay", "Stubbed")]
    public async Task ReplayBridge_returns_known_TIE_Fighter_count()
    {
        if (!_fixture.ReplayBinaryAvailable)
        {
            throw new SkipException("swfoc_replay.exe not available; replay tests skipped");
        }

        var runner = new BridgeAssertionRunner(_fixture.Bridge);
        var assertion = new BridgeAssertion
        {
            PreStateProbe = "return SWFOC_ReplayObjectCount(\"TIE_Fighter\")",
            LuaCommand = "return SWFOC_ReplayObjectCount(\"TIE_Fighter\")",
            PostStateProbe = "return SWFOC_ReplayObjectCount(\"TIE_Fighter\")",
            ExpectDelta = (pre, post) =>
                pre == post && pre == ReplayHarnessFixture.FixtureTieFighterCount.ToString(),
            Description = "fixture bakes 12 TIE_Fighter instances into the object catalog",
        };

        var result = await runner.RunAsync(assertion, CancellationToken.None);
        result.Passed.Should().BeTrue(
            because: $"FailureReason='{result.FailureReason}', stderr='{_fixture.GetStderrSnapshot()}'");
    }
}
