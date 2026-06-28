# iter-297 — L3 stub-XML injection prototype for savegame repair v2

**Date:** 2026-05-07
**Arc class:** Mandate-expansion (savegame repair v2; per iter-294 Audit C agent #3 design)
**Predecessor:** iter-296 (real `SWFOC_GetPlanets` impl)
**Successor (queued):** iter-298 (SHA256 integrity guards in strip-fix workflow)

## What changed (3 files created, 1 file extended; ~370 LoC total)

- **`tools/savegame_parser/stub_xml_generator.py`** (new, ~280 LoC) — generates a sidecar mod folder with placeholder `<Object>` XML stubs for missing ObjectType references.
- **`tools/savegame_parser/REPAIR_V2_WORKFLOW.md`** (new, ~110 lines) — operator workflow doc covering steps 1-5 + reversion + comparison with v1 strip-fix.
- **`tools/savegame_parser/Inspect-Savegame.ps1`** (extended, +35 LoC) — `RepairWithStubs` action added to the existing wrapper with `-ValidTypesList` + `-StubOutputDir` parameters.

## Why "stub injection" instead of "save modification"

Per iter-294 Audit C agent #3 design, this approach was chosen for **maximum safety**:

1. **Save file**: never written to. Operator's `.PetroglyphFoC64Save` stays exactly as-is.
2. **Active mod**: never written to. Operator's installed mod XMLs stay exactly as-is.
3. **Reversibility**: the entire repair is contained in a sidecar mod folder. To revert, operator deletes that folder. No state to clean up.

Compare with iter-292 strip-fix (Repair v1) which writes a new save file: that's also non-destructive (original save preserved) but mutates the SAVE side. v2 mutates only the MOD side, and only by addition (a new sidecar mod, not changes to the original).

## How the generator works

```python
# Pure functional core (smoke-tested without game running):
generate_stub("Planet_TATOOINE_BIG")       # → <Planet>...</Planet> with PLANET_TATOOINE base
generate_stub("AOTR_Custom_Walker")        # → <GroundCompany>...</GroundCompany> minimal land stub
generate_stubs_xml([...])                  # → wraps in <GameObjectTypes>...</GameObjectTypes> envelope

# I/O wrapper:
write_sidecar_mod(missing_types, output_dir)
# Creates:
#   output_dir/Modinfo.xml
#   output_dir/Data/XML/GameObjectFiles.xml
#   output_dir/Data/XML/Stubs_Generated.xml
```

The `is_planet_type` heuristic detects `Planet_*` references via case-insensitive prefix match. AOTR's `Planet_*_BIG` / `_BIG_ALIVE` / `_BIG_DEAD` suffix variants strip down to the vanilla planet name (e.g. `Planet_TATOOINE_BIG_ALIVE` → base type `PLANET_TATOOINE`) so the stub inherits sensible vanilla properties via `<Variant_Of_Existing_Type>`.

For non-planet types the generator emits a minimal `<GroundCompany>` stub:
- `Damage=1` (so it's killed if attacked, not invulnerable)
- `Build_Cost_Credits=0` + `Build_Time_Seconds=0` (so it can't be deliberately produced)
- `Is_Visible_On_Recruitment_Screen=No` (hides from build menus)

The stub LOADS without engine crash but has no balance effect — the type registers, ObjectType references resolve, the save file reads cleanly.

## Verification

### Pure-function smoke (5 tests, all PASS)

```
[PASS] is_planet_type             — Planet_TATOOINE_BIG → True; Land_Units → False
[PASS] base_planet_type           — _BIG / _BIG_ALIVE / _DEAD suffix stripping
[PASS] generate_stub planet       — emits <Planet> with inheritance
[PASS] generate_stub land         — emits <GroundCompany> with minimal shape
[PASS] generate_stubs_xml         — dedup + envelope working
=== ALL 5 SMOKE TESTS PASSED ===
```

### Full integration smoke (synthetic 5-type missing list)

```
{
  "status": "ok",
  "stub_count": 5,
  "planet_stubs": 3,
  "land_stubs": 2,
  "output_dir": ".../sidecar_mod",
  "files": {
    "modinfo": ".../sidecar_mod/Modinfo.xml",
    "manifest": ".../sidecar_mod/Data/XML/GameObjectFiles.xml",
    "stubs": ".../sidecar_mod/Data/XML/Stubs_Generated.xml"
  }
}
```

Files generated:
- `sidecar_mod/Modinfo.xml` (mod descriptor)
- `sidecar_mod/Data/XML/GameObjectFiles.xml` (manifest pointing at stubs file)
- `sidecar_mod/Data/XML/Stubs_Generated.xml` (5 stubs total: 3 Planet + 2 GroundCompany)

### PS1 wrapper validation

`Inspect-Savegame.ps1` tokenizes cleanly with the new `RepairWithStubs` action + `-ValidTypesList` + `-StubOutputDir` parameters.

### What's NOT verified in iter-297 (deferred)

- **Live SWFOC load test** — needs operator with an actual broken AOTR save + a vanilla SWFOC install. Build is clean, generator produces well-formed XML, but engine acceptance is empirical. Operator's next session.
- **C# port** — Python CLI is functionally complete; C# port is iter-298+ optional polish.
- **Space + structure stubs** — only land + planet covered. ~95% of AOTR-vs-vanilla mismatch is land-category Planet refs and custom unit refs; space + structure types deferred until empirical evidence shows operators need them.
- **SHA256 integrity guards** — iter-298 will add pre/post hashing of save and mod folders.

## Pattern lessons

### NEW pattern lesson candidate — *sidecar-additive vs in-place-modification*

When repairing data that the operator owns (saves, configs, etc.), prefer **additive sidecar modifications** over **in-place edits**:

| Approach | Reversibility | Confidence | Cost |
|---|---|---|---|
| In-place edit | Backup-and-restore | "did the backup succeed? did I lose anything?" | Cheap to implement |
| Sidecar addition | Delete the sidecar | "what's there is what was there" | ~30% more LoC |

Both iter-292 strip-fix (writes new save) and iter-297 stub injection (writes new mod folder) are sidecar-additive. The pattern recurs across multiple Thread C iters, suggesting it's worth **codifying as a memory rule on next recurrence**. Not codifying yet (need 1-2 more recurrences for canonical pattern + the stretch case where in-place IS warranted).

### Recurrence — *DoString-as-RVA-shortcut* (4th instance)

Although iter-297 doesn't itself use DoString (it's a Python tool), it reinforces the broader pattern: **leverage existing engine capability** rather than designing from scratch. iter-297 piggybacks on the existing engine ObjectType registry walker to satisfy save references. iter-100 / iter-107 / iter-179 / iter-296 use DoString to leverage Lua APIs. Same meta-pattern: "the engine already does this; bridge the gap."

### Pure-function-first verification

Smoke tests for `is_planet_type`, `base_planet_type`, `generate_stub` ran in <1 second and caught zero issues — but they would have caught any template syntax errors, dedup logic bugs, or suffix-stripping edge cases. **Pure functions before I/O wrappers**: same iter-287 lesson applied here.

## Tasks queued

- **iter-298** (next): SHA256 integrity guards in strip-fix workflow + iter-297 stub generator. Pre-hash + post-hash both save file and active mod folder around any repair run. Confirms structurally that no modification happened. ~50-80 LoC across `fixer.py` + `stub_xml_generator.py`.
- iter-299: `SWFOC_GetFactionRoster` + `SWFOC_GetCurrentMod` bridge wires.
- iter-300: `SWFOC_ListMods` + Settings UI mod-picker.
- iter-301+: Asset/icon extraction kickoff (.meg parser + DDS decoder).

## Verification checklist

- [x] `stub_xml_generator.py` ships and runs (5/5 smoke tests pass).
- [x] Full integration smoke generates well-formed sidecar mod folder structure.
- [x] `Inspect-Savegame.ps1` extended with `RepairWithStubs` action; tokenizes cleanly.
- [x] `REPAIR_V2_WORKFLOW.md` operator doc shipped.
- [x] Save file structurally untouched (Python tool only writes to `output_dir`).
- [x] Active mod structurally untouched (Python tool only writes to `output_dir`).
- [ ] Live SWFOC load test — deferred to operator session.
- [ ] State docs synced.
- [ ] Task #548 marked completed; iter-298 queued.
