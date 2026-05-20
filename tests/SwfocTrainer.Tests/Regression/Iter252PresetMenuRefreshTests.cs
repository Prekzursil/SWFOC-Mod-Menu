using System.IO;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-07 (iter 252) -- pins the Lua Playground preset menu refresh
/// covering iter 224-251 LIVE wires. iter-223 had ~90 entries (covering
/// iter 100-219); iter-252 adds 12 new presets covering the iter 224-251
/// LIVE wires that the iter-223 menu missed:
///   - iter 225: SetFireRateMultiplierGlobal (set + reset)
///   - iter 231: SetCreditsFreezeGlobal/GetCreditsFreezeGlobal/SetCreditsMultiplierGlobal/GetCreditsMultiplierGlobal (5 entries)
///   - iter 237: SetCameraPos + GetCameraPos
///   - iter 243: SetUnitField('invuln_flag') + SetUnitField('prevent_death')
///
/// Iters that don't ship LIVE flips: 224 (RE), 226-228 (sim+UX+verify), 229 (docs),
/// 230 (RE), 232-234 (sim+UX+verify), 235 (docs), 236 (RE), 238-240 (sim+UX+verify),
/// 241 (docs), 242 (RE), 244-246 (sim+UX+verify), 247 (docs), 248-249 (RE+honest defer),
/// 250 (audit), 251 (audit-followup). So the 12 LIVE wires from iter 225/231/237/243
/// are the canonical preset additions for this iter.
///
/// Pattern reference: same source-grep approach as iter-223 (avoids the VM
/// constructor's bridge dependency).
/// </summary>
public sealed class Iter252PresetMenuRefreshTests
{
    private static string ReadPresetMenuSource()
    {
        // Walk up from the test bin dir to the repo root, then to the VM file.
        var asmDir = Path.GetDirectoryName(typeof(Iter252PresetMenuRefreshTests).Assembly.Location)!;
        var dir = new DirectoryInfo(asmDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "SwfocTrainer.App", "V2", "ViewModels", "LuaPlaygroundTabViewModel.cs")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("test must locate the VM source file via grep — iter 223 pattern");
        var path = Path.Combine(dir!.FullName, "src", "SwfocTrainer.App", "V2", "ViewModels", "LuaPlaygroundTabViewModel.cs");
        File.Exists(path).Should().BeTrue($"VM source file missing at {path}");
        return File.ReadAllText(path);
    }

    [Fact]
    public void PresetMenuSourceFile_ContainsIter225FireRatePresets()
    {
        var src = ReadPresetMenuSource();
        src.Should().Contain("[225]", "iter-225 SetFireRateMultiplierGlobal preset bucket");
        src.Should().Contain("SWFOC_SetFireRateMultiplierGlobal",
            "iter-225 LIVE wire SWFOC_* function name must appear in a preset script");
    }

    [Fact]
    public void PresetMenuSourceFile_ContainsIter231CreditsFreezePresets()
    {
        var src = ReadPresetMenuSource();
        src.Should().Contain("[231]", "iter-231 FreezeCredits preset bucket");
        src.Should().Contain("SWFOC_SetCreditsFreezeGlobal",
            "iter-231 freeze setter must appear in a preset");
        src.Should().Contain("SWFOC_GetCreditsFreezeGlobal",
            "iter-231 freeze getter must appear in a preset");
        src.Should().Contain("SWFOC_SetCreditsMultiplierGlobal",
            "iter-231 mult setter must appear in a preset");
        src.Should().Contain("SWFOC_GetCreditsMultiplierGlobal",
            "iter-231 mult getter must appear in a preset");
    }

    [Fact]
    public void PresetMenuSourceFile_ContainsIter237CameraPosPresets()
    {
        var src = ReadPresetMenuSource();
        src.Should().Contain("[237]", "iter-237 SetCameraPos preset bucket");
        src.Should().Contain("SWFOC_SetCameraPos",
            "iter-237 SetCameraPos LIVE wire");
        src.Should().Contain("SWFOC_GetCameraPos",
            "iter-237 GetCameraPos LIVE wire (NEW catalog entry)");
    }

    [Fact]
    public void PresetMenuSourceFile_ContainsIter243SetUnitFieldExtrasPresets()
    {
        var src = ReadPresetMenuSource();
        src.Should().Contain("[243]", "iter-243 SetUnitField extras preset bucket");
        src.Should().Contain("invuln_flag",
            "iter-243 invuln_flag sub-field LIVE branch preset");
        src.Should().Contain("prevent_death",
            "iter-243 prevent_death sub-field LIVE branch preset");
        // Operator-trust documentation: presets must cite the engine-state-aware
        // alternative wires per feedback_flag_flipping_vs_engine_state memory rule.
        src.Should().Contain("MakeInvulnerableLua",
            "iter-243 invuln_flag preset must point operators at iter-110 engine-state-aware alternative");
        src.Should().Contain("SetCannotBeKilledLua",
            "iter-243 prevent_death preset must point operators at iter-153 engine-state-aware alternative");
    }

    [Fact]
    public void GroupBoxHeader_ReflectsIter258Coverage()
    {
        // Walk up to find the XAML.
        // iter-264 update: header bumped from "Iter 100-251" to "Iter 100-258"
        // when iter 257-260 LIVE sub-field flips (max_hull + max_shield)
        // got presets. Iter-264 changelog mirrors iter-183/223/252 cadence.
        // iter-271 update: header bumped from "Iter 100-258 LIVE wires" to
        // "Iter 100-270 LIVE wires (+2 honest-defer notes)" when iter 267-268
        // (max_speed) + iter 269-270 (attack_power) honest-defer informational
        // entries got added. The "(+2 honest-defer notes)" annotation
        // distinguishes them from runnable LIVE wires per the alternative-set
        // pattern (iter-270 NEW). Per iter-260 lesson #3, this test stays
        // tagged "Iter 252" but now pins the iter-271 header to keep the
        // header-history audit trail live without splitting the test file.
        // iter-335 update: header bumped from "Iter 100-270 LIVE wires" to
        // "Iter 100-300 LIVE wires (+2 honest-defer notes)" when iter 282
        // (GetFireRateMultiplierGlobal getter) + iter 285 (Tier 3 overlay
        // bridge wires: GetPlayerKills/GetPlayerDeaths/GetTotalUnitsAlive) +
        // iter 296 (GetPlanets) + iter 299 (GetFactionRoster + GetCurrentMod) +
        // iter 300 (ListMods) presets got added. The "(+2 honest-defer notes)"
        // annotation persists from iter-271 (max_speed + attack_power).
        var asmDir = Path.GetDirectoryName(typeof(Iter252PresetMenuRefreshTests).Assembly.Location)!;
        var dir = new DirectoryInfo(asmDir);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("test must locate MainWindowV2.xaml");
        var xaml = File.ReadAllText(Path.Combine(dir!.FullName, "src", "SwfocTrainer.App", "V2", "MainWindowV2.xaml"));
        // v1.0.2 update: header rewritten from "Iter 100-300 LIVE wires (+2 honest-defer notes)"
        // to "LIVE wire examples (300+)" per improvement_plan_2026-05-20.md Part 1 HIGH #2 —
        // drop iter-N + internal "honest-defer" jargon from operator-visible text.
        xaml.Should().Contain("LIVE wire examples (300+)",
            "v1.0.2 GroupBox header drops iter-N + honest-defer jargon (operator-trust pattern)");
        xaml.Should().NotContain("Iter 100-300 LIVE wires (+2 honest-defer notes)",
            "v1.0.2 removed the iter-300-era header; subsequent edits must not regress this");
    }
}
