using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 232) -- pins the simulator handlers + round-trip behavior
/// for the iter-231 LIVE wires SWFOC_Set/GetCreditsFreezeGlobal +
/// SWFOC_Set/GetCreditsMultiplierGlobal. Closes A1.x FreezeCredits arc at
/// simulator level (iter 233 adds Economy tab UX, iter 234 verifies live).
///
/// Bridge-side: AddCredits @ 0x27F370 MinHook detour. Bool freeze precedence
/// (short-circuits AddCredits, returns unchanged balance). Mult fast-path
/// at 1.0 (zero overhead). Sanity clamp [0.0, 100.0] for mult.
///
/// Simulator-side: 4 handlers mirror bridge clamp + bool semantics. Stores
/// into FakeGameState.GlobalCreditsFreeze + .GlobalCreditsMultiplier.
///
/// Pattern parallels iter-226 SetFireRate + iter-97 SetDamageMultiplierGlobal.
/// </summary>
public sealed class Iter232CreditsFreezeAndMultGlobalSimulatorTests
{
    private static (SwfocSimulator sim, V2BridgeAdapter adapter) NewSession()
    {
        var sim = new SwfocSimulator(FakeGameState.NewTacticalSkirmish());
        sim.Start();
        var pipe = new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500);
        var adapter = new V2BridgeAdapter(pipe);
        return (sim, adapter);
    }

    [Fact]
    public void CatalogEntries_AllFourCreditsGlobalEntriesAreLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_SetCreditsFreezeGlobal"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_GetCreditsFreezeGlobal"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_SetCreditsMultiplierGlobal"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_GetCreditsMultiplierGlobal"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_DocumentsIter231WireAndIter230Design()
    {
        // Pin: catalog rationale must reference iter-231 (the implementation
        // iter), iter-230 RE design doc filename, AddCredits @ 0x27F370,
        // freeze precedence, and the engine semantic caveats. These cross-
        // references make the operator-facing documentation traceable.
        var setFreeze = CapabilityStatusCatalog.Entries["SWFOC_SetCreditsFreezeGlobal"].Note;
        setFreeze.Should().Contain("Iter 231 LIVE");
        setFreeze.Should().Contain("AddCredits");
        setFreeze.Should().Contain("0x27F370");
        setFreeze.Should().Contain("iter230_freeze_credits_re_kickoff.md");
        setFreeze!.ToLowerInvariant().Should().Contain("short-circuit");
        // Engine semantic caveats: AI subsidies blocked, cap +0x74 still applies, analytics suppressed.
        setFreeze.ToLowerInvariant().Should().Contain("ai subsidies");
        setFreeze.Should().Contain("0x74");

        var setMult = CapabilityStatusCatalog.Entries["SWFOC_SetCreditsMultiplierGlobal"].Note;
        setMult.Should().Contain("Iter 231 LIVE");
        setMult.Should().Contain("AddCredits");
        setMult.Should().Contain("0x27F370");
        setMult!.ToLowerInvariant().Should().Contain("clamp");
    }

    [Fact]
    public void FakeGameState_HasFreezeAndMultFieldsWithCorrectDefaults()
    {
        // Pin: FakeGameState.GlobalCreditsFreeze + .GlobalCreditsMultiplier
        // exist with defaults bool=false / float=1.0 (engine identity, no scaling).
        // Reflection walk so we don't depend on construction args.
        var t = typeof(FakeGameState);

        var freezeProp = t.GetProperty("GlobalCreditsFreeze");
        freezeProp.Should().NotBeNull("iter-232 added GlobalCreditsFreeze field");
        freezeProp!.PropertyType.Should().Be(typeof(bool));

        var multProp = t.GetProperty("GlobalCreditsMultiplier");
        multProp.Should().NotBeNull("iter-232 added GlobalCreditsMultiplier field");
        multProp!.PropertyType.Should().Be(typeof(float));

        var state = FakeGameState.NewTacticalSkirmish();
        state.GlobalCreditsFreeze.Should().BeFalse("default false = no short-circuit");
        state.GlobalCreditsMultiplier.Should().Be(1.0f, "default 1.0 = no scaling");
    }

    [Fact]
    public async Task Simulator_FreezeRoundTrip_PreservesBoolValue()
    {
        // Pin: SWFOC_SetCreditsFreezeGlobal(1) → SWFOC_GetCreditsFreezeGlobal() = 1
        // and toggle back to 0.
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var setOn = await adapter.SendRawAsync(
            "return SWFOC_SetCreditsFreezeGlobal(1)", CancellationToken.None);
        setOn.Succeeded.Should().BeTrue();
        setOn.Response.Should().Be("ok");
        sim.GameState.GlobalCreditsFreeze.Should().BeTrue();

        var getOn = await adapter.SendRawAsync(
            "return SWFOC_GetCreditsFreezeGlobal()", CancellationToken.None);
        getOn.Response.Should().Be("1");

        var setOff = await adapter.SendRawAsync(
            "return SWFOC_SetCreditsFreezeGlobal(0)", CancellationToken.None);
        setOff.Response.Should().Be("ok");
        sim.GameState.GlobalCreditsFreeze.Should().BeFalse();

        var getOff = await adapter.SendRawAsync(
            "return SWFOC_GetCreditsFreezeGlobal()", CancellationToken.None);
        getOff.Response.Should().Be("0");
    }

    [Fact]
    public async Task Simulator_MultRoundTrip_PreservesValue()
    {
        // Pin: SWFOC_SetCreditsMultiplierGlobal(2.0) → Get = 2.0
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_SetCreditsMultiplierGlobal(2.0)", CancellationToken.None);
        sim.GameState.GlobalCreditsMultiplier.Should().Be(2.0f);

        var getResult = await adapter.SendRawAsync(
            "return SWFOC_GetCreditsMultiplierGlobal()", CancellationToken.None);
        float.Parse(getResult.Response!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(2.0f, 0.001f);
    }

    [Fact]
    public async Task Simulator_MultClampLowerBound_StoresZero()
    {
        // Pin: SWFOC_SetCreditsMultiplierGlobal(0.0) stores 0.0 (soft freeze
        // identity — distinct from hard freeze in that AddCredits IS still
        // called with 0 delta, so events fire normally).
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_SetCreditsMultiplierGlobal(0.0)", CancellationToken.None);
        sim.GameState.GlobalCreditsMultiplier.Should().Be(0.0f,
            "0.0 is the soft-freeze identity (vs hard freeze via SetCreditsFreezeGlobal)");
    }

    [Fact]
    public async Task Simulator_MultClampOverCap_StoresHundred()
    {
        // Pin: SWFOC_SetCreditsMultiplierGlobal(200.0) clamps to 100.0
        // (matches bridge clamp; prevents overflow).
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_SetCreditsMultiplierGlobal(200.0)", CancellationToken.None);
        sim.GameState.GlobalCreditsMultiplier.Should().Be(100.0f,
            "clamped to 100 to prevent overflow");

        var getResult = await adapter.SendRawAsync(
            "return SWFOC_GetCreditsMultiplierGlobal()", CancellationToken.None);
        float.Parse(getResult.Response!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(100.0f, 0.001f);
    }

    [Fact]
    public async Task Simulator_FreezePrecedence_CoexistsWithMult()
    {
        // Pin: freeze=true and mult=2.0 are independent state; both round-trip
        // their own getters correctly. Freeze precedence is enforced bridge-side
        // at AddCredits hook time (Hook_AddCredits returns early on freeze=true);
        // simulator stores both values so editor-tests can verify the bridge's
        // detour logic by inspecting the stored values directly. This pin
        // catches any regression where setting one global accidentally clobbers
        // the other.
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_SetCreditsFreezeGlobal(1)", CancellationToken.None);
        await adapter.SendRawAsync(
            "return SWFOC_SetCreditsMultiplierGlobal(2.0)", CancellationToken.None);

        sim.GameState.GlobalCreditsFreeze.Should().BeTrue("freeze stored");
        sim.GameState.GlobalCreditsMultiplier.Should().Be(2.0f, "mult stored independently");

        // Both getters return their own state — no cross-contamination.
        var getFreeze = await adapter.SendRawAsync(
            "return SWFOC_GetCreditsFreezeGlobal()", CancellationToken.None);
        getFreeze.Response.Should().Be("1");

        var getMult = await adapter.SendRawAsync(
            "return SWFOC_GetCreditsMultiplierGlobal()", CancellationToken.None);
        float.Parse(getMult.Response!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(2.0f, 0.001f);
    }

    [Fact]
    public void CatalogPairs_FreezeAndMultBothHaveGetSibling()
    {
        // Pin: every Set wire has a corresponding Get pair-flip (mirrors
        // iter-225 SetFireRateMultiplier pattern). Catches regressions where
        // a Get sibling is accidentally removed.
        CapabilityStatusCatalog.Entries.Should().ContainKey("SWFOC_GetCreditsFreezeGlobal",
            "Set/Get pair-flip required by iter-231 design");
        CapabilityStatusCatalog.Entries.Should().ContainKey("SWFOC_GetCreditsMultiplierGlobal",
            "Set/Get pair-flip required by iter-231 design");
    }
}
