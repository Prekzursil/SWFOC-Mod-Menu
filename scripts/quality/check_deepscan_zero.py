#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

_SCRIPT_DIR = Path(__file__).resolve().parent
_HELPER_ROOT = _SCRIPT_DIR if (_SCRIPT_DIR / "security_helpers.py").exists() else _SCRIPT_DIR.parent
if str(_HELPER_ROOT) not in sys.path:
    sys.path.insert(0, str(_HELPER_ROOT))

from security_helpers import normalize_https_url

TOTAL_KEYS = {"total", "totalItems", "total_items", "count", "hits", "open_issues"}


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Assert DeepScan has zero total open issues.")
    parser.add_argument("--token", default="", help="DeepScan API token (falls back to DEEPSCAN_API_TOKEN env)")
    parser.add_argument("--out-json", default="deepscan-zero/deepscan.json", help="Output JSON path")
    parser.add_argument("--out-md", default="deepscan-zero/deepscan.md", help="Output markdown path")
    return parser.parse_args()


def _find_total_in_dict(payload: dict) -> Optional[int]:
    """Search a dict's own keys for a recognized total key with a numeric value."""
    for key, value in payload.items():
        if key in TOTAL_KEYS and isinstance(value, (int, float)):
            return int(value)
    return None


def _find_total_in_children(children: Any) -> Optional[int]:
    """Recursively search an iterable of child values for a total count."""
    for child in children:
        total = extract_total_open(child)
        if total is not None:
            return total
    return None


def extract_total_open(payload: Any) -> Optional[int]:
    """Walk a JSON payload tree to find the first recognized total-count field."""
    if isinstance(payload, dict):
        direct = _find_total_in_dict(payload)
        if direct is not None:
            return direct
        return _find_total_in_children(payload.values())
    if isinstance(payload, list):
        return _find_total_in_children(payload)
    return None


def _request_json(url: str, token: str) -> Dict[str, Any]:
    safe_url = normalize_https_url(url, allowed_host_suffixes={"deepscan.io"})
    req = urllib.request.Request(
        safe_url,
        headers={
            "Accept": "application/json",
            "Authorization": f"Bearer {token}",
            "User-Agent": "reframe-deepscan-zero-gate",
        },
        method="GET",
    )
    # URL validated above via normalize_https_url
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8"))


def _render_md(payload: dict) -> str:
    lines = [
        "# DeepScan Zero Gate",
        "",
        f"- Status: `{payload['status']}`",
        f"- Open issues: `{payload.get('open_issues')}`",
        f"- Source URL: `{payload.get('open_issues_url') or 'n/a'}`",
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


def _validate_config(token: str, open_issues_url: str) -> Tuple[str, List[str]]:
    """Validate token and URL configuration, returning the normalized URL and any findings."""
    findings: List[str] = []
    if not token:
        findings.append("DEEPSCAN_API_TOKEN is missing.")
    if not open_issues_url:
        findings.append("DEEPSCAN_OPEN_ISSUES_URL is missing.")
    else:
        try:
            open_issues_url = normalize_https_url(
                open_issues_url,
                allowed_host_suffixes={"deepscan.io"},
            )
        except ValueError as exc:
            findings.append(str(exc))
    return open_issues_url, findings


def _query_and_evaluate(open_issues_url: str, token: str) -> Tuple[str, Optional[int], List[str]]:
    """Query DeepScan API and evaluate the result against the zero-issue threshold."""
    findings: List[str] = []
    open_issues: Optional[int] = None
    try:
        payload = _request_json(open_issues_url, token)
        open_issues = extract_total_open(payload)
        if open_issues is None:
            findings.append("DeepScan response did not include a parseable total issue count.")
        elif open_issues != 0:
            findings.append(f"DeepScan reports {open_issues} open issues (expected 0).")
        status = "pass" if not findings else "fail"
    except (urllib.error.URLError, OSError, ValueError) as exc:  # pragma: no cover - network/runtime surface
        findings.append(f"DeepScan API request failed: {exc}")
        status = "fail"
    return status, open_issues, findings


def _write_reports(payload: dict, out_json: Path, out_md: Path) -> None:
    """Write the JSON and markdown report files and print the markdown to stdout."""
    out_json.parent.mkdir(parents=True, exist_ok=True)
    out_md.parent.mkdir(parents=True, exist_ok=True)
    out_json.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    out_md.write_text(_render_md(payload), encoding="utf-8")
    print(out_md.read_text(encoding="utf-8"), end="")


def main() -> int:
    args = _parse_args()
    token = (args.token or os.environ.get("DEEPSCAN_API_TOKEN", "")).strip()
    raw_url = os.environ.get("DEEPSCAN_OPEN_ISSUES_URL", "").strip()

    open_issues_url, findings = _validate_config(token, raw_url)
    open_issues: Optional[int] = None

    status = "fail"
    if not findings:
        status, open_issues, api_findings = _query_and_evaluate(open_issues_url, token)
        findings.extend(api_findings)

    payload = {
        "status": status,
        "open_issues": open_issues,
        "open_issues_url": open_issues_url,
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "findings": findings,
    }

    try:
        out_json = _safe_output_path(args.out_json, "deepscan-zero/deepscan.json")
        out_md = _safe_output_path(args.out_md, "deepscan-zero/deepscan.md")
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 1

    _write_reports(payload, out_json, out_md)
    return 0 if status == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
