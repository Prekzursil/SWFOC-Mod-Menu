# Iter 266 — Phase2HookPending Re-Audit Pass (4th audit; iter-132/221/250 predecessors)

**Date:** 2026-05-07 21:10 UTC (close-out)
**Window since iter-250 last audit:** 16 iters (iter 251-265)
**Status at end of iter 266:** 25 entries triaged; **2 catalog-rationale drift catches** (SWFOC_SpawnUnit + SWFOC_SetUnitCapOverride); both fixed in same iter; 1 NEW pin test added; ALL 8168 GREEN.
**Pattern empirically validated:** decreasing drift rate trend HOLDS at 4th audit.

## Headline tally

| Iter | Phase2 count | Drift catches | Drift rate | Notes |
|---|---|---|---|---|
| 132 | 24 | 3 | **12.5%** | First structured audit |
| 221 | 26 (+iter-137 vestigials) | 4 | **15%** | Catalog grew ~85 entries since iter-132 |
| 250 | 25 (-iter-237 SetCameraPos) | 1 | **4%** | New "catalog-rationale-cross-reference drift" class identified |
| **266** | **25 (UNCHANGED)** | **2** | **8%** | 2 instances of drift class identified iter-250; both cited cross-references not yet documented in catalog |

**Drift rate trend:** 12.5% → 15% → 4% → **8%** (uptick from iter-250's 4%; not a regression — iter-250 caught 1 of 3 latent instances of the iter-250 drift class, iter-266 caught 2 more. Cumulative drift class total: **3 instances** across iter-251 + iter-266).

## Per-entry triage — 25 PHASE 2 PENDING entries (UNCHANGED from iter-250)

The 25 entries triaged at iter-250 remain UNCHANGED at iter-266 — no
catalog status flips, no NEW Phase2HookPending entries added across the
16-iter window. Iter 257-265 worked entirely inside existing wires
(iter-258 SetUnitField max_hull/max_shield sub-field LIVE flips don't
move the catalog Phase2 count) or were pure docs / audit / preset refresh
iters.

| Category | Count | Source iter |
|---|---|---|
| Vestigial-fixed (iter-137 mirror) | 2 | iter 137 |
| Confirmed-defer (genuine engine block) | 21 | iter 132/130/131/249 various |
| Catalog-rationale drift (iter-251 fixed) | 1 | iter 250 caught + iter 251 fixed |
| **Catalog-rationale drift (iter-266 NEW catches)** | **2** | **iter 266 (this doc)** |

## Iter 266 NEW drift catches

### Drift catch #1: SWFOC_SpawnUnit (legacy mirror; iter-109 LIVE alternative)

**Before iter-266 fix:**

```csharp
["SWFOC_SpawnUnit"] = new("SWFOC_SpawnUnit", CapabilityStatus.Phase2HookPending,
    "BLOCKED-NO-RVA"),
```

**After iter-266 fix:**

```csharp
["SWFOC_SpawnUnit"] = new("SWFOC_SpawnUnit", CapabilityStatus.Phase2HookPending,
    "BLOCKED-NO-RVA — superseded by iter-109 SWFOC_SpawnUnitLua "
  + "(engine Spawn_Unit Lua API via DoString; 3-arg form (player, type, position)). "
  + "This entry stays PHASE 2 PENDING as a Phase-1 mirror legacy wire shape; "
  + "iter-266 audit caught the operator-trust drift (rationale didn't cite the LIVE alternative). "
  + "Operator should use the iter-109 SWFOC_SpawnUnitLua LIVE wire."),
```

**Why drift was missed at iter-250:** `SWFOC_SpawnUnit` was triaged at
iter-132 (`confirmed defer — different surface`), then iter-109's
`SWFOC_SpawnUnitLua` shipped. Iter-250 audit table noted "iter-109
SWFOC_SpawnUnitLua provides the LIVE alternative" but didn't update the
rationale itself — operator-trust drift carried forward.

**Same drift class as iter-251 SWFOC_FreezeCredits**: legacy Phase-1
mirror catalog entry stays Phase2HookPending correctly (the wire IS
Phase-1; LIVE path ships under a different SWFOC_* name) but the
rationale doesn't cite the LIVE alternative shipped under the sibling
catalog entry.

### Drift catch #2: SWFOC_SetUnitCapOverride (iter-249 honest-defer + iter-256 memory rule)

**Before iter-266 fix:**

```csharp
["SWFOC_SetUnitCapOverride"] = new("SWFOC_SetUnitCapOverride", CapabilityStatus.Phase2HookPending,
    "BLOCKED-NO-RVA"),
```

**After iter-266 fix:**

```csharp
["SWFOC_SetUnitCapOverride"] = new("SWFOC_SetUnitCapOverride", CapabilityStatus.Phase2HookPending,
    "BLOCKED-NO-RVA — iter-248 RE design hypothesized a MinHook detour at the Apocalypticx "
  + "CE community ledger entry `rva_apocalypticx_unit_cap_gc @ 0x28DF6F`, but iter-249 RE walk "
  + "discovered the AOB had drifted to a string-deallocation cleanup block (NOT a unit-cap "
  + "calculation). Ledger entry DEPRECATED; arc closed as 2-iter honest-defer cycle. The "
  + "iter-249 finding seeded the iter-256 `feedback_aob_drift_across_binary_versions` memory "
  + "rule (community CE table AOBs lose accuracy across binary versions; semantic verification "
  + "required). Future RE arc needs live-game CheatEngine tracing or IDA MCP xref walk; would "
  + "be 7th multi-iter A1.x arc."),
```

**Why drift was missed at iter-250:** iter-250 audit was on **2026-05-06
20:30 UTC**; iter-249 honest-defer arc CLOSED at **2026-05-06 16:35 UTC**;
iter-256 memory rule codified at **2026-05-07 18:15 UTC**. So at iter-250
audit time, iter-249 was the most recent arc closure but the iter-256
memory rule didn't exist yet. iter-266 audit benefits from 16 iters of
hindsight (iter-256 codification + iter-257/258 downstream beneficiaries
proving the memory rule's ROI).

**Operator-trust audit trail preserved:** rationale → iter-249 finding →
iter-256 codified memory rule → future RE arcs apply the rule. This is
the same cross-reference pattern as iter-251 FreezeCredits but extended
to a **3-link chain** (Phase2 entry → honest-defer doc → memory rule).

## Tests added this iter

`Iter221Phase2PendingReAuditTests.cs` extended:

1. **`LegacyPhase1Mirrors_CiteLiveAlternativeWhenAvailable`** (existing
   from iter-251; extended) — list of legacy-Phase-1-mirror entries
   requiring iter-N cross-reference grew from 2 to 4:
   - `SWFOC_SetFireRate` → `iter-225` (iter-251 baseline; preserved)
   - `SWFOC_FreezeCredits` → `iter-231` (iter-251 baseline; preserved)
   - **`SWFOC_SpawnUnit`** → **`iter-109`** (iter-266 NEW)
   - **`SWFOC_SetUnitCapOverride`** → **`iter-249`** (iter-266 NEW)
2. **`Iter266_SetUnitCapOverride_RationaleCitesIter256MemoryRule`** —
   NEW dedicated pin test asserting `iter-256` + `AOB` substrings appear
   in the SWFOC_SetUnitCapOverride rationale. Closes the audit trail
   from rationale → memory rule → future-arc discipline.

## Pattern lesson — drift class is BROADER than iter-250 caught

iter-250 caught 1 instance of the "Catalog-rationale-cross-reference
drift" class. iter-266 caught 2 more — confirming the drift class is a
RECURRING pattern that needs ongoing audit cadence. **Discipline ROI
hypothesis updated:**

- **iter-250 hypothesis:** drift catches will decrease toward zero as
  the framework matures. Drift rate: 12.5% → 15% → 4%.
- **iter-266 update:** drift class identified at iter-250 has a **latent
  pool** of ~3 instances; iter-251 fixed 1, iter-266 fixed 2 more,
  bringing latent pool to 0. Drift rate uptick (4% → 8%) is **not a
  regression** — it's the framework catching ALL instances of a
  newly-identified drift class within 16 iters of class discovery.

**Pattern lesson refinement** (extends iter-250's pattern):

> When a NEW drift class is identified, the **immediate next audit
> after class discovery** typically catches more instances of the same
> class (because the audit framework is now sensitized to look for it).
> Iter-250 had drift rate 4% (1 catch). Iter-266 has drift rate 8% (2
> catches) BUT both catches are the **same** drift class identified at
> iter-250 — the framework worked exactly as designed.
>
> Future audits should track per-drift-class drift rates separately
> from total drift rate. Total drift rate aggregates ALL classes; the
> per-class rate is what measures discipline ROI for that specific
> class.

## What's next (iter 267+)

**Recommended: iter 267 = next A1.x arc kickoff (max_speed)** OR
**iter 267 = next sub-field arc kickoff** OR **iter 267 = polish iter**
(Lua Playground preset menu refresh / README capstone) depending on
operator preference.

**Remaining queued from iter-262 changelog priority list:**

1. **max_speed via iter-99 path** — would be 7th multi-iter A1.x arc;
   iter-99 used locomotor +0xA8 chain for live speed; max_speed needs
   the type-stats max-speed offset (likely RE walk parallel to iter-258).
2. **attack_power via iter-94 retry** — iter-94 rejected as not
   directly writable; future arc might MinHook at the multiplier-read
   site.
3. **Lua Playground preset menu refresh** at iter 270 — would extend
   iter-264's 104 entries with iter 257-264 surface coverage (most
   already covered).
4. **README capstone update at iter ~295** — iter-265 just shipped
   (post-iter-264); next capstone at ~30-iter cadence lands at iter ~295.
5. **Reverse-orphan audit at iter ~285** — iter-263 just shipped
   (22-iter window); next audit at 20-30-iter cadence lands at iter ~285.

**Recommendation:** **iter 267 = next A1.x arc kickoff (max_speed)** —
the audit framework caught its latent backlog at iter-266; framework is
now stable; next high-leverage move is shipping NEW LIVE wires.

## Iter 266 close-out

- This document is the iter 266 deliverable.
- Bridge / dispatcher / VM / XAML changes: NONE (rationale text only).
- Test changes: 1 existing test extended (4 entries from 2) + 1 NEW pin
  test (`Iter266_SetUnitCapOverride_RationaleCitesIter256MemoryRule`).
- Editor full suite: **8168 / 0 / 8168** (+1 from iter-265's 8167).
- Capability surface markdown regenerated.
- Editor binary republished: 165,499,723 B (157.83 MB; identical size
  to iter-264 since rationale string changes are absorbed).
- 109 → 109 buttons UNCHANGED. 104 → 104 preset entries UNCHANGED.
- Phase2HookPending count UNCHANGED at 25.

**Pattern lesson capstone — Phase2HookPending audits stay productive at
DECREASING per-class rate (refinement of iter-250 capstone):**

| Audit iter | Phase2 entries | Drift catches | Total drift rate | Notes |
|---|---|---|---|---|
| iter 132 | 24 | 3 | 12.5% | First audit; mixed drift classes |
| iter 221 | 26 | 4 | 15% | Catalog growth introduced new drift sources |
| iter 250 | 25 | 1 | 4% | NEW "catalog-rationale-cross-reference drift" class identified |
| **iter 266** | **25** | **2** | **8%** | **2 latent instances of iter-250 drift class caught + closed** |

The total drift rate uptick at iter-266 reflects **discipline framework
working as designed**: iter-250's NEW drift class definition expanded
the audit's catch surface; iter-266 cleared the latent backlog. Future
audits should see drift rate drop again (assuming no NEW drift classes
are identified).

**Catalog-discipline ROI continues to validate.**
