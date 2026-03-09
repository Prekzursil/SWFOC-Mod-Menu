#!/usr/bin/env python3
from __future__ import annotations

import argparse
import http.client
import json
import os
import sys
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


def build_api_path(repo: str, path: str) -> str:
    repo_clean = repo.strip().strip("/")
    path_clean = path.strip().lstrip("/")
    if not repo_clean or not path_clean:
        raise ValueError("Repository and API path must be non-empty.")
    return f"/repos/{repo_clean}/{path_clean}"


def api_get(repo: str, path: str, token: str) -> dict[str, Any]:
    api_path = build_api_path(repo, path)
    headers = {
        "Accept": "application/vnd.github+json",
        "Authorization": f"Bearer {token}",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "swfoc-legacy-snyk-policy",
    }

    # nosemgrep: python.lang.security.audit.httpsconnection-detected.httpsconnection-detected
    connection = http.client.HTTPSConnection(ALLOWED_GITHUB_HOST, timeout=30)
    try:
        connection.request("GET", api_path, headers=headers)
        response = connection.getresponse()
        body = response.read().decode("utf-8")
    finally:
        connection.close()

    if response.status >= 400:
        raise RuntimeError(f"GitHub API request failed: status={response.status}, path={api_path}")

    return json.loads(body)


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


def normalize_context_prefix(context_prefix: str) -> str:
    return (context_prefix or DEFAULT_CONTEXT_PREFIX).strip().lower()


def build_missing_context_result(context_prefix: str) -> dict[str, Any]:
    return {
        "status": "fail",
        "policy_outcome": "missing_context",
        "selected_context": "",
        "selected_state": "",
        "selected_description": "",
        "selected_target_url": "",
        "findings": [f"No status contexts matched prefix '{context_prefix}'."],
    }


def evaluate_selected_context(selected: dict[str, Any]) -> dict[str, Any]:
    selected_context = str(selected.get("context") or "")
    selected_state = str(selected.get("state") or "")
    selected_description = str(selected.get("description") or "")
    selected_target_url = str(selected.get("target_url") or "")

    state_normalized = selected_state.strip().lower()
    description_normalized = selected_description.strip().lower()

    if state_normalized == "success":
        return {
            "status": "pass",
            "policy_outcome": "validated",
            "selected_context": selected_context,
            "selected_state": selected_state,
            "selected_description": selected_description,
            "selected_target_url": selected_target_url,
            "findings": [],
        }

    if "code test limit reached" in description_normalized:
        return {
            "status": "pass",
            "policy_outcome": "skipped_quota",
            "selected_context": selected_context,
            "selected_state": selected_state,
            "selected_description": selected_description,
            "selected_target_url": selected_target_url,
            "findings": [
                "Legacy code/snyk returned quota limit message; treated as skipped_quota."
            ],
        }

    return {
        "status": "fail",
        "policy_outcome": "invalid",
        "selected_context": selected_context,
        "selected_state": selected_state,
        "selected_description": selected_description,
        "selected_target_url": selected_target_url,
        "findings": [
            f"Legacy code/snyk context was non-success (state={selected_state}, description={selected_description})."
        ],
    }


def evaluate_legacy_snyk_context(statuses: list[dict[str, Any]], context_prefix: str) -> dict[str, Any]:
    prefix = normalize_context_prefix(context_prefix)
    selected = next(
        (
            status
            for status in statuses
            if str(status.get("context", "")).strip().lower().startswith(prefix)
        ),
        None,
    )

    if selected is None:
        return build_missing_context_result(context_prefix)

    return evaluate_selected_context(selected)


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
