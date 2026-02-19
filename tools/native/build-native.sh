#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
BUILD_DIR="${1:-$ROOT_DIR/native/build-wsl}"
CONFIG="${2:-Release}"

LOCAL_BIN="$ROOT_DIR/tools/native/.local/bin"
if [[ -d "$LOCAL_BIN" ]]; then
  export PATH="$LOCAL_BIN:$PATH"
fi

if ! command -v cmake >/dev/null 2>&1; then
  echo "cmake not found. Run tools/native/bootstrap-wsl-toolchain.sh first." >&2
  exit 1
fi

if ! command -v ninja >/dev/null 2>&1; then
  echo "ninja not found. Run tools/native/bootstrap-wsl-toolchain.sh first." >&2
  exit 1
fi

cmake -S "$ROOT_DIR/native" -B "$BUILD_DIR" -G Ninja -DCMAKE_BUILD_TYPE="$CONFIG"
cmake --build "$BUILD_DIR" --config "$CONFIG"

echo "Native build complete: $BUILD_DIR ($CONFIG)"
