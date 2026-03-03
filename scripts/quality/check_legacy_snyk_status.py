#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


DEFAULT_CONTEXT_PREFIX = "code/snyk"
DEFAULT_JSON_PATH = "quality-zero-gate/legacy-snyk.json"
DEFAULT_MD_PATH = "quality-zero-gate/legacy-snyk.md"
ALLOWED_GITHUB_HOST = "api.github.com"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Evaluate legacy code/snyk status context with quota-aware policy."
    )
    parser.add_argument("--repo", required=True, help="owner/repo")
    parser.add_argument("--sha", required=True, help="commit SHA")
    parser.add_argument(
        "--context-prefix",
        default=DEFAULT_CONTEXT_PREFIX,
        help="status context prefix to evaluate (default: code/snyk)",
    )
    parser.add_argument("--out-json", default=DEFAULT_JSON_PATH)
    parser.add_argument("--out-md", default=DEFAULT_MD_PATH)
    return parser.parse_args()


def build_api_url(repo: str, path: str) -> str:
    repo_clean = repo.strip().strip("/")
    path_clean = path.strip().lstrip("/")
    url = f"https://{ALLOWED_GITHUB_HOST}/repos/{repo_clean}/{path_clean}"
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme != "https" or parsed.netloc != ALLOWED_GITHUB_HOST:
        raise ValueError(f"Refusing to call unexpected API host/scheme: {url}")
    return url


def api_get(repo: str, path: str, token: str) -> dict[str, Any]:
    url = build_api_url(repo, path)
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


def resolve_token() -> str:
    token = (os.environ.get("GITHUB_TOKEN", "") or os.environ.get("GH_TOKEN", "")).strip()
    if not token:
        raise RuntimeError("GITHUB_TOKEN or GH_TOKEN is required")
    return token


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


def evaluate_legacy_snyk_context(statuses: list[dict[str, Any]], context_prefix: str) -> dict[str, Any]:
    findings: list[str] = []
    prefix = (context_prefix or DEFAULT_CONTEXT_PREFIX).strip().lower()
    matching = [
        status
        for status in statuses
        if str(status.get("context", "")).strip().lower().startswith(prefix)
    ]

    if not matching:
        findings.append(f"No status contexts matched prefix '{context_prefix}'.")
        return {
            "status": "fail",
            "policy_outcome": "missing_context",
            "selected_context": "",
            "selected_state": "",
            "selected_description": "",
            "selected_target_url": "",
            "findings": findings,
        }

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
        findings.append(
            "Legacy code/snyk returned quota limit message; treated as skipped_quota."
        )
    else:
        policy_outcome = "invalid"
        status = "fail"
        findings.append(
            f"Legacy code/snyk context was non-success (state={selected_state}, description={selected_description})."
        )

    return {
        "status": status,
        "policy_outcome": policy_outcome,
        "selected_context": selected_context,
        "selected_state": selected_state,
        "selected_description": selected_description,
        "selected_target_url": selected_target_url,
        "findings": findings,
    }


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


def write_outputs(payload: dict[str, Any], out_json_raw: str, out_md_raw: str) -> None:
    out_json = safe_output_path(out_json_raw, DEFAULT_JSON_PATH)
    out_md = safe_output_path(out_md_raw, DEFAULT_MD_PATH)
    out_json.parent.mkdir(parents=True, exist_ok=True)
    out_md.parent.mkdir(parents=True, exist_ok=True)
    out_json.write_text(json.dumps(payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    out_md.write_text(render_md(payload), encoding="utf-8")
    print(out_md.read_text(encoding="utf-8"), end="")


def main() -> int:
    args = parse_args()

    try:
        token = resolve_token()
    except RuntimeError as exc:
        print(str(exc), file=sys.stderr)
        return 1

    statuses_payload = api_get(args.repo, f"commits/{args.sha}/status", token)
    statuses = statuses_payload.get("statuses", []) or []
    evaluation = evaluate_legacy_snyk_context(statuses, args.context_prefix)

    payload = {
        "status": evaluation["status"],
        "policy_outcome": evaluation["policy_outcome"],
        "repo": args.repo,
        "sha": args.sha,
        "context_prefix": args.context_prefix,
        "selected_context": evaluation["selected_context"],
        "selected_state": evaluation["selected_state"],
        "selected_description": evaluation["selected_description"],
        "selected_target_url": evaluation["selected_target_url"],
        "findings": evaluation["findings"],
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
    }

    try:
        write_outputs(payload, args.out_json, args.out_md)
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 1

    return 0 if payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
