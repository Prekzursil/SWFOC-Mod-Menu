#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
LOCAL_DIR="$ROOT_DIR/tools/native/.local"
LOCAL_BIN="$LOCAL_DIR/bin"
mkdir -p "$LOCAL_BIN"

have_cmake=0
have_ninja=0

if command -v cmake >/dev/null 2>&1; then
  have_cmake=1
fi

if command -v ninja >/dev/null 2>&1; then
  have_ninja=1
fi

if [[ $have_cmake -eq 1 && $have_ninja -eq 1 ]]; then
  echo "WSL toolchain already available: cmake + ninja"
  exit 0
fi

if command -v sudo >/dev/null 2>&1 && sudo -n true >/dev/null 2>&1; then
  echo "Installing cmake + ninja-build via apt..."
  sudo apt-get update
  sudo apt-get install -y cmake ninja-build
  echo "Installed via apt."
  exit 0
fi

echo "sudo non-interactive install unavailable; bootstrapping portable toolchain in $LOCAL_DIR"

download() {
  local url="$1"
  local out="$2"
  curl -fsSL "$url" -o "$out"
  return 0
}

if [[ $have_cmake -eq 0 ]]; then
  CMAKE_VER="3.30.5"
  CMAKE_TAR="cmake-${CMAKE_VER}-linux-x86_64.tar.gz"
  CMAKE_URL="https://github.com/Kitware/CMake/releases/download/v${CMAKE_VER}/${CMAKE_TAR}"
  TMP_CMAKE="$LOCAL_DIR/$CMAKE_TAR"
  download "$CMAKE_URL" "$TMP_CMAKE"
  tar -xzf "$TMP_CMAKE" -C "$LOCAL_DIR"
  ln -sf "$LOCAL_DIR/cmake-${CMAKE_VER}-linux-x86_64/bin/cmake" "$LOCAL_BIN/cmake"
  rm -f "$TMP_CMAKE"
fi

if [[ $have_ninja -eq 0 ]]; then
  NINJA_VER="1.12.1"
  NINJA_ZIP="ninja-linux.zip"
  NINJA_URL="https://github.com/ninja-build/ninja/releases/download/v${NINJA_VER}/${NINJA_ZIP}"
  TMP_NINJA="$LOCAL_DIR/$NINJA_ZIP"
  download "$NINJA_URL" "$TMP_NINJA"
  if command -v unzip >/dev/null 2>&1; then
    unzip -o "$TMP_NINJA" -d "$LOCAL_BIN"
  else
    python3 -m zipfile -e "$TMP_NINJA" "$LOCAL_BIN"
  fi
  chmod +x "$LOCAL_BIN/ninja"
  rm -f "$TMP_NINJA"
fi

echo "Portable toolchain ready."
echo "Add to PATH for this shell:"
echo "  export PATH=\"$LOCAL_BIN:\$PATH\""
