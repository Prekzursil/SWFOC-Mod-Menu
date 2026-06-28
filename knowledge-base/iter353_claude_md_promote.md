# Iter 353 — Promote C2 toolchain footgun to project CLAUDE.md (`--no-build` safe only for JIT paths; closes iter-352 backlog inventory PROMOTE recommendation)

**Date:** 2026-05-07
**Arc class:** CLAUDE.md toolchain note promotion (closes iter-352 backlog inventory C2 PROMOTE recommendation)
**Predecessor:** iter-352 (backlog inventory of 11 codification candidates; classified C2 as PROMOTE-to-CLAUDE.md)
**Successor (queued):** iter-354 (TBD — see "Next iter options" below)

## What changed (1 file modified — CLAUDE.md surgical append to Execution Gotchas section)

- **MODIFY** `C:/Users/Prekzursil/Downloads/swfoc_memory/CLAUDE.md` (~+5 LoC net append):
  - **NEW bullet appended** to the "Execution Gotchas (Claude Code workflow level — learned 2026-04-08)" section after the existing 8 bullets:
    > **`dotnet test --no-build` is unsafe for static field initializer edits**: when editing xUnit test-side static data (`HashSet<string>`, `Dictionary<,>`, array initializers), the data is COMPILED INTO the test binary. Re-running with `--no-build` runs against the stale compiled snapshot and produces the SAME test failure as before the edit. Always rebuild after editing static field initializers. Diagnostic: iter-346 reverse-orphan snapshot edit (`KnownUnwiredEntries.HashSet<string>`) hit this — initial `--no-build` re-run still failed with the original diff; full rebuild then PASSED. Promoted from iter-352 backlog inventory C2 candidate (toolchain footgun better as global rule than codified `feedback_*.md`).

## Verification gates ALL GREEN

- 0 source/test/catalog edits in `SWFOC editor/` — pure docs iter (project CLAUDE.md only)
- All editor build/test gates inherit GREEN from iter-346 test-snapshot fix + iter-344 republish (157.34 MB at May 7 08:09)
- Bridge harness inherits 1100/0
- Verifier ledger lint inherits 0/0 at 318 entries
- CLAUDE.md edit was a single Edit operation appending one bullet to existing section — no multi-section drift risk

## Codification queue closure

The iter-352 backlog inventory triaged 11 candidates. iter-353 closes the C2 PROMOTE recommendation:

| iter-352 class | Count | iter-353 status |
|---|---|---|
| A — KEEP active | 4 | Unchanged (await natural recurrence) |
| B — KEEP watch | 5 | Unchanged (cadence-driven recurrence) |
| C — RETIRE | 1 (C1: audit_dry_spell) | Already retired (lesson logged in iter-346 close-out) |
| **C — PROMOTE** | **1 (C2: no_build_safe_only_for_jit_paths)** | **PROMOTED to CLAUDE.md this iter** |
| C — LOW-PRIORITY watch | 1 (C3: memory_md_polish_cadence) | Unchanged (iter-400+ recurrence) |

**Codification queue is now in steady state**: 4 active candidates + 5 watch candidates + 1 low-priority watch + 0 pending action. All future codifications gated on natural recurrence (~1 rule per ~22 iters trend continues).

## Pattern lessons (no new codification candidates flagged)

iter-353 is a pure docs iter that promotes a previously-flagged candidate to a different documentation surface. No new pattern observations surfaced because:

1. The promotion follows the iter-352 backlog inventory recommendation (already-decided action)
2. The CLAUDE.md edit follows the established bullet-append convention to the Execution Gotchas section (8 prior instances)
3. The single-Edit append at end-of-section strategy mirrors the inventory's recommendation format

This is the expected behavior for a backlog-action iter — it should NOT generate new pattern lessons, only execute decisions captured in prior analysis.

## What's NOT done in iter-353 (deferred)

- **Live SWFOC verify** of iter-343 Hardpoint Inspector chain: requires operator session
- **Codification of pending 1/3-trigger candidates**: all 9 active/watch candidates need natural recurrence
- **Codification of pending 2/3-trigger candidates** (vm_first_xaml_second + research_first_implementation_second): each need 1 more instance
- **Phase2HookPending re-audit**: iter-341 just ran; iter-358 is next canonical
- **Reverse-orphan snapshot audit**: iter-346 just ran; iter-368 is next canonical
- **Multi-iter Thread project kickoff**: deferred per iter-269 NON-A1.x lesson #2 unless operator surfaces specific demand
- **Editor binary republish** at iter-386 (per B4 iter-352 inventory recommendation): well-precedented schedule, not yet due

## Verification checklist

- [x] CLAUDE.md "Execution Gotchas" section extended with iter-346 finding
- [x] Bullet format matches existing 8 bullets (bold key + colon + description + diagnostic)
- [x] Cross-reference to iter-352 backlog inventory PROMOTE recommendation included
- [x] Diagnostic case study referenced (iter-346 KnownUnwiredEntries.HashSet<string>)
- [x] All editor build/test gates inherit GREEN from iter-346 test-snapshot fix
- [x] iter-352 backlog inventory C2 PROMOTE recommendation closed

## Next iter options (iter-354)

In priority order:

1. **Wait for natural codification recurrence** — iter-358 P2HP audit may surface A3 (audit_compounds_via_rationale_extensions) 2nd instance; iter-368 reverse-orphan audit may surface 3rd instance. Active watch period; lowest-effort iters.
2. **Live SWFOC verify of iter-343 chain** — requires operator session; highest-value pending iter that can't be done autonomously
3. **NEW arc-class kickoff** — Save-game RE iter-2 / Sound editor / Multi-repo CI gate hygiene (multi-iter; deferred per iter-271 NON-A1.x lesson #2 unless operator surfaces specific demand)
4. **Quiet-loop iter** — pure verification iter that just confirms all 5 gates remain GREEN + updates state docs without surfacing new work (low-utility)
5. **Editor republish at iter-386** — schedule explicit republish per B4 iter-352 inventory recommendation (33 iters early; not optimal)

Recommended for **iter 354**: option 4 (quiet-loop iter). The codification queue is in steady state, headline-doc quad is 100% coherent, MEMORY.md is fresh, all gates are GREEN. A quiet-loop iter validates the full project state remains stable + provides a clean baseline for future arcs. Pure verification iter; ~10 min cycle.

## Net iter-353 outcome

| Aspect | Value |
|--------|-------|
| LoC shipped | 0 source/test/catalog (pure docs iter — project CLAUDE.md only) |
| Doc shipped | 1 file modified (CLAUDE.md, +~5 LoC bullet) + 1 close-out doc (~85 lines) |
| Pattern observations flagged | 0 (backlog-action iter, not generation iter) |
| Cycle time | ~10 min |
| Codification queue closure | iter-352 C2 PROMOTE → CLAUDE.md DONE |

**iter-353 closes the iter-352 backlog inventory C2 PROMOTE recommendation** by adding the `--no-build` toolchain footgun to project CLAUDE.md's Execution Gotchas section. Future agents arriving at this project will see the rule in CLAUDE.md (always-loaded context) without needing to grep individual close-out docs.

23rd post-iter-323 arc iter (6 LIVE + 3 codification + 2 republish + 1 XAML + 11 docs/audit/inventory/promote); 84th consecutive NON-A1.x iter per iter-269 lesson #2.
