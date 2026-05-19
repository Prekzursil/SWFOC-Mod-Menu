using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using SwfocTrainer.Core.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace SwfocTrainer.Tests.Diagnostics;

/// <summary>
/// 2026-04-27 (iter 42) — informational guard for the REVERSE-orphan
/// direction of the catalog audit. The forward direction (called but
/// not catalogued) is enforced by <see cref="CapabilityCatalogOrphanTests"/>.
/// Reverse direction (catalogued but never called by editor source) is
/// NOT enforced — those entries usually represent bridge functions the
/// editor doesn't yet wire (planned/future surface that the bridge DLL
/// already supports).
/// </summary>
/// <remarks>
/// <para>
/// We track the count + list as a soft assertion: if the unwired set
/// drifts, the test prints the diff so a reviewer sees the change. They
/// can then decide: (a) the new entries are legitimate planned surface
/// — bump the expected list, (b) the entries should be removed from the
/// catalog as truly dead, or (c) the editor should grow a wiring for
/// them now.
/// </para>
/// </remarks>
public sealed class CapabilityCatalogReverseOrphanTests
{
    private readonly ITestOutputHelper _output;

    public CapabilityCatalogReverseOrphanTests(ITestOutputHelper output) => _output = output;

    /// <summary>
    /// 2026-04-27 (iter 42) snapshot of "catalogued but not yet wired" —
    /// every entry here is a known bridge function that the editor doesn't
    /// expose today. Adding entries here implies "the editor will wire this
    /// in a future iteration"; removing implies "the catalog entry is
    /// genuinely dead and should be deleted from the catalog".
    /// </summary>
    private static readonly HashSet<string> KnownUnwiredEntries = new(System.StringComparer.Ordinal)
    {
        // 2026-04-29 (iter 143-145 + iter 147): the camera primitive arc
        // shipped 6 LIVE wires in iter 143/144/145. Iter 147 added them to
        // the Lua Playground iter 100-113 preset menu (renamed "iter
        // 100-145 LIVE wires" in source comments) so the textual
        // SWFOC_X( call sites are now in editor source — they're no
        // longer "catalogued but uncalled". Native per-tab buttons on
        // Camera & Debug tab are still queued for follow-up but the
        // forward-grep no longer flags them as unwired.
        // iter 211 NOTE: SetGarrisonSpawnLua now has native UX in UnitControl
        // tab Selected Unit Lua Actions GroupBox via iter-204 hardcoded-bool
        // pattern. Dispatcher uses BuildUnitLuaMethodCall("SWFOC_X", ...)
        // string-literal form, regex-invisible — entry stays for count-stable
        // assertion. (Activate_Ability/Disable_Capture/Cancel_Hyperspace weren't
        // in snapshot — likely had iter-183 presets.)
        "SWFOC_SetGarrisonSpawnLua",   // iter 156 LIVE — iter 211 native UX (UnitControl)
        // iter 212 NOTE: SetCheckContestedSpaceLua + SellUnitLua now have native
        // UX in UnitControl tab unit-method mega-batch row. Dispatchers use
        // BuildUnitLuaMethodCall / BuildUnitLuaNoArgCall string-literal forms,
        // regex-invisible — entries stay for count-stable assertion.
        // (Set_In_Limbo/Bribe/Move_To/Fire_Special_Weapon weren't in iter-157
        // snapshot — likely had iter-183 presets.)
        "SWFOC_SetCheckContestedSpaceLua", // iter 157 LIVE — iter 212 native UX (UnitControl)
        "SWFOC_SellUnitLua",           // iter 157 LIVE — iter 212 native UX (UnitControl)
        "SWFOC_FlashGuiObjectLua",     // iter 158 LIVE
        "SWFOC_HideGuiObjectLua",      // iter 158 LIVE
        // iter 201 NOTE: PlaySfxEventLua now has native UX in WorldState tab
        // Story & Audio GroupBox. Dispatcher uses BuildUnitLuaNoArgCall string-
        // literal form, regex-invisible — entry stays for count-stable assertion.
        // (Story_Event/Add_Objective/Play_Music weren't in snapshot — likely had presets.)
        "SWFOC_PlaySfxEventLua",       // iter 159 LIVE — iter 201 native UX (WorldState)
        // iter 217 dropped: DisableOrbitalBombardmentLua now wired in PlayerState
        // tab via interpolated `$"return SWFOC_DisableOrbitalBombardmentLua(...)"`
        // form which the regex matches. Same regex-visibility as the iter-195
        // CreateGenericObjectLua / iter-200 FOWRevealLua drops.
        // iter 202 NOTE: StoryEventTriggerLua now has native UX in WorldState
        // tab Story & Audio GroupBox row 3. Dispatcher uses BuildUnitLuaNoArgCall
        // string-literal form, regex-invisible — entry stays for count-stable assertion.
        "SWFOC_StoryEventTriggerLua",  // iter 160 LIVE — iter 202 native UX (WorldState)
        // iter 209 NOTE: LockTechLua + MakeEnemyLua now have native UX in
        // PlayerState tab diplomacy GroupBox. Dispatchers use
        // BuildUnitLuaMethodCall("SWFOC_X", ...) string-literal form, regex-
        // invisible — entries stay for count-stable assertion.
        // (MakeAllyLua wasn't in the iter-161 snapshot — likely had a preset.)
        "SWFOC_LockTechLua",           // iter 161 LIVE — iter 209 native UX (PlayerState)
        "SWFOC_MakeEnemyLua",          // iter 161 LIVE — iter 209 native UX (PlayerState)
        // iter 219 dropped: SuspendAiLua now wired in Combat tab via interpolated
        // `$"return SWFOC_SuspendAiLua(...)"` form which the regex matches.
        // iter 192 dropped: ZoomCamera now wired via Camera & Debug tab.
        // iter 194 NOTE: GuardTargetLua / DivertLua now have native UX in
        // UnitControl tab combat-order extension. Dispatcher uses
        // BuildUnitLuaMethodCall("SWFOC_X", ...) string-literal form which the
        // regex `\bSWFOC_X\s*\(` does not match — entries stay here for the
        // count-stable assertion. (Same pattern as iter 191 Inspector entries
        // above.)
        "SWFOC_GuardTargetLua",        // iter 163 LIVE — iter 194 native UX (UnitControl)
        "SWFOC_DivertLua",             // iter 163 LIVE — iter 194 native UX (UnitControl)
        // iter 210 NOTE: EnableAsActorLua + SelectObjectLua now have native UX
        // in PlayerState tab PlayerWrapper extension GroupBox. Dispatchers use
        // BuildUnitLuaNoArgCall / BuildUnitLuaMethodCall string-literal forms,
        // regex-invisible — entries stay for count-stable assertion.
        // (ReleaseCreditsForTacticalLua wasn't in iter-164 snapshot — likely had a preset.)
        "SWFOC_EnableAsActorLua",      // iter 164 LIVE — iter 210 native UX (PlayerState)
        "SWFOC_SelectObjectLua",       // iter 164 LIVE — iter 210 native UX (PlayerState)
        // iter 192 dropped: FadeScreenOut / RotateCameraBy / PointCameraAt now
        // wired via Camera & Debug tab "Camera primitive arc — extras" GroupBox.
        // Dispatcher uses SWFOC_X(arg) form so regex matches.
        "SWFOC_ShowGuiObjectLua",      // iter 166 LIVE — native UX queued
        "SWFOC_GetHealthLua",          // iter 167 LIVE — native UX queued
        "SWFOC_GetShieldLua",          // iter 167 LIVE — native UX queued
        // iter 191 NOTE: HasAttackTargetLua / GetOwnerLua now have
        // native UX in the Inspector tab "Selected Unit Lua Read-side" GroupBox,
        // but the regex `\bSWFOC_X\s*\(` only matches function-call form. The
        // dispatcher uses string-arg form `BuildUnitLuaNoArgCall("SWFOC_X", ...)`,
        // so the regex still classifies these as "no source call site". The
        // entries stay here to keep actuallyUnwired.Count == KnownUnwiredEntries.Count.
        // iter 343 dropped: GetTypeLua now wired in CombatTabViewModel.
        // ResolveHardpointIconAsync via interpolated `$"return SWFOC_GetTypeLua(...)"`
        // form which the regex matches (SWFOC name immediately followed by `(`).
        // Same regex-visibility as the iter-200 FOWRevealLua / iter-218
        // TaskForceMoveToTargetLua drops. Iter-346 reverse-orphan audit caught it
        // (first drift-catch in iter-238/255/263/272/346 sequence after 4 CLEAN PASSes).
        "SWFOC_HasAttackTargetLua",    // iter 168 LIVE — iter 191 native UX (Inspector)
        "SWFOC_GetOwnerLua",           // iter 168 LIVE — iter 191 native UX (Inspector)
        "SWFOC_GetFactionLua",         // iter 169 LIVE — native UX queued
        "SWFOC_GetTechLevelLua",       // iter 169 LIVE — native UX queued
        "SWFOC_IsStealthedLua",        // iter 170 LIVE — native UX queued
        "SWFOC_IsInLimboLua",          // iter 170 LIVE — native UX queued
        "SWFOC_IsCapturableLua",       // iter 170 LIVE — native UX queued
        // iter 197 NOTE: 6 entries below now have native UX in Inspector tab
        // read-side extension. Dispatcher uses BuildUnitLuaNoArgCall("SWFOC_X", ...)
        // string-literal form, regex-invisible — entries stay here for the
        // count-stable assertion. Same pattern as iter-191/194 NOTE blocks above.
        "SWFOC_GetParentObjectLua",    // iter 171 LIVE — iter 197 native UX (Inspector)
        "SWFOC_GetAttackTargetLua",    // iter 171 LIVE — iter 197 native UX (Inspector)
        "SWFOC_GetDamageModifierLua",  // iter 171 LIVE — iter 197 native UX (Inspector)
        "SWFOC_GetContainedObjectCountLua", // iter 172 LIVE — iter 197 native UX (Inspector)
        "SWFOC_GetBehaviorIdLua",      // iter 172 LIVE — iter 197 native UX (Inspector)
        "SWFOC_GetRateOfFireModifierLua", // iter 172 LIVE — iter 197 native UX (Inspector)
        // iter 198 NOTE: HasPropertyLua / IsCategoryLua / GetDistanceLua now
        // have native UX in Inspector tab arg-getter extension. Dispatcher
        // uses BuildUnitLuaMethodCall("SWFOC_X", ...) string-literal form,
        // regex-invisible — entries stay here for count-stable assertion.
        // (IsAbilityActiveLua wasn't in this snapshot — likely had a preset.)
        "SWFOC_HasPropertyLua",        // iter 173 LIVE — iter 198 native UX (Inspector)
        "SWFOC_IsCategoryLua",         // iter 173 LIVE — iter 198 native UX (Inspector)
        "SWFOC_GetDistanceLua",        // iter 173 LIVE — iter 198 native UX (Inspector)
        // iter 214 NOTE: ContainsObjectTypeLua + GetSpaceStationLevelLua +
        // GetTypeOfUnitLua now have native UX in Inspector tab cross-receiver
        // arg-getter row. Dispatchers use BuildUnitLuaMethodCall string-literal
        // form, regex-invisible — entries stay for count-stable assertion.
        // (GetBonePositionLua wasn't in iter-174 snapshot — likely had iter-183 preset.)
        "SWFOC_ContainsObjectTypeLua", // iter 174 LIVE — iter 214 native UX (Inspector)
        "SWFOC_GetSpaceStationLevelLua", // iter 174 LIVE — iter 214 native UX (Inspector)
        "SWFOC_GetTypeOfUnitLua",      // iter 174 LIVE — iter 214 native UX (Inspector, first TaskForce wire)
        // iter 215 NOTE: 6 of the 8 TaskForce write-side wires now have native
        // UX in Galactic tab TaskForce write-side row. Dispatchers use
        // BuildUnitLuaMethodCall / BuildUnitLuaNoArgCall string-literal forms,
        // regex-invisible — entries stay for count-stable assertion.
        // (TaskForceMoveToLua + TaskForceLandUnitsLua weren't in iter-175/176
        // snapshot — likely had iter-183 presets.)
        "SWFOC_TaskForceReinforceLua", // iter 175 LIVE — iter 215 native UX (Galactic)
        "SWFOC_TaskForceReleaseReinforcementsLua", // iter 175 LIVE — iter 215 native UX (Galactic)
        "SWFOC_TaskForceLaunchUnitsLua", // iter 175 LIVE — iter 215 native UX (Galactic)
        "SWFOC_TaskForceAttackTargetLua", // iter 176 LIVE — iter 215 native UX (Galactic)
        "SWFOC_TaskForceGuardTargetLua", // iter 176 LIVE — iter 215 native UX (Galactic)
        "SWFOC_TaskForceSetAsGoalSystemRemovableLua", // iter 176 LIVE — iter 215 native UX (Galactic)
        // iter 203 NOTE: FindFirstObjectLua now has native UX in Spawning tab
        // Discovery helpers GroupBox. Dispatcher uses BuildUnitLuaNoArgCall string-
        // literal form, regex-invisible — entry stays for count-stable assertion.
        // (FindObjectTypeLua + FindPlanetLua weren't in snapshot — likely had presets.)
        "SWFOC_FindFirstObjectLua",    // iter 177 LIVE — iter 203 native UX (Spawning)
        "SWFOC_GetSecondsPerGameMinuteLua", // iter 178 LIVE — native UX queued
        // iter 199 NOTE: IsAllyLua now has native UX in PlayerState tab read-side
        // extension. Dispatcher uses BuildUnitLuaMethodCall("SWFOC_X", ...)
        // string-literal form, regex-invisible — entry stays for count-stable assertion.
        "SWFOC_IsAllyLua",             // iter 179 LIVE — iter 199 native UX (PlayerState)
        // iter 218 dropped: TaskForceMoveToTargetLua now wired in Galactic tab
        // via interpolated `$"return SWFOC_TaskForceMoveToTargetLua(...)"` form
        // which the regex matches.
        // iter 200 NOTE: FOWRevealAllLua now has native UX in Galactic tab "Fog
        // of War" GroupBox (LIVE button). Dispatcher uses
        // BuildUnitLuaNoArgCall("SWFOC_X", ...) string-literal form which is
        // regex-invisible (no SWFOC name immediately followed by `(`). Entry
        // would also stay here for FOWRevealAllLua if it had been listed,
        // but the iter-180 snapshot shipped without it (likely had a preset).
        "SWFOC_FOWUndoRevealAllLua",   // iter 180 LIVE — iter 200 native UX (Galactic "Fog of War")
        // iter 200 dropped: FOWRevealLua now has native UX. BuildFOWRevealCommand
        // uses an interpolated `$"return SWFOC_FOWRevealLua('...')"` form which
        // the regex matches (SWFOC name immediately followed by `(`). Same
        // regex visibility as iter-195 CreateGenericObjectLua's drop.
        // iter 195 dropped: ReinforceUnitLua + SpawnFromReinforcementPoolLua
        // now wired in Spawning tab via interpolated $"return SWFOC_X(...)" form
        // (BuildSpawnVariantPlayerTypePosCommand uses interpolation, not the
        // string-literal helper) — regex matches them now.
        // iter 195 dropped: CreateGenericObjectLua now has native UX. Dispatcher
        // uses interpolated $"return SWFOC_X('...')" form which the regex
        // matches (SWFOC name immediately followed by `(`).
        // iter 203 dropped: FindNearestLua now wired in Spawning tab Discovery
        // helpers via interpolated $"return SWFOC_X(...)" form — regex matches.
        "SWFOC_CombinedGodOHK",        // composite — Combat tab uses individual toggles
        "SWFOC_DiagGameTick",          // tick counter probe — diagnostics tab doesn't fire it
        "SWFOC_EnumerateUnits",        // tactical-unit enumerator — Tactical Units tab uses ListTacticalUnits instead
        "SWFOC_GetAiBrain",            // read-only AI-brain probe — VM doesn't surface it yet
        "SWFOC_FreezeAI",              // legacy Phase-2 AI-freeze helper — UI uses LIVE SWFOC_SuspendAiLua instead
        "SWFOC_SetHeroRespawnTimer",   // per-hero timer still Phase-2 — UI uses LIVE global SWFOC_SetHeroRespawn instead
        // iter 360 NOTE (pre-compounding for iter-368 audit per iter-359 codified rule):
        // Entries below are catalogued + LIVE-or-deprecated, but lack regex-visible call
        // sites in editor source. Each cross-references the iter that resolved its status
        // so a future iter-368 audit can recognize them as resolved without re-investigating
        // (mirrors iter-329 catalog rationale extension pattern at the test-file layer).
        "SWFOC_GetPlanetTechAndBuildings", // iter 326 DEPRECATED ORPHAN — superseded by iter-296 SWFOC_GetPlanets (galactic-mode planet enumeration with name;faction;tech CSV); buildings genuinely deferred per iter-326 audit
        "SWFOC_GetSelectedUnits",      // plural variant of GetSelectedUnit
        "SWFOC_GetUnitShield",         // iter 131 LIVE pair-flip with iter-129 SetUnitShield writer (FrontShield_Read @ 0x3963C0); regex-invisible because used via service-layer wrapper
        "SWFOC_ListFactions",          // editor derives faction list from GetAllPlayers
        // iter 239 dropped: SWFOC_GetCameraPos now wired in BridgeCameraDebugDispatcher
        // via `return SWFOC_GetCameraPos()` literal (regex-visible). Camera & Debug tab
        // UX surfaces it via the new "Read camera pos (LIVE)" button.
        // iter 227 dropped: SetFireRateMultiplierGlobal + GetFireRateMultiplierGlobal
        // now wired in BridgeCombatDispatcher via interpolated $"return SWFOC_X(...)"
        // form — same regex-visible pattern as iter-96 SetDamageMultiplierGlobal.
        // Combat tab buttons surface them; XAML tooltip text references them too.
        // iter 233 dropped: 4 iter-231 entries (SetCreditsFreezeGlobal +
        // GetCreditsFreezeGlobal + SetCreditsMultiplierGlobal +
        // GetCreditsMultiplierGlobal) now wired in BridgeEconomyDispatcher via
        // interpolated $"return SWFOC_X(...)" form — same regex-visible pattern
        // as iter-96 + iter-225 + iter-227. Economy tab "GLOBAL economy controls"
        // GroupBox surfaces them with Freeze on/off + Apply (GLOBAL) + Read (GLOBAL).
        // 2026-04-28 (iter 107-117 batch): the engine-Lua-API + DoString
        // wires below ship LIVE on the bridge AND have textual call
        // sites in the editor source. They were originally listed here
        // pending per-tab UX surfaces, but iter 116 added Lua Playground
        // presets and iter 117 added UnitControl-tab buttons — the
        // textual `SWFOC_X(` patterns now appear in editor source so the
        // forward-grep no longer flags them as "catalogued but uncalled".
        // Per-tab native buttons landing for SWFOC_ChangeUnitOwner /
        // SpawnUnitLua are still future UX polish, but the "unwired"
        // soft-snapshot no longer applies to them.
    };

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

    private static IEnumerable<string> EnumerateCsFiles(string root)
    {
        foreach (var f in Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories))
        {
            yield return f;
        }
    }

    [Fact]
    public void UnwiredEntries_MatchKnownSnapshot()
    {
        // Find every SWFOC_* token actually called from src/ (any project
        // that ships, including the bridge harness — wider net than the
        // forward-orphan check, which only looks at editor + services).
        var rx = new Regex(@"\bSWFOC_([A-Z][A-Za-z_0-9]*)\s*\(", RegexOptions.Compiled);
        var root = ResolveSourceRoot();
        var called = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var file in EnumerateCsFiles(root))
        {
            var text = File.ReadAllText(file);
            // Skip the catalog file itself — it mentions every helper as a
            // dictionary key, which would make this test always pass.
            if (file.EndsWith("CapabilityStatusCatalog.cs", System.StringComparison.Ordinal)) continue;
            foreach (Match m in rx.Matches(text))
            {
                called.Add("SWFOC_" + m.Groups[1].Value);
            }
        }
        called.Remove("SWFOC_X"); // doc-comment placeholder

        var catalogue = CapabilityStatusCatalog.Entries.Keys.ToHashSet(System.StringComparer.Ordinal);
        var actuallyUnwired = catalogue.Where(name => !called.Contains(name))
            .OrderBy(n => n, System.StringComparer.Ordinal).ToHashSet(System.StringComparer.Ordinal);

        var newlyUnwired = actuallyUnwired.Except(KnownUnwiredEntries).ToList();
        var noLongerUnwired = KnownUnwiredEntries.Except(actuallyUnwired).ToList();

        if (newlyUnwired.Count > 0)
        {
            _output.WriteLine("Newly unwired (catalogued but no source call site):");
            foreach (var e in newlyUnwired) _output.WriteLine("  " + e);
            _output.WriteLine("Decide: add to KnownUnwiredEntries (legitimate planned surface), " +
                "remove from catalog (truly dead), or wire it in the editor now.");
        }
        if (noLongerUnwired.Count > 0)
        {
            _output.WriteLine("No-longer-unwired (now has a call site — drop from KnownUnwiredEntries):");
            foreach (var e in noLongerUnwired) _output.WriteLine("  " + e);
        }

        // Soft assertion: the total count should be stable. The test prints
        // the diff via _output above; the assertion just locks in "you
        // changed something — review the output and update the snapshot."
        actuallyUnwired.Count.Should().Be(KnownUnwiredEntries.Count,
            "the count of catalogued-but-unwired entries drifted. " +
            "See test output for the diff and update KnownUnwiredEntries to match the new set.");
    }
}
