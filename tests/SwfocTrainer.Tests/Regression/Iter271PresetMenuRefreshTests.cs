using System.IO;
using FluentAssertions;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// Iter 271 — Lua Playground preset menu refresh covering iter 267-268 + iter 269-270
/// honest-defer arcs. Source-grep pin tests per iter-260 lesson #2 (bypasses VM
/// construction; ~10x faster than instantiating LuaPlaygroundTabViewModel).
///
/// Adds 2 INFORMATIONAL preset entries:
///   1. [267-268] max_speed HONEST DEFER → cite iter-99/100 LIVE alternatives.
///   2. [269-270] attack_power HONEST DEFER → cite iter-96/154/225 alternative-set.
///
/// These entries surface the operator-trust audit trail at the preset-menu source
/// layer. Operators searching for "max_speed" or "attack_power" find the honest-defer
/// note + LIVE alternative cross-references in 1 click instead of grepping docs.
///
/// MainWindowV2.xaml GroupBox header bumped "Iter 100-258" → "Iter 100-270 LIVE wires
/// (+2 honest-defer notes)" to reflect both the preset-list expansion and the
/// honest-defer entries' distinct status from runnable LIVE wires.
/// </summary>
public class Iter271PresetMenuRefreshTests
{
    private static string LoadVmSource()
    {
        // Walk up from test bin/ to repo root, then to LuaPlaygroundTabViewModel.cs.
        // Source-grep pattern bypasses construction so this test runs in ~1 ms.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SwfocTrainer.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull("must find repo root containing SwfocTrainer.sln");
        var path = Path.Combine(
            dir!.FullName,
            "src", "SwfocTrainer.App", "V2", "ViewModels", "LuaPlaygroundTabViewModel.cs");
        File.Exists(path).Should().BeTrue($"LuaPlaygroundTabViewModel.cs must exist at {path}");
        return File.ReadAllText(path);
    }

    private static string LoadXamlSource()
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "SwfocTrainer.sln")))
            dir = dir.Parent;
        dir.Should().NotBeNull("must find repo root");
        var path = Path.Combine(
            dir!.FullName, "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml");
        File.Exists(path).Should().BeTrue($"MainWindowV2.xaml must exist at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void Preset_MaxSpeedHonestDefer_IsPresent_WithIter267To268Tag()
    {
        // Iter 267-268 max_speed HONEST DEFER preset must surface the iter-99/100
        // LIVE alternative cross-references so operators can route correctly.
        var source = LoadVmSource();
        source.Should().Contain("[267-268] max_speed HONEST DEFER",
            "iter-271 introduces the max_speed honest-defer informational preset");
        source.Should().Contain("iter-99 SetUnitSpeed",
            "iter-271 max_speed preset must cite iter-99 per-instance LIVE alternative");
        source.Should().Contain("iter-100 SetPerFactionSpeedMultiplier",
            "iter-271 max_speed preset must cite iter-100 per-faction LIVE alternative");
        source.Should().Contain("SWFOC_SetUnitSpeed(0x12345678, 2.0)",
            "iter-271 max_speed preset must include a runnable example LIVE alternative script");
    }

    [Fact]
    public void Preset_AttackPowerHonestDefer_IsPresent_WithIter269To270Tag()
    {
        // Iter 269-270 attack_power HONEST DEFER preset must surface ALL THREE
        // alternative-set LIVE alternatives (iter-96 + iter-154 + iter-225) so
        // operators can pick by SCOPE (global / per-instance / fire-rate).
        var source = LoadVmSource();
        source.Should().Contain("[269-270] attack_power HONEST DEFER",
            "iter-271 introduces the attack_power honest-defer informational preset");
        source.Should().Contain("alternative-set",
            "iter-271 attack_power preset must label the pattern explicitly for operator-trust");
    }

    [Fact]
    public void Preset_AttackPowerHonestDefer_CitesAllThreeAlternativesByScope()
    {
        // Alternative-set pattern (iter-270 NEW) requires ALL THREE LIVE alternatives
        // to be cited by SCOPE so operators don't miss the right one for their need.
        var source = LoadVmSource();
        source.Should().Contain("iter-96  SWFOC_SetDamageMultiplierGlobal",
            "iter-271 attack_power preset must cite iter-96 GLOBAL outgoing damage scaling");
        source.Should().Contain("iter-154 SWFOC_SetDamageModifierLua",
            "iter-271 attack_power preset must cite iter-154 PER-INSTANCE damage scaling");
        source.Should().Contain("iter-225 SWFOC_SetFireRateMultiplierGlobal",
            "iter-271 attack_power preset must cite iter-225 GLOBAL fire-rate scaling");
    }

    [Fact]
    public void Preset_HonestDeferEntries_ReferenceIter256MemoryRule()
    {
        // The iter-256 memory rule (feedback_aob_drift_across_binary_versions) is
        // what justified the fresh RE walks at iter-267 + iter-269. Both honest-defer
        // entries must reference it so operators see the audit trail.
        var source = LoadVmSource();
        source.Should().Contain("iter-256 memory rule",
            "iter-271 honest-defer entries must reference the iter-256 memory rule that justified the RE walks");
    }

    [Fact]
    public void GroupBoxHeader_ReflectsIter270Coverage()
    {
        // MainWindowV2.xaml GroupBox header bumped "Iter 100-258" → "Iter 100-270"
        // with explicit honest-defer-notes annotation distinguishing them from
        // runnable LIVE wires.
        // iter-335 update: header bumped "Iter 100-270" → "Iter 100-300 LIVE wires
        // (+2 honest-defer notes)" when iter 282/285/296/299/300 presets got added
        // (closes 30-iter doc gap). Per iter-260 lesson #3, this test stays tagged
        // "Iter 271" but now pins the iter-335 header to keep the header-history
        // audit trail live without splitting the test file.
        var xaml = LoadXamlSource();
        xaml.Should().Contain("Iter 100-300 LIVE wires (+2 honest-defer notes)",
            "iter-335 GroupBox header must reflect iter-300 coverage + the 2 honest-defer informational entries (iter-271 era)");
        xaml.Should().NotContain("Iter 100-258 LIVE wires",
            "iter-271 originally removed the iter-258-era header; iter-335 must NOT regress this");
        xaml.Should().NotContain("Iter 100-270 LIVE wires",
            "iter-335 must remove the iter-270-era header (replaced by iter-300 header)");
    }
}
