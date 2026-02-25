# Ghidra Headless Pipeline

This directory contains an automation-first reverse-engineering pipeline for SWFOC binaries.

## Goals

- Run Ghidra in headless mode only.
- Emit machine-readable symbol and capability artifacts.
- Keep symbol-pack anchor IDs/addresses deterministic across exporter order variance.
- Keep raw decompilation output in CI artifacts instead of committing it to git.

## Entry Points

- `run-headless.ps1` (Windows)
- `run-headless.sh` (Linux/WSL)
- `emit-symbol-pack.py` (normalizes raw symbols into schema-backed outputs)
- `check-determinism.py` (verifies anchor/capability determinism and emits classification-coded report)
- `export_symbols.py` (Ghidra post-script executed by `analyzeHeadless`)
- `check-determinism.py` (verifies deterministic symbol-pack output across reordered input symbols)

## Environment

- Set `GHIDRA_HOME` so scripts can find `support/analyzeHeadless`.
- Optional: set `SWFOC_GHIDRA_SYMBOL_PACK_ROOT` at runtime to override symbol-pack lookup root.
- `run-headless` now emits `determinism/determinism-report.json` and fails with classification code `GHIDRA_DETERMINISM_MISMATCH` when output diverges.
