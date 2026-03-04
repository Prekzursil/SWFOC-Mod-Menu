#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Assert coverage thresholds for all language components in manifest.")
    parser.add_argument("--manifest", required=True, help="Path to coverage-manifest.json")
    parser.add_argument("--min-line", type=float, default=100.0, help="Minimum line coverage percent")
    parser.add_argument("--min-branch", type=float, default=100.0, help="Minimum branch coverage percent")
    parser.add_argument(
        "--required-languages",
        default="csharp,cpp,lua,powershell,python",
        help="Comma-separated required language list",
    )
    parser.add_argument("--out-json", default="coverage-100/coverage-all.json", help="Output JSON summary path")
    parser.add_argument("--out-md", default="coverage-100/coverage-all.md", help="Output markdown summary path")
    return parser.parse_args()


def safe_percent(covered: int, total: int) -> float:
    if total <= 0:
        return 100.0
    return (covered / total) * 100.0


def load_manifest(path: Path) -> dict[str, Any]:
    with path.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)
    if not isinstance(payload, dict):
        raise ValueError("Manifest root must be a JSON object")
    return payload


def evaluate_components(
    components: list[dict[str, Any]],
    min_line: float,
    min_branch: float,
    required_languages: set[str],
) -> tuple[str, list[str], list[dict[str, Any]]]:
    findings: list[str] = []
    normalized: list[dict[str, Any]] = []

    seen_languages = {str(component.get("language", "")).strip().lower() for component in components}
    missing = sorted(language for language in required_languages if language not in seen_languages)
    if missing:
        findings.append(f"missing required language components: {', '.join(missing)}")

    for component in components:
        name = str(component.get("name", "unknown"))
        language = str(component.get("language", "unknown")).strip().lower()
        source_type = str(component.get("sourceType", "unknown"))
        line_covered = int(component.get("lineCovered", 0))
        line_total = int(component.get("lineTotal", 0))
        branch_covered = int(component.get("branchCovered", 0))
        branch_total = int(component.get("branchTotal", 0))
        artifact_path = str(component.get("artifactPath", ""))

        line_percent = safe_percent(line_covered, line_total)
        branch_percent = safe_percent(branch_covered, branch_total)

        if line_percent < min_line:
            findings.append(
                f"{name} ({language}) line coverage below {min_line:.2f}%: {line_percent:.2f}% ({line_covered}/{line_total})"
            )
        if branch_percent < min_branch:
            findings.append(
                f"{name} ({language}) branch coverage below {min_branch:.2f}%: {branch_percent:.2f}% ({branch_covered}/{branch_total})"
            )

        normalized.append(
            {
                "name": name,
                "language": language,
                "sourceType": source_type,
                "lineCovered": line_covered,
                "lineTotal": line_total,
                "linePercent": line_percent,
                "branchCovered": branch_covered,
                "branchTotal": branch_total,
                "branchPercent": branch_percent,
                "artifactPath": artifact_path,
            }
        )

    status = "pass" if not findings else "fail"
    return status, findings, normalized


def render_markdown(payload: dict[str, Any]) -> str:
    lines = [
        "# Coverage All Gate",
        "",
        f"- Status: `{payload['status']}`",
        f"- Timestamp (UTC): `{payload['timestampUtc']}`",
        f"- Min line threshold: `{payload['minLine']}`",
        f"- Min branch threshold: `{payload['minBranch']}`",
        "",
        "## Components",
    ]

    components = payload.get("components", [])
    if not components:
        lines.append("- None")
    else:
        for component in components:
            lines.append(
                "- `{name}` ({language}, {sourceType}): line `{linePercent:.2f}%` ({lineCovered}/{lineTotal}), "
                "branch `{branchPercent:.2f}%` ({branchCovered}/{branchTotal}) artifact `{artifactPath}`".format(
                    **component
                )
            )

    lines.append("")
    lines.append("## Findings")
    findings = payload.get("findings", [])
    if findings:
        lines.extend(f"- {finding}" for finding in findings)
    else:
        lines.append("- None")

    return "\n".join(lines) + "\n"


def ensure_output(path: Path) -> Path:
    resolved = path.resolve(strict=False)
    resolved.parent.mkdir(parents=True, exist_ok=True)
    return resolved


def main() -> int:
    args = parse_args()
    manifest_path = Path(args.manifest)
    manifest = load_manifest(manifest_path)
    components = manifest.get("components", [])
    if not isinstance(components, list):
        raise ValueError("Manifest components must be an array")

    required_languages = {
        language.strip().lower()
        for language in str(args.required_languages).split(",")
        if language.strip()
    }

    status, findings, normalized = evaluate_components(
        [component for component in components if isinstance(component, dict)],
        min_line=args.min_line,
        min_branch=args.min_branch,
        required_languages=required_languages,
    )

    payload = {
        "status": status,
        "timestampUtc": datetime.now(timezone.utc).isoformat(),
        "manifest": str(manifest_path),
        "minLine": args.min_line,
        "minBranch": args.min_branch,
        "requiredLanguages": sorted(required_languages),
        "components": normalized,
        "findings": findings,
    }

    out_json = ensure_output(Path(args.out_json))
    out_md = ensure_output(Path(args.out_md))
    out_json.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    out_md.write_text(render_markdown(payload), encoding="utf-8")
    print(out_md.read_text(encoding="utf-8"), end="")

    return 0 if status == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
