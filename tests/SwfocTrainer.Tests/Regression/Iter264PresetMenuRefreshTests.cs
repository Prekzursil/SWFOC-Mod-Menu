using System.IO;
using FluentAssertions;
using Xunit;

namespace SwfocTrainer.Tests.Regression;

/// <summary>
/// 2026-05-07 (iter 264) — pins the Lua Playground preset menu refresh
/// covering iter 257-260 LIVE sub-field flips (max_hull + max_shield).
///
/// Iter-258 promoted SetUnitField max_hull + max_shield from Phase-1 mirror
/// to LIVE TYPE-LEVEL writes (walks GameObj+0x298 → UnitType*; affects every
/// unit instance of the same type for the session). Iter-264 adds 2 presets
/// to the operator-facing dropdown so the LIVE wires are discoverable
/// without grepping docs or remembering SWFOC_* names.
///
/// Pattern parallels iter-183/iter-223/iter-252 preset menu refresh
/// cadence. Pure VM/XAML iter — no bridge changes; uses iter-260 source-grep
/// pin pattern to bypass VM construction (~10x faster than VM-construction
/// based assertions per iter-259 / iter-260 pattern lesson).
/// </summary>
public sealed class Iter264PresetMenuRefreshTests
{
    private static string LoadVmSource()
    {
        var asmDir = Path.GetDirectoryName(typeof(Iter264PresetMenuRefreshTests).Assembly.Location)!;
        var dir = new DirectoryInfo(asmDir);
        while (dir != null && !File.Exists(Path.Combine(
            dir.FullName, "src", "SwfocTrainer.App", "V2", "ViewModels",
            "LuaPlaygroundTabViewModel.cs")))
        {
            dir = dir.Parent;
        }
        dir.Should().NotBeNull("test must locate LuaPlaygroundTabViewModel.cs");
        return File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "SwfocTrainer.App", "V2", "ViewModels",
            "LuaPlaygroundTabViewModel.cs"));
    }

    [Fact]
    public void Preset_MaxHull_IsPresent_WithIter258Tag()
    {
        var src = LoadVmSource();
        src.Should().Contain("[258] Set max_hull",
            "iter-264 added the iter-258 max_hull preset with iter-tag prefix per iter-183/223/252 convention");
        src.Should().Contain("'max_hull'",
            "preset must use canonical snake_case wire format matching iter-258 bridge branch");
    }

    [Fact]
    public void Preset_MaxShield_IsPresent_WithIter258Tag()
    {
        var src = LoadVmSource();
        src.Should().Contain("[258] Set max_shield",
            "iter-264 added the iter-258 max_shield preset with iter-tag prefix");
        src.Should().Contain("'max_shield'",
            "preset must use canonical snake_case wire format matching iter-258 bridge branch");
    }

    [Fact]
    public void Preset_MaxHull_DocumentsTypeLevelCaveat()
    {
        // iter-264 must surface the iter-258 TYPE-LEVEL write semantic in the
        // preset label so operators don't expect per-instance buff/nerf when
        // they pick the preset. This is the operator-trust bridge between
        // the catalog rationale (iter-258) and the dropdown surface
        // (iter-264) — both must agree on the caveat scope.
        var src = LoadVmSource();
        src.Should().Contain("TYPE-LEVEL",
            "iter-258 type-shared semantic must surface in preset label for operator trust");
        src.Should().Contain("affects EVERY unit of this type",
            "preset label must explicitly state the cross-instance propagation scope");
    }

    [Fact]
    public void Preset_MaxShield_DocumentsDualWritePattern()
    {
        // iter-258 max_shield dual-writes UnitType+0xDD0 (front) AND
        // UnitType+0xDD4 (rear). The preset label should hint at the
        // dual-write pattern so operators understand front+rear collapse to
        // one operator-facing field.
        var src = LoadVmSource();
        src.Should().Contain("UnitType+0xDD0",
            "iter-258 max_shield dual-write front offset must surface in preset");
        src.Should().Contain("UnitType+0xDD4",
            "iter-258 max_shield dual-write rear offset must surface in preset");
    }

    [Fact]
    public void Iter258Section_HasComment_DocumentingMemoryRuleAndREProvenance()
    {
        // The iter-258 preset section preamble must cite (a) the iter-256
        // memory rule that prevented an iter-249-style invalidation cycle
        // by requiring semantic verification, and (b) the iter-258 RE walk
        // that semantically verified ObjectTypePtr at +0x298. This is the
        // operator-trust audit trail from the catalog rationale → preset
        // dropdown.
        var src = LoadVmSource();
        src.Should().Contain("Iter 258 — A1.x SetUnitField max_*",
            "iter-264 preset section preamble must mark the iter-258 LIVE-flip provenance");
        src.Should().Contain("iter-256 memory rule",
            "iter-258 preset section must cite iter-256 memory rule (feedback_aob_drift_across_binary_versions)");
        src.Should().Contain("ObjectTypePtr",
            "iter-258 preset section must cite the semantically-verified +0x298 access pattern");
    }
}
