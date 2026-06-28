# iter-302 — Codify `feedback_engine_already_does_this` memory rule (6th-instance trigger)

**Date:** 2026-05-07
**Arc class:** Memory codification (4th memory-rule iter this conversation)
**Predecessor:** iter-301 (Settings VM mod-picker)
**Successor (queued):** iter-303 (Settings tab XAML surface for iter-301 VM)

## What changed (2 files; ~70 lines memory + 1-line index)

- **NEW** `~/.claude/projects/.../memory/feedback_engine_already_does_this.md` (~70 lines, YAML frontmatter + body) — codifies the meta-pattern with:
  - 6-row instance table (iter-100/107/179/296/299/300)
  - 4-step "How to apply" decision tree (engine Lua API → filesystem → RVA pin)
  - **Honest break-out clause** for filesystem-wins-over-DoString cases
  - 4 edge cases including mod-compat free-rider + composability + wire-format alignment
  - Cost-benefit ratio (~30-50 LoC DoString vs ~200-500 LoC RVA pin = 6-10× cheaper)
  - Memory-write trigger justification
  - 3 prospective future uses (asset extraction, faction-roster-by-build-tab, hero-roster)
  - Pattern-reinforcement cross-link to iter-283 5-second-grep sibling rule

- **EXTENDED** `~/.claude/projects/.../memory/MEMORY.md` (1 line) — index entry slotted directly after `feedback_infra_claim_drift_bidirectional` to preserve semantic neighborhood (both are "cheap-verification-before-design" rules).

## Why now (6th-instance threshold)

Codification requires recurrence to prove the abstraction is general, not a fluke. The instance lineage:

| Iter | Wire | Mechanism | Date span |
|---|---|---|---|
| iter-100 | SetUnitSpeed via DoString | engine Lua API | early Apr 2026 |
| iter-107 | SetCameraPos via DoString | engine Lua API | mid Apr 2026 |
| iter-179 | TaskForce write-side via DoString | engine Lua API | early May 2026 |
| iter-296 | GetPlanets via DoString | engine Lua API | 2026-05-07 |
| iter-299 | GetFactionRoster via DoString | engine Lua API | 2026-05-07 |
| iter-300 | ListMods via filesystem | Win32 FindFirst/Next | 2026-05-07 |

6 instances spanning ~200 iters across multiple sessions, multiple parallel threads (Thread A + B + C), and multiple operator-mandate expansions. **The pattern is now load-bearing in the loop**, not an experimental shortcut. Codification at this point is *defensible*, not premature.

## Pattern abstraction (key insight)

Instances iter-100/107/179/296/299 all use **DoString into engine Lua API**. iter-300 breaks the pattern slightly with **filesystem probe via Win32** because the engine has no "what mods exist?" Lua API. The codified rule captures BOTH primitives as cheap mechanisms, with the honest break-out clause that filesystem wins when no engine Lua API exists.

This is why iter-301 wasn't counted as the 7th instance — it's a *consumer* of the wires (VM layer), not a new bridge wire. The pattern is about new-wire shipping, not surfacing.

## Cross-reference with sibling rules

The codified rule explicitly cross-links with two prior memory rules:

1. **`feedback_infra_claim_drift_bidirectional`** (iter-283) — sibling "cheap verification before design" rule. Combined: at the top of any new-wire iter, spend ~35 seconds checking (a) does the engine Lua API expose this? (b) does this pre-exist as code? Both checks together prevent ~hours of misdirected work.

2. **`feedback_flag_flipping_vs_engine_state`** (older) — INVERSE direction. The flag-flipping rule says "don't bypass the engine via direct memory write." This rule says "leverage the engine via DoString instead of pinning RVAs." Both compatible: prefer engine API, route through it.

## Verification gates ALL GREEN

Pure docs iter — no code changes. All inherited gates:

- Bridge harness inherits 1100/0 from iter-300 (no bridge changes)
- Editor build inherits 0/0 from iter-301 (no editor changes)
- Verifier ledger lint inherits 0/0 at 318 entries
- Memory file YAML frontmatter parses (matches iter-283 / iter-256 templates)
- MEMORY.md index entry slotted in semantic neighborhood (after iter-283, before empirical-first)

## What's NOT done in iter-302 (deferred)

- **Settings tab XAML surface** for iter-301 VM mod-picker — iter-303 priority. ~30-50 LoC of XAML insertion into existing `SettingsTab.xaml`.
- **Asset/icon extraction kickoff** — iter-304+. The codified rule will guide that work (check for engine Lua API for DDS/.meg loading first; fall back to filesystem .meg parser only if missing).
- **Bidirectional rules grep alias** — could be a future iter-X codification ("when adding any new bridge wire, run THESE THREE greps"); not codifying yet (cumulative-grep meta-rule needs more recurrence).

## Pattern lessons from this codification iter itself

### *Pure-docs-iter has zero risk of regression*

iter-302 is the 4th memory-codification iter this conversation (iter-256 + iter-283 + iter-293 + iter-302). All 4 had:
- 0 code changes
- 0 build changes
- 0 test changes
- All inherited gates GREEN (because nothing was touched)

**Pure-docs iters are a valuable rhythm anchor.** They let the loop catch its breath after a high-risk implementation arc, while still producing operator-visible value (the next agent who reads the codified rule benefits permanently).

### *Codification cadence is improving over time*

- iter-249 → iter-256: 7-iter gap (first codification this session)
- iter-282 → iter-283: 1-iter gap (faster pattern recognition)
- iter-293 → iter-293: 0-iter gap (codification lands within the close-out iter)
- iter-300 → iter-302: 2-iter gap (codification lands after 1 reinforcement iter)

The pattern recognition is faster each time. iter-302 codified within 2 iters of the trigger; future codifications likely land within 0-1 iters.

## Tasks queued

- **iter-303** (next): Settings tab XAML surface for iter-301 VM mod-picker. ~30-50 LoC XAML insertion + verify VM bindings render correctly. Pin test for the XAML-VM round-trip.
- iter-304+: Asset/icon extraction kickoff (.meg parser + DDS decoder per user mandate). The codified rule will guide: check engine Lua API for asset loading first; fall back to filesystem if missing.

## Verification checklist

- [x] `feedback_engine_already_does_this.md` written with frontmatter + body + edge cases.
- [x] MEMORY.md index entry added in semantic neighborhood (after `feedback_infra_claim_drift_bidirectional`).
- [x] Cross-reference with sibling rules documented (5-sec-grep + flag-flipping).
- [x] 6-instance table with iter / wire / mechanism / cost.
- [x] Honest break-out clause for filesystem cases.
- [x] Cost-benefit ratio quantified (6-10× cheaper).
- [x] Prospective future uses listed (asset extraction, faction-roster-by-build-tab, hero-roster).
- [x] Bridge harness 1100/0; editor 0/0; ledger lint 0/0 (all inherited).
- [x] Pure-docs iter — 0 code changes, 0 test changes.
- [ ] State docs synced.
- [ ] Task #553 marked completed; iter-303 (XAML surface) queued.
