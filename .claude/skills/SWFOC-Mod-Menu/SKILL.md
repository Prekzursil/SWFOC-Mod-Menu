```markdown
# SWFOC-Mod-Menu Development Patterns

> Auto-generated skill from repository analysis

## Overview
This skill provides guidance on contributing to the SWFOC-Mod-Menu project, a TypeScript codebase with a focus on modularity and maintainability. It outlines the project's coding conventions, commit patterns, testing strategies, and CI maintenance workflows. By following these patterns, contributors can ensure consistency and reliability across the codebase.

## Coding Conventions

### File Naming
- Use **snake_case** for all file names.
  - Example: `mod_menu.ts`, `user_settings.test.ts`

### Import Style
- Use **relative imports** for modules within the project.
  ```typescript
  import { getUserSettings } from './user_settings';
  ```

### Export Style
- Use **named exports** for functions, constants, and types.
  ```typescript
  // user_settings.ts
  export function getUserSettings() { ... }
  export const DEFAULT_SETTINGS = { ... };
  ```

### Commit Patterns
- Follow **conventional commits**.
- Use the `fix` prefix for bug fixes.
  - Example: `fix: correct menu rendering on settings page`

## Workflows

### CI Pipeline Fix and Maintenance
**Trigger:** When CI workflows fail or need updates due to coverage issues, secrets misconfiguration, or integration problems with tools like SonarCloud.  
**Command:** `/fix-ci`

1. **Identify** the failing or misconfigured CI pipeline (e.g., coverage, secrets, external tools).
2. **Update** relevant `.github/workflows/*.yml` files to fix parameters, secrets, or steps.
   - Example: Adjust a secret name or update a step to use a new action version.
3. **Upgrade or patch** dependencies in test or tool subprojects if required.
   - Example: Update `tools/package.json` and `package-lock.json` to resolve a vulnerability.
4. **Commit** changes with a descriptive message referencing the specific CI issue.
   - Example: `fix: update SonarCloud integration in CI workflow`

**Files Involved:**
- `.github/workflows/*.yml`
- `tests/**/Tests.csproj`
- `tools/**/package.json`
- `tools/**/package-lock.json`

## Testing Patterns

- Test files follow the `*.test.*` naming convention.
  - Example: `mod_menu.test.ts`
- The specific testing framework is not detected, but tests are colocated with source files or in dedicated test directories.
- Typical test structure:
  ```typescript
  // mod_menu.test.ts
  import { getUserSettings } from './user_settings';

  describe('getUserSettings', () => {
    it('returns default settings if none are set', () => {
      expect(getUserSettings()).toEqual(DEFAULT_SETTINGS);
    });
  });
  ```

## Commands

| Command  | Purpose                                                        |
|----------|----------------------------------------------------------------|
| /fix-ci  | Run the CI pipeline fix and maintenance workflow as described. |
```
