# iter-283 — Codify feedback_infra_claim_drift_bidirectional memory rule

**Date:** 2026-05-08
**Arc class:** Memory codification iter (mirror to iter-256 codifying iter-249's AOB-drift lesson)
**Predecessor:** iter-282 (firerate_mult overlay consumer + mid-iter pre-existing-infra catch)
**Successor (queued):** iter-284 (Phase2HookPending re-audit OR Tier 3 overlay content OR Lua Playground preset menu refresh OR new A1.x arc kickoff)

## What changed (2 files, ~40 lines new memory content)

- **`~/.claude/projects/.../memory/feedback_infra_claim_drift_bidirectional.md`** (NEW, 49 lines): codified the iter-282 NEW pattern lesson as a peer-level memory entry to iter-256's `feedback_aob_drift_across_binary_versions.md`. Structured per the memory format: frontmatter (name/description/type=feedback) → rule → table mapping both directions → **Why** (iter-282 incident + iter-249/iter-128 mirror incident) → **How to apply** (5 steps with cost-benefit ratio + frequency analysis) → edge cases → memory-write triggers.
- **`~/.claude/projects/.../memory/MEMORY.md`** (1-line index entry appended): `- [Bidirectional Infra-Claim Drift](feedback_infra_claim_drift_bidirectional.md) — ...`. Slotted directly after the iter-256 AOB-drift entry to surface the bidirectional generalization in the same conceptual neighborhood.

## Why a separate memory entry instead of extending iter-256

The user explicitly maintains `feedback_aob_drift_across_binary_versions.md` as a single-direction rule (iter-256 codification). Adding the bidirectional generalization to that file would either:
1. Force a rename of the iter-256 file → breaks the index reference + commit history reference + searchability of the old file.
2. Diverge the file's title from its scope → the title still says "AOB Drift" while the body now also covers "missing infra" claims → confusing.

Better: ship the bidirectional rule as a NEW entry that **explicitly references iter-256 as direction A** and surfaces direction B as the new contribution. Both entries cross-reference each other in their bodies. Future incidents in either direction extend the bidirectional umbrella entry rather than the per-direction starter entries.

## The bidirectional rule (gist)

| Direction | Claim shape | Failure mode | Prior codification |
|---|---|---|---|
| **A** | "Infra X is present at address/symbol P" | P is wrong (drifted/deprecated/mis-resolved) | iter-256 `feedback_aob_drift_across_binary_versions.md` |
| **B** | "Infra X is missing — needs to be added" | X already exists from prior iter's pair-completion | iter-283 `feedback_infra_claim_drift_bidirectional.md` (THIS) |

Mitigation in both cases: 5-second `grep` against current-state codebase BEFORE designing/writing the iter's deliverable. Cost ratio:
- Direction A skip cost: ~5 iters of misdirected RE work (iter-105 → iter-128 SetUnitShield trail).
- Direction B skip cost: ~50 LoC duplicate code + merge conflict + ~30 minutes rework.
- Verification cost: 5 seconds.

**Always grep first.**

## Pattern lesson — memory-codification cadence

- iter-249 (NEW lesson surfaced) → iter-256 (codified, 7-iter gap).
- iter-282 (NEW lesson surfaced) → iter-283 (codified, 1-iter gap).

Iter-283's faster turnaround reflects two improvements:
1. **Pattern recognition is sharper** — the loop now identifies "this is a NEW pattern lesson worth codifying" mid-iter rather than ~iter+5 retroactively.
2. **Memory-write iter is now a recognized iter shape** — the loop has a template for this (mirror to iter-256), so fewer cycles needed to scope and execute.

Codification cadence is also a leading indicator of loop maturity: 2 codification iters across 283 iters = ~0.7% of iters spent on memory-rule writes. This is a healthy ratio — too high signals the loop isn't internalizing rules; too low signals lessons are being lost.

## Verification gates

- [x] Memory file written with correct frontmatter (name + description + type=feedback).
- [x] MEMORY.md index entry appended directly after iter-256 entry (semantic neighborhood preservation).
- [x] iter-256 entry NOT renamed or modified — preserves backward references + commit history.
- [x] Body cross-references iter-256 + iter-249 + iter-128 incidents → future readers can trace both directions to their source incidents.
- [x] No bridge / overlay / editor / build changes — pure memory-write iter.
- [x] All inherited gates green (bridge harness 1100/0, ledger lint 0/0 at 318 entries, overlay DLL 1,038,848 B unchanged, editor test suite unchanged).

## Tasks queued

- **iter-284** candidates (loop operator picks):
  1. **Phase2HookPending re-audit (6th audit)** — RECOMMENDED for cadence; ~9-iter window since iter-274. iter-132/221/250/266/274 audits established the cadence at ~16-22 iters; iter-284 would be at 10 iters from iter-274 — slightly early but the catalog has churned in the iter 275-282 Thread B + Tier 2 resolution arc.
  2. **Tier 3 overlay content** — kill/death tally, scenario-event ring buffer, current-mission timer. Extends the iter-275 → iter-282 Phase 2-full work from Tier 2 (multipliers) to Tier 3 (state-rate counters). Higher operator value than another audit; lower marginal cost than a NEW arc kickoff.
  3. **Lua Playground preset menu refresh** — last refreshed iter-264. iter 257-282 wires unsurfaced (~30 wires across SetUnitField max_*, A1.x camera/credits/cap, Phase 2 Tier 2). Strong "operator-polish" iter.
  4. **New A1.x arc kickoff** — per iter-269 ledger-state asymptote signal (37.5% honest-defer rate). Higher risk but unblocks new RE territory.

Lean toward **(2) Tier 3 overlay** — extends the active work track without breaking flow + has clear scope (3-4 fields appended to HudSnapshot, mirror iter-281 worker probes + iter-282 render lambda).

## Verification checklist

- [x] Memory file `feedback_infra_claim_drift_bidirectional.md` created at correct path.
- [x] MEMORY.md index updated with 1-line entry adjacent to iter-256 entry.
- [x] Memory frontmatter follows spec (name/description/type).
- [x] Body structured per feedback type (rule → Why → How to apply).
- [x] Both directions cross-reference incidents (iter-282 + iter-249 + iter-128).
- [ ] State docs synced (.remember/now.md, .remember/ralph_loop_state.md, STATUS.md).
- [ ] Task #533 marked completed; iter-284 queued.
