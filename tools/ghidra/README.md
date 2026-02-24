# Ghidra Headless Pipeline

This directory contains an automation-first reverse-engineering pipeline for SWFOC binaries.

## Goals

- Run Ghidra in headless mode only.
- Emit machine-readable symbol and capability artifacts.
- Keep raw decompilation output in CI artifacts instead of committing it to git.

## Entry Points

- `run-headless.ps1` (Windows)
- `run-headless.sh` (Linux/WSL)
- `emit-symbol-pack.py` (normalizes raw symbols into schema-backed outputs)
- `export_symbols.py` (Ghidra post-script executed by `analyzeHeadless`)

## Environment

- Set `GHIDRA_HOME` so scripts can find `support/analyzeHeadless`.
- Optional: set `SWFOC_GHIDRA_SYMBOL_PACK_ROOT` at runtime to override symbol-pack lookup root.
