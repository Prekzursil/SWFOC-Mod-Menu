```markdown
# SWFOC-Mod-Menu Development Patterns

> Auto-generated skill from repository analysis

## Overview
This skill teaches the core development patterns and workflows used in the SWFOC-Mod-Menu TypeScript codebase. It covers file naming, import/export conventions, commit message style, testing patterns, and CI/CD workflow maintenance. This guide is intended to help contributors quickly align with the project's established practices and efficiently manage both code and automation workflows.

## Coding Conventions

### File Naming
- Use **camelCase** for filenames.
  - Example: `modMenu.ts`, `userSettings.ts`

### Import Style
- Use **relative imports** within the codebase.
  - Example:
    ```typescript
    import { getUserSettings } from './userSettings';
    ```

### Export Style
- Use **named exports** for all modules.
  - Example:
    ```typescript
    // userSettings.ts
    export function getUserSettings() { ... }
    export const DEFAULT_SETTINGS = { ... };
    ```

### Commit Messages
- Follow the **conventional commit** style.
- Use the `fix` prefix for bug fixes.
  - Example: `fix: correct menu rendering on load`
- Keep commit messages concise (average ~62 characters).

## Workflows

### ci-workflow-pipeline-fix
**Trigger:** When CI/CD pipelines fail due to misconfiguration, missing secrets, runner issues, or integration problems.  
**Command:** `/fix-ci-workflow`

1. Identify the failing CI/CD workflow or analysis step (e.g., build, test, coverage).
2. Edit the relevant `.github/workflows/*.yml` file(s) to fix configuration issues.  
   - Files involved:
     - `.github/workflows/sonarcloud.yml`
     - `.github/workflows/quality-zero-platform.yml`
     - `.github/workflows/codecov-analytics.yml`
3. Commit and push your changes.
4. Verify that the pipeline passes with the new configuration.

#### Example: Fixing a Runner Issue
```yaml
# .github/workflows/sonarcloud.yml
jobs:
  build:
    runs-on: ubuntu-latest  # Change runner if needed
    steps:
      - uses: actions/checkout@v3
      # ... other steps
```

## Testing Patterns

- Test files use the `*.test.*` naming pattern (e.g., `modMenu.test.ts`).
- The specific testing framework is **unknown**, but standard TypeScript testing practices apply.
- Example test file structure:
  ```typescript
  // modMenu.test.ts
  import { openMenu } from './modMenu';

  describe('openMenu', () => {
    it('should open the menu', () => {
      expect(openMenu()).toBe(true);
    });
  });
  ```

## Commands

| Command            | Purpose                                                      |
|--------------------|--------------------------------------------------------------|
| /fix-ci-workflow   | Fix or update CI/CD workflow configuration and rerun pipeline|
```
