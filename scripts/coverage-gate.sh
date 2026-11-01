#!/usr/bin/env bash
# C++ tests + STRICT 100% line AND branch coverage for the swfoc_overlay
# header-only units — the compiled-language half of QZP lean gate 3
# ("gate-tests-coverage compiled (go / c++)", reusable-quality.yml).
#
# MEASURED SCOPE (declared; also in CONTRIBUTING.md):
#   * every swfoc_overlay/*_test.cpp is compiled with plain g++ -std=c++17
#     --coverage at -O0 and executed; gcovr then measures the
#     swfoc_overlay/*.h header units those tests exercise, and fails unless
#     every measured line AND every measured branch is covered.
#   * --exclude-throw-branches and --exclude-unreachable-branches drop the
#     exception-unwind and no-code branches gcc synthesizes for C++; every
#     source-level decision is still counted.
#
# DECLARED OUT of the Linux-measured scope (Win32/MinGW-only code that cannot
# link on the ubuntu runner; reasoned here and in CONTRIBUTING.md — never a
# silent pass):
#   * swfoc_lua_bridge/ (test_harness.cpp and the bridge DLL sources): needs
#     <windows.h>, MinHook and x86_64-w64-mingw32-g++; built and run on
#     Windows by that directory's own .bat scripts.
#   * the swfoc_overlay DLL translation units that need ImGui/D3D9/Win32
#     (dllmain.cpp, overlay.cpp, hud_state.cpp, overlay_action_worker.cpp):
#     built by swfoc_overlay/build.bat with MinGW on Windows. Their logic
#     lives in the header units measured above.
#
# The run leaves coverage/cpp/{lcov.info,summary.json} (gitignored) for anyone
# who wants the raw numbers.
set -euo pipefail

cd "$(dirname "$0")/.."

GPP="${GPP:-g++}"
GCOVR_VERSION="8.7"
BUILD_DIR="build/cpp-test"

"$GPP" --version | head -n 1

# ── 1. build + run every portable overlay unit test, instrumented ───────────
rm -rf "$BUILD_DIR"
mkdir -p "$BUILD_DIR"

tests=(swfoc_overlay/*_test.cpp)
if [ "${#tests[@]}" -eq 0 ]; then
  echo "FAIL C++ coverage gate: no swfoc_overlay/*_test.cpp found — the gate cannot silently skip." >&2
  exit 1
fi

for t in "${tests[@]}"; do
  name="$(basename "$t" .cpp)"
  echo "::group::build+run $name"
  # -Wall -Wextra (not -Werror): the Windows build scripts already enforce
  # -Werror with the MinGW toolchain this code targets; a different g++ on the
  # CI runner must not turn an unrelated new warning into a coverage-lane
  # failure. Warnings are still printed in the log.
  "$GPP" -std=c++17 -O0 -g -Wall -Wextra --coverage "$t" -o "$BUILD_DIR/$name"
  "$BUILD_DIR/$name"
  echo "::endgroup::"
done
echo "PASS C++ unit tests: ${#tests[@]} suite(s) built and executed."

# ── 2. gcovr over the measured scope, 100% line AND branch ─────────────────
# gcovr lives in a throwaway virtualenv outside the checkout, so nothing it
# installs is visible to the gates that scan the tree after this one.
gcovr_venv="$(mktemp -d)"
trap 'rm -rf "$gcovr_venv"' EXIT
python3 -m venv "$gcovr_venv"
"$gcovr_venv/bin/python" -m pip install --disable-pip-version-check --quiet \
  "gcovr==${GCOVR_VERSION}"
gcovr="$gcovr_venv/bin/gcovr"

mkdir -p coverage/cpp
tracefile="coverage/cpp/coverage.json"

"$gcovr" \
  --root . \
  --filter '^swfoc_overlay/' \
  --exclude 'swfoc_overlay/.*_test\.cpp$' \
  --object-directory "$BUILD_DIR" \
  --merge-mode-functions=merge-use-line-min \
  --exclude-throw-branches \
  --exclude-unreachable-branches \
  --exclude-lines-by-pattern '^\s*\}\s*$' \
  --json "$tracefile"

echo "::group::C++ line coverage by file (Missing = uncovered lines)"
"$gcovr" --root . --add-tracefile "$tracefile" --txt-metric line --txt
echo "::endgroup::"
echo "::group::C++ branch coverage by file (Missing = lines with an uncovered branch)"
"$gcovr" --root . --add-tracefile "$tracefile" --txt-metric branch --txt
echo "::endgroup::"

# gcovr exits 2 (line) / 4 (branch) below the bar; --print-summary always
# prints both totals first, so a failing run still shows the measured figures.
"$gcovr" --root . --add-tracefile "$tracefile" \
  --lcov coverage/cpp/lcov.info \
  --json-summary coverage/cpp/summary.json \
  --print-summary \
  --fail-under-line 100 \
  --fail-under-branch 100 || {
  echo "FAIL C++ coverage: the swfoc_overlay header units must be 100% line AND branch covered (see the per-file tables above)." >&2
  exit 1
}
echo "PASS C++ coverage: the swfoc_overlay header units are 100% line and branch covered."
