# Backlog inventory — Codification candidates assessment (iter 352; 11 candidates accumulated iter 333-351)

**Date:** 2026-05-07
**Iter:** 352
**Purpose:** Triage the 11 codification candidates accumulated across iter 333-351 by recurrence likelihood + recommend per-candidate action (keep flagged / consider retirement / move to global note)

## Summary

| Class | Count | Recommendation |
|---|---|---|
| **A — Likely to recur naturally within 20 iters** | 4 | Keep flagged; codify on natural 3rd recurrence |
| **B — Uncertain recurrence; may or may not hit 3rd instance** | 5 | Keep flagged but ALSO note context; retire if 50+ iters pass without 2nd instance |
| **C — Narrow scope or better suited to global note** | 2 | Retire from codification queue; consider promoting to global toolchain note |

**Total**: 11 candidates → 4 keep + 5 watch + 2 retire/promote.

## Class A — Likely to recur naturally within 20 iters (4 candidates)

These patterns are tied to recurring iter shapes (research/implementation cycles, VM/XAML splits, codification cadence, audit cadence). High probability of natural 3rd-instance recurrence within the next 20 iters.

### A1. `feedback_research_first_implementation_second.md` (current trigger: 2/3)

- **Instances**: iter-336 + iter-338/339 (research-then-implement) + iter-342 + iter-343 (research-then-implement)
- **Pattern**: When complexity of a feature is unknown, ship a research iter FIRST + implementation iter SECOND.
- **Recurrence prediction**: HIGH. Any unknown-complexity feature triggers this. Likely 3rd instance within 5-10 iters when next operator-driven feature lands.
- **Recommendation**: KEEP FLAGGED at 2/3. Codify on natural 3rd recurrence (likely iter-355-365 timeframe).

### A2. `feedback_vm_first_xaml_second_iter_split.md` (current trigger: 2/3)

- **Instances**: iter-148/149 (Camera VM then XAML) + iter-338/339 (Hardpoint VM then XAML)
- **Pattern**: When adding new tab UI surface, ship VM iter FIRST + XAML iter SECOND. Allows VM tests to land independently of XAML pin tests.
- **Recurrence prediction**: HIGH. Every new tab GroupBox follows this shape. Likely 3rd instance within 10-15 iters when next tab UX lands.
- **Recommendation**: KEEP FLAGGED at 2/3. Codify on natural 3rd recurrence (likely iter-360-370 timeframe).

### A3. `feedback_audit_compounds_via_rationale_extensions.md` (current trigger: 1/3)

- **Instances**: iter-341 (Phase2HookPending audit ran ~6× faster than iter-323 thanks to iter-329 5-entry rationale extensions)
- **Pattern**: Periodic catalog audits compound when rationale extensions are present. Investing in rationale text once pays back in subsequent audit cycles.
- **Recurrence prediction**: HIGH. Audit cadence is ~17 iters; next P2HP audit (iter-358±) will either continue the compounding (2nd instance) or reset (if catalog grows without rationale extensions). Reverse-orphan audit cadence is similar.
- **Recommendation**: KEEP FLAGGED at 1/3. Watch iter-358 P2HP audit + iter-368 reverse-orphan audit for 2nd instance.

### A4. `feedback_codification_value_proven_by_next_iter.md` (current trigger: 1/3)

- **Instances**: iter-338 (codified iter-337 preflight stack at iter-337 → iter-338 immediately consumed it = codification proven by next iter)
- **Pattern**: When codifying a memory rule, the next iter to consume it validates the codification was timely (vs premature codification that sits unused).
- **Recurrence prediction**: HIGH. Every codification iter is a candidate for this pattern; iter-345 codification + iter-346 audit consumed it; iter-351 codification candidate + iter-352 may consume it. Could codify at 3rd instance when iter-355+ codification iter ships.
- **Recommendation**: KEEP FLAGGED at 1/3. Watch next 2 codification iters for 2nd + 3rd instances.

## Class B — Uncertain recurrence; may or may not hit 3rd instance (5 candidates)

These patterns surfaced once, in specific contexts that may or may not recur. Keep flagged but add context-specific notes; if 50+ iters pass without a 2nd instance, consider retiring.

### B1. `feedback_glob_walker_prefix_overlap_audit.md` (current trigger: 1/3)

- **Instances**: iter-333 (Asset Browser tab `Directory.EnumerateFiles(root, "i_button_*.dds")` was a SUPERSET of `i_button_hp_*.dds` + `i_button_ability_*.dds`)
- **Pattern**: Glob walkers with overlapping prefixes silently match more files than intended. Audit at 2nd consumer of any prefix-based glob pattern.
- **Recurrence prediction**: MEDIUM. Specific to glob-walking consumer code. Recurs if/when we add another asset class that walks files by prefix (iter-313 LocateByConvention plugin set is at N=6; if it grows to N=7+, this pattern could re-trigger).
- **Recommendation**: KEEP FLAGGED at 1/3 with context note: "audit when iter-313 plugin set grows beyond N=6 OR when any new glob-walking consumer ships".

### B2. `feedback_consumer_extensibility_audit.md` (current trigger: 1/3)

- **Instances**: iter-333 (producer-side resolver was clean but consumer-side file walker had glob-overlap bug)
- **Pattern**: Producer-layer extensibility audit ≠ consumer-layer extensibility audit. Both layers need separate validation when extending plugin sets.
- **Recurrence prediction**: MEDIUM. Specific to producer/consumer architecture extensions. Same likely-recurrence trigger as B1.
- **Recommendation**: KEEP FLAGGED at 1/3. Combined with B1 — these may codify together at next plugin-set extension.

### B3. `feedback_pin_synchronization_across_test_files.md` (current trigger: 1/3)

- **Instances**: iter-335 (2 different test files Iter252 + Iter271 both pin the same GroupBox header text; updating only Iter252 left Iter271 broken)
- **Pattern**: When multiple test files pin the same source string, updating one without the others causes silent drift caught only by full-suite runs.
- **Recurrence prediction**: MEDIUM. Recurs when source strings have multiple test pins. Heuristic: any preset menu / CapabilityAwareAction title / XAML header that has 2+ tests pinning it.
- **Recommendation**: KEEP FLAGGED at 1/3 with context note: "watch for any string-edit iter that touches text pinned by 2+ test files; if iter-360+ ships another instance, codify".

### B4. `feedback_binary_republish_staleness_audit.md` (current trigger: 1/3)

- **Instances**: iter-336 (closed 145-iter staleness gap from iter-190 to iter-336; binary was outdated by ~5 months)
- **Pattern**: Editor binary at `publish/SwfocTrainer.App.exe` should be republished at ~50-iter intervals. Long staleness gaps mean operators run outdated builds.
- **Recurrence prediction**: MEDIUM. Specific to editor republish discipline. Recurs if iter-386+ doesn't have an explicit republish iter (50-iter cadence from iter-336).
- **Recommendation**: KEEP FLAGGED at 1/3. Action: schedule explicit republish at iter-386 even if no source changes; if it surfaces a 2nd instance of "X-iter staleness gap closed", codify.

### B5. `feedback_graceful_failure_enables_empirical_feedback.md` (current trigger: 1/3)

- **Instances**: iter-343 (Hardpoint icon-resolution chain shipped Approach A optimistic with graceful userdata fallback; pending live tostring(handle) verification)
- **Pattern**: When feature has empirical unknown, ship optimistic-with-graceful-failure FIRST + get operator feedback in NEXT iter + refine in iter+2 if needed.
- **Recurrence prediction**: HIGH-MEDIUM. Any feature with operator-verify-pending status uses this. Iter-343 is the canonical case but probably exists in earlier work that wasn't flagged.
- **Recommendation**: KEEP FLAGGED at 1/3. Active watch — likely to recur within 10-15 iters when next empirical-unknown feature ships.

## Class C — Narrow scope or better suited to global note (2 candidates)

These patterns are toolchain footguns or project-state metadata that may be better captured in global instructions (~/.claude/CLAUDE.md or project CLAUDE.md) than as `feedback_*.md` codified rules.

### C1. `feedback_audit_dry_spell_is_not_convergence.md` (current trigger: 1/3)

- **Instances**: iter-346 (4 consecutive CLEAN reverse-orphan audits ≠ permanent convergence; iter-272 lesson #2 was overconfident)
- **Pattern**: When an automated audit shows N consecutive CLEAN passes, do NOT downgrade to "regression-confirmation only"; mechanism's signal lies dormant until trigger condition.
- **Recurrence prediction**: LOW. This is a meta-rule about a specific iter-272 lesson reversal. Unlikely to surface again unless another audit-mechanism declares "convergence" prematurely.
- **Assessment**: This insight is captured in `iter346_reverse_orphan_audit.md` close-out and the iter-346 ralph_loop_state.md entry. The lesson has effectively been logged; codifying it as a separate `feedback_*.md` rule would be redundant.
- **Recommendation**: RETIRE from codification queue. Mark as "lesson-only" — remains in the iter-346 close-out doc. If a future audit-convergence claim recurs, codify at that point.

### C2. `feedback_no_build_safe_only_for_jit_paths.md` (current trigger: 1/3)

- **Instances**: iter-346 (`dotnet test --no-build` ran against stale snapshot because HashSet<string> static field initializer is compiled into the test binary)
- **Pattern**: When editing test-side static data (HashSet/Dictionary/array initializers), always re-run with full build; `--no-build` flag is safe only for JIT-compiled paths.
- **Recurrence prediction**: LOW-MEDIUM. Specific to xUnit + .NET 8 test runner with static field initializers. Could recur if other tests use similar patterns.
- **Assessment**: This is a toolchain footgun specific to the .NET test runner. Better captured as a brief note in `CLAUDE.md` toolchain section than as a codified `feedback_*.md` rule.
- **Recommendation**: PROMOTE to project `CLAUDE.md` (or user global `CLAUDE.md`) as a toolchain note. Sample: "When editing xUnit static field initializers (HashSet/Dictionary/array), always rebuild before re-running tests — `--no-build` runs against stale compiled snapshot." Drop from codification queue.

### C3 (informational) — `feedback_memory_md_polish_cadence.md` (current trigger: 1/3, just-flagged at iter-351)

- **Instances**: iter-351 (FIRST quantitative MEMORY.md staleness audit; 4/35 = ~11% staleness ratio)
- **Pattern**: Periodic MEMORY.md polish iters identify project-state snapshot entries that decay over time, while codified pattern rules and game-engine facts remain stable.
- **Recurrence prediction**: LOW. Project-specific meta-rule about MEMORY.md hygiene. Recurs only at the next MEMORY.md polish iter (likely iter-400+).
- **Assessment**: Same category as C1 + C2 — meta-rule that's better captured in this inventory doc + iter-351 close-out than as a separate `feedback_*.md` rule.
- **Recommendation**: KEEP at 1/3 for now (just flagged). If next MEMORY.md polish iter (iter-400+) shows similar findings, codify; otherwise retire similarly to C1.

## Recurrence outlook (next 50 iters)

| Iter window | Likely codification candidates that mature to 3/3 trigger |
|---|---|
| iter-353-365 | A1 (research_first_implementation_second; HIGH probability) |
| iter-360-370 | A2 (vm_first_xaml_second; HIGH probability), A4 (codification_value_proven; MEDIUM) |
| iter-358 (P2HP audit) | A3 (audit_compounds_via_rationale_extensions; 2nd instance) |
| iter-368 (reverse-orphan) | A3 (audit_compounds; 3rd instance possible) |
| iter-386+ (republish) | B4 (binary_republish_staleness_audit; 2nd instance possible) |
| iter-360-380 | B3 (pin_synchronization), B5 (graceful_failure_empirical_feedback) — opportunistic |
| iter-400+ (MEMORY polish) | C3 (memory_md_polish_cadence; 2nd instance possible) |

**Predicted codification cadence**: 2-3 new codified rules across iter 353-400 = continuation of ~1 rule per ~22 iters trend (iter-345 last was 11th rule).

## Verification gates ALL GREEN

- 0 source/test/catalog edits — pure analysis iter
- All editor build/test gates inherit GREEN from iter-346 test-snapshot fix + iter-344 republish
- Bridge harness inherits 1100/0; ledger lint inherits 0/0 at 318 entries

## Decisions captured (action per candidate)

| # | Candidate | Action | Rationale |
|---|---|---|---|
| 1 | glob_walker_prefix_overlap_audit (B1) | KEEP at 1/3 with context note | Combined with B2 |
| 2 | consumer_extensibility_audit (B2) | KEEP at 1/3 with context note | Combined with B1 |
| 3 | pin_synchronization_across_test_files (B3) | KEEP at 1/3 with watch trigger | String-pin work cadence-driven |
| 4 | binary_republish_staleness_audit (B4) | KEEP at 1/3 with iter-386 schedule | Republish discipline-driven |
| 5 | codification_value_proven_by_next_iter (A4) | KEEP at 1/3 (active watch) | Codification iter cadence |
| 6 | audit_compounds_via_rationale_extensions (A3) | KEEP at 1/3 (active watch) | Audit cadence iter-358 + iter-368 |
| 7 | research_first_implementation_second (A1) | KEEP at 2/3 | High-probability natural recurrence |
| 8 | graceful_failure_enables_empirical_feedback (B5) | KEEP at 1/3 (active watch) | Operator-verify-pending features |
| 9 | audit_dry_spell_is_not_convergence (C1) | RETIRE | Lesson logged in iter-346 close-out |
| 10 | no_build_safe_only_for_jit_paths (C2) | PROMOTE to CLAUDE.md toolchain note | Footgun better as global rule |
| 11 | memory_md_polish_cadence (C3) | KEEP at 1/3 (low-priority watch) | Meta-rule; iter-400+ recurrence |
| 12 | vm_first_xaml_second_iter_split (A2) | KEEP at 2/3 | High-probability natural recurrence |

**Net**: 9 candidates KEPT (with notes / watch triggers) + 1 RETIRED + 1 PROMOTED to CLAUDE.md.

## Recommended actions for iter-353+

1. **Iter 353** — Pick from canonical list:
   - Live SWFOC verify of iter-343 chain (highest-value if operator session available)
   - Promote C2 to CLAUDE.md (low-effort; ~10 min cycle)
   - NEW arc-class kickoff (multi-iter; deferred per iter-271)
2. **Iter 358 (P2HP audit cadence)** — A3 likely 2nd instance
3. **Iter 368 (reverse-orphan audit cadence)** — A3 possible 3rd instance + maturation watch
4. **Iter 386 (republish staleness)** — B4 possible 2nd instance (schedule explicit republish even if no source changes)

## Net iter-352 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure analysis iter) |
| Doc shipped | 1 NEW backlog inventory doc (~210 lines) + 1 close-out reference (this doc serves as both) |
| Pattern observations flagged | 0 NEW (consolidation iter, not generation iter) |
| Cycle time | ~25 min |
| Codification queue | 11 candidates → 9 KEEP + 1 RETIRE + 1 PROMOTE |

**iter-352 closes the 11-candidate codification backlog by classification**: 4 high-probability natural recurrences (Class A) + 5 medium-probability watches (Class B) + 2 retire-or-promote (Class C). Provides clear roadmap for iter-353+ to know which candidates are worth waiting for vs which can be safely closed.

22nd post-iter-323 arc iter (6 LIVE + 3 codification + 2 republish + 1 XAML + 10 docs/audit/inventory); 83rd consecutive NON-A1.x iter per iter-269 lesson #2.
