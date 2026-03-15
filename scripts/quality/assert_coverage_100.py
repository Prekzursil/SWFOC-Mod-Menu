#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import re
import sys
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path


@dataclass
class CoverageStats:
    name: str
    path: str
    line_covered: int
    line_total: int
    branch_covered: int
    branch_total: int

    @property
    def line_percent(self) -> float:
        if self.line_total <= 0:
            return 100.0
        return (self.line_covered / self.line_total) * 100.0

    @property
    def branch_percent(self) -> float:
        if self.branch_total <= 0:
            return 100.0
        return (self.branch_covered / self.branch_total) * 100.0


_PAIR_RE = re.compile(r"^(?P<name>[^=]+)=(?P<path>.+)$")
_XML_LINES_VALID_RE = re.compile(r'lines-valid="([0-9]+(?:\\.[0-9]+)?)"')
_XML_LINES_COVERED_RE = re.compile(r'lines-covered="([0-9]+(?:\\.[0-9]+)?)"')
_XML_LINE_HITS_RE = re.compile(r'<line\b[^>]*\bhits="([0-9]+(?:\.[0-9]+)?)"')
_CONDITION_COVERAGE_RE = re.compile(r"\((?P<covered>\d+)/(?P<total>\d+)\)")
_XML_CLASS_RE = re.compile(r"<class\b(?P<attrs>[^>]*)>(?P<body>.*?)</class>", re.IGNORECASE | re.DOTALL)
_XML_LINE_RE = re.compile(r"<line\b(?P<attrs>[^>]*)/?>", re.IGNORECASE)
_XML_ATTR_RE = re.compile(r"([A-Za-z0-9_-]+)\s*=\s*\"([^\"]*)\"")
_LCOV_BRANCH_FOUND_RE = re.compile(r"^BRF:(\d+)$")
_LCOV_BRANCH_HIT_RE = re.compile(r"^BRH:(\d+)$")
_GENERATED_FILE_PATTERNS = [
    re.compile(r"(^|[\\/])obj([\\/])", re.IGNORECASE),
    re.compile(r"\.g\.cs$", re.IGNORECASE),
    re.compile(r"\.g\.i\.cs$", re.IGNORECASE),
]


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Assert 100% coverage for all declared components.")
    parser.add_argument("--xml", action="append", default=[], help="Coverage XML input: name=path")
    parser.add_argument("--lcov", action="append", default=[], help="LCOV input: name=path")
    parser.add_argument("--out-json", default="coverage-100/coverage.json", help="Output JSON path")
    parser.add_argument("--out-md", default="coverage-100/coverage.md", help="Output markdown path")
    parser.add_argument(
        "--include-generated",
        action="store_true",
        help="Include generated artifacts in coverage denominator (default excludes obj/*.g.cs/*.g.i.cs).",
    )
    return parser.parse_args()


def parse_named_path(value: str) -> tuple[str, Path]:
    match = _PAIR_RE.match(value.strip())
    if not match:
        raise ValueError(f"Invalid input '{value}'. Expected format: name=path")
    return match.group("name").strip(), Path(match.group("path").strip())


def _is_generated(filename: str) -> bool:
    return any(pattern.search(filename) for pattern in _GENERATED_FILE_PATTERNS)


def _count_hit(hits_raw: str) -> int:
    try:
        return 1 if int(float(hits_raw)) > 0 else 0
    except ValueError:
        return 0


def _parse_xml_attrs(raw: str) -> dict[str, str]:
    return {match.group(1): match.group(2) for match in _XML_ATTR_RE.finditer(raw)}


def _parse_class_lines(class_body: str) -> tuple[int, int, int, int]:
    line_total = 0
    line_covered = 0
    branch_total = 0
    branch_covered = 0

    for match in _XML_LINE_RE.finditer(class_body):
        attrs = _parse_xml_attrs(match.group("attrs"))
        line_total += 1
        line_covered += _count_hit(attrs.get("hits", "0"))
        condition_coverage = attrs.get("condition-coverage", "")
        coverage_match = _CONDITION_COVERAGE_RE.search(condition_coverage)
        if coverage_match:
            branch_covered += int(coverage_match.group("covered"))
            branch_total += int(coverage_match.group("total"))

    return line_total, line_covered, branch_total, branch_covered


def _parse_xml_classes(text: str, include_generated: bool) -> tuple[int, int, int, int]:
    line_total = 0
    line_covered = 0
    branch_total = 0
    branch_covered = 0

    for class_match in _XML_CLASS_RE.finditer(text):
        class_attrs = _parse_xml_attrs(class_match.group("attrs"))
        filename = class_attrs.get("filename", "")
        if not include_generated and _is_generated(filename):
            continue

        class_lines = _parse_class_lines(class_match.group("body"))
        line_total += class_lines[0]
        line_covered += class_lines[1]
        branch_total += class_lines[2]
        branch_covered += class_lines[3]

    return line_total, line_covered, branch_total, branch_covered


def _parse_fallback_line_totals(text: str) -> tuple[int, int]:
    lines_valid_match = _XML_LINES_VALID_RE.search(text)
    lines_covered_match = _XML_LINES_COVERED_RE.search(text)
    if lines_valid_match and lines_covered_match:
        return int(float(lines_covered_match.group(1))), int(float(lines_valid_match.group(1)))

    line_total = 0
    line_covered = 0
    for hits_raw in _XML_LINE_HITS_RE.findall(text):
        line_total += 1
        line_covered += _count_hit(hits_raw)
    return line_covered, line_total


def parse_coverage_xml(name: str, path: Path, include_generated: bool) -> CoverageStats:
    text = path.read_text(encoding="utf-8")
    line_total, line_covered, branch_total, branch_covered = _parse_xml_classes(text, include_generated)

    # Fallback for malformed XML without class/line data.
    if line_total == 0:
        line_covered, line_total = _parse_fallback_line_totals(text)

    if line_total == 0:
        raise ValueError(f"{name} coverage XML did not contain any parseable line data: {path}")

    return CoverageStats(
        name=name,
        path=str(path),
        line_covered=line_covered,
        line_total=line_total,
        branch_covered=branch_covered,
        branch_total=branch_total,
    )


def parse_lcov(name: str, path: Path) -> CoverageStats:
    line_total = 0
    line_covered = 0
    branch_total = 0
    branch_covered = 0

    for raw in path.read_text(encoding="utf-8").splitlines():
        line = raw.strip()
        if line.startswith("LF:"):
            line_total += int(line.split(":", 1)[1])
        elif line.startswith("LH:"):
            line_covered += int(line.split(":", 1)[1])
        else:
            branch_found_match = _LCOV_BRANCH_FOUND_RE.match(line)
            if branch_found_match:
                branch_total += int(branch_found_match.group(1))
                continue
            branch_hit_match = _LCOV_BRANCH_HIT_RE.match(line)
            if branch_hit_match:
                branch_covered += int(branch_hit_match.group(1))

    return CoverageStats(
        name=name,
        path=str(path),
        line_covered=line_covered,
        line_total=line_total,
        branch_covered=branch_covered,
        branch_total=branch_total,
    )


def evaluate(stats: list[CoverageStats]) -> tuple[str, list[str]]:
    findings: list[str] = []
    for item in stats:
        if item.line_percent < 100.0:
            findings.append(
                f"{item.name} line coverage below 100%: {item.line_percent:.2f}% ({item.line_covered}/{item.line_total})"
            )
        if item.branch_percent < 100.0:
            findings.append(
                f"{item.name} branch coverage below 100%: {item.branch_percent:.2f}% ({item.branch_covered}/{item.branch_total})"
            )

    combined_line_total = sum(item.line_total for item in stats)
    combined_line_covered = sum(item.line_covered for item in stats)
    combined_line = 100.0 if combined_line_total <= 0 else (combined_line_covered / combined_line_total) * 100.0

    combined_branch_total = sum(item.branch_total for item in stats)
    combined_branch_covered = sum(item.branch_covered for item in stats)
    combined_branch = (
        100.0 if combined_branch_total <= 0 else (combined_branch_covered / combined_branch_total) * 100.0
    )

    if combined_line < 100.0:
        findings.append(
            f"combined line coverage below 100%: {combined_line:.2f}% ({combined_line_covered}/{combined_line_total})"
        )
    if combined_branch < 100.0:
        findings.append(
            f"combined branch coverage below 100%: {combined_branch:.2f}% ({combined_branch_covered}/{combined_branch_total})"
        )

    status = "pass" if not findings else "fail"
    return status, findings


def _render_md(payload: dict) -> str:
    lines = [
        "# Coverage 100 Gate",
        "",
        f"- Status: `{payload['status']}`",
        f"- Timestamp (UTC): `{payload['timestamp_utc']}`",
        "",
        "## Components",
    ]

    for item in payload.get("components", []):
        lines.append(
            f"- `{item['name']}`: line `{item['line_percent']:.2f}%` ({item['line_covered']}/{item['line_total']}), "
            f"branch `{item['branch_percent']:.2f}%` ({item['branch_covered']}/{item['branch_total']}) from `{item['path']}`"
        )

    if not payload.get("components"):
        lines.append("- None")

    lines.extend(["", "## Findings"])
    findings = payload.get("findings") or []
    if findings:
        lines.extend(f"- {finding}" for finding in findings)
    else:
        lines.append("- None")

    return "\n".join(lines) + "\n"


def _safe_output_path(raw: str, fallback: str, base: Path | None = None) -> Path:
    root = (base or Path.cwd()).resolve()
    candidate = Path((raw or "").strip() or fallback).expanduser()
    if not candidate.is_absolute():
        candidate = root / candidate
    resolved = candidate.resolve(strict=False)
    try:
        resolved.relative_to(root)
    except ValueError as exc:
        raise ValueError(f"Output path escapes workspace root: {candidate}") from exc
    return resolved


def main() -> int:
    args = _parse_args()

    stats: list[CoverageStats] = []
    for item in args.xml:
        name, path = parse_named_path(item)
        stats.append(parse_coverage_xml(name, path, include_generated=args.include_generated))
    for item in args.lcov:
        name, path = parse_named_path(item)
        stats.append(parse_lcov(name, path))

    if not stats:
        raise SystemExit("No coverage files were provided; pass --xml and/or --lcov inputs.")

    status, findings = evaluate(stats)
    payload = {
        "status": status,
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "components": [
            {
                "name": item.name,
                "path": item.path,
                "line_covered": item.line_covered,
                "line_total": item.line_total,
                "line_percent": item.line_percent,
                "branch_covered": item.branch_covered,
                "branch_total": item.branch_total,
                "branch_percent": item.branch_percent,
            }
            for item in stats
        ],
        "findings": findings,
    }

    try:
        out_json = _safe_output_path(args.out_json, "coverage-100/coverage.json")
        out_md = _safe_output_path(args.out_md, "coverage-100/coverage.md")
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 1

    out_json.parent.mkdir(parents=True, exist_ok=True)
    out_md.parent.mkdir(parents=True, exist_ok=True)
    out_json.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    out_md.write_text(_render_md(payload), encoding="utf-8")
    print(out_md.read_text(encoding="utf-8"), end="")

    return 0 if status == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
