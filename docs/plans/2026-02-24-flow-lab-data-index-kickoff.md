# Flow Lab + Data Index Kickoff (2026-02-24)

## Scope
- Add first-class project scaffolds for game-flow extraction and MegaFiles indexing.
- Keep runtime mutation paths unchanged and fail-closed.
- Provide fixture-based deterministic tests to lock behavior before broader implementation.

## Delivered in Kickoff
1. `SwfocTrainer.Flow` project
- `FlowModeHint` with `Unknown`, `Galactic`, `TacticalLand`, `TacticalSpace`.
- `StoryPlotFlowExtractor` that parses story XML and extracts `STORY_*` events.
- Event records carry source file, script reference, mode hint, and attributes.

1. `SwfocTrainer.DataIndex` project
- `MegaFilesXmlIndexBuilder` that parses MegaFiles-style XML declarations.
- Produces ordered file index with enabled/disabled state and diagnostics for invalid entries.

1. Test coverage
- `StoryPlotFlowExtractorTests` validates tactical/galactic event mapping and synthetic-plot fallback.
- `MegaFilesXmlIndexBuilderTests` validates load-order parsing, enabled-state parsing, and diagnostics.

## Guardrails
- No change to runtime attach/mutation behavior in this kickoff.
- No claim of full MEG binary parsing yet; this phase is XML/index contract foundation.

## Next Steps
1. Add MEG archive reader primitives and integrate into DataIndex composition.
2. Expand Flow extractor to resolve event reward references and linked Lua scripts.
3. Introduce a UI surface to browse flow/index outputs and export JSON snapshots.
4. Align core runtime mode model to explicit land/space/galactic semantics after compatibility review.
