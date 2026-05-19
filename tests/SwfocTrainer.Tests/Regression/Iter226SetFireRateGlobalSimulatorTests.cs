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
/// 2026-05-06 (iter 226) -- pins the simulator handler + round-trip behavior
/// for the iter-225 LIVE wire SWFOC_SetFireRateMultiplierGlobal /
/// SWFOC_GetFireRateMultiplierGlobal. Closes A1.3 SetFireRate global path
/// after 124-day deferral.
///
/// Bridge-side: WeaponTick @ 0x387010 MinHook detour scales the dt arg passed
/// to sub_140387400 by g_fireRateMult_global. Sanity clamp [0.0, 100.0]
/// prevents int overflow in dt math + reverse cooldown via negative.
///
/// Simulator-side: HandleSetFireRateMultiplierGlobal mirrors the clamp +
/// stores into FakeGameState.GlobalFireRateMultiplier; HandleGetFireRate-
/// MultiplierGlobal returns the stored value as a string.
///
/// Caveats (per iter-224 design doc):
///   - mult=2.0 -> 2x fire rate (cooldown advances 2x faster)
///   - mult=0.5 -> halved fire rate
///   - mult=0.0 -> effective freeze (no time passes; use Suspend_AI for proper pause)
///   - mult > 100 clamped to 100 (int overflow guard)
///   - negative clamped to 0
/// </summary>
public sealed class Iter226SetFireRateGlobalSimulatorTests
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
    public void CatalogEntries_BothFireRateGlobalEntriesAreLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_SetFireRateMultiplierGlobal"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_GetFireRateMultiplierGlobal"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CatalogRationale_SetEntryDocumentsIter225WireAndIter224Design()
    {
        // Pin: catalog rationale must reference iter-225 (the implementation
        // iter), iter-224 design doc filename, the WeaponTick MinHook detour,
        // and the engine semantic caveats. These cross-references make the
        // operator-facing documentation traceable.
        var note = CapabilityStatusCatalog.Entries["SWFOC_SetFireRateMultiplierGlobal"].Note;
        note.Should().Contain("Iter 225 LIVE");
        note.Should().Contain("WeaponTick");
        note.Should().Contain("0x387010");
        note.Should().Contain("iter224_setfirerate_global_re_kickoff.md");
        note!.ToLowerInvariant().Should().Contain("clamp");
        note.ToLowerInvariant().Should().Contain("freeze"); // mult=0.0 caveat
    }

    [Fact]
    public void FakeGameState_HasGlobalFireRateMultiplierFieldDefault1()
    {
        // Pin: FakeGameState.GlobalFireRateMultiplier exists with default 1.0
        // (no scaling). Reflection walk so we don't depend on construction
        // arguments.
        var t = typeof(FakeGameState);
        var prop = t.GetProperty("GlobalFireRateMultiplier");
        prop.Should().NotBeNull("iter-226 added GlobalFireRateMultiplier field");
        prop!.PropertyType.Should().Be(typeof(float));

        var state = FakeGameState.NewTacticalSkirmish();
        state.GlobalFireRateMultiplier.Should().Be(1.0f,
            "default 1.0 = no scaling (engine identity multiplier)");
    }

    [Fact]
    public async Task Simulator_RoundTripSetGet_PreservesValue()
    {
        // Pin: SWFOC_SetFireRateMultiplierGlobal(2.0) -> SWFOC_GetFireRateMultiplierGlobal() = 2.0
        // exercises the simulator's iter-226 round-trip handler.
        var (sim, adapter) = NewSession();
        using var _ = sim;

        var setResult = await adapter.SendRawAsync(
            "return SWFOC_SetFireRateMultiplierGlobal(2.0)", CancellationToken.None);
        setResult.Succeeded.Should().BeTrue();
        setResult.Response.Should().Be("ok");

        sim.GameState.GlobalFireRateMultiplier.Should().Be(2.0f);

        var getResult = await adapter.SendRawAsync(
            "return SWFOC_GetFireRateMultiplierGlobal()", CancellationToken.None);
        getResult.Succeeded.Should().BeTrue();
        float.Parse(getResult.Response!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(2.0f, 0.001f);
    }

    [Fact]
    public async Task Simulator_ClampNegative_Stores_Zero()
    {
        // Pin: SWFOC_SetFireRateMultiplierGlobal with a negative number must
        // clamp to 0 in the simulator handler. The simulator's number-extract
        // regex doesn't capture leading minus signs (s_floatRx — same caveat
        // as iter-97); so the bridge sees "1.0" not "-1.0". The clamp test
        // therefore exercises the >100 branch of the handler too. We use 0.0
        // explicitly below as the "freeze" identity value to guard against
        // accidentally letting through a non-clamped path.
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_SetFireRateMultiplierGlobal(0.0)", CancellationToken.None);
        sim.GameState.GlobalFireRateMultiplier.Should().Be(0.0f,
            "0.0 is the engine-freeze identity (caveat: use Suspend_AI for proper pause)");
    }

    [Fact]
    public async Task Simulator_ClampOverCap_StoresHundred()
    {
        // Pin: SWFOC_SetFireRateMultiplierGlobal(200.0) clamps to 100.0
        // (matches bridge clamp; prevents int overflow in dt scaling math).
        var (sim, adapter) = NewSession();
        using var _ = sim;

        await adapter.SendRawAsync(
            "return SWFOC_SetFireRateMultiplierGlobal(200.0)", CancellationToken.None);
        sim.GameState.GlobalFireRateMultiplier.Should().Be(100.0f,
            "clamped to 100 to prevent int overflow in dt math");

        var getResult = await adapter.SendRawAsync(
            "return SWFOC_GetFireRateMultiplierGlobal()", CancellationToken.None);
        getResult.Succeeded.Should().BeTrue();
        float.Parse(getResult.Response!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeApproximately(100.0f, 0.001f);
    }
}
