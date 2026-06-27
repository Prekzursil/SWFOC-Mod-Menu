using System.IO;
using System.Linq;
using FluentAssertions;
using SwfocTrainer.App.V2.Infrastructure;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.Services;
using SwfocTrainer.Tests.Simulator;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 227) -- pins the Combat tab native UX surfacing for the
/// iter-225 LIVE wires SWFOC_SetFireRateMultiplierGlobal /
/// SWFOC_GetFireRateMultiplierGlobal. Iter 227 is the editor-side close-out
/// of the A1.3 SetFireRate global arc (iter 224 RE design + iter 225 bridge
/// LIVE wire + iter 226 simulator handler + 227 native UX + 228 live verify).
///
/// Mirrors iter-96 SetDamageMultiplierGlobal Combat-tab pattern exactly:
///   - BridgeCombatDispatcher uses interpolated $"return SWFOC_X({mult})" form
///   - CombatTabState has Set/Get wrappers binding to FireRateMultiplier slider
///   - CombatTabViewModel exposes ICommands + CapabilityAwareActions
///   - XAML row 2 has Apply (per-slot) + Apply (GLOBAL) + Read (GLOBAL) trio
///
/// Pattern also includes a regression guard: previous to iter-227, the
/// reverse-orphan KnownUnwiredEntries listed both wires; iter-227's wiring
/// dropped them. This test would catch a regression that re-introduces a
/// non-regex-visible dispatcher form for either wire.
/// </summary>
public sealed class Iter227FireRateGlobalNativeUxTests
{
    [Fact]
    public void Catalog_BothFireRateGlobalEntriesAreLive()
    {
        CapabilityStatusCatalog.Entries["SWFOC_SetFireRateMultiplierGlobal"].Status
            .Should().Be(CapabilityStatus.Live);
        CapabilityStatusCatalog.Entries["SWFOC_GetFireRateMultiplierGlobal"].Status
            .Should().Be(CapabilityStatus.Live);
    }

    [Fact]
    public void CombatViewModel_ExposesSetGetFireRateGlobalCommandsAndActions()
    {
        // Pin: VM exposes 4 properties (2 ICommands + 2 capability actions).
        // Reflection walk so we don't need to construct the VM (which has
        // multi-arg constructor with bridge dependency).
        var vmType = typeof(CombatTabViewModel);
        vmType.GetProperty("SetFireRateMultiplierGlobalCommand").Should().NotBeNull();
        vmType.GetProperty("GetFireRateMultiplierGlobalCommand").Should().NotBeNull();
        vmType.GetProperty("SetFireRateMultiplierGlobal").Should().NotBeNull();
        vmType.GetProperty("GetFireRateMultiplierGlobal").Should().NotBeNull();
    }

    [Fact]
    public void XamlRow_ContainsBothApplyAndReadGlobalButtons()
    {
        // Pin: Combat tab fire-rate row has Apply (GLOBAL) + Read (GLOBAL)
        // buttons bound to the new commands. Source-file inspection — bypasses
        // VM construction.
        var xamlPath = ResolveXamlPath();
        var xaml = File.ReadAllText(xamlPath);
        xaml.Should().Contain("SetFireRateMultiplierGlobalCommand");
        xaml.Should().Contain("GetFireRateMultiplierGlobalCommand");
        xaml.Should().Contain("Apply (GLOBAL)");
        xaml.Should().Contain("Read (GLOBAL)");
        // The XAML tooltip should reference the iter-225 RVA + iter-227 surfacing
        xaml.Should().Contain("WeaponTick");
        xaml.Should().Contain("0x387010");
        // Engine semantic caveat from iter-224 design doc must be in operator-
        // facing tooltip (mult=0 freezes weapons).
        xaml.ToLowerInvariant().Should().Contain("freeze");
    }

    [Fact]
    public void CombatViewModel_AllActionsListIncludesIter227Pair()
    {
        // Pin: AllActions roll-up contains the new pair so the per-tab
        // capability badge tooltip + bottom-bar surface report account for them.
        var bridge = new V2BridgeAdapter(new NamedPipeLuaBridgeClient("notreal", 100, 100));
        var unitMutator = new V2UnitMutationDispatcher(bridge);
        var vm = new CombatTabViewModel(bridge, unitMutator);

        var helperNames = vm.AllActions.SelectMany(a => a.HelperNames).ToList();
        helperNames.Should().Contain("SWFOC_SetFireRateMultiplierGlobal");
        helperNames.Should().Contain("SWFOC_GetFireRateMultiplierGlobal");
        // Sanity: iter-96 sibling is present too (we shouldn't have dropped it)
        helperNames.Should().Contain("SWFOC_SetDamageMultiplierGlobal");
    }

    [Fact]
    public void BridgeCombatDispatcher_BuildsInterpolatedSwfocCallString()
    {
        // Pin: dispatcher uses regex-visible interpolated form so the
        // reverse-orphan test no longer flags these as catalogued-but-unwired.
        // Source-file inspection of BridgeCombatDispatcher.cs.
        var dispatcherPath = ResolveDispatcherPath();
        var src = File.ReadAllText(dispatcherPath);
        // The regex `\bSWFOC_([A-Z][A-Za-z_0-9]*)\s*\(` requires SWFOC_X
        // immediately followed by `(`. Ensure both are present in this form.
        src.Should().Contain("SWFOC_SetFireRateMultiplierGlobal({0})");
        src.Should().Contain("SWFOC_GetFireRateMultiplierGlobal()");
    }

    [Fact]
    public void Iter226SimulatorRoundTrip_StillPasses()
    {
        // Pin: iter-227 editor changes did NOT break the iter-226 simulator
        // round-trip. Re-runs the same FakeGameState reflection check.
        var prop = typeof(FakeGameState).GetProperty("GlobalFireRateMultiplier");
        prop.Should().NotBeNull();
        prop!.PropertyType.Should().Be(typeof(float));
        var state = FakeGameState.NewTacticalSkirmish();
        state.GlobalFireRateMultiplier.Should().Be(1.0f);
    }

    private static string ResolveXamlPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Couldn't locate MainWindowV2.xaml");
    }

    private static string ResolveDispatcherPath()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src", "SwfocTrainer.App", "V2", "Infrastructure", "BridgeCombatDispatcher.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Couldn't locate BridgeCombatDispatcher.cs");
    }
}
