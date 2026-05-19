using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Simulator.E2E;

/// <summary>
/// 2026-05-07 (iter 451): pin tests for the SWFOC_TriggerVictory simulator
/// handler. Mirrors the bridge wrapper's input-validation contract against
/// the 14-of-18 known VictoryType enum names (per rva_victory_type_enum_init
/// @ 0x341FF0). Pinning the contract NOW means iter-450a's MinHook flip
/// can't accidentally regress the validation logic — any rewrite of
/// Lua_TriggerVictory or HandleTriggerVictory that changes the error
/// taxonomy will fail these tests.
/// </summary>
/// <remarks>
/// <para>
/// iter-450a will replace the simulator's PHASE2_PENDING return string with
/// "ok" once the MinHook detour at rva_victory_monitor_counter_inc @
/// 0x341FE0 is enabled and AwaitingVictoryTest injection works. When that
/// happens, the <see cref="ValidType_StagesPendingAndReturnsPhase2Pending"/>
/// assertion will fail (expected) — flip the prefix from "PHASE2_PENDING"
/// to "ok" at that time, and add a new test asserting iter-450a's actual
/// in-game injection semantics.
/// </para>
/// </remarks>
public sealed class Iter451_TriggerVictoryHandlerTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession()
    {
        var state = FakeGameState.NewTacticalSkirmish();
        var sim = new SwfocSimulator(state);
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, connectTimeoutMs: 1500, readTimeoutMs: 1500);
        return (sim, new V2BridgeAdapter(pipe));
    }

    [Fact]
    public async Task NoArg_ReturnsErrNoArg()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_TriggerVictory()", CancellationToken.None);

        round.Response.Should().Contain("ERR_NO_ARG");
        sim.GameState.VictoryTriggerPending.Should().BeFalse(
            "no-arg call must not stage pending state");
        sim.GameState.VictoryTriggerType.Should().BeEmpty();
    }

    [Fact]
    public async Task EmptyString_ReturnsErrBadArg()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_TriggerVictory(\"\")", CancellationToken.None);

        round.Response.Should().Contain("ERR_BAD_ARG");
        sim.GameState.VictoryTriggerPending.Should().BeFalse(
            "empty-string call must not stage pending state");
    }

    [Fact]
    public async Task UnknownType_ReturnsErrUnknownTypeWithLedgerCitation()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_TriggerVictory(\"Not_A_Real_Type\")", CancellationToken.None);

        round.Response.Should().Contain("ERR_UNKNOWN_TYPE");
        round.Response.Should().Contain("rva_victory_type_enum_init",
            "error message must cite the ledger entry so operators can audit the source-of-truth");
        sim.GameState.VictoryTriggerPending.Should().BeFalse(
            "unknown-type call must not stage pending state");
    }

    [Fact]
    public async Task ValidType_StagesPendingAndReturnsPhase2Pending()
    {
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_TriggerVictory(\"Galactic_Conquer\")", CancellationToken.None);

        round.Response.Should().Contain("PHASE2_PENDING",
            "iter-450 scaffolding emits PHASE2_PENDING — flip to \"ok\" when iter-450a ships");
        sim.GameState.VictoryTriggerPending.Should().BeTrue();
        sim.GameState.VictoryTriggerType.Should().Be("Galactic_Conquer");
    }

    [Fact]
    public async Task SubTacticalStory_StagesAcrossEnumPrefixFamily()
    {
        // Validates that the simulator accepts ALL 3 enum prefix families
        // (Galactic_*, Skirmish_*, Sub_Tactical_*) — not just one.
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_TriggerVictory(\"Sub_Tactical_Story\")", CancellationToken.None);

        round.Response.Should().Contain("PHASE2_PENDING");
        sim.GameState.VictoryTriggerType.Should().Be("Sub_Tactical_Story");
    }

    [Fact]
    public async Task SkirmishControl_RoundTripsCorrectly()
    {
        // Skirmish_* family round-trip — completes the 3-family coverage.
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var round = await adapter.SendRawAsync(
            "return SWFOC_TriggerVictory(\"Skirmish_Control\")", CancellationToken.None);

        round.Response.Should().Contain("PHASE2_PENDING");
        round.Response.Should().Contain("iter-450a",
            "PHASE2_PENDING response must reference iter-450a so operators know the resolution iter");
        sim.GameState.VictoryTriggerType.Should().Be("Skirmish_Control");
    }

    [Fact]
    public async Task SecondCall_OverwritesFirstStaging()
    {
        // Documents the staging contract: the most-recent valid call wins.
        // iter-450a's actual injection logic will follow the same semantics
        // (one trigger per pending-flag flip).
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_TriggerVictory(\"Galactic_Conquer\")", CancellationToken.None);
        var round2 = await adapter.SendRawAsync(
            "return SWFOC_TriggerVictory(\"Skirmish_All_Enemies\")", CancellationToken.None);

        round2.Response.Should().Contain("PHASE2_PENDING");
        sim.GameState.VictoryTriggerType.Should().Be("Skirmish_All_Enemies",
            "second valid call overwrites first staged type");
        sim.GameState.VictoryTriggerPending.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidAfterValid_LeavesPriorStagingIntact()
    {
        // Documents the staging contract: an invalid call after a valid one
        // does NOT clear the previously-staged state. iter-450a's injection
        // logic should treat invalid calls as no-ops (per the wrapper's
        // ERR_* return paths, which short-circuit before modifying state).
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_TriggerVictory(\"Galactic_Conquer\")", CancellationToken.None);
        var round2 = await adapter.SendRawAsync(
            "return SWFOC_TriggerVictory(\"Not_A_Real_Type\")", CancellationToken.None);

        round2.Response.Should().Contain("ERR_UNKNOWN_TYPE");
        sim.GameState.VictoryTriggerPending.Should().BeTrue(
            "prior valid staging must survive a subsequent invalid call");
        sim.GameState.VictoryTriggerType.Should().Be("Galactic_Conquer");
    }
}
