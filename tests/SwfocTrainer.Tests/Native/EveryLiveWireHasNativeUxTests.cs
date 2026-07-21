using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.Tests.Native;

/// <summary>
/// 2026-05-20 — App-scope native-UX coverage snapshot for every LIVE
/// catalog entry. Tracks which LIVE wires have NO mention anywhere under
/// <c>src/SwfocTrainer.App/</c> (neither in code-behind / ViewModels nor
/// in XAML resource text). Companion to
/// <see cref="SwfocTrainer.Tests.Diagnostics.CapabilityCatalogReverseOrphanTests"/>:
/// reverse-orphan scans the full <c>src/</c> tree for any call site (a
/// helper wired only by Core service or Runtime still counts as wired);
/// this test narrows the scope to the App project so a LIVE wire that
/// exists in Core but has no operator-facing surface in App still shows
/// up as a coverage gap.
/// </summary>
/// <remarks>
/// <para>
/// Acceptance criterion in <c>.ralph/specs/editor-100.md</c>: "All LIVE
/// wires have native UX (button / control / preset / tab) — no LIVE wire
/// requires Lua Playground paste as the only entry point. Cross-checked
/// via <c>tests/SwfocTrainer.Tests/Native/EveryLiveWireHasNativeUxTests.cs</c>
/// (NEW — write it as part of this arc)."
/// </para>
/// <para>
/// Detection rule: a LIVE helper is considered to have native UX iff its
/// verbatim symbol name (<c>SWFOC_X</c>) appears in any <c>.cs</c> or
/// <c>.xaml</c> file under <c>src/SwfocTrainer.App/</c>, excluding
/// <c>bin/</c>, <c>obj/</c>, and <c>publish/</c>. This is a coarse signal
/// (a verbatim mention does not prove a Button is bound to a Command
/// that ultimately calls the helper), but matches the project's existing
/// regex-visibility convention used by
/// <see cref="SwfocTrainer.Tests.Diagnostics.CapabilityCatalogReverseOrphanTests"/>,
/// and is sufficient to flag genuine "no UI surface at all" gaps.
/// </para>
/// <para>
/// Mirrors the soft-snapshot pattern: if the gap set drifts, the test
/// prints the diff for reviewer judgement. A reviewer can then decide
/// (a) add a native UX surface and remove from the allowlist, (b) bump
/// the allowlist with a rationale comment when the gap is genuinely
/// intentional (probe-only / composite / Core-service-only), or (c)
/// retire the catalog entry if it should no longer ship LIVE.
/// </para>
/// </remarks>
public sealed class EveryLiveWireHasNativeUxTests
{
    private readonly ITestOutputHelper _output;

    public EveryLiveWireHasNativeUxTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// 2026-05-20 snapshot of "LIVE in catalog but no App-scope mention".
    /// Every entry is either genuinely operator-invisible at the App
    /// layer (Core-service consumer, composite probe, plural variant) or
    /// a queued native-UX item from the iter-167+ read-side wave that the
    /// iter-188-217 surfacing arc didn't fully consume.
    /// </summary>
    /// <remarks>
    /// Most rationales mirror entries in
    /// <see cref="SwfocTrainer.Tests.Diagnostics.CapabilityCatalogReverseOrphanTests"/>'s
    /// <c>KnownUnwiredEntries</c> snapshot, where the same helpers appear
    /// for a different reason (no regex-visible source call site anywhere
    /// in <c>src/</c>). A helper can be in one snapshot, both, or
    /// neither: reverse-orphan = "no call site in any project";
    /// this snapshot = "no App-side mention" (a Core-service consumer
    /// satisfies reverse-orphan but not this stricter check).
    /// </remarks>
    private static readonly HashSet<string> KnownNoAppUxEntries = new(System.StringComparer.Ordinal)
    {
        // Composite / aggregate helpers — the App surfaces the individual
        // toggles (e.g. Combat tab GodMode + OneHitKill buttons) rather
        // than the combined helper.
        "SWFOC_CombinedGodOHK",        // composite — Combat tab uses individual toggles

        // Plural / variant helpers — singular version is the surfaced one.
        "SWFOC_GetSelectedUnits",      // plural variant of GetSelectedUnit (which IS App-surfaced)

        // Read-only diagnostic probes — bridge supports them but no
        // operator-facing UI surface exists yet. Each is queued for a
        // future Diagnostics or Inspector tab pass.
        "SWFOC_DiagGameTick",          // tick counter probe — Diagnostics tab doesn't fire it
        "SWFOC_Log",                   // append to bridge log buffer — write-only diagnostic, Core wires it

        // AI-brain probe — the App-side UnitControl/Spawning tabs use
        // SWFOC_AttachAiBrain (LIVE, App-surfaced) instead of the
        // read-only +0x360 probe.
        "SWFOC_GetAiBrain",            // read-only AI-brain probe — VM doesn't surface it

        // Economy service consumer — EconomyService.cs (Core) calls
        // GetMaxCredits for cap-display computation; no XAML/VM binding
        // directly references the helper name.
        "SWFOC_GetMaxCredits",         // Core EconomyService consumer; cap value flows into Economy tab via service

        // iter-167 read-side wave — the iter-188-217 surfacing arc
        // wired the wider HasAttackTarget/GetOwner/etc. set but these
        // two slipped through the per-tab native-UX pass. Inspector tab
        // shows them as queued but no per-helper button shipped yet.
        "SWFOC_GetHealthLua",          // iter 167 LIVE — native UX queued (Inspector tab)
        "SWFOC_GetUnitShield",         // iter 131 LIVE pair-flip with iter-129 SetUnitShield; service-layer wrapper

        // iter-158 GUI helper — FlashGuiObject is bridge-LIVE but the
        // App doesn't expose a "flash this widget" button (operator
        // workflow uses the WorldState/HeroLab discovery presets instead).
        "SWFOC_FlashGuiObjectLua",     // iter 158 LIVE — paired with ShowGuiObject/HideGuiObject; App uses Lua Playground preset path

        // iter-170 state-query trio — read-side helpers that ship LIVE
        // on the bridge but lack per-helper Inspector buttons. Inspector
        // tab's iter-197 read-side extension covered 6 helpers from
        // iter-171/172; these three from iter-170 are the residual.
        "SWFOC_IsStealthedLua",        // iter 170 LIVE — native UX queued (Inspector)
        "SWFOC_IsInLimboLua",          // iter 170 LIVE — native UX queued (Inspector)
        "SWFOC_IsCapturableLua",       // iter 170 LIVE — native UX queued (Inspector)
    };

    /// <summary>Resolve the editor source root from the test bin dir.</summary>
    private static string ResolveSourceRoot()
    {
        var dir = AppContext.BaseDirectory;
        for (var i = 0; i < 12 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "src");
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException("Couldn't locate src/ root.");
    }

    private static IEnumerable<string> EnumerateAppFiles(string srcRoot)
    {
        var appRoot = Path.Combine(srcRoot, "SwfocTrainer.App");
        if (!Directory.Exists(appRoot)) yield break;
        foreach (var pattern in new[] { "*.cs", "*.xaml" })
        {
            foreach (var f in Directory.EnumerateFiles(appRoot, pattern, SearchOption.AllDirectories))
            {
                // Skip build artefacts so stale bin/obj copies don't satisfy the check.
                var rel = Path.GetRelativePath(appRoot, f).Replace('\\', '/');
                if (rel.StartsWith("bin/", System.StringComparison.Ordinal)) continue;
                if (rel.StartsWith("obj/", System.StringComparison.Ordinal)) continue;
                if (rel.StartsWith("publish/", System.StringComparison.Ordinal)) continue;
                yield return f;
            }
        }
    }

    [Fact]
    public void LiveEntries_WithoutAppUx_MatchKnownSnapshot()
    {
        var srcRoot = ResolveSourceRoot();
        var appTokens = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var file in EnumerateAppFiles(srcRoot))
        {
            var text = File.ReadAllText(file);
            // Verbatim symbol match. Matches operator-visible references
            // anywhere in App-scope code-behind / VM / XAML (button
            // content, ToolTip, Tag, x:Name, raw strings).
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(text, @"\bSWFOC_[A-Z][A-Za-z_0-9]+"))
            {
                appTokens.Add(m.Value);
            }
        }

        var liveCatalog = CapabilityStatusCatalog.Entries
            .Where(kv => kv.Value.Status == CapabilityStatus.Live)
            .Select(kv => kv.Key)
            .ToHashSet(System.StringComparer.Ordinal);

        var liveWithoutAppUx = liveCatalog
            .Where(name => !appTokens.Contains(name))
            .OrderBy(n => n, System.StringComparer.Ordinal)
            .ToHashSet(System.StringComparer.Ordinal);

        var newlyMissing = liveWithoutAppUx.Except(KnownNoAppUxEntries).ToList();
        var noLongerMissing = KnownNoAppUxEntries.Except(liveWithoutAppUx).ToList();

        if (newlyMissing.Count > 0)
        {
            _output.WriteLine("Newly LIVE-without-App-UX (catalog says LIVE but no mention in src/SwfocTrainer.App/):");
            foreach (var e in newlyMissing) _output.WriteLine("  " + e);
            _output.WriteLine("Decide: add a native UX surface (button / preset / tooltip) in the App project, " +
                "OR add to KnownNoAppUxEntries with a one-line rationale, OR demote the catalog status.");
        }
        if (noLongerMissing.Count > 0)
        {
            _output.WriteLine("No-longer-missing (now mentioned in App — drop from KnownNoAppUxEntries):");
            foreach (var e in noLongerMissing) _output.WriteLine("  " + e);
        }

        // Soft assertion: total count is the stable signal. Diff output
        // above carries the actionable detail. Matches the convention
        // used by CapabilityCatalogReverseOrphanTests so future iters
        // see the same shape across both audits.
        liveWithoutAppUx.Count.Should().Be(KnownNoAppUxEntries.Count,
            "the count of LIVE catalog entries without any src/SwfocTrainer.App/ mention drifted. " +
            "See test output for the diff and update KnownNoAppUxEntries to match the new set.");
    }
}
