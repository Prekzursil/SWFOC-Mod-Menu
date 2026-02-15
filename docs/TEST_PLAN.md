# Test Plan

## Automated tests

- `ProfileInheritanceTests`
  - verifies cross-profile inheritance (`roe` includes base + aotr actions)
  - verifies manifest includes all target profiles
- `SaveCodecTests`
  - load schema + synthetic save
  - edit key fields
  - validate rules
  - roundtrip write/load

## Manual runtime checks

For each profile (`base_sweaw`, `base_swfoc`, `aotr_1397421866_swfoc`, `roe_3447786229_swfoc`):

1. Launch game + target mode.
2. Load profile and attach.
3. Execute:
   - credits change
   - timer freeze toggle
   - fog reveal toggle
   - selected unit HP/shield/speed edit (tactical)
   - helper spawn action
4. Save editor pass:
   - load save
   - edit credits + hero respawn fields
   - validate + write edited save
   - load in-game to confirm integrity.
