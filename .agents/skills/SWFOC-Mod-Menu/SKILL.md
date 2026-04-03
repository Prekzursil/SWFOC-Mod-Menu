```markdown
# SWFOC-Mod-Menu Development Patterns

> Auto-generated skill from repository analysis

## Overview
This skill teaches you the development conventions and workflows used in the SWFOC-Mod-Menu TypeScript codebase. You'll learn how to structure files, write imports/exports, follow commit conventions, and organize tests to match the repository's established patterns.

## Coding Conventions

### File Naming
- Use **snake_case** for all file names.
  - Example: `mod_menu.ts`, `user_settings.test.ts`

### Import Style
- Use **relative imports** for modules within the project.
  - Example:
    ```typescript
    import { getUserSettings } from './user_settings';
    ```

### Export Style
- Use **named exports** for all exported functions, classes, or constants.
  - Example:
    ```typescript
    export function openModMenu() { ... }
    export const MENU_TITLE = "Mod Menu";
    ```

### Commit Messages
- Follow **conventional commit** style.
- Use the `fix` prefix for bug fixes.
- Keep commit messages concise (average 52 characters).
  - Example:
    ```
    fix: correct menu alignment on settings page
    ```

## Workflows

### Code Contribution
**Trigger:** When adding or updating code in the repository  
**Command:** `/contribute`

1. Create or update files using snake_case naming.
2. Use relative imports and named exports.
3. Write clear, conventional commit messages (e.g., `fix: ...`).
4. Add or update relevant test files (`*.test.ts`).
5. Submit your changes via pull request.

### Testing
**Trigger:** When verifying code correctness  
**Command:** `/test`

1. Identify or create test files matching `*.test.*` pattern.
2. Write or update tests for new or changed code.
3. Run tests using the project's test runner (framework unknown; check project docs or scripts).
4. Ensure all tests pass before committing.

## Testing Patterns

- Test files use the `*.test.*` naming convention (e.g., `mod_menu.test.ts`).
- The specific testing framework is not detected; check the repository for more details.
- Place tests alongside or near the code they test, following the naming pattern.

## Commands
| Command      | Purpose                                      |
|--------------|----------------------------------------------|
| /contribute  | Steps for contributing code to the project   |
| /test        | Steps for running and writing tests          |
```
