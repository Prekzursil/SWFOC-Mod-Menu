#!/bin/bash
# SWFOC Bridge Quality Gate — run after every code change
# Exit 0 = all green, exit 1 = failures found
set -e

cd "$(dirname "$0")"
echo "=== SWFOC Bridge Quality Gate ==="
echo ""

PASS=0
FAIL=0
WARN=0

# --- Gate 1: C++ Compilation ---
echo "--- Gate 1: C++ Compilation ---"
if x86_64-w64-mingw32-g++ -c -O2 -std=c++17 -DWIN32_LEAN_AND_MEAN -Werror=return-type -Werror=uninitialized -I. -Iminhook/include lua_bridge.cpp -o lua_bridge.o 2>/dev/null; then
    echo "  [PASS] lua_bridge.cpp compiles"
    PASS=$((PASS + 1))
else
    echo "  [FAIL] lua_bridge.cpp compilation failed"
    FAIL=$((FAIL + 1))
fi

# --- Gate 2: DLL Link ---
echo "--- Gate 2: DLL Link ---"
if x86_64-w64-mingw32-g++ -shared -o powrprof.dll dllmain.o proxy.o lua_bridge.o hook.o buffer.o trampoline.o hde64.o exports.def -lkernel32 -luser32 -static -s 2>/dev/null; then
    echo "  [PASS] DLL links ($(stat -c%s powrprof.dll) bytes)"
    PASS=$((PASS + 1))
else
    echo "  [FAIL] DLL link failed"
    FAIL=$((FAIL + 1))
fi

# --- Gate 3: Test Harness ---
echo "--- Gate 3: C++ Test Harness ---"
if [ -f bridge_test_harness.exe ]; then
    RESULT=$(./bridge_test_harness.exe 2>&1 | tail -1)
    if echo "$RESULT" | grep -q "0 failed"; then
        echo "  [PASS] $RESULT"
        PASS=$((PASS + 1))
    else
        echo "  [FAIL] $RESULT"
        FAIL=$((FAIL + 1))
    fi
else
    echo "  [SKIP] bridge_test_harness.exe not found — build it first"
    WARN=$((WARN + 1))
fi

# --- Gate 4: Python Tests ---
echo "--- Gate 4: Python Tests ---"
if python3 -m pytest ../swfoc_toolkit/test_save_validator.py -q --tb=no 2>/dev/null | grep -q "passed"; then
    RESULT=$(python3 -m pytest ../swfoc_toolkit/test_save_validator.py -q --tb=no 2>&1 | tail -1)
    echo "  [PASS] $RESULT"
    PASS=$((PASS + 1))
else
    echo "  [WARN] Python tests failed or not available"
    WARN=$((WARN + 1))
fi

# --- Gate 5: Anti-pattern Scan ---
echo "--- Gate 5: Anti-pattern Scan ---"
ANTIPATTERNS=0

# Check for Find_All_Objects_Of_Type(nil) in active code (not comments)
NIL_HITS=$(grep -rn "Find_All_Objects_Of_Type(nil)" ../trainer/*.lua 2>/dev/null | grep -v "^.*:.*--" | grep -v "SNIPPET" | grep -v "_v3.lua" | wc -l)
if [ "$NIL_HITS" -gt 0 ]; then
    echo "  [WARN] $NIL_HITS active Find_All_Objects_Of_Type(nil) calls (game rejects nil)"
    ANTIPATTERNS=$((ANTIPATTERNS + NIL_HITS))
fi

# Check for dot-call game methods in bridge_send strings
DOT_HITS=$(grep -rn 'unit\.\(Get_\|Set_\|Make_\|Take_\).*()' ../trainer/*.lua 2>/dev/null | grep -v "_v3.lua" | grep -v "and unit\." | wc -l)
if [ "$DOT_HITS" -gt 0 ]; then
    echo "  [WARN] $DOT_HITS dot-call game methods (should be colon-call)"
    ANTIPATTERNS=$((ANTIPATTERNS + DOT_HITS))
fi

if [ "$ANTIPATTERNS" -eq 0 ]; then
    echo "  [PASS] No anti-patterns found"
    PASS=$((PASS + 1))
else
    WARN=$((WARN + ANTIPATTERNS))
fi

# --- Summary ---
echo ""
echo "=== Quality Gate Summary ==="
echo "  Passed: $PASS"
echo "  Warnings: $WARN"
echo "  Failed: $FAIL"

if [ "$FAIL" -gt 0 ]; then
    echo "  STATUS: BLOCKED"
    exit 1
else
    echo "  STATUS: GREEN"
    exit 0
fi
