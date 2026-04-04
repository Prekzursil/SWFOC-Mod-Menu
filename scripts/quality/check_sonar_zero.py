#!/usr/bin/env python3
from __future__ import annotations

import argparse
import base64
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

_SCRIPT_DIR = Path(__file__).resolve().parent
_HELPER_ROOT = _SCRIPT_DIR if (_SCRIPT_DIR / "security_helpers.py").exists() else _SCRIPT_DIR.parent
if str(_HELPER_ROOT) not in sys.path:
    sys.path.insert(0, str(_HELPER_ROOT))

from security_helpers import normalize_https_url

SONAR_API_BASE = "https://sonarcloud.io"
UNRESOLVED_HOTSPOT_STATUS = "TO_REVIEW"


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description=(
            "Assert SonarCloud has zero open issues, zero unresolved security hotspots, "
            "and a passing quality gate."
        )
    )
    parser.add_argument("--project-key", required=True, help="Sonar project key")
    parser.add_argument("--token", default="", help="Sonar token (falls back to SONAR_TOKEN env)")
    parser.add_argument("--branch", default="", help="Optional branch scope")
    parser.add_argument("--pull-request", default="", help="Optional PR scope")
    parser.add_argument("--out-json", default="sonar-zero/sonar.json", help="Output JSON path")
    parser.add_argument("--out-md", default="sonar-zero/sonar.md", help="Output markdown path")
    return parser.parse_args()


def _auth_header(token: str) -> str:
    raw = f"{token}:".encode("utf-8")
    return "Basic " + base64.b64encode(raw).decode("ascii")


def _request_json(url: str, auth_header: str) -> Dict[str, Any]:
    safe_url = normalize_https_url(url, allowed_host_suffixes={"sonarcloud.io"}).rstrip("/")
    req = urllib.request.Request(
        safe_url,
        headers={
            "Accept": "application/json",
            "Authorization": auth_header,
            "User-Agent": "reframe-sonar-zero-gate",
        },
        method="GET",
    )
    # URL validated above via normalize_https_url
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8"))


def _render_md(payload: dict) -> str:
    lines = [
        "# Sonar Zero Gate",
        "",
        f"- Status: `{payload['status']}`",
        f"- Project: `{payload['project_key']}`",
        f"- Open issues: `{payload.get('open_issues')}`",
        f"- Security hotspots total: `{payload.get('security_hotspots_total')}`",
        f"- Security hotspots to review: `{payload.get('security_hotspots_to_review')}`",
        f"- Quality gate: `{payload.get('quality_gate')}`",
        f"- Timestamp (UTC): `{payload['timestamp_utc']}`",
        "",
        "## Findings",
    ]
    findings = payload.get("findings") or []
    if findings:
        lines.extend(f"- {item}" for item in findings)
    else:
        lines.append("- None")
    return "\n".join(lines) + "\n"


def _safe_output_path(raw: str, fallback: str, base: Optional[Path] = None) -> Path:
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


def _scope_query(project_key: str, branch: str, pull_request: str) -> Dict[str, str]:
    query: Dict[str, str] = {"projectKey": project_key}
    if branch:
        query["branch"] = branch
    if pull_request:
        query["pullRequest"] = pull_request
    return query


def _search_total(api_base: str, endpoint: str, query: Dict[str, str], auth_header: str) -> int:
    url = f"{api_base}{endpoint}?{urllib.parse.urlencode(query)}"
    payload = _request_json(url, auth_header)
    paging = payload.get("paging") or {}
    return int(paging.get("total") or 0)


def _fetch_sonar_metrics(
    api_base: str, auth: str, project_key: str, branch: str, pull_request: str
) -> Tuple[int, int, int, str]:
    """Query SonarCloud for open issues, hotspot counts, and quality gate status."""
    issues_query: Dict[str, str] = {
        "componentKeys": project_key,
        "resolved": "false",
        "ps": "1",
    }
    if branch:
        issues_query["branch"] = branch
    if pull_request:
        issues_query["pullRequest"] = pull_request

    open_issues = _search_total(api_base, "/api/issues/search", issues_query, auth)

    hotspots_query = _scope_query(project_key, branch, pull_request)
    hotspots_query["ps"] = "1"
    security_hotspots_total = _search_total(
        api_base, "/api/hotspots/search", dict(hotspots_query), auth
    )
    to_review_query = dict(hotspots_query)
    to_review_query["status"] = UNRESOLVED_HOTSPOT_STATUS
    security_hotspots_to_review = _search_total(
        api_base, "/api/hotspots/search", to_review_query, auth
    )

    gate_query = _scope_query(project_key, branch, pull_request)
    gate_url = f"{api_base}/api/qualitygates/project_status?{urllib.parse.urlencode(gate_query)}"
    gate_payload = _request_json(gate_url, auth)
    project_status = gate_payload.get("projectStatus") or {}
    quality_gate = str(project_status.get("status") or "UNKNOWN")

    return open_issues, security_hotspots_total, security_hotspots_to_review, quality_gate


def _evaluate_metrics(
    open_issues: int, security_hotspots_to_review: int, quality_gate: str
) -> List[str]:
    """Compare fetched metrics against the zero-issue thresholds and return findings."""
    findings: List[str] = []
    if open_issues != 0:
        findings.append(f"Sonar reports {open_issues} open issues (expected 0).")
    if security_hotspots_to_review != 0:
        findings.append(
            f"Sonar reports {security_hotspots_to_review} unresolved security hotspots (expected 0)."
        )
    if quality_gate != "OK":
        findings.append(f"Sonar quality gate status is {quality_gate} (expected OK).")
    return findings


def _write_reports(payload: dict, out_json: Path, out_md: Path) -> None:
    """Write the JSON and markdown report files and print the markdown to stdout."""
    out_json.parent.mkdir(parents=True, exist_ok=True)
    out_md.parent.mkdir(parents=True, exist_ok=True)
    out_json.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    out_md.write_text(_render_md(payload), encoding="utf-8")
    print(out_md.read_text(encoding="utf-8"), end="")


def main() -> int:
    args = _parse_args()
    token = (args.token or os.environ.get("SONAR_TOKEN", "")).strip()
    api_base = normalize_https_url(SONAR_API_BASE, allowed_hosts={"sonarcloud.io"}).rstrip("/")

    findings: List[str] = []
    open_issues: Optional[int] = None
    quality_gate: Optional[str] = None
    security_hotspots_total: Optional[int] = None
    security_hotspots_to_review: Optional[int] = None

    if not token:
        findings.append("SONAR_TOKEN is missing.")
        status = "fail"
    else:
        auth = _auth_header(token)
        try:
            open_issues, security_hotspots_total, security_hotspots_to_review, quality_gate = (
                _fetch_sonar_metrics(api_base, auth, args.project_key, args.branch, args.pull_request)
            )
            findings.extend(_evaluate_metrics(open_issues, security_hotspots_to_review, quality_gate))
            status = "pass" if not findings else "fail"
        except (urllib.error.URLError, OSError, ValueError) as exc:  # pragma: no cover - network/runtime surface
            status = "fail"
            findings.append(f"Sonar API request failed: {exc}")

    payload = {
        "status": status,
        "project_key": args.project_key,
        "open_issues": open_issues,
        "security_hotspots_total": security_hotspots_total,
        "security_hotspots_to_review": security_hotspots_to_review,
        "quality_gate": quality_gate,
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "findings": findings,
    }

    try:
        out_json = _safe_output_path(args.out_json, "sonar-zero/sonar.json")
        out_md = _safe_output_path(args.out_md, "sonar-zero/sonar.md")
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 1

    _write_reports(payload, out_json, out_md)
    return 0 if status == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
