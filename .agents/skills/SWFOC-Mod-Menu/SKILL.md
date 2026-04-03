```markdown
# SWFOC-Mod-Menu Development Patterns

> Auto-generated skill from repository analysis

## Overview
This skill teaches you the core development patterns and conventions used in the SWFOC-Mod-Menu TypeScript codebase. You'll learn how to structure files, write imports and exports, follow commit message conventions, and write tests in alignment with the project's established practices. This guide is ideal for contributors looking to maintain consistency and quality in the codebase.

## Coding Conventions

### File Naming
- Use **snake_case** for all file names.
  - Example: `mod_menu.ts`, `user_settings.test.ts`

### Import Style
- Use **relative imports** for referencing modules within the project.
  - Example:
    ```typescript
    import { getUserSettings } from './user_settings';
    ```

### Export Style
- Use **named exports** for all exported functions, types, or constants.
  - Example:
    ```typescript
    export function openModMenu() { ... }
    export const MOD_MENU_VERSION = '1.0.0';
    ```

### Commit Messages
- Use **conventional commits** with a `fix` prefix for bug fixes.
- Keep commit messages concise (average ~66 characters).
  - Example:
    ```
    fix: resolve crash when opening mod menu with no settings
    ```

## Workflows

### Making a Bug Fix
**Trigger:** When you need to fix a bug in the codebase  
**Command:** `/fix-bug`

1. Create a new branch for your fix.
2. Make your code changes, following the coding conventions above.
3. Write or update relevant tests (see Testing Patterns).
4. Commit your changes using the `fix:` prefix and a concise description.
5. Push your branch and open a pull request.

## Testing Patterns

- Test files use the `*.test.*` naming pattern (e.g., `mod_menu.test.ts`).
- The specific testing framework is not detected; check existing test files for structure.
- Place test files alongside the modules they test or in a dedicated test directory.

  Example test file:
  ```typescript
  // mod_menu.test.ts
  import { openModMenu } from './mod_menu';

  describe('openModMenu', () => {
    it('should open the menu without errors', () => {
      expect(() => openModMenu()).not.toThrow();
    });
  });
  ```

## Commands
| Command     | Purpose                                      |
|-------------|----------------------------------------------|
| /fix-bug    | Start the workflow for fixing a bug          |
```
