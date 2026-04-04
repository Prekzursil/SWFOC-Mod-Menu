# CLAUDE.md — Universal Quality Contract

## Iron Law

**Every line of code you write must pass all quality gates on the first push.**
This is not aspirational. This is a hard requirement. If you push code that fails any gate, you have failed.

## Thresholds (non-negotiable)

- **Issues on all providers:** 0 (SonarCloud, CodeQL, QLTY)
- **Coverage:** 100% line AND branch (Codecov, SonarCloud)
- **Security alerts:** 0 (Dependabot, CodeQL)
- **Suppressions:** FORBIDDEN. No `// NOSONAR`, no `#pragma warning disable`, no `# noqa`, no `[SuppressMessage]`. Fix the code.

## Before Writing Any Code

1. **Read the existing gate configurations** in `.github/workflows/`, `sonar-project.properties`, `.qlty/qlty.toml` — know what will be checked.
2. **Check current dashboard state** — if the repo already has issues, your code must not add any, and you should fix existing ones when touching those files.
3. **Plan tests first** — TDD is mandatory. No production code without a failing test.

## Language-Specific Mandatory Patterns

### C# / .NET

```xml
<!-- Every .csproj MUST have these -->
<PropertyGroup>
  <Nullable>enable</Nullable>
  <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
  <AnalysisLevel>latest-all</AnalysisLevel>
  <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
</PropertyGroup>
```

**Null safety:** Every reference type parameter gets `ArgumentNullException.ThrowIfNull()`. Every member access on a nullable gets `?.` or a guard. Never use `!` null-forgiving operator unless you can prove the value is non-null in that exact scope.

**CLS Compliance:** Public APIs must not expose `uint`, `sbyte`, `ushort`, `ulong`. Add `[assembly: CLSCompliant(true)]` to `AssemblyInfo.cs` or a global using file.

**Exceptions:** Never catch bare `Exception`. Catch specific types. Never swallow exceptions with empty catch blocks.

**Complexity:** No method exceeds CCN 15. No method exceeds 50 lines. No method takes more than 5 parameters. If you're about to violate any of these, extract helper methods BEFORE writing the logic.

**Coverage:** Every public method has tests. Every branch has a test. Use `[Theory]` with `[InlineData]` for multiple branches. Use `Moq` or `NSubstitute` for dependencies. Run `dotnet test --collect:"XPlat Code Coverage"` locally before pushing.

### Python

**Type hints:** All function signatures must have type annotations. Use `from __future__ import annotations`.

**Security:** Never `subprocess.call(cmd, shell=True)`. Never hardcode secrets. Use `os.environ[]` or a config system. Never `eval()` or `exec()` with untrusted input.

**Complexity:** Same limits — CCN ≤ 15, function length ≤ 50 lines, parameters ≤ 5. Use dispatch tables instead of long if/elif chains.

**Imports:** No wildcard imports. No unused imports. Sort with `isort`.

### TypeScript / JavaScript

**Strict mode:** `"strict": true` in tsconfig.json. No `any` types unless wrapped in a named type with documentation.

**ESLint:** `--max-warnings 0`. Zero warnings, zero errors.

**No `console.log` in production code.** Use a proper logger.

### C / C++

**Modern C++:** Use `auto` where type is obvious. Use smart pointers (`std::unique_ptr`, `std::shared_ptr`) instead of raw `new/delete`. Use `std::string_view` for read-only string parameters. Use `constexpr` where possible.

**Casts:** Prefer `static_cast` over C-style casts. Use `std::bit_cast` for type punning. `reinterpret_cast` only in isolated, documented wrapper functions.

**Includes:** Use lowercase for all includes. Include what you use, no transitive dependency reliance.

## Pre-Push Verification (MANDATORY)

Before EVERY push, run and confirm zero failures:

```bash
# .NET repos
dotnet build --warnaserror
dotnet test --collect:"XPlat Code Coverage" -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.ExcludeByAttribute=GeneratedCodeAttribute
# Verify: 0 errors, 0 warnings, 100% coverage

# Python repos
python -m pytest --cov=src --cov-branch --cov-fail-under=100
python -m bandit -r src/ -ll
python -m lizard src/ -C 15 -L 60 -a 5
# Verify: 0 failures, 0 findings, 0 violations

# Node.js repos
npx eslint . --max-warnings 0
npx jest --coverage --coverageThreshold='{"global":{"branches":100,"functions":100,"lines":100}}'
# Verify: 0 warnings, 100% coverage

# C++ repos
cmake --build build --config Release 2>&1 | grep -c "warning:" # must be 0
ctest --test-dir build --output-on-failure
# Verify: 0 warnings, 0 test failures
```

**If any of these fail, DO NOT PUSH. Fix first.**

## Working with Quality Gates

**Do not:**
- Add files to exclusion lists (`sonar-project.properties` exclusions)
- Add suppression comments/attributes
- Lower thresholds
- Mark issues as "won't fix" or "false positive" without explicit human approval
- Ignore a failing check because "it'll pass after merge"
- Assume a check will pass — verify

**Do:**
- Fix the actual code that triggers the finding
- If something is genuinely a false positive, explain WHY to the human and wait for approval
- Run local verification before pushing
- Check dashboard counts after every push
- Treat every provider's findings as valid until proven otherwise

## Agent Delegation Rules

When delegating to subagents (Codex, parallel agents):
- Every subagent must follow these same rules
- Verify subagent output independently — agent reports of "success" are not evidence
- Run the pre-push verification on subagent work before committing
- If a subagent introduces quality regressions, fix them before merging the subagent's work

## Definition of Done

A task is complete when:
1. All tests pass (verified by running them, not by assumption)
2. Coverage is 100% line AND branch (verified by coverage report)
3. Local linting/analysis shows 0 issues (verified by running tools)
4. CI is green after push (verified by checking GitHub Actions)
5. SonarCloud, CodeQL, and QLTY dashboards show 0 issues
6. No suppressions, exclusions, or workarounds were used
