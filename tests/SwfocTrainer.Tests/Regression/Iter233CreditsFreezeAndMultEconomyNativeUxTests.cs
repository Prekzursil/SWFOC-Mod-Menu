using System.IO;
using System.Linq;
using FluentAssertions;
using SwfocTrainer.App.V2.ViewModels;
using SwfocTrainer.Core.Diagnostics;
using SwfocTrainer.Core.V2Vm;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-06 (iter 233) -- pins the Economy tab native UX surfacing for the
/// iter-231 LIVE wires SWFOC_Set/GetCreditsFreezeGlobal +
/// SWFOC_Set/GetCreditsMultiplierGlobal. Mirrors iter-227 Combat tab pattern.
///
/// 4 buttons surfaced as a NEW "GLOBAL economy controls (LIVE)" GroupBox:
///   - Freeze on / Freeze off (hardcoded-bool pair, iter-204 lineage 8 iters deep)
///   - Apply (GLOBAL) / Read (GLOBAL) (mult control, mirrors iter-227's pattern)
///
/// BridgeEconomyDispatcher uses interpolated $"return SWFOC_X(...)" form —
/// regex-visible. Drops the 4 iter-231 entries from reverse-orphan snapshot.
/// </summary>
public sealed class Iter233CreditsFreezeAndMultEconomyNativeUxTests
{
    [Fact]
    public void Catalog_AllFourCreditsGlobalEntriesStillLive()
    {
        // Pin: iter-231 catalog state preserved through iter-233 UX surfacing.
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
    public void EconomyViewModel_ExposesIter233CommandsAndActions()
    {
        // Pin: VM exposes 4 ICommands + 3 capability actions (freeze pair fans
        // out to a single CapabilityAwareAction with both Set/Get HelperNames).
        var vmType = typeof(EconomyTabViewModel);
        vmType.GetProperty("SetCreditsFreezeOnCommand").Should().NotBeNull();
        vmType.GetProperty("SetCreditsFreezeOffCommand").Should().NotBeNull();
        vmType.GetProperty("SetCreditsMultiplierGlobalCommand").Should().NotBeNull();
        vmType.GetProperty("GetCreditsMultiplierGlobalCommand").Should().NotBeNull();

        vmType.GetProperty("SetCreditsFreezeGlobal").Should().NotBeNull();
        vmType.GetProperty("SetCreditsMultiplierGlobal").Should().NotBeNull();
        vmType.GetProperty("GetCreditsMultiplierGlobal").Should().NotBeNull();

        vmType.GetProperty("GlobalCreditsMultiplier").Should().NotBeNull();
    }

    [Fact]
    public void XamlRow_ContainsFreezePairAndMultPairButtons()
    {
        // Pin: Economy tab "GLOBAL economy controls" GroupBox has all 4 buttons
        // bound to the new commands + tooltip references the iter-230 RE doc
        // landmarks (AddCredits @ 0x27F370, freeze precedence, clamp range).
        var xaml = File.ReadAllText(ResolveXamlPath());
        xaml.Should().Contain("SetCreditsFreezeOnCommand");
        xaml.Should().Contain("SetCreditsFreezeOffCommand");
        xaml.Should().Contain("SetCreditsMultiplierGlobalCommand");
        xaml.Should().Contain("GetCreditsMultiplierGlobalCommand");
        xaml.Should().Contain("GLOBAL economy controls");
        xaml.Should().Contain("Freeze on");
        xaml.Should().Contain("Freeze off");
        // Tooltip references for traceability (iter-230 design doc landmarks).
        xaml.Should().Contain("AddCredits");
        xaml.Should().Contain("0x27F370");
        xaml.ToLowerInvariant().Should().Contain("clamp");
    }

    [Fact]
    public void EconomyViewModel_AllActionsListIncludesIter233Triple()
    {
        // Pin: AllActions roll-up extended (10 → 13). Catches stale-count drift
        // per iter-208/iter-227 lessons.
        var vm = new EconomyTabViewModel(new RecordingDispatcher());
        vm.AllActions.Should().HaveCount(13);

        var helpers = vm.AllActions.SelectMany(a => a.HelperNames).ToList();
        helpers.Should().Contain("SWFOC_SetCreditsFreezeGlobal");
        helpers.Should().Contain("SWFOC_GetCreditsFreezeGlobal");
        helpers.Should().Contain("SWFOC_SetCreditsMultiplierGlobal");
        helpers.Should().Contain("SWFOC_GetCreditsMultiplierGlobal");
    }

    [Fact]
    public void BridgeEconomyDispatcher_BuildsRegexVisibleInterpolatedForm()
    {
        // Pin: dispatcher uses regex-visible interpolated form so the
        // reverse-orphan test no longer flags these as catalogued-but-unwired.
        var dispatcherPath = ResolveDispatcherPath();
        var src = File.ReadAllText(dispatcherPath);
        src.Should().Contain("SWFOC_SetCreditsFreezeGlobal({0})");
        src.Should().Contain("SWFOC_GetCreditsFreezeGlobal()");
        src.Should().Contain("SWFOC_SetCreditsMultiplierGlobal({0})");
        src.Should().Contain("SWFOC_GetCreditsMultiplierGlobal()");
    }

    [Fact]
    public void Iter231CatalogRationaleStillReferencesAddCreditsAndDesignDoc()
    {
        // Pin: iter-231 rationale notes preserved through iter-233 surfacing.
        // Catches accidental rationale-text edits that would lose iter-230
        // RE design doc traceability.
        var setFreeze = CapabilityStatusCatalog.Entries["SWFOC_SetCreditsFreezeGlobal"].Note;
        setFreeze.Should().Contain("Iter 231 LIVE");
        setFreeze.Should().Contain("AddCredits");
        setFreeze.Should().Contain("0x27F370");
        setFreeze.Should().Contain("iter230_freeze_credits_re_kickoff.md");

        var setMult = CapabilityStatusCatalog.Entries["SWFOC_SetCreditsMultiplierGlobal"].Note;
        setMult.Should().Contain("Iter 231 LIVE");
        setMult.Should().Contain("AddCredits");
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
            var candidate = Path.Combine(dir, "src", "SwfocTrainer.App", "V2", "Infrastructure", "BridgeEconomyDispatcher.cs");
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new FileNotFoundException("Couldn't locate BridgeEconomyDispatcher.cs");
    }

    /// <summary>Minimal recording dispatcher for VM construction (no bridge calls).</summary>
    private sealed class RecordingDispatcher : IEconomyDispatcher
    {
        public Task<bool> SetCreditsAsync(int slot, double amount, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetTechAsync(int slot, int level, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> DrainEnemyCreditsAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> UncapCreditsAsync(CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetIncomeMultiplierAsync(int slot, float mult, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetBuildSpeedAsync(int slot, float mult, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetBuildCostAsync(int slot, float mult, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetFreezeCreditsAsync(int slot, bool enable, double target, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetInstantBuildAsync(bool enable, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> SetFreeBuildAsync(bool enable, CancellationToken ct) => Task.FromResult(true);
    }
}
