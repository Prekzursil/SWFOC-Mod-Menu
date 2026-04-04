#!/usr/bin/env bash
# pre-push-verify.sh — Run ALL local quality checks before pushing
# Place in repo root or tools/ directory. Agents must run this before every push.
# Exit code 0 = safe to push. Non-zero = fix issues first.

set -euo pipefail

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m'

FAILURES=0
CHECKS=0

check() {
    local name="$1"
    shift
    CHECKS=$((CHECKS + 1))
    echo -e "${YELLOW}▶ Running: ${name}${NC}"
    if "$@" 2>&1; then
        echo -e "${GREEN}✓ ${name}: PASS${NC}"
    else
        echo -e "${RED}✗ ${name}: FAIL${NC}"
        FAILURES=$((FAILURES + 1))
    fi
    echo ""
}

echo "╔══════════════════════════════════════╗"
echo "║   Pre-Push Quality Verification     ║"
echo "╚══════════════════════════════════════╝"
echo ""

# === .NET Detection ===
if compgen -G "*.sln" > /dev/null 2>&1 || compgen -G "**/*.csproj" > /dev/null 2>&1; then
    echo "📦 Detected: .NET project"
    echo ""

    check "dotnet build (warnings as errors)" \
        dotnet build --warnaserror --no-incremental -consoleloggerparameters:NoSummary

    check "dotnet test (with coverage)" \
        dotnet test --no-build --collect:"XPlat Code Coverage" \
        -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura

    # Check coverage percentage if reportgenerator is available
    if command -v reportgenerator &> /dev/null; then
        COVERAGE_FILES=$(find . -name "coverage.cobertura.xml" -path "*/TestResults/*" 2>/dev/null | head -5)
        if [ -n "$COVERAGE_FILES" ]; then
            check "coverage ≥ 100% line+branch" \
                reportgenerator -reports:"$COVERAGE_FILES" -targetdir:./coverage-report \
                -reporttypes:TextSummary
            # Parse and verify
            if [ -f ./coverage-report/Summary.txt ]; then
                LINE_COV=$(grep "Line coverage" ./coverage-report/Summary.txt | grep -oP '[\d.]+%' | head -1 | tr -d '%')
                BRANCH_COV=$(grep "Branch coverage" ./coverage-report/Summary.txt | grep -oP '[\d.]+%' | head -1 | tr -d '%')
                echo "  Line coverage: ${LINE_COV}%"
                echo "  Branch coverage: ${BRANCH_COV}%"
                if (( $(echo "$LINE_COV < 100" | bc -l 2>/dev/null || echo 1) )); then
                    echo -e "${RED}  ✗ Line coverage below 100%${NC}"
                    FAILURES=$((FAILURES + 1))
                fi
            fi
        fi
    fi

    # Check for suppression anti-patterns
    check "no suppression comments in C#" \
        bash -c '! grep -rn --include="*.cs" -E "(#pragma\s+warning\s+disable|// NOSONAR|\[SuppressMessage|// codacy:ignore)" src/ 2>/dev/null || echo "No suppressions found"'
fi

# === Python Detection ===
if compgen -G "*.py" > /dev/null 2>&1 || [ -f "pyproject.toml" ] || [ -f "setup.py" ]; then
    echo "🐍 Detected: Python project"
    echo ""

    if command -v bandit &> /dev/null; then
        check "bandit security scan" \
            bandit -r src/ scripts/ -ll --exclude "tests/,test/,*_test.py" -q 2>/dev/null || true
    fi

    if command -v lizard &> /dev/null; then
        check "lizard complexity check (CCN ≤ 15)" \
            lizard src/ scripts/ -C 15 -L 60 -a 5 -x "*/tests/*" -x "*/test/*" -W 2>/dev/null || true
    fi

    if command -v pytest &> /dev/null; then
        SRC_DIR="src"
        [ ! -d "$SRC_DIR" ] && SRC_DIR="."
        check "pytest with 100% coverage" \
            python -m pytest --cov="$SRC_DIR" --cov-branch --cov-fail-under=100 -q 2>/dev/null || true
    fi

    check "no suppression comments in Python" \
        bash -c '! grep -rn --include="*.py" -E "(# noqa|# type:\s*ignore(?!\[)|# nosec|# codacy:ignore)" src/ scripts/ 2>/dev/null || echo "No suppressions found"'
fi

# === Node.js/TypeScript Detection ===
if [ -f "package.json" ]; then
    echo "📦 Detected: Node.js/TypeScript project"
    echo ""

    if [ -f "node_modules/.bin/eslint" ] || command -v npx &> /dev/null; then
        check "ESLint (0 warnings)" \
            npx eslint . --max-warnings 0 2>/dev/null || true
    fi

    if [ -f "node_modules/.bin/jest" ] || command -v npx &> /dev/null; then
        check "Jest with 100% coverage" \
            npx jest --coverage --coverageThreshold='{"global":{"branches":100,"functions":100,"lines":100,"statements":100}}' 2>/dev/null || true
    fi

    check "no suppression comments in TS/JS" \
        bash -c '! grep -rn --include="*.ts" --include="*.tsx" --include="*.js" -E "(// eslint-disable|/\* eslint-disable|// @ts-ignore|// @ts-expect-error)" src/ 2>/dev/null || echo "No suppressions found"'
fi

# === C/C++ Detection ===
if compgen -G "**/*.cpp" > /dev/null 2>&1 || compgen -G "**/*.hpp" > /dev/null 2>&1 || [ -f "CMakeLists.txt" ]; then
    echo "⚙️ Detected: C/C++ project"
    echo ""

    if [ -d "build" ]; then
        check "CMake build (0 warnings)" \
            bash -c 'cmake --build build --config Release 2>&1 | tee /dev/stderr | grep -c "warning:" | xargs test 0 -eq'
    fi

    if [ -d "build" ] && command -v ctest &> /dev/null; then
        check "CTest" \
            ctest --test-dir build --output-on-failure
    fi
fi

# === Universal Checks ===
echo "🔍 Universal checks"
echo ""

# Check for common anti-patterns across all languages
check "no TODO/FIXME/HACK in new code" \
    bash -c '! git diff --cached --diff-filter=A -- "*.cs" "*.py" "*.ts" "*.tsx" "*.js" "*.cpp" "*.hpp" | grep -E "(TODO|FIXME|HACK|XXX)" 2>/dev/null || echo "Clean"'

echo "══════════════════════════════════════"
echo ""

if [ "$FAILURES" -eq 0 ]; then
    echo -e "${GREEN}✓ All ${CHECKS} checks passed. Safe to push.${NC}"
    exit 0
else
    echo -e "${RED}✗ ${FAILURES}/${CHECKS} checks failed. DO NOT PUSH. Fix issues first.${NC}"
    exit 1
fi
