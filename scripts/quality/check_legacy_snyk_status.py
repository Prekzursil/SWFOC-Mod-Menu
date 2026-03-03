#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Evaluate legacy code/snyk status context with quota-aware policy."
    )
    parser.add_argument("--repo", required=True, help="owner/repo")
    parser.add_argument("--sha", required=True, help="commit SHA")
    parser.add_argument(
        "--context-prefix",
        default="code/snyk",
        help="status context prefix to evaluate (default: code/snyk)",
    )
    parser.add_argument("--out-json", default="quality-zero-gate/legacy-snyk.json")
    parser.add_argument("--out-md", default="quality-zero-gate/legacy-snyk.md")
    return parser.parse_args()


def api_get(repo: str, path: str, token: str) -> dict[str, Any]:
    url = f"https://api.github.com/repos/{repo}/{path.lstrip('/')}"
    request = urllib.request.Request(
        url,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "swfoc-legacy-snyk-policy",
        },
        method="GET",
    )
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.loads(response.read().decode("utf-8"))


def safe_output_path(raw: str, fallback: str, base: Path | None = None) -> Path:
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


def render_md(payload: dict[str, Any]) -> str:
    lines = [
        "# Legacy Snyk Status Policy",
        "",
        f"- Status: `{payload['status']}`",
        f"- Policy Outcome: `{payload['policy_outcome']}`",
        f"- Repo/SHA: `{payload['repo']}@{payload['sha']}`",
        f"- Context Prefix: `{payload['context_prefix']}`",
        f"- Timestamp (UTC): `{payload['timestamp_utc']}`",
        "",
        "## Selected context",
        f"- Name: `{payload.get('selected_context') or ''}`",
        f"- State: `{payload.get('selected_state') or ''}`",
        f"- Description: `{payload.get('selected_description') or ''}`",
        f"- Target URL: `{payload.get('selected_target_url') or ''}`",
    ]

    findings = payload.get("findings") or []
    lines.append("")
    lines.append("## Findings")
    if findings:
        lines.extend(f"- {entry}" for entry in findings)
    else:
        lines.append("- None")

    return "\n".join(lines) + "\n"


def main() -> int:
    args = parse_args()
    token = (os.environ.get("GITHUB_TOKEN", "") or os.environ.get("GH_TOKEN", "")).strip()
    if not token:
        print("GITHUB_TOKEN or GH_TOKEN is required", file=sys.stderr)
        return 1

    findings: list[str] = []
    selected_context = ""
    selected_state = ""
    selected_description = ""
    selected_target_url = ""
    policy_outcome = "missing_context"
    status = "fail"

    statuses_payload = api_get(args.repo, f"commits/{args.sha}/status", token)
    statuses = statuses_payload.get("statuses", []) or []
    prefix = (args.context_prefix or "code/snyk").strip().lower()
    matching = [s for s in statuses if str(s.get("context", "")).strip().lower().startswith(prefix)]

    if not matching:
        findings.append(f"No status contexts matched prefix '{args.context_prefix}'.")
    else:
        selected = matching[0]
        selected_context = str(selected.get("context") or "")
        selected_state = str(selected.get("state") or "")
        selected_description = str(selected.get("description") or "")
        selected_target_url = str(selected.get("target_url") or "")

        state_normalized = selected_state.strip().lower()
        description_normalized = selected_description.strip().lower()

        if state_normalized == "success":
            policy_outcome = "validated"
            status = "pass"
        elif "code test limit reached" in description_normalized:
            policy_outcome = "skipped_quota"
            status = "pass"
            findings.append("Legacy code/snyk returned quota limit message; treated as skipped_quota.")
        else:
            policy_outcome = "invalid"
            status = "fail"
            findings.append(
                f"Legacy code/snyk context was non-success (state={selected_state}, description={selected_description})."
            )

    payload = {
        "status": status,
        "policy_outcome": policy_outcome,
        "repo": args.repo,
        "sha": args.sha,
        "context_prefix": args.context_prefix,
        "selected_context": selected_context,
        "selected_state": selected_state,
        "selected_description": selected_description,
        "selected_target_url": selected_target_url,
        "findings": findings,
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
    }

    try:
        out_json = safe_output_path(args.out_json, "quality-zero-gate/legacy-snyk.json")
        out_md = safe_output_path(args.out_md, "quality-zero-gate/legacy-snyk.md")
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 1

    out_json.parent.mkdir(parents=True, exist_ok=True)
    out_md.parent.mkdir(parents=True, exist_ok=True)
    out_json.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    out_md.write_text(render_md(payload), encoding="utf-8")
    print(out_md.read_text(encoding="utf-8"), end="")

    return 0 if status == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
