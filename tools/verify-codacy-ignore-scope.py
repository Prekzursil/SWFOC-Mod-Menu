#!/usr/bin/env python3
"""Verify Codacy exclude_paths stay within minimal allowed scope."""

from __future__ import annotations

import argparse
import fnmatch
import json
import sys
from collections import Counter
from pathlib import Path

DEFAULT_ALLOWED_PATTERNS = [
    "(new)codex(plans)/**",
    "1397421866(original mod)/**",
    "3447786229(submod)/**",
    "3661482670(cheat_mode_example)/**",
    "TestResults/**",
    "artifacts/**",
]

DEFAULT_DISALLOWED_BROAD_PATTERNS = [
    "src/**",
    "tests/**",
    "tools/**",
    ".github/**",
    "docs/**",
    "native/**",
    "**/*.md",
]

PROTECTED_PREFIXES = [
    "src/",
    "tests/",
    "tools/",
    ".github/",
    "docs/",
    "native/",
]

SCANNER_EXCLUDED_ROOT_PREFIXES = {
    ".git",
    "scratch",
}

SCANNER_EXCLUDED_DIR_NAMES = {
    "bin",
    "obj",
    "__pycache__",
    ".pytest_cache",
}


def unquote(value: str) -> str:
    if (value.startswith('"') and value.endswith('"')) or (value.startswith("'") and value.endswith("'")):
        return value[1:-1]
    return value


def update_quote_state(char: str, in_single_quote: bool, in_double_quote: bool) -> tuple[bool, bool]:
    if char == "'" and not in_double_quote:
        return (not in_single_quote, in_double_quote)
    if char == '"' and not in_single_quote:
        return (in_single_quote, not in_double_quote)
    return (in_single_quote, in_double_quote)


def is_comment_delimiter(char: str, in_single_quote: bool, in_double_quote: bool) -> bool:
    return char == "#" and not in_single_quote and not in_double_quote


def strip_inline_comment(value: str) -> str:
    in_single_quote = False
    in_double_quote = False
    for index, char in enumerate(value):
        if is_comment_delimiter(char, in_single_quote, in_double_quote):
            return value[:index].rstrip()
        in_single_quote, in_double_quote = update_quote_state(
            char,
            in_single_quote,
            in_double_quote,
        )
    return value.strip()


def is_top_level_key(line: str, stripped: str) -> bool:
    if not stripped or stripped.startswith("#"):
        return False
    if not line.startswith((" ", "\t")):
        return not stripped.startswith("- ")
    return False


def parse_exclude_item(stripped: str) -> str | None:
    if not stripped or stripped.startswith("#"):
        return None
    if not stripped.startswith("- "):
        return None
    normalized_value = strip_inline_comment(stripped[2:].strip())
    if not normalized_value:
        return None
    return unquote(normalized_value)


def parse_exclude_paths(codacy_path: Path) -> list[str]:
    paths: list[str] = []
    lines = codacy_path.read_text(encoding="utf-8").splitlines()
    in_block = False
    for line in lines:
        stripped = line.strip()
        if not in_block:
            in_block = strip_inline_comment(stripped) == "exclude_paths:"
            continue

        if is_top_level_key(line, stripped):
            break

        parsed_item = parse_exclude_item(stripped)
        if parsed_item is not None:
            paths.append(parsed_item)

    return paths


def should_skip_scanned_path(relative_path: str) -> bool:
    parts = relative_path.split("/")
    if parts[0] in SCANNER_EXCLUDED_ROOT_PREFIXES:
        return True
    return any(part in SCANNER_EXCLUDED_DIR_NAMES for part in parts)


def list_repository_files(repo_root: Path) -> list[str]:
    files: list[str] = []
    for path in repo_root.rglob("*"):
        if not path.is_file():
            continue
        relative = path.relative_to(repo_root).as_posix()
        if should_skip_scanned_path(relative):
            continue
        files.append(relative)
    return sorted(files)


def match_ignored_files(files: list[str], patterns: list[str]) -> tuple[list[str], dict[str, int]]:
    ignored: list[str] = []
    pattern_counts: Counter[str] = Counter()
    for path in files:
        matched = False
        for pattern in patterns:
            normalized_pattern = pattern.replace("\\", "/")
            if fnmatch.fnmatch(path, normalized_pattern) or fnmatch.fnmatch(f"/{path}", f"/{normalized_pattern}"):
                matched = True
                pattern_counts[pattern] += 1
        if matched:
            ignored.append(path)
    return ignored, dict(pattern_counts)


def build_report(repo_root: Path, codacy_file: Path, patterns: list[str]) -> dict[str, object]:
    repository_files = list_repository_files(repo_root)
    ignored_files, pattern_counts = match_ignored_files(repository_files, patterns)
    return {
        "codacyFile": codacy_file.as_posix(),
        "trackedFilesTotal": len(repository_files),
        "excludePatterns": patterns,
        "ignoredTrackedFilesTotal": len(ignored_files),
        "ignoredPatternMatchCounts": pattern_counts,
        "ignoredSample": ignored_files[:100],
    }


def strict_violations(patterns: list[str], ignored_files: list[str]) -> list[dict[str, object]]:
    violations: list[dict[str, object]] = []

    for disallowed in DEFAULT_DISALLOWED_BROAD_PATTERNS:
        if disallowed in patterns:
            violations.append(
                {
                    "code": "CODACY_SCOPE_DISALLOWED_BROAD_PATTERN",
                    "message": f"disallowed broad Codacy exclude pattern present: {disallowed}",
                    "pattern": disallowed,
                }
            )

    unexpected = sorted(set(patterns) - set(DEFAULT_ALLOWED_PATTERNS))
    if unexpected:
        violations.append(
            {
                "code": "CODACY_SCOPE_UNEXPECTED_PATTERN",
                "message": "unexpected Codacy exclude pattern(s): " + ", ".join(unexpected),
                "patterns": unexpected,
            }
        )

    protected_ignored = [f for f in ignored_files if any(f.startswith(prefix) for prefix in PROTECTED_PREFIXES)]
    if protected_ignored:
        sample = protected_ignored[:10]
        violations.append(
            {
                "code": "CODACY_SCOPE_PROTECTED_PATH_IGNORED",
                "message": "protected code/docs paths are ignored by Codacy config",
                "sample": sample,
            }
        )

    return violations


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--repo-root",
        default=".",
        help="Repository root containing .git and .codacy.yml (default: current directory).",
    )
    parser.add_argument(
        "--codacy-file",
        default=".codacy.yml",
        help="Path to Codacy config relative to repo root (default: .codacy.yml).",
    )
    parser.add_argument(
        "--strict",
        action="store_true",
        help="Fail if broad or unexpected Codacy exclusions are present.",
    )
    parser.add_argument(
        "--output",
        default="",
        help="Optional path for JSON report output.",
    )
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    repo_root = Path(args.repo_root).resolve()
    codacy_file = (repo_root / args.codacy_file).resolve()

    if not codacy_file.exists():
        print(
            json.dumps(
                {
                    "error": {
                        "code": "CODACY_SCOPE_CONFIG_NOT_FOUND",
                        "message": f"codacy file not found: {codacy_file.as_posix()}",
                    }
                }
            )
        )
        return 1

    patterns = parse_exclude_paths(codacy_file)
    report = build_report(repo_root, codacy_file.relative_to(repo_root), patterns)

    violations: list[dict[str, object]] = []
    if args.strict:
        # Recompute full ignored list for strict checks to avoid sample-only validation.
        full_ignored, _ = match_ignored_files(list_repository_files(repo_root), patterns)
        violations = strict_violations(patterns, full_ignored)
        report["strictViolations"] = violations

    rendered = json.dumps(report, indent=2)
    if args.output:
        output_path = Path(args.output)
        if not output_path.is_absolute():
            output_path = repo_root / output_path
        output_path.parent.mkdir(parents=True, exist_ok=True)
        output_path.write_text(rendered + "\n", encoding="utf-8")

    print(rendered)

    if args.strict and violations:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
