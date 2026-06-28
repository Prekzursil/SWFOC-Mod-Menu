# Polish Backlog — 2026-05-20

Non-blocking MEDIUM / LOW findings from adversarial review. These do not gate the next commit but should be batched into a polish iter when the editor-polish hat has bandwidth.

Source: `.ralph/state/review_log.md` adversarial verdicts.

---

## From review of `cdbe4f12` (iter-468..470 LuaPlayground codename sweep)

### MEDIUM — Global codename-leak fact across all presets
The iter-468/469/470 negative-regex guards only fire on the specific scripts being relabelled in each iter. The next stale `[NNN]` codename added anywhere else in `Iter100to113Presets` won't trip any test. Replace per-script Theory guards with a single project-wide `Fact` that enumerates ALL `Iter100to113Presets` and asserts none carry `iter[ -]?\d+` / `^\[\d+(?:[- ]\d+)*\]` forms — with an explicit allowlist `HashSet<string>` for known/intentional survivors (e.g. `[181]` until the [write]/[mut] cluster lands).

**Suggested location:** new `Iter471CodenameLeakSweepTests.cs` (or fold into an existing project-wide ViewModel sanity test file). Pairs naturally with the iter-388 codified rule's prospective uses section.

**Status:** RESOLVED 2026-05-21 (iter-482, commit pending). New file `tests/SwfocTrainer.Tests/Regression/Iter482PresetCodenameLeakSweepTests.cs` (208 LoC) implements the project-wide sweep with **three** invariants — beyond the MEDIUM's original two:
  1. `NoPresetLabelContainsIterDigitsSubstring` — no label may contain the `iter[ -]?\d+` substring anywhere (case-insensitive). Caught 5 real drift instances on first run: 2× `[243]` cross-refs to `iter-110/iter-153`, `[267-268]` HONEST DEFER label referencing `iter-99/iter-100`, `[269-270]` alternative-set label referencing `iter-96/iter-154/iter-225`, and `[282]` pair-flip label referencing `iter-225`. Same commit rewrites those 5 labels to use the `[NNN]` catalog prefix form (operator can scroll to the cross-referenced preset).
  2. `BracketedNCodenamePrefixes_StayWithinAllowlist` — every `[NNN]` / `[NNN-NNN]` / `[NNN/NNN]` prefix must be in `AllowlistedBracketedPrefixes` HashSet. Allowlist snapshots the 54 surviving prefixes as of iter-482; future additions must extend the set with a rationale comment.
  3. `Allowlist_OnlyContainsActuallyPresentPrefixes` — bonus parity invariant beyond the MEDIUM. Forces the allowlist to shrink when a future sweep drains the last production label using a prefix. Catches the failure mode where a sweep removes the label but forgets to update the allowlist.

Discoverer: adversarial-reviewer at cdbe4f12 (iter-470). Closed in iter-482 on `release/v1.0.2`.

### MEDIUM — Cluster membership identity, not just floor counts
Cluster-floor `Fact`s (`[disc] >= 9`, `[read] >= 11`) test cardinality but not identity. AllActions count-pin drift is a captured project lesson (`feedback_allactions_count_pin_drift.md`). Add per-cluster script-content pins (HashSet<string> of expected Script values) so cluster contents can't silently rotate while floor stays satisfied.

**Suggested location:** add `PresetMenu_DiscCluster_ContainsExpectedScripts` and `PresetMenu_ReadCluster_ContainsExpectedScripts` `Fact`s to the existing Iter468/Iter469 regression files (or to a new dedicated cluster-identity file).

### MEDIUM — Per-object vs global [read] cluster discriminator
Iter470 comment "Absence of an object name in the label IS the global-scope signal" is heuristic, not enforceable. No test pins the discriminator. Either drop the heuristic from the comment or codify it as a test (e.g. labels containing `Find_First_Object` / `Find_Player` references in their script ARE per-object reads; the rest are globals).

### LOW — Iter183 negative-pin form
Iter183 dropped `[InlineData("[167]")] / [InlineData("[173]")] / [InlineData("[178]")]` rows. Consider inverting into negative pins (`PresetMenu_HasNoPresetForCodename(string iterTag)` with the same data) so the test retains institutional memory of which codenames were retired and prevents accidental re-introduction.

### LOW — Inline-rationale comment density
4 inline rationale comment blocks in the ViewModel pin specific iter-N narrative ("iter-467/468", "iter-181 SFXManager") into production source. Per CLAUDE.md (don't reference the current task in production code), these are mildly out-of-band — though the project's iter-388 provenance convention likely accepts them. Consider consolidating into a single class-level XML-doc summary block instead of 4 inline blocks.

### LOW — Brittle full-script-string match in iter-181 defensive pin
`Iter470LuaPlaygroundReadGlobalsCodenameTests.cs::PresetMenu_Iter181WriteSideStaysAsCodename_NotRecategorised` matches by full script string `"return SWFOC_SFXAllowUnitReponseVoLua('false')"` (engine-typo `Reponse`). If the bridge ever fixes the typo to `Response`, this defensive pin breaks with a NullReferenceException-via-`!` rather than a clean assertion failure. Prefer a stable identifier (label substring or preset enum) over the full script string. Pairs with the HIGH-severity fix from the same review (invert the assertion to `NotStartWith("[read] ")`).

---

## Promotion criteria

A MEDIUM/LOW item promotes to a regular iter when:
- 3+ similar items accumulate against the same surface (codified-rule-application threshold per iter-388 precedent), OR
- Editor Polish has an idle iter and explicitly picks up backlog work, OR
- A future bug traces back to one of these items.

Otherwise items age in this file. Reviewed periodically per `feedback_audit_compounds_via_rationale_extensions.md` cadence.

---

# Adversarial review — c083bc3a (iter-471, 2026-05-20T18:01:26Z, APPROVED)

### MEDIUM — Iter-471 defensive pin: `NotStartWith` is narrower than docstring scope
`Iter470LuaPlaygroundReadGlobalsCodenameTests.cs::PresetMenu_Iter181WriteSideNotInReadCluster` now asserts `Label.Should().NotStartWith("[read] ").And.NotStartWith("[disc] ")`. The docstring's defensive intent is "don't sweep this mutation into a *read* cluster" but the assertion only blocks two exact literal prefixes. It passes for:
- `[read]Disable VO` (no space after `]`)
- `  [read] Disable VO` (leading whitespace)
- `Disable VO [read]` (suffix form)
- `[query] Disable VO`, `[get] Disable VO` (a future read-like cluster with a different prefix)

A future relabel sweep using any of those shapes silently slips through — the exact class of "overzealous relabel" this test exists to catch.

**Suggested fix:** add a positive assertion alongside the negatives — e.g. `preset.Label.Should().ContainAny("Disable", "VO")` (the mutation-describing substrings) OR enumerate `AllReadClusterLabels` / `AllDiscClusterLabels` as known sets and assert the iter-181 label is in neither.

**Suggested location:** same test file, same `Fact` method (extend the existing iter-181 pin).

### MEDIUM — Iter-471 preset lookup couples to full script string
The iter-181 pin locates the preset by `Script == "return SWFOC_SFXAllowUnitReponseVoLua('false')"`. If a future sweep consolidates SWFOC_* wrapper names or fixes the engine-typo `Reponse` → `Response` at the bridge layer, `SingleOrDefault` returns null and `NotBeNull` fails with a misleading "preset survives iter-470 sweep" message — masking the actual relabel regression the test should catch. Same fragility shape as the existing "Brittle full-script-string match" LOW from cdbe4f12 review, but elevated to MEDIUM because iter-471's `NotStartWith` shape leaves identifier brittleness as the only remaining lookup mechanism.

**Suggested fix:** locate by a more durable property — e.g. `Label.Contains("Reponse")` (the engine typo is itself the durable signature, and is independently pinned by the third assertion in the same test), or extract a preset-enum / constant if one exists in the VM.

**Suggested location:** same test file, same `Fact` method (rewrite the `SingleOrDefault` predicate).

### LOW — Iter-471 docstring deferral lacks a tracking entry
The new docstring states "Future [write]/[mut] cluster can land in a later iter and update this pin accordingly." This is unverifiable guidance — no link to a tracking entry in `knowledge-base/blocked_items_*.md`, MEMORY index, or a `TODO(iter-XXX)` marker. Iter-388 is the project's most-validated rule (88 instances, Tier 1); leaving `[181]` in the production label `[181] Disable unit VO (engine typo Reponse)` with only an inline test-file comment as the "we'll fix it later" record is the kind of deferral that gets lost across compactions.

**Suggested fix:** Either (a) add a `TODO(iter-XXX): rename [181] → [write]/[mut]` marker on the production label in `LuaPlaygroundTabViewModel.cs` adjacent to the iter-181 entry, or (b) add a `blocked_items_2026-05-20.md` entry referencing the deferred iter-388 cleanup with a one-line rationale, or (c) when the next iter-388 batch sweep lands (the project has done iter-380, iter-388, iter-464, iter-466 sweeps), include the iter-181 → `[write]` rename in that batch and link the iter-471 test to it. Picking up this item naturally when a `[write]`/`[mut]` cluster is introduced is the cleanest path.

**Resolution (2026-05-20 iter 472):** Added the tracking entry below (`iter_181_write_mut`), linked from `Iter470LuaPlaygroundReadGlobalsCodenameTests.cs::PresetMenu_Iter181WriteSideNotInReadCluster` docstring. Picks resolution path (b) — reusing this file as the conventional backlog surface rather than introducing a new `blocked_items_<date>.md`.

---

## Deferred work — anchor for future iters

<a id="iter_181_write_mut"></a>
### `iter_181_write_mut` — iter-181 [write]/[mut] cluster relabel

**Status:** DEFERRED. Production label `[181] Disable unit VO (note: engine typo 'Reponse')` in `LuaPlaygroundTabViewModel.cs` (line 271) is the lone remaining `[NNN]` iter-N codename in the iter-100-to-113 preset cluster post iter-467/468/469/470. The relabel sweep recategorised reads ([read]) and discovery ([disc]) clusters but intentionally left mutation wires alone — there is no `[write]` / `[mut]` semantic cluster yet, and inventing one for a single entry would be premature abstraction (per the iter-316 "Extract on Second Use" codified rule, wait for the second mutation entry to land before extracting the cluster shape).

**Promotion criteria (any-of):**
- A second mutation wire surfaces as a Lua Playground preset → extract `[write]` or `[mut]` cluster, sweep both entries in same iter
- The next iter-388 batch sweep lands (iter-380, iter-388, iter-464, iter-466 are the existing batches) → include iter-181 in that batch
- An adversarial reviewer flags the `[181]` codename in a future review as a hard issue → forces the relabel forward

**Test pin tracking the deferral:**
`tests/SwfocTrainer.Tests/Regression/Iter470LuaPlaygroundReadGlobalsCodenameTests.cs::PresetMenu_Iter181WriteSideNotInReadCluster`. The test currently uses `Label.Contains("Reponse")` to locate the preset and asserts (a) it is not in `[read]` or `[disc]` clusters and (b) the mutation verb "Disable" survives any future relabel. When this deferred item is picked up, the test should be updated to assert the entry IS in the new `[write]` / `[mut]` cluster (pin inversion same as iter-471 did for the iter-470 sweep).

**Out of scope right now:** introducing a single-member `[write]` cluster purely to drain the `[181]` codename. Wait for a second mutation wire.

---

## Findings from 6dbe73e adversarial review (iter-472) — 2026-05-20T18:15:36Z

### MEDIUM — `Label.Contains("Reponse")` lookup couples to `Contain("typo")` assertion semantics

**Status:** RESOLVED 2026-05-20 (iter 474). Discoverer: adversarial-reviewer at 6dbe73e (iter-472).

**Location:** `tests/SwfocTrainer.Tests/Regression/Iter470LuaPlaygroundReadGlobalsCodenameTests.cs::PresetMenu_Iter181WriteSideNotInReadCluster` lines 165-166 (lookup) + line 174 (typo assertion).

**Issue:** iter-472 changed the preset lookup from `Script == "return SWFOC_SFXAllowUnitReponseVoLua('false')"` to `Label.Contains("Reponse")`. The lookup discriminator and the existing `Contain("typo")` assertion are no longer independent — both correlate on the engine-typo `Reponse`. If a future iter adds a second `Reponse`-bearing preset (e.g., a paired read-side discovery wire `[disc] Read Allow_Unit_Reponse_VO state`), `SingleOrDefault` throws `InvalidOperationException` with a less-actionable multi-match failure than the prior `Script ==` lookup. The swap moved fragility (label-drift vs script-drift), didn't eliminate it.

**Suggested fix:** Conjunction lookup `p => p.Label.Contains("Reponse") && p.Script.Contains("Allow_Unit_Reponse_VO")`. Survives label-typo-fix (script still matches), survives script-rewrite that keeps engine symbol (label still matches), AND disambiguates from a hypothetical paired read-side preset.

**Suggested location:** same test file, same `Fact` method (single-line edit).

**Resolution (iter 474):** Applied conjunction lookup with the codebase-correct disambiguator — `Script.Contains("SFXAllowUnitReponseVoLua")` (the SWFOC_ wrapper symbol actually present in the preset's script) rather than the literal-prescription `Allow_Unit_Reponse_VO` (the engine STATE field name, present only in dispatcher comments / BridgeAssertion text — NOT in the preset script body). The wrapper symbol preserves the reviewer's intent (label-typo-fix survives, script-arg-rewrite survives, disambiguates from paired read-side preset via DIFFERENT Lua surface) while actually matching the real preset data. Divergence from literal prescription is documented in the test docstring with the rationale.

---

### LOW — iter-181 docstring tracking-entry pointer missing deferral condition

**Status:** RESOLVED 2026-05-20 (iter 474). Discoverer: adversarial-reviewer at 6dbe73e (iter-472).

**Location:** `tests/SwfocTrainer.Tests/Regression/Iter470LuaPlaygroundReadGlobalsCodenameTests.cs::PresetMenu_Iter181WriteSideNotInReadCluster` lines 130-131.

**Issue:** iter-472 added the `polish_backlog_2026-05-20.md::iter_181_write_mut` tracking-entry pointer per c083bc3a LOW finding, but the docstring doesn't surface WHY the entry is deferred. Reader sees the pointer, chases the backlog file to learn the deferral condition ("no [write]/[mut] cluster exists yet, premature abstraction to invent one for a single entry").

**Suggested fix:** Append "(deferred until a `[write]`/`[mut]` semantic-prefix cluster lands — no current cluster to migrate into; see backlog entry for promotion criteria)" to lines 130-131. Keeps the docstring self-contained without re-stating the full backlog rationale.

**Suggested location:** same test file, same docstring block.

**Resolution (iter 474):** Appended reviewer-prescribed phrasing verbatim to lines 130-131, including the `(per iter-474 6dbe73e LOW finding)` provenance.

---

## Findings from 7eb7020 adversarial review (iter-475 FsCheck + BDN scaffold) — 2026-05-20T23:00:00Z

### MEDIUM — `<UseWPF>true</UseWPF>` in new test csprojs is gratuitous and inconsistent with project convention

**Status:** RESOLVED 2026-05-20 (iter-476). `<UseWPF>true</UseWPF>` dropped from both `SwfocTrainer.Property.Tests.csproj:8` and `SwfocTrainer.Benchmarks.csproj:9`. Build verified clean (0W/0E) after edit; no WPF type was referenced by either project. Discoverer: adversarial-reviewer at 7eb7020 (iter-475). Closed in iter-476 commit on `release/v1.0.2`.

**Location:**
- `tests/SwfocTrainer.Benchmarks/SwfocTrainer.Benchmarks.csproj:9`
- `tests/SwfocTrainer.Property.Tests/SwfocTrainer.Property.Tests.csproj:8`

**Issue:** Existing test projects do NOT enable WPF (`SwfocTrainer.UiTests.csproj` explicitly `<UseWPF>false</UseWPF>`; `SwfocTrainer.Tests.csproj` omits UseWPF, defaulting to false). Both new projects reference only `SwfocTrainer.Core` — no WPF surface. UseWPF=true pulls in PresentationFramework / PresentationCore / WindowsBase. For Benchmarks specifically this is a real concern: BDN AppDomain warmup is sensitive to extra dependency-resolution time (BDN docs note "Pause" mode behavior with heavy host assemblies). Bloats build + warmup variance for no functional gain.

**Suggested fix:** Remove `<UseWPF>true</UseWPF>` from both csprojs. Verify build still passes (it will — neither project references any WPF type).

**Suggested location:** same csproj files, single-line delete each.

---

### LOW — Phantom relative-path references to `.ralph/specs/` and `.ralph/scripts/` in csproj/Program.cs comments

**Status:** RESOLVED 2026-05-20 (iter-479, commit d86435e). Reviewer-prescribed fix path (b) "reference by stable convention name without path" applied to both csproj comments. The Program.cs:12 entry was a FALSE POSITIVE — `git show 7eb7020:tests/SwfocTrainer.Benchmarks/Program.cs` confirms `.ralph/scripts/benchmark_gate.sh` was never in the file; that reference appeared only in the 7eb7020 commit MESSAGE. Discoverer: adversarial-reviewer at 7eb7020 (iter-475). Closed in iter-479 on `release/v1.0.2`.

**Location:**
- `tests/SwfocTrainer.Property.Tests/SwfocTrainer.Property.Tests.csproj:10` (was line 13 in 7eb7020; shifted post-iter-476 UseWPF removal) cited `.ralph/specs/perf-benchmarker.md`
- `tests/SwfocTrainer.Benchmarks/SwfocTrainer.Benchmarks.csproj:11` (was line 14 in 7eb7020; shifted post-iter-476 UseWPF removal) cited `.ralph/specs/perf-benchmarker.md`
- `tests/SwfocTrainer.Benchmarks/Program.cs:12` — FALSE POSITIVE: never actually contained the cited reference

**Issue:** The referenced files DO exist in the orchestrator root `C:/Users/Prekzursil/Downloads/swfoc_memory/.ralph/` (verified: 10 spec files present, 7 script files present). But from inside the editor repo (`C:/Users/Prekzursil/Downloads/SWFOC editor/`) the relative paths `.ralph/specs/...` resolve to non-existent locations. The comments are pointing at sibling-directory artifacts without expressing the navigation.

**Suggested fix:** Either (a) anchor with sibling-relative convention e.g. `"see ../swfoc_memory/.ralph/specs/perf-benchmarker.md"` (still brittle to relocation), or (b) reference by stable convention name without path e.g. `"see Ralph orchestrator spec: perf-benchmarker.md"`, or (c) drop the spec-pointer comments entirely and let the project name + commit message provide the linkage. Option (b) or (c) preferred — option (a) trades one phantom for a different brittle path.

**Suggested location:** 3 comment blocks across 3 files; ~5 LoC delete-or-edit total.

**Resolution (iter-479):** Picked option (b) for both csproj comments — preserves discoverability without path-brittleness. `"Performance benchmark project per .ralph/specs/perf-benchmarker.md."` → `"Performance benchmark project per Ralph orchestrator perf-benchmarker spec."` + `".ralph/state/perf/baseline.json"` → `"the perf-benchmarker baseline file"` in Benchmarks.csproj; matching transform in Property.Tests.csproj. Program.cs left untouched (false positive).

---

### LOW — `BenchmarkDotNet.Diagnostics.Windows` package referenced but unused

**Status:** RESOLVED 2026-05-20 (iter-479, commit d86435e). Reviewer-prescribed fix path (a) "drop" applied — single-line `<PackageReference Include="BenchmarkDotNet.Diagnostics.Windows" Version="0.13.12" />` removed from `Benchmarks.csproj`. No referencing code changes needed (only `[MemoryDiagnoser]` is attached, which lives in the core BDN package). Build verified clean after edit. Discoverer: adversarial-reviewer at 7eb7020 (iter-475). Closed in iter-479 on `release/v1.0.2`.

**Location:** `tests/SwfocTrainer.Benchmarks/SwfocTrainer.Benchmarks.csproj:30`

**Issue:** The csproj includes `<PackageReference Include="BenchmarkDotNet.Diagnostics.Windows" Version="0.13.12" />` but the only diagnoser attached in `ObjAddrParserBenchmarks.cs` is `[MemoryDiagnoser]`, which is part of the core BDN package — NOT Diagnostics.Windows. The Diagnostics.Windows package adds `[EtwProfiler]` / `[ConcurrencyVisualizerProfiler]` / `[NativeMemoryProfiler]` and ~5 MB of native ETW deps. None are referenced.

**Suggested fix:** Either (a) drop the `BenchmarkDotNet.Diagnostics.Windows` package reference (clean), or (b) attach `[EtwProfiler]` to `ObjAddrParserBenchmarks` if ETW data is actually wanted (more work; only justified if perf-benchmarker hat needs ETW). Path (a) preferred for iter-475 scope — perf-benchmarker spec doesn't appear to require ETW.

**Suggested location:** `Benchmarks.csproj` single-line delete.

---

### WATCH-LIST — Skipped property tests with fabricated TODOs (codification candidate)

**Status:** WATCHING. Single instance from 7eb7020 (iter-475) — does not yet meet 2/3 codification trigger per iter-337 meta-rule precedent.

**Pattern shape:** A `[Property(..., Skip = "...")]` attribute is added with a TODO comment claiming FsCheck "found an edge case" / "found a counter-example" / "surfaced a parser bug", but the claimed bug either (a) cannot actually occur per the production source (the parser unconditionally rejects the input class), or (b) misinterprets intentional documented design as a bug. The skipped property ships permanent misinformation in test code: future readers believe the production code has known issues when it doesn't.

**Recorded instances:**
1. iter-475 — `TryParse_OfNullOrEmpty_AlwaysFails` claims "Success=true on whitespace input"; parser source `ObjAddrParser.cs:43-46` unconditionally returns Success=false on IsNullOrWhiteSpace. Most likely cause: author confused FsCheck's "not enough valid samples" error (Prop.When discards too many random inputs) for a counter-example.
2. iter-475 — `TryParse_RoundTrip_Decimal` claims "decimal vs hex default needs fixing"; parser source `ObjAddrParser.cs:19-26` documents "numeric-only strings are interpreted as hex" as the INTENTIONAL contract per v1.0.2 Cross-Cutting #2.

**Promotion criteria:** If a second iter ships skipped tests with fabricated/contract-violating TODO claims, codify a Tier-4 rule. Candidate name: `feedback_skipped_test_fake_coverage.md`. Tier-4 because it's review-process meta (catches a class of overclaim that survives commit-time review but pollutes the test corpus).

**Verification command for future reviewers:** When a PR adds `[Property(..., Skip = "...")]` or `[Fact(Skip = "...")]`, ALWAYS read the production code path referenced in the TODO and confirm the claimed gap is real before approving.

---

## iter-476 b064ddb review — 1 new LOW (naming polish)

**Source:** `.ralph/state/review_log.md` entry 2026-05-20T21:50:00Z (b064ddb APPROVED)

### LOW — `TryParse_NumericString_IsInterpretedAsHex` name imprecision
**Location:** `tests/SwfocTrainer.Property.Tests/ObjAddrParserProperties.cs:70`
**Issue:** Method name says "NumericString" but the body uses `addr.ToString("X")` which produces hex-formatted strings (containing A-F for `n.Item >= 10`). Internal variable `hexNoPrefix` already identifies the input nature correctly. For NonNegativeInt n=49374, ToString("X") = "C0DE" — not a "numeric string" in the conventional decimal sense.
**Suggested fix:** Rename to `TryParse_HexNoPrefix_RoundTrips` (or `TryParse_NoPrefixInput_IsInterpretedAsHex`) and update the inline rationale comment block to match. Single test file, single rename + 1-2 doc-line touch.
**Status:** RESOLVED 2026-05-21 (iter-487, commit pending). Renamed to `TryParse_HexNoPrefix_RoundTrips` per reviewer's primary suggestion — matches the internal `hexNoPrefix` variable name and accurately describes the v1.0.2 Cross-Cutting #2 invariant being pinned (hex strings without "0x" prefix round-trip via `addr.ToString("X")`). Inline rationale comment extended with the iter-487 rename note + back-pointer to the prior name so future reviewers can trace the iter-477 review → iter-487 close cycle. Gates: build 0W/0E in 3:23, `ObjAddrParserProperties` filter 7/0/0 in 75 ms, ledger lint 0/0 @ 341, bridge harness 1100/0 (241 consecutive iters). Discoverer: adversarial-reviewer at b064ddb (iter-476 b064ddb review, filed 2026-05-20T21:50Z). Closed in iter-487 on `release/v1.0.2`.


---

## Findings from 9298748 adversarial review (iter-482 project-wide codename-leak sweep) — 2026-05-21T00:35:00Z

### MEDIUM — Test-class name + commit narrative oversell scope
**Location:** `tests/SwfocTrainer.Tests/Regression/Iter482PresetCodenameLeakSweepTests.cs` (class name + XML doc); commit 9298748 message body ("project-wide", "global codename-leak fact across all presets").
**Issue:** All three facts iterate only `LuaPlaygroundTabViewModel.Iter100to113Presets`. The class name and prose imply project-wide guarantees; the actual surface is one collection on one VM. If `LuaPlaygroundTabViewModel` ever gains a second `IList<Preset>` property, or if other tab VMs surface preset dropdowns with iter-N codenames, those collections are unguarded.
**Suggested fix:** Pick ONE of:
  - (a) **Rename + scope-honest** — rename class to `Iter100to113PresetCodenameLeakSweepTests`, update XML doc + commit-message narrative (impossible retroactively, but a follow-up commit can clarify) to drop "project-wide" claim. Cycle cost: ~5 min.
  - (b) **Reflection-based extension** — refactor the three facts to enumerate via reflection over all `public IEnumerable<Preset>`-typed properties on `LuaPlaygroundTabViewModel` (and optionally all tab VMs assignable from `ObservableBase`). Honours the "project-wide" claim. Cycle cost: ~30 min including new fixture + extension tests.
**Status:** RESOLVED 2026-05-21 (iter-484). Reviewer fix option (a) "Rename + scope-honest" applied: file + class renamed to `Iter100to113PresetCodenameLeakSweepTests`, XML doc summary rewritten with explicit SCOPE block disclaiming project-wide guarantees, reflection-based extension explicitly captured as future arc. Cycle cost: ~5 min (in line with reviewer estimate). Discoverer: adversarial-reviewer at 9298748. Closed in iter-484 on `release/v1.0.2`.

### MEDIUM — Script-body codename deferral lacks tracked placeholder
**Location:** `tests/SwfocTrainer.Tests/Regression/Iter482PresetCodenameLeakSweepTests.cs` (no `[Fact(Skip = "...")]` placeholder); scratchpad iter-482 close-out (where the deferral is currently documented).
**Issue:** Lua script bodies (the second `new(...)` argument that pastes into the editor pane when the operator selects a preset) still contain `-- iter 267-268:` / `iter-99` / `iter-100` / `iter-225` substrings. The script-body is operator-visible per iter-388's "operator-visible-only" scope — it renders in the editor pane on selection. Author defers this to a "future arc" in scratchpad + commit narrative; nothing in code marks the gap.
**Suggested fix:** Add a `[Fact(Skip = "iter-482 future arc: script-body codename sweep — see polish_backlog_2026-05-20.md")]` placeholder in the same test file, with a one-paragraph XML doc citing iter-388 and explaining why the sweep is split (label vs body). The skipped fact serves as a code-side breadcrumb for future reviewers; deletion of the Skip attribute is the natural commit boundary for the arc.
**Status:** RESOLVED 2026-05-21 (iter-484). New skipped Fact `ScriptBodyCodenameSweep_PlaceholderForFutureArc` added to the renamed `Iter100to113PresetCodenameLeakSweepTests` class with a 4-step XML-doc playbook for the future arc: drop Skip → mirror regex over `p.Script` → rewrite violating bodies → close backlog entry. Deletion of the Skip attribute IS the arc's natural entry point. Discoverer: adversarial-reviewer at 9298748. Closed in iter-484 on `release/v1.0.2`.

### LOW — Regex `iter[ -]?\d+` lacks word boundaries
**Location:** `tests/SwfocTrainer.Tests/Regression/Iter482PresetCodenameLeakSweepTests.cs` (`NoPresetLabelContainsIterDigitsSubstring`, line of regex declaration).
**Issue:** With `RegexOptions.IgnoreCase`, `iter[ -]?\d+` matches innocuous substrings like `filter1`, `rerouter5`, `writer42`, `transmitter9`. The current label corpus is clean of these, but the fact is intended as a forward drift catcher — false positives would block legitimate future labels containing these substrings.
**Suggested fix:** Tighten to `\biter[ -]?\d+\b`. Trivial edit; zero cost; no impact on current passing state. Reviewer's strongest LOW — easy fix, real surface.
**Status:** RESOLVED 2026-05-21 (iter-484). Regex tightened to `\biter[ -]?\d+\b` in the now-renamed `Iter100to113PresetCodenameLeakSweepTests.NoPresetLabelContainsIterDigitsSubstring`. Inline comment block updated to explain the boundary intent. Test still passes (current label corpus clean of both substring and word-bounded form). Discoverer: adversarial-reviewer at 9298748. Closed in iter-484 on `release/v1.0.2`.

### LOW — Test fixture wastes 3 simulator startups
**Location:** `tests/SwfocTrainer.Tests/Regression/Iter482PresetCodenameLeakSweepTests.cs` (`CreateVm` helper called by each of the three facts).
**Issue:** Each fact spins up `SwfocSimulator` + named-pipe server + `V2BridgeAdapter` purely to construct the VM. The presets are static initializer data; none of these tests exercise the bridge. ~1-2s waste per run × N runs across the iter cadence.
**Suggested fix:** Refactor to either (a) a class-level static accessor that constructs the VM once, OR (b) pass a no-op `IV2BridgeAdapter` double (simpler — `Substitute.For<IV2BridgeAdapter>()` if the codebase already uses NSubstitute, or a hand-rolled fake otherwise). Eliminates the per-test sim startup.
**Status:** DEFERRED 2026-05-21 (iter-484). The same `CreateVm` idiom is shared across 6+ peer test files (`Iter183`, `Iter467`, `Iter468`, `Iter469`, `Iter470`, `Iter100to113`). Refactoring just this one file would create a per-iter-46x/47x/48x convention split that out-weighs the ~3-6 s per-run savings. Option (b) is blocked at the source: `V2BridgeAdapter` is `sealed` and `LuaPlaygroundTabViewModel` accepts the concrete type — no interface to fake against. Promotes to its own multi-file fixture-pattern arc when bandwidth permits (estimated ~30-45 min covering all 6 files + introducing a shared `LuaPlaygroundPresetFixture : IClassFixture<>` helper). Discoverer: adversarial-reviewer at 9298748.

### LOW — Commit message "54 entries pinned" vs actual 48 in HashSet
**Location:** `feat(iter-482): ...` commit message body (sentence "Catches new `[NNN]` additions outside the sweep cadence; the allowlist must be extended (with a rationale comment) before any new prefix lands. **54 entries pinned as of iter-482.**"); `Iter482PresetCodenameLeakSweepTests.cs` `AllowlistedBracketedPrefixes` HashSet definition.
**Issue:** Manual count of HashSet contents: 2 + 7 + 3 + 16 + 7 + 3 + 5 + 2 + 2 + 1 = **48**. Commit message + scratchpad both claim 54. Cosmetic doc drift; doesn't affect functionality (invariant #3 self-corrects future stale entries), but the next reviewer who treats the commit message as ground truth will be off by 6.
**Suggested fix:** Update scratchpad + future commit narrative on next allowlist edit to use the actual count. Commit message itself is immutable; no rewrite needed.
**Status:** RESOLVED 2026-05-21 (iter-484). HashSet XML-doc updated with the verified count: "Count (verified iter-484 manual recount post 9298748 review): 48." Future narrative uses this corrected count. Original 9298748 commit message remains immutable (off-by-6) as a historical artifact; the in-source XML-doc is now ground truth for any reviewer who lands on the file. Discoverer: adversarial-reviewer at 9298748. Closed in iter-484 on `release/v1.0.2`.

---

## From review of `8f97e1d` (iter-484 drain of 9298748 review backlog)

Adversarial review at 8f97e1d found 4 MEDIUM + 4 LOW (subagent flagged 1 HIGH + 3 MEDIUM + 4 LOW; the adversarial-reviewer hat demoted the HIGH to MEDIUM with rationale recorded in `.ralph/state/review_log.md`). Verdict: APPROVED, all findings non-blocking.

### MEDIUM — Misfiled directory: `Regression/` vs `DriftCatchers/`
**Location:** `tests/SwfocTrainer.Tests/Regression/Iter100to113PresetCodenameLeakSweepTests.cs` (the iter-484-renamed file) plus the 6 peer files using the same drift-catcher shape (`Iter183`, `Iter467`, `Iter468`, `Iter469`, `Iter470`, plus iter-471/472/473/474 cycle files).
**Issue:** Per CLAUDE.md "Regression Guard Discipline": regression tests must fail on the OLD broken form AND pass on the NEW form, both in the same file. The Iter100to113 file's three facts have neither — they're forward drift-catchers that PASS today and FAIL on future drift. Same shape applies to the 6 peer files. The `Regression/` directory name miscounts these as regression tests when they're not. A future reader auditing regression coverage will get a wrong total.
**Suggested fix:** Introduce a `tests/SwfocTrainer.Tests/DriftCatchers/` (or `Invariants/`) namespace + move all forward-drift-catcher files. Larger arc (~6-10 files), so probably a single-iter directory move + namespace rename + csproj update. Risk: zero (test-only namespace rename). Recommend bundling with M1 (CreateVm fixture) since both touch the same 6-file cluster.
**Status:** OPEN. Discoverer: adversarial-reviewer at 8f97e1d.

### MEDIUM — Compound-word codename forms not caught by `\biter[ -]?\d+\b`
**Location:** `Iter100to113PresetCodenameLeakSweepTests.cs::NoPresetLabelContainsIterDigitsSubstring` (invariant #1 regex, line ~108).
**Issue:** The `\b` boundaries added in iter-484 (to fix the 9298748 LOW about false positives on `filter1`/`rerouter5`/`writer42`/`transmitter9`) mean labels like `subiter-100`, `xiter5`, or `superiter-200` (word-char preceding `iter`) would NOT trip the assertion. The current `\b` is the right pragmatic call because (a) all 88 iter-388 codename instances are PREFIX-position, (b) zero compound-form labels exist in the corpus, (c) removing `\b` re-introduces the false-positive regression that was just fixed. Watch-item: if a compound-form label ever lands, the regex needs a future arc that handles both directions without regression.
**Suggested fix:** Defer until a real compound-form drift instance lands. If/when triggered, candidate approaches: (a) lookahead-based `(?<![A-Za-z])iter[ -]?\d+(?![A-Za-z]\d*)` (preserves false-positive containment, catches compound forms), or (b) two separate assertions (one with `\b`, one with content scan + explicit allowlist for `filter`/`rerouter` etc.). Stick with `\b` until reality forces the issue.
**Status:** OPEN (watch-item, low probability). Discoverer: adversarial-reviewer at 8f97e1d (severity demoted from subagent HIGH per review-log rationale).

### MEDIUM — Invariant #3 failure-message remediation hint
**Location:** `Iter100to113PresetCodenameLeakSweepTests.cs::Allowlist_OnlyContainsActuallyPresentPrefixes` (invariant #3, FluentAssertions `.Should().BeEmpty(...)` call).
**Issue:** When invariant #3 fails on a legitimate production drainage (operator removed the last `[NNN]` preset in a sweep, forgot to shrink the allowlist), the failure-message should explicitly tell them: "delete the stale entry from `AllowlistedBracketedPrefixes`." Spot-check shows the docstring DOES say this, but the assertion's `because` argument is the path that surfaces on failure (xunit doesn't show docstrings on assertion failure). A reviewer who treats the failure as "real production drainage is broken" might revert the production-side change.
**Suggested fix:** Ensure the BeEmpty `because` includes explicit remediation text (e.g. "...stale entry. If you just drained the last production label using this prefix in a sweep, ALSO remove the prefix from `AllowlistedBracketedPrefixes` in this file. This invariant exists to force allowlist-shrinkage symmetry with sweep-drainage."). Visual scan of the diff suggests this is already present in spirit but worth a precision pass.
**Status:** RESOLVED 2026-05-21 (iter-485). `because` text expanded from 3 lines to 14 lines covering: explicit "REMEDIATION: delete the stale entry" lead-in, allowlist-shrinkage-symmetry rationale, explicit "EXPECTED outcome of legitimate sweep work and is NOT a regression" anti-reversion guard ("do NOT revert the production-side drainage"), and the same (a)/(b) cause taxonomy with identical-fix collapse. Bundled L4 ("FluentAssertions because audit") into this same edit per reviewer recommendation. Discoverer: adversarial-reviewer at 8f97e1d. Closed in iter-485 on `release/v1.0.2`.

### MEDIUM — Sim-startup fixture refactor (cross-file arc, re-affirmed)
**Location:** `Iter100to113PresetCodenameLeakSweepTests.cs::CreateVm` helper + 5 peer files using the same idiom.
**Issue:** Re-affirms the iter-484 DEFERRED 9298748 LOW. Each Fact starts `SwfocSimulator` + named-pipe server + `V2BridgeAdapter` purely to construct the VM; none of the facts exercise the bridge. ~3-6s waste per CI run on this one file × 6 peers × N runs is non-trivial across hours. The actual blocker is the cross-file scope (6+ peer files: `Iter183`, `Iter467`, `Iter468`, `Iter469`, `Iter470`, `Iter100to113`) — changing this file alone would create a per-file convention split.
**Suggested fix:** Multi-file arc. Two design options at apply time: (a) **shared fixture using real adapter** — extract `tests/SwfocTrainer.Tests/Fixtures/LuaPlaygroundPresetFixture.cs : IClassFixture<>` that calls the same `CreateVm()` factory once and shares it across the 6 peer files; works regardless of `V2BridgeAdapter` sealing; ~30-45 min apply. (b) **interface extraction + fake adapter** — extracts `IV2BridgeAdapter` on the App.V2.Infrastructure surface (requires unsealing `V2BridgeAdapter` first) + introduces no-op fake; more thorough but requires the unsealing change. Pairs naturally with the `DriftCatchers/` namespace move (same 6-file scope). **Note**: the iter-485 in-file docstring incorrectly cited the sealed adapter as the blocker; corrected here in iter-486 — sealing only blocks mocking, not shared-fixture extraction.
**Status:** OPEN (re-affirmed from 9298748 LOW DEFERRED; iter-486 rationale correction). Discoverer: adversarial-reviewer at 8f97e1d (originally 9298748).

### LOW — Skip-message back-reference durability
**Location:** `Iter100to113PresetCodenameLeakSweepTests.cs::ScriptBodyCodenameSweep_PlaceholderForFutureArc` `[Fact(Skip = "...see knowledge-base/polish_backlog_2026-05-20.md MEDIUM 'Script-body codename deferral'. Deletion of this Skip attribute IS the arc's natural entry point.")]`.
**Issue:** Cites a date-stamped backlog file. If the backlog file is archived/renamed (project has `archive/` convention; current pattern is to add date suffix on rotation), the Skip pointer rots and becomes dead text. The exact MEDIUM heading text 'Script-body codename deferral' doesn't appear verbatim in the current backlog at this date — closest match is the 9298748 MEDIUM 2 narrative. Minor — the Skip placeholder's main purpose (code-side breadcrumb) is intact; the cross-reference precision is the polish.
**Suggested fix:** Either (a) anchor to a more durable pointer (`feedback_internal_codename_in_tooltips_drift.md` is a stable filename in `~/.claude/projects/.../memory/`), or (b) update the citation when the backlog gets rotated. Defer until next backlog rotation forces the question.
**Status:** OPEN. Discoverer: adversarial-reviewer at 8f97e1d.

### LOW — Placeholder Skip-fact body lacks fail-on-activation guard
**Location:** Same file, `ScriptBodyCodenameSweep_PlaceholderForFutureArc` empty body.
**Issue:** Empty body means if a future operator deletes the `Skip = "..."` attribute (the documented arc-entry-point), the test passes (no assertions, no failures) instead of going red. The intent is that deleting Skip is the start of work, but a "passes with empty body" outcome could mask incomplete arc work. Subtle pattern improvement.
**Suggested fix:** Change body to `Assert.Fail("Not yet implemented — see Skip attribute message for arc playbook. When you delete the Skip attribute, replace this Fail with the actual regex-over-script-bodies assertion.");`. Then deleting Skip puts the test in a deliberately-red state, forcing arc completion. ~1 LoC change.
**Status:** RESOLVED 2026-05-21 (iter-485). Empty body replaced with `Assert.Fail("Script-body codename sweep arc activated but not implemented. Replace this Assert.Fail with the actual assertion — mirror the NoPresetLabelContainsIterDigitsSubstring shape but apply the `\\biter[ -]?\\d+\\b` regex to `p.Script` instead of `p.Label`. See the Skip attribute message for the 4-step playbook.");`. Skip attribute remains; the placeholder still shows as `[SKIP]` in xunit until a future arc deletes the Skip. Deletion of Skip then puts the test deliberately RED until the arc author replaces Assert.Fail with the actual assertion. Discoverer: adversarial-reviewer at 8f97e1d. Closed in iter-485 on `release/v1.0.2`.

### LOW — `using SwfocTrainer.Core.Services;` may be unused
**Location:** `Iter100to113PresetCodenameLeakSweepTests.cs` line 5 (top using block).
**Issue:** Subagent flagged this as possibly unused. Not verifiable from the diff alone — `NamedPipeLuaBridgeClient` lives in `SwfocTrainer.App.V2.Infrastructure` already (separate using). Quick grep at next touchpoint resolves: if the `Core.Services` namespace contains no types referenced by this file, the using is dead. Build is 0W/0E with treat-warnings-as-errors disabled for unused-using, so this is style polish, not a build defect.
**Suggested fix:** 30-second grep at next touchpoint; remove if unused. Or bundle into a broader "unused-using sweep" arc across the test project if the project ever sees an audit of that shape.
**Status:** NOT-A-DEFECT 2026-05-21 (iter-485). 30-second grep performed during iter-485 polish work: `SwfocTrainer.Core.Services` namespace contains `NamedPipeLuaBridgeClient.cs` (verified at `src/SwfocTrainer.Core/Services/NamedPipeLuaBridgeClient.cs`), and `CreateVm()` at line 112 instantiates it via `new NamedPipeLuaBridgeClient(sim.PipeName, 1500, 1500)`. The using is required for compilation. Subagent flag was a false positive on the diff (the using line wasn't part of the diff hunk, just nearby context). Docstring updated in-file to record this NOT-A-DEFECT finding so future reviewers don't re-flag. Discoverer: adversarial-reviewer at 8f97e1d. Closed in iter-485 on `release/v1.0.2`.

### LOW — FluentAssertions `because` messages are extensive but worth a precision audit
**Location:** All three `BeEmpty(...)` calls in `Iter100to113PresetCodenameLeakSweepTests.cs`.
**Issue:** Spot-check at apply time confirms the assertion messages are extensive and operator-actionable per the iter-484 docstring rewrite. This is the standard FluentAssertions pattern; not a defect. Recording here in case a future review wants to compact them OR confirm specific remediation phrasing matches M3 above.
**Suggested fix:** Audit at next touchpoint to confirm M3's remediation-text concern is satisfied by the existing `because` strings. If not, fold the precision pass into M3.
**Status:** RESOLVED 2026-05-21 (iter-485). Bundled into M3 above per reviewer recommendation ("Audit at next touchpoint to confirm M3's remediation-text concern is satisfied"). Invariant #1 and #2 `because` strings already include actionable remediation guidance (drop label codename / extend AllowlistedBracketedPrefixes); invariant #3's rewrite covers the missing precision. No further audit needed at this touchpoint. Discoverer: adversarial-reviewer at 8f97e1d. Closed in iter-485 on `release/v1.0.2`.


## Iter-485 (f18bc78) adversarial-review findings — 2026-05-21 — 2 MEDIUM + 4 LOW

Adversarial review at f18bc78 found 2 MEDIUM + 4 LOW (subagent verdict matched at 0 CRITICAL / 0 HIGH / 2 MEDIUM / 4 LOW with no judgment-layer demotions or promotions). Verdict: APPROVED, all findings non-blocking. Approval count 7 → 8.

### MEDIUM — Class-doc archaeology accretion (95-line `<summary>` block)
**Location:** `tests/SwfocTrainer.Tests/Regression/Iter100to113PresetCodenameLeakSweepTests.cs` lines 11-105 (the `<summary>` XML doc block on the class declaration).
**Issue:** The class-level doc now spans 95 lines with chronological per-iter resolution-status bullets stacked for iter-470 / iter-482 / iter-484 / iter-485 in append-only fashion. Iter-485 added 21 lines of which 4 bullets (the DEFERRED iter-485 items) duplicate content already in this polish backlog file. The class-doc has crossed the inflection point from "scope-honesty contract" (the iter-484 SCOPE block, which earned its keep) to "archaeology layer that duplicates polish_backlog".
**Suggested fix:** Next iter that touches this file, compact the doc header by (a) keeping the active SCOPE-honesty contract from iter-484 + the currently-OPEN drift-catcher list, (b) moving the per-iter RESOLVED bullet history (iter-470 / iter-482 / iter-484 / iter-485) to git-blame-only — `git log -p` already preserves the audit trail. Net delta: ~95 lines → ~20 lines. Or, if the trail has real value for future readers, move it to a dedicated `tests/SwfocTrainer.Tests/Regression/DRIFT_CATCHER_HISTORY.md` adjacent file. Either approach passes the "would a fresh reader at iter-500 understand this faster?" test.
**Status:** RESOLVED 2026-05-21 (iter-486). Applied the dedicated-adjacent-file approach: created `tests/SwfocTrainer.Tests/Regression/DRIFT_CATCHER_HISTORY.md` with curated per-iter resolution history (iter-470/482/484/485/486) + provenance + "when to update" guidance. Compacted in-file class `<summary>` block from 95 lines to ~35 lines: SCOPE-honesty contract (kept), 3-invariant summary (kept), currently-OPEN sibling drift surface pointers (kept), per-iter history (extracted to HISTORY.md). Net delta on the class doc: -60 lines source. Discoverer: adversarial-reviewer at f18bc78. Closed in iter-486 on `release/v1.0.2`.

### MEDIUM — DEFERRED rationale hand-waves "sealed V2BridgeAdapter" for sim-startup fixture
**Location:** `Iter100to113PresetCodenameLeakSweepTests.cs` XML doc header lines 96-97 (the iter-485 M4 DEFERRED rationale) PLUS the prior polish_backlog entry "MEDIUM — Sim-startup fixture refactor" above.
**Issue:** The DEFERRED rationale "multi-file arc blocked by sealed V2BridgeAdapter (needs IV2BridgeAdapter interface extraction first)" misrepresents the real blocker. A shared `IClassFixture<>` xunit fixture would call the same `CreateVm()` factory regardless of whether `V2BridgeAdapter` is sealed — sealing doesn't block fixture extraction, only mocking. The actual blocker is the cross-file scope (6+ peer files, same as M1's directory move). Citing the sealed adapter creates the impression that an interface-extraction arc is a prerequisite when in reality the simpler refactor is to extract a shared fixture without touching the adapter at all.
**Suggested fix:** Update the polish_backlog "Sim-startup fixture refactor" entry above to drop the "sealed V2BridgeAdapter" clause OR substantiate it (if the real issue is that the fixture needs to fake bridge responses for some peer files, say so). The iter-485 in-file docstring line 96-97 can stay or be tightened to "multi-file arc (6+ peer files)". When the arc actually lands, the choice between (a) shared fixture using real adapter (simpler) and (b) interface extraction + fake adapter (more thorough but blocked by sealing) is the real decision.
**Status:** RESOLVED 2026-05-21 (iter-486). Polish_backlog "Sim-startup fixture refactor" entry above rewritten: blocker correctly identified as cross-file scope (6+ peer files), not sealing; two design options enumerated at apply time (shared fixture using real adapter — simpler, no unsealing — vs interface extraction + fake adapter — more thorough, requires unsealing); explicit "iter-485 in-file docstring incorrectly cited the sealed adapter as the blocker; corrected here in iter-486" note added. In-file docstring on the test class was compacted in this same iter-486 (per MEDIUM above), removing the erroneous line 96-97 entirely — the per-iter history now lives in `DRIFT_CATCHER_HISTORY.md` where the iter-485 entry simply says "see iter-486 entry for corrected blocker analysis". Discoverer: adversarial-reviewer at f18bc78. Closed in iter-486 on `release/v1.0.2`.

### LOW — `Assert.Fail` + `[Fact(Skip = "...")]` semantic is xunit-version-fragile
**Location:** `Iter100to113PresetCodenameLeakSweepTests.cs::ScriptBodyCodenameSweep_PlaceholderForFutureArc`.
**Issue:** The placeholder relies on xunit's `Skip` evaluation order (Skip checked BEFORE body execution → Assert.Fail dormant under Skip). This is correct for xunit v2 and v3, but no version pin in the doc. If the project ever migrates to a test framework that evaluates Skip differently (or a custom xunit-extensibility plugin reorders execution), the placeholder turns red prematurely. Defensive engineering for a future migration that may never happen.
**Suggested fix:** Add a 1-line doc comment in the placeholder body: `// Requires xunit v2/v3 semantics: Skip is evaluated before body, so Assert.Fail is dormant while Skip is set.` Or no fix — accept the version coupling and re-evaluate IF/WHEN a framework migration is proposed.
**Status:** RESOLVED 2026-05-21 (iter-486). Reviewer-prescribed 1-line (expanded to 5 lines for readability) doc comment added in the `ScriptBodyCodenameSweep_PlaceholderForFutureArc` body: documents the xunit v2/v3 Skip-before-body evaluation order coupling + future-framework-migration re-evaluation trigger. Discoverer: adversarial-reviewer at f18bc78. Closed in iter-486 on `release/v1.0.2`.

### LOW — Invariant #3 `because` wall-of-text (~700-char single line) with ~30% redundancy
**Location:** `Iter100to113PresetCodenameLeakSweepTests.cs::Allowlist_OnlyContainsActuallyPresentPrefixes` lines ~258-274.
**Issue:** The iter-485 expansion added "REMEDIATION:" lead-in (correct call) AND a trailing "Likely causes (a)/(b)" taxonomy that duplicates REMEDIATION intent. FluentAssertions concatenates `because` into one line in test runner output; CI failure summary will be one ~800-char line. Readable in xunit's full-message view, ugly in `dotnet test --logger console`. The (a)/(b) cause taxonomy ("sweep forgot to remove…" / "preset was deleted in unrelated work") is genuinely useful for triage but says the same thing as "REMEDIATION: delete the stale entry" with extra words.
**Suggested fix:** Trim the (a)/(b) taxonomy to one line: "Both `(a) sweep forgot to drain` and `(b) preset deleted in unrelated work` reduce to the same fix: drop the stale entry." Net delta: ~3 lines shorter, identical signal-to-noise. Or no fix — verbose is acceptable for a once-per-failure assertion that's meant to prevent reviewer panic-reverts.
**Status:** RESOLVED 2026-05-21 (iter-486). Reviewer-prescribed one-line collapse applied: the (a)/(b) cause taxonomy compressed to "Both `(a) sweep forgot to drain` and `(b) preset deleted in unrelated work` reduce to the same fix: drop the stale entry from `AllowlistedBracketedPrefixes`." Net delta: -3 lines on the `because` text. REMEDIATION lead-in + allowlist-shrinkage-symmetry rationale + anti-reversion guard ("do NOT revert the production-side drainage") all preserved per iter-485 M3 design. Discoverer: adversarial-reviewer at f18bc78. Closed in iter-486 on `release/v1.0.2`.

### LOW — Regex string escape verification (no defect)
**Location:** `Iter100to113PresetCodenameLeakSweepTests.cs::ScriptBodyCodenameSweep_PlaceholderForFutureArc` Assert.Fail message, the `"\biter[ -]?\d+\b"` substring.
**Issue:** Recording the audit pass. The double-backslash escaping `"\b"` correctly produces the regex `\b` (word boundary) when the string is emitted to the runner output. No defect.
**Suggested fix:** None. Audit-pass recording for future reviewers.
**Status:** NOT-A-DEFECT 2026-05-21 (iter-485). Closed at filing. Discoverer: adversarial-reviewer at f18bc78.

### LOW — Commit-message count drift (+43/-8 narrative vs +51/-8 actual)
**Location:** Commit f18bc78 message body says "Files: 1 file, +43 / -8". `git show --stat HEAD` reports `51 insertions(+), 8 deletions(-)`.
**Issue:** Recurring pattern — iter-482 commit said "54 entries pinned" when actual was 48; iter-485 commit narrative says +43/-8 when actual is +51/-8. Trivial doc drift, but signals the author isn't running `git diff --stat` as a final sanity check before writing the commit message. The 8-line undercount maps cleanly to the 8 bullets of XML doc additions (4 RESOLVED + 4 DEFERRED) not being included in the "narrative" tally; suggests the author counted the semantic-change lines (because-text + Assert.Fail body) but missed the doc-header additions.
**Suggested fix:** Pre-commit reflex: `git diff --stat --cached` as the last step before authoring the commit message body. Or no fix — the discrepancy is doc-only, doesn't affect functionality, and the pattern is auto-correcting (each cycle of "next reviewer notices the drift" pushes back on the next author). Note this is the 2nd consecutive iter where the count-drift LOW fires (iter-482 + iter-485), so the pattern may be worth a CLAUDE.md guardrail addition if it lands a 3rd time.
**Status:** OPEN (pattern-watch). Discoverer: adversarial-reviewer at f18bc78. Watch-tier de-escalation candidate as of iter-487: iter-487 commit's "+11 / -5" body matched `git show --stat` exactly per the pre-commit reflex; pattern broke the 2-iter run. One more clean iter (iter-488) closes this LOW as watch-tier resolved.


## Iter-487 (08e0c59) adversarial-review findings — 2026-05-21 — 0 MEDIUM + 3 LOW

Adversarial review at 08e0c59 found 0 MEDIUM + 3 LOW (subagent verdict matched at 0 CRITICAL / 0 HIGH / 0 MEDIUM / 3 LOW with no judgment-layer demotions or promotions). Verdict: APPROVED, all findings non-blocking. Approval count 8 → 9. Cleanest verdict in loop history (3 LOWs only, no MEDIUMs).

### LOW — Iter-framing inconsistency between commit body and inline comment
**Location:** Commit body of 08e0c59 vs `tests/SwfocTrainer.Property.Tests/ObjAddrParserProperties.cs` inline comment lines 65-75 (after the rename).
**Issue:** Commit body says "iter-476 b064ddb adversarial review LOW (filed 2026-05-20T21:50Z)" using originating-commit framing — iter-476 was when b064ddb landed. Inline comment says "iter-477 b064ddb adversarial review LOW (naming polish)" using review-iter framing — iter-477 was when the review verdict actually fired against b064ddb. Both reference the same b064ddb artifact + same LOW finding, so neither is "wrong" — but the two framings produce divergent grep hits for the same provenance trail. A future reviewer who searches `grep iter-476` finds the commit message; one who searches `grep iter-477` finds the comment; neither alone finds both.
**Suggested fix:** Pick a project-wide convention. Review-iter framing (iter-477) is more useful because the comment lives at the fix site and the review verdict is what generated the rename ask; originating-commit framing (iter-476) reflects when the imprecise name was introduced. If a future review-log scan turns up this drift in another file, codify under iter-337 meta-rule (3rd-instance trigger). Until then: non-blocking, audit-pass record only.
**Status:** OPEN (cosmetic). Discoverer: adversarial-reviewer at 08e0c59.

### LOW — Rationale example understates the contract scope
**Location:** `Iter100to113PresetCodenameLeakSweepTests.cs` — wait, no, this is `ObjAddrParserProperties.cs` lines 67-69 (the new rationale text after the rename).
**Issue:** The inline comment justifies the rename with "addr=10 → 'A'" as the failure-of-the-old-name example. The property tests `NonNegativeInt` spanning 0..int.MaxValue — the single-digit-vs-A-F distinction is the narrow case; the broader invariant ("hex-no-prefix round-trips through ToString('X') for ALL non-negative longs") holds uniformly. The example is pedagogically fine for a reader who just wants the gist, but slightly understates the contract scope a property test covers.
**Suggested fix:** Either (a) accept the example as a teaching aid (the method name `TryParse_HexNoPrefix_RoundTrips` already accurately describes the full invariant — the comment is a quick "why the rename" gloss, not a contract spec), or (b) replace "addr=10 → 'A'" with "addr >= 10 → digit-with-letter mix (e.g. 49374 → 'C0DE')" to surface the broader case. Recommend (a) — zero work, signal-to-noise is fine.
**Status:** OPEN (cosmetic). Discoverer: adversarial-reviewer at 08e0c59.

### LOW — Method name now couples to internal variable `hexNoPrefix`
**Location:** `ObjAddrParserProperties.cs::TryParse_HexNoPrefix_RoundTrips` — method signature + the `var hexNoPrefix = addr.ToString("X");` body line.
**Issue:** The new method name `TryParse_HexNoPrefix_RoundTrips` is explicitly justified in the rationale comment by matching the internal variable name `hexNoPrefix`. If a future refactor renames the variable (e.g. to `hexUpper`, `hexFormatted`, `hexNoOxPrefix`, etc.), the method-name/variable-name correspondence cited in the rationale silently breaks — neither test discovery nor execution catches it; only a re-reading of the comment surfaces the drift. Fragile naming dependence. The drift class is the same as iter-482/iter-484/iter-485 docstring-vs-source drift; the asymmetric direction is what makes this LOW rather than MEDIUM (only the comment goes stale, not the test).
**Suggested fix:** Pick one: (a) accept the coupling as intentional pinning — variable rename triggers comment rewrite by the next reviewer who notices, no work needed; (b) add a 1-line pin-comment near the `hexNoPrefix` variable declaration: `// Variable name is load-bearing — TryParse_HexNoPrefix_RoundTrips method name depends on this identifier; rename in lockstep.` Cost: 1 line of comment. Reversibility: trivially undone. Recommend (b) at the next touch of this file; standalone iter cost too small (~3 min) to justify dedicated iter.
**Status:** RESOLVED 2026-05-21 (iter-489, commit pending). Applied reviewer's suggested fix option (b) — 1-line (expanded to 4 lines for readability) pin-comment added immediately above the `hexNoPrefix` variable declaration: documents the load-bearing nature of the variable name, references the rationale block above, and prescribes lockstep rename if a future refactor touches either side. Costs ~4 LoC (reviewer estimate: ~3 min cycle, actual <5 min). Discoverer: adversarial-reviewer at 08e0c59. Closed in iter-489 on `release/v1.0.2`. Codification still a candidate if a second method-name/variable-name coupling pattern fires in another file (iter-337 2/3 trigger).



## Iter-489 (1926626) adversarial-review findings — 2026-05-21 — 1 MEDIUM + 1 LOW

Adversarial review at 1926626 found 1 MEDIUM + 1 LOW (subagent verdict matched at 0 CRITICAL / 0 HIGH / 1 MEDIUM / 1 LOW with no judgment-layer demotions or promotions). Verdict: APPROVED, all findings non-blocking. Approval count 9 → 10 (triple-fire confluence at count=10 milestone: security.tick + ux.tick + mutation.tick — see review_log.md entry).

### MEDIUM — Sha-iter pairing transposition in new inline comment
**Location:** `tests/SwfocTrainer.Property.Tests/ObjAddrParserProperties.cs` lines 81-82 (the new 4-line comment block added in iter-489).
**Issue:** The inline comment ends with "iter-488 b064ddb adversarial-review LOW (08e0c59 review verdict, naming-coupling pin)." Two errors: (a) sha `b064ddb` is iter-477's commit per the same-file rationale block at line 71 (the established same-file convention is `iter-N <implementing-sha>` — iter-476→7eb7020, iter-477→b064ddb), not iter-488's; (b) the citation conflates "review-verdict sha" with "iter sha" by tagging `08e0c59` as the "review verdict" but `08e0c59` is the iter-487 implementing commit, not a review artifact. The actual provenance is "iter-487's commit 08e0c59 was reviewed during the iter-488 adversarial-review pass, which filed the LOW finding that iter-489 (commit 1926626) drains." A future grep for `iter-488 b064ddb` finds nothing meaningful; a future reader of line 71's convention vs line 81's drift sees an unexplained inconsistency in the same file.
**Suggested fix:** Subagent prescription: replace the trailing two lines with `// iter-487 08e0c59 adversarial-review LOW (naming-coupling pin, fix option b).` — one iter→sha pair, matching the same-file convention, dropping the redundant duplicate sha. Cost: ~2 LoC same-file delta. Reversibility: trivially undone. Recommend at next touch of file (~3 min cycle). Watch-flag: if a 2nd file shows the same sha-iter pairing drift, iter-337 2/3 meta-rule promotes to codifiable pattern.
**Status:** RESOLVED at iter-490 04e3946 (paired drain with L1 below; L1's 2-line prescription superseded M1's by collapsing 4 lines to 2 while correcting iter-488 b064ddb → iter-487 08e0c59 pairing). Discoverer: adversarial-reviewer at 1926626.

### LOW — Pin-comment verbosity (4 lines vs reviewer's "1-line" budget)
**Location:** `tests/SwfocTrainer.Property.Tests/ObjAddrParserProperties.cs` lines 79-82 (the new comment block).
**Issue:** iter-487's reviewer prescribed "a 1-line pin-comment near the variable declaration." The iter-489 implementation expanded to 4 lines (load-bearing rationale + same-file cross-ref + lockstep rename guidance + provenance pin). Subagent notes the "see rationale block above" pointer makes the iter-citation on lines 81-82 partially redundant with the iter-487 rename history already in lines 56-74. The 4× expansion is at the upper edge of reasonable; not a defect, but worth a future compaction note.
**Suggested fix:** Subagent prescription: collapse to 2 lines: `// Variable name is load-bearing — method name TryParse_HexNoPrefix_RoundTrips` / `// depends on this identifier; rename in lockstep. (iter-487 08e0c59 LOW pin.)` Net delta: -2 LoC same-file. Pairs naturally with the M1 fix above (single touch of the same comment block). OR accept the 4-line form as project-consistent with prior multi-line pins (e.g. the iter-484 SCOPE-honesty docstring pattern is also multi-line).
**Status:** RESOLVED at iter-490 04e3946 (combined drain with M1 above; subagent's 2-line prescription applied verbatim; net delta -2 LoC same-file matches prediction). Discoverer: adversarial-reviewer at 1926626. Pairs with M1 for combined drainage at next touch.


## Iter-490 (04e3946) adversarial-review findings — 2026-05-21 — 1 MEDIUM + 2 LOW

Adversarial review at 04e3946 found 1 MEDIUM + 2 LOW (subagent verdict matched at 0 CRITICAL / 0 HIGH / 1 MEDIUM / 2 LOW with no judgment-layer demotions or promotions). Verdict: APPROVED, all findings non-blocking. Approval count 10 → 11 (single-tick iter: security.tick only; cadence-5/10/15/20/25 all miss at count=11).

### MEDIUM — Sha-iter pairing drift introduced while fixing the prior drift (2nd same-file instance)
**Location:** `tests/SwfocTrainer.Property.Tests/ObjAddrParserProperties.cs` line 80 (the new collapsed pin-comment).
**Issue:** The iter-490 fix collapsed iter-489's 4-line pin-comment to 2 lines AND corrected the iter-489 M1 sha-iter pairing transposition ("iter-488 b064ddb" → fixed to remove b064ddb entirely). But the replacement reads `(iter-487 08e0c59 LOW pin.)` — where iter-487 was the IMPLEMENTING iter (commit 08e0c59 is iter-487's own commit), and the LOW was filed by iter-488's review of 08e0c59. Per the same-file convention at line 71 ("iter-477 b064ddb adversarial review LOW") + 2 cross-file reference sites (`Iter100to113PresetCodenameLeakSweepTests.cs:104` "iter-484 9298748 adversarial-review LOW fix" + `:247` "iter-485 (from 8f97e1d adversarial-review LOW...)"), the established convention is **REVIEW-iter + REVIEWED-commit-sha**. Under that convention, the pin should read `iter-488 08e0c59 LOW pin`. Same defect class as the iter-489 M1, just less egregious — the implementer faithfully transcribed the reviewer's verbatim prescription, which itself encoded the wrong iter on the wrong side. 2-instance drift now documented in the same file; 3rd-instance trigger from iter-337 2/3 meta-rule rule arms a codifiable pattern (`feedback_sha_iter_pairing_convention.md`) on the next file that breaks the local convention.
**Suggested fix:** Subagent prescription (bundled with L1 below): change line 80's `(iter-487 08e0c59 LOW pin.)` to `(iter-488 08e0c59 adversarial-review LOW pin.)`. Same 2-line budget. Cost: ~12 char delta (rename `iter-487` → `iter-488`, insert `adversarial-review ` qualifier). Reversibility: trivially undone.
**Status:** RESOLVED at iter-491 af4e905 (paired drain with L1 below; subagent's verbatim prescription applied; net delta +1/-1 LoC same-file matches prediction). Discoverer: adversarial-reviewer at 04e3946.

### LOW — Pin-comment phrasing "LOW pin" is novel vs established "adversarial-review LOW"
**Location:** `tests/SwfocTrainer.Property.Tests/ObjAddrParserProperties.cs` line 80 (the new collapsed pin-comment).
**Issue:** Line 71 same-file + 2 cross-file reference sites (`Iter100to113PresetCodenameLeakSweepTests.cs:104` + `:247`) all use the phrase "adversarial review LOW" or "adversarial-review LOW" with the "adversarial-review" qualifier explicit. The qualifier disambiguates from other LOW-severity sources (lint, codacy, semgrep, etc.). The new line 80 shortens to bare `LOW pin`, dropping the qualifier. Cost to restore: ~12 chars (insert `adversarial-review ` between `08e0c59` and `LOW pin`). Still fits the 2-line budget. Pairs naturally with M1 above.
**Suggested fix:** Bundled with M1 fix above: change `(iter-487 08e0c59 LOW pin.)` to `(iter-488 08e0c59 adversarial-review LOW pin.)`. Same-edit drain.
**Status:** RESOLVED at iter-491 af4e905 (combined drain with M1 above; "adversarial-review " qualifier restored per subagent verbatim prescription). Discoverer: adversarial-reviewer at 04e3946.

### LOW — Parenthetical wrapper inconsistency (3 sites = 3 formats, no de-facto convention)
**Location:** Cross-file. Line 71 (this file) uses inline em-dash form: `per iter-477 b064ddb adversarial review LOW (naming polish) — ...`. New line 80 (this file) uses trailing parens-wrapped form: `... rename in lockstep. (iter-487 08e0c59 LOW pin.)`. `Iter100to113PresetCodenameLeakSweepTests.cs:247` uses parens-prefix form: `iter-485 (from 8f97e1d adversarial-review LOW ...)`.
**Issue:** 3 reference sites, 3 distinct wrapper formats — no de-facto convention to anchor on. Not a defect at any individual site; an audit-pass observation that a project-wide style-pin pass could choose ONE and migrate the others. Low priority because the variations don't impair grep-ability (the iter-N and sha-7 tokens are still searchable regardless of wrapper).
**Suggested fix:** None at the per-finding level. If a future style-pin pass selects one format (recommend the trailing parens-wrapped form since it's the most recent and cleanly separates rationale from provenance), migrate the 3 sites in a single same-day pass. Otherwise: audit-pass record only. Pairs with iter-487 LOW 1 (iter-framing inconsistency) as the broader "provenance citation conventions are unstandardized" cluster.
**Status:** OPEN (cosmetic, audit-pass record only). Discoverer: adversarial-reviewer at 04e3946. Cluster with iter-487 LOW 1.



## Iter-491 (af4e905) adversarial-review findings — 2026-05-21 — 1 MEDIUM + 1 LOW (+ 1 audit-confirm)

Adversarial review at af4e905 found 1 MEDIUM + 2 LOW (subagent verdict matched at 0 CRITICAL / 0 HIGH / 1 MEDIUM / 2 LOW with no judgment-layer demotions or promotions; L2 is a confirm-pairing-correct finding, not a defect). Verdict: APPROVED, all findings non-blocking. Approval count 11 → 12 (single-tick iter: security.tick only; cadence-5/10/15/20/25 all miss at count=12 — 2nd consecutive single-tick iter).

### MEDIUM — Intra-file consistency drift: hyphen vs space form of "adversarial review"
**Location:** `tests/SwfocTrainer.Property.Tests/ObjAddrParserProperties.cs` line 80 (the new restored pin-comment).
**Issue:** iter-491 restored the sha-iter pairing correctly (`iter-488 08e0c59` per the established `<REVIEW-iter> <REVIEWED-sha>` convention at same-file lines 18 + 71) and added the "adversarial-review" qualifier per iter-490 L1. But the qualifier was written in the HYPHENATED form (`adversarial-review`) rather than the SPACE form (`adversarial review`) used by all 3 prior same-file precedents (lines 18, 59, 71). Same-file precedent is now 3:1 in favor of space form; new line 80 contradicts the very convention that iter-491's commit body claimed to be following. The hyphenated form IS the cross-file convention (`Iter100to113PresetCodenameLeakSweepTests.cs:104/220/247`), so the drift class is "intra-file convention contradicted by cross-file convention; same-file authority should win when both exist." iter-491's implementer faithfully transcribed iter-490 subagent's verbatim prescription, which itself used the hyphenated form — same root-cause shape as iter-490 M1 (defect in reviewer prescription, faithfully transcribed by implementer). 3rd instance now in same-file pin-comment convention drift cluster (iter-489 M1 sha-iter pairing direction + iter-490 M1 sha-iter pairing target + iter-491 M1 token-format consistency). **iter-337 2/3 meta-rule trigger: codification ARMED for `feedback_pin_comment_convention_drift.md` on the 4th-instance drift (any axis, any file)**.
**Suggested fix:** Change line 80's `adversarial-review` → `adversarial review` (drop the hyphen; 1-char delta). Per subagent's primary prescription. Alternative: backfill lines 18/59/71 to hyphenated form for project-wide alignment with cross-file convention — but this is +6 char-deltas vs +1 char-delta with no demonstrated benefit since cross-file references aren't currently being grepped for joint use. Recommend (a) minimum-churn fix at next touch of file. Cost: ~2 min cycle; pairs naturally with the iter-490 L2 parenthetical-wrapper inconsistency observation (3 sites, 3 formats) as the broader "pin-comment provenance convention is project-wide undersurfaced" cluster.
**Status:** RESOLVED at iter-492 9989b6c (single-char hyphen→space drain on line 80; subagent's primary prescription applied verbatim; minimum-churn fix shape held). Discoverer: adversarial-reviewer at af4e905. Closed in iter-492 on `release/v1.0.2`.

### LOW — Qualifier form "LOW pin" introduces novel taxonomy
**Location:** `tests/SwfocTrainer.Property.Tests/ObjAddrParserProperties.cs` line 80 (the new restored pin-comment).
**Issue:** Cross-file taxonomy uses `<SEVERITY> <ACTION-or-QUOTED-TITLE>` pattern: `Iter100to113PresetCodenameLeakSweepTests.cs:104` "adversarial-review LOW fix", `:220` `adversarial-review MEDIUM "Script-body codename"`, `:247` `adversarial-review LOW "Placeholder...`. The action `fix` or a quoted-title TITLE is the standard token after severity. iter-491's new "LOW pin" uses `pin` as the action — novel token, project-internal jargon for "pin-comment". Subagent flagged: "Acceptable as-is if `pin` is the intended action verb (mirroring `fix`); otherwise replace with `pin fix` or `(naming polish)` to match the line 71 parenthetical-qualifier sub-pattern." Recommend accept-as-is — `pin` is a valid project-internal noun-verb (pin-comment-as-verb) and serves the same disambiguating role as "fix" in cross-file references. The novelty is justified by domain (pin-comment is a project-specific artifact type), not a defect.
**Suggested fix:** None. Accept-as-is per subagent's prescription. If a future project-wide style-pin pass standardizes taxonomy, `pin` is a reasonable verb to canonize alongside `fix` (e.g. `<SEVERITY> {fix | pin | ...}`). Until then: audit-pass record only.
**Status:** OPEN (cosmetic, audit-pass record only). Discoverer: adversarial-reviewer at af4e905.

### LOW (audit-confirm, not a defect) — sha-iter pairing IS correct
**Location:** `tests/SwfocTrainer.Property.Tests/ObjAddrParserProperties.cs` line 80.
**Issue:** Subagent verified the iter-491 commit's central thesis (sha-iter pairing convention restore from broken iter-490 form). Per git log: 08e0c59 = iter-487 commit. iter-488 = adversarial review that filed the LOW. Per same-file authority lines 18 + 71, convention is `<REVIEW-iter> <REVIEWED-sha>`. iter-491's `iter-488 08e0c59` correctly restores this. iter-490's `iter-487 08e0c59` was broken. **Confirms iter-491's correctness on the pairing axis** — drains iter-490 M1.
**Suggested fix:** None — finding is a confirm, not a defect. Closes iter-490 M1.
**Status:** RESOLVED (iter-491 af4e905). iter-490 M1 superseded by this confirm.


## Iter-561/562 (`d8d6b3c` + `b773180`) adversarial-review findings — 2026-05-22 — 1 MEDIUM + 2 LOW (+ 2 pre-existing watch)

Adversarial review of the 2-commit `editor.subtask.done` unit (register Savegame Editor tab in App shell + correct stale max_speed preset pin). Subagent verdict 0 CRITICAL / 0 HIGH / 1 MEDIUM / 2 LOW; judgment layer held all 3 (1 promotion: the slice-fragility item, surfaced by the subagent as the review's primary structural concern, held at MEDIUM). Verdict: APPROVED, all findings non-blocking. Approval count 12 → 13 (single-tick iter: security.tick only; cadence-5/10/15/20/25 all miss at count=13 — 3rd consecutive single-tick iter).

### MEDIUM — Substring-slice test is fragile to future nested `</TabItem>`
**Location:** `tests/SwfocTrainer.Tests/Regression/Iter561SavegameEditorTabRegistrationTests.cs` — test `MainWindowV2Xaml_SavegameEditorTabIsSavegameModeScoped`.
**Issue:** The test slices `MainWindowV2.xaml` text from `Header="Savegame Editor"` to the *next* `</TabItem>`, then asserts the sliced block contains `Visibility="{Binding SavegameTabsVisibility}"` and `tabs:SavegameEditorTab`. This is correct ONLY because the iter-561 TabItem hosts a single self-closed `<tabs:SavegameEditorTab/>` with no nested `</TabItem>`. If a future iter nests a `TabControl` (or any element with its own `</TabItem>`) inside the Savegame Editor tab, the slice ends at the *inner* close tag — the test then silently validates the wrong (truncated) block and can false-PASS even if the savegame-mode Visibility binding were removed. Not a crash, not a current-tree bug; a latent test-correctness erosion.
**Suggested fix:** When this file is next touched, harden the slice: either (a) anchor the end on `</TabItem>` only after confirming no `<TabItem` opens between header and close (depth-count), or (b) replace the text-slice with an actual XML parse (`XDocument` / `XmlDocument`) and assert on the parsed `TabItem` element's attributes — the parse approach is the durable fix and matches what a few other XAML-shape tests in the suite already do. Cost: ~15-25 LoC if (b). Pairs with no other item — standalone.
**Status:** OPEN (watch-tier; non-blocking, works on current tree). Discoverer: adversarial-reviewer at d8d6b3c.

### LOW — DataContext placement diverges from sibling savegame tabs without a clarifying comment
**Location:** `src/SwfocTrainer.App/V2/MainWindowV2.xaml` — the new `<tabs:SavegameEditorTab DataContext="{Binding SavegameEditor}"/>`.
**Issue:** Sibling savegame-mode tabs (Savegame Rescue / Save Monitor / Galaxy Visualizer) attach `DataContext` to an inner `<Grid>`; the iter-561 tab attaches it to the `<tabs:SavegameEditorTab>` UserControl directly. The divergence is *correct and necessary* — a self-contained UserControl needs its own DataContext, not an inline Grid — and `SavegameEditorTab.xaml` deliberately sets no runtime DataContext so it inherits cleanly. But a future reader scanning the sibling pattern may mistake the divergence for an inconsistency.
**Suggested fix:** Add a one-line XAML comment on the TabItem, e.g. `<!-- DataContext on the UserControl itself (not an inner Grid) — SavegameEditorTab is a self-contained UserControl that inherits its host's DataContext. -->`. Cost: ~1 line. Optional polish.
**Status:** OPEN (cosmetic). Discoverer: adversarial-reviewer at d8d6b3c.

### LOW — `MainViewModelV2Source_ConstructsSavegameEditorInCtor` is a brittle whole-line string match
**Location:** `tests/SwfocTrainer.Tests/Regression/Iter561SavegameEditorTabRegistrationTests.cs` — test #3.
**Issue:** Asserts `src.Should().Contain("SavegameEditor = new SavegameEditorTabViewModel();")` against the raw `MainViewModelV2.cs` text. False-fails on a trivial reformat (line-wrap, whitespace change around `=`). It is redundant with test #2 (`MainViewModelV2_ExposesSavegameEditorProperty`, reflection) + test #1 (`SavegameEditorTabViewModel_ParameterlessCtor_Constructs`). It asserts source *spelling*, not behavior.
**Suggested fix:** Accept-as-is — source-text pins are an established project convention (the iter-271 family + many `LoadVmSource()` pins use exactly this shape) and the redundancy is intentional regression-guard depth. If a future test-quality pass (mutation-sweep) flags it as a non-killing assertion, replace with a reflection check that the ctor assigns a non-null `SavegameEditor`. Until then: audit-pass record only.
**Status:** OPEN (cosmetic, audit-pass record only). Discoverer: adversarial-reviewer at d8d6b3c.

### Pre-existing watch (NOT a defect in this diff — out of iter-561/562 scope) — `SavegameEditorTabViewModel.LoadAsync` whole-file slurp
**Location:** `src/SwfocTrainer.App/Tabs/SavegameEditorTabViewModel.cs` (iter-289b code, NOT in the iter-561/562 diff).
**Issue:** `LoadAsync` reads the entire save via `File.ReadAllBytes` into a `byte[]`; the XML doc comment claims "200 MB+ save" support. A 200 MB+ buffer plus the parsed chunk document is Large-Object-Heap pressure / fragmentation risk. Surfaced by the subagent as an FYI — explicitly out of this hat's review scope (this hat reviews the iter-561/562 diff only).
**Suggested fix:** Future savegame-engineer hardening pass — stream the chunk tree or memory-map the file rather than slurping. Not actionable from adversarial-reviewer.
**Status:** OPEN (pre-existing watch; forwarded to savegame-engineer). Discoverer: adversarial-reviewer subagent at d8d6b3c (FYI, not filed against the diff).

### Pre-existing watch (NOT a defect in this diff — out of iter-561/562 scope) — savegame path inputs have no traversal/extension validation
**Location:** `src/SwfocTrainer.App/Tabs/SavegameEditorTabViewModel.cs` — `ModDataPath` / `SavePath` / `ReadModObjectTypeData` (iter-289b code, NOT in the iter-561/562 diff).
**Issue:** Operator-typed paths are accepted with no path-traversal or extension validation; `ReadModObjectTypeData` recursively enumerates `*.xml` under an arbitrary directory. Risk is low — offline single-user trainer reading local files — but there is zero input validation at the boundary.
**Suggested fix:** Future savegame-engineer hardening pass — validate the extension (`.PetroglyphFoC64Save`) and constrain the enumeration root. Not actionable from adversarial-reviewer.
**Status:** OPEN (pre-existing watch; forwarded to savegame-engineer). Discoverer: adversarial-reviewer subagent at d8d6b3c (FYI, not filed against the diff).
