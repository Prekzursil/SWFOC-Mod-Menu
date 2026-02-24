#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 2 ]]; then
  echo "usage: $0 <binary_path> <output_dir> [analysis_run_id]" >&2
  exit 2
fi

BINARY_PATH="$1"
OUTPUT_DIR="$2"
ANALYSIS_RUN_ID="${3:-$(date -u +%Y%m%d-%H%M%S)}"

if [[ -z "${GHIDRA_HOME:-}" ]]; then
  echo "error: GHIDRA_HOME is required" >&2
  exit 3
fi

ANALYZE_HEADLESS="$GHIDRA_HOME/support/analyzeHeadless"
if [[ ! -x "$ANALYZE_HEADLESS" ]]; then
  echo "error: analyzeHeadless not executable at $ANALYZE_HEADLESS" >&2
  exit 4
fi

if [[ ! -f "$BINARY_PATH" ]]; then
  echo "error: binary not found: $BINARY_PATH" >&2
  exit 5
fi

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
mkdir -p "$OUTPUT_DIR"

PROJECT_DIR="$OUTPUT_DIR/ghidra-project"
PROJECT_NAME="swfoc-${ANALYSIS_RUN_ID}"
RAW_SYMBOLS_PATH="$OUTPUT_DIR/raw-symbols.json"
SYMBOL_PACK_PATH="$OUTPUT_DIR/symbol-pack.json"
SUMMARY_PATH="$OUTPUT_DIR/analysis-summary.json"

"$ANALYZE_HEADLESS" "$PROJECT_DIR" "$PROJECT_NAME" \
  -import "$BINARY_PATH" \
  -scriptPath "$REPO_ROOT/tools/ghidra" \
  -postScript export_symbols.py "$RAW_SYMBOLS_PATH" \
  -deleteProject

python3 "$REPO_ROOT/tools/ghidra/emit-symbol-pack.py" \
  --raw-symbols "$RAW_SYMBOLS_PATH" \
  --binary-path "$BINARY_PATH" \
  --analysis-run-id "$ANALYSIS_RUN_ID" \
  --output-pack "$SYMBOL_PACK_PATH" \
  --output-summary "$SUMMARY_PATH"

echo "ghidra headless analysis complete"
echo " - raw symbols: $RAW_SYMBOLS_PATH"
echo " - symbol pack: $SYMBOL_PACK_PATH"
echo " - summary: $SUMMARY_PATH"
