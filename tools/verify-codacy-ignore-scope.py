#!/usr/bin/env python3
"""Verify Codacy exclude_paths stay within minimal allowed scope."""

from __future__ import annotations

import argparse
import fnmatch
import json
import subprocess
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


def unquote(value: str) -> str:
    if (value.startswith('"') and value.endswith('"')) or (value.startswith("'") and value.endswith("'")):
        return value[1:-1]
    return value


def is_top_level_key(line: str, stripped: str) -> bool:
    if not line.startswith((" ", "\t")) and stripped:
        return not stripped.startswith("- ")
    return False


def parse_exclude_item(stripped: str) -> str | None:
    if not stripped or stripped.startswith("#"):
        return None
    if not stripped.startswith("- "):
        return None
    return unquote(stripped[2:].strip())


def parse_exclude_paths(codacy_path: Path) -> list[str]:
    paths: list[str] = []
    lines = codacy_path.read_text(encoding="utf-8").splitlines()
    in_block = False
    for line in lines:
        stripped = line.strip()
        if not in_block:
            in_block = stripped == "exclude_paths:"
            continue

        if is_top_level_key(line, stripped):
            break

        parsed_item = parse_exclude_item(stripped)
        if parsed_item is not None:
            paths.append(parsed_item)

    return paths


def list_tracked_files(repo_root: Path) -> list[str]:
    result = subprocess.run(
        ["git", "ls-files"],
        cwd=repo_root,
        check=True,
        capture_output=True,
        text=True,
    )
    return [line.strip().replace("\\", "/") for line in result.stdout.splitlines() if line.strip()]


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
    tracked_files = list_tracked_files(repo_root)
    ignored_files, pattern_counts = match_ignored_files(tracked_files, patterns)
    return {
        "codacyFile": codacy_file.as_posix(),
        "trackedFilesTotal": len(tracked_files),
        "excludePatterns": patterns,
        "ignoredTrackedFilesTotal": len(ignored_files),
        "ignoredPatternMatchCounts": pattern_counts,
        "ignoredSample": ignored_files[:100],
    }


def strict_violations(patterns: list[str], ignored_files: list[str]) -> list[str]:
    violations: list[str] = []

    for disallowed in DEFAULT_DISALLOWED_BROAD_PATTERNS:
        if disallowed in patterns:
            violations.append(f"disallowed broad Codacy exclude pattern present: {disallowed}")

    unexpected = sorted(set(patterns) - set(DEFAULT_ALLOWED_PATTERNS))
    if unexpected:
        violations.append(
            "unexpected Codacy exclude pattern(s): " + ", ".join(unexpected)
        )

    protected_ignored = [f for f in ignored_files if any(f.startswith(prefix) for prefix in PROTECTED_PREFIXES)]
    if protected_ignored:
        sample = ", ".join(protected_ignored[:10])
        violations.append(
            "protected code/docs paths are ignored by Codacy config (sample): " + sample
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
        print(json.dumps({"error": f"codacy file not found: {codacy_file.as_posix()}"}))
        return 1

    patterns = parse_exclude_paths(codacy_file)
    report = build_report(repo_root, codacy_file.relative_to(repo_root), patterns)

    violations: list[str] = []
    if args.strict:
        # Recompute full ignored list for strict checks to avoid sample-only validation.
        full_ignored, _ = match_ignored_files(list_tracked_files(repo_root), patterns)
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
