#!/usr/bin/env python3

import argparse
import json
import os
import sys
import time
import urllib.parse
import urllib.request
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

NONE_MARKER = "- None"
PENDING_STATES = {"pending", "queued", "in_progress"}


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Wait for required GitHub check contexts and assert they are successful.")
    parser.add_argument("--repo", required=True, help="owner/repo")
    parser.add_argument("--sha", required=True, help="commit SHA")
    parser.add_argument("--required-context", action="append", default=[], help="Required context name")
    parser.add_argument("--timeout-seconds", type=int, default=900)
    parser.add_argument("--poll-seconds", type=int, default=20)
    parser.add_argument("--out-json", default="quality-zero-gate/required-checks.json")
    parser.add_argument("--out-md", default="quality-zero-gate/required-checks.md")
    return parser.parse_args()


def _api_get(repo: str, path: str, token: str) -> Dict[str, Any]:
    url = f"https://api.github.com/repos/{repo}/{path.lstrip('/')}"
    req = urllib.request.Request(
        url,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "reframe-quality-zero-gate",
        },
        method="GET",
    )
    # nosemgrep: python.lang.security.audit.dynamic-urllib-use-detected.dynamic-urllib-use-detected
    with urllib.request.urlopen(req, timeout=30) as resp:
        return json.loads(resp.read().decode("utf-8"))


def _collect_contexts(check_runs_payload: Dict[str, Any], status_payload: Dict[str, Any]) -> Dict[str, Dict[str, str]]:
    contexts: Dict[str, Dict[str, str]] = {}

    for run in check_runs_payload.get("check_runs", []) or []:
        name = str(run.get("name") or "").strip()
        if not name:
            continue
        contexts[name] = {
            "state": str(run.get("status") or ""),
            "conclusion": str(run.get("conclusion") or ""),
            "source": "check_run",
        }

    for status in status_payload.get("statuses", []) or []:
        name = str(status.get("context") or "").strip()
        if not name:
            continue
        contexts[name] = {
            "state": str(status.get("state") or ""),
            "conclusion": str(status.get("state") or ""),
            "source": "status",
        }

    return contexts


def _evaluate(
    required: List[str],
    contexts: Dict[str, Dict[str, str]],
    *,
    timed_out: bool = False,
) -> Tuple[str, List[str], List[str], List[str]]:
    missing: List[str] = []
    failed: List[str] = []
    pending: List[str] = []

    for context in required:
        observed = contexts.get(context)
        if not observed:
            missing.append(context)
            continue

        failed_entry, pending_entry = _evaluate_observed_context(context, observed)
        if failed_entry:
            failed.append(failed_entry)
        elif pending_entry:
            pending.append(pending_entry)

    status = _resolve_status(missing, failed, pending, timed_out)
    if status == "fail" and pending and timed_out:
        failed.extend(pending)
    return status, missing, failed, pending


def _resolve_status(
    missing: List[str],
    failed: List[str],
    pending: List[str],
    timed_out: bool,
) -> str:
    if missing or failed or (pending and timed_out):
        return "fail"
    if pending:
        return "pending"
    return "pass"


def _evaluate_observed_context(context: str, observed: Dict[str, str]) -> Tuple[Optional[str], Optional[str]]:
    source = observed.get("source")
    if source == "check_run":
        state = observed.get("state")
        conclusion = observed.get("conclusion")
        if state != "completed":
            return None, f"{context}: status={state}"
        if conclusion != "success":
            return f"{context}: conclusion={conclusion}", None
        return None, None

    conclusion = observed.get("conclusion")
    if conclusion in PENDING_STATES:
        return None, f"{context}: state={conclusion}"
    if conclusion != "success":
        return f"{context}: state={conclusion}", None
    return None, None


def _render_md(payload: Dict[str, Any]) -> str:
    lines = [
        "# Quality Zero Gate - Required Contexts",
        "",
        f"- Status: `{payload['status']}`",
        f"- Repo/SHA: `{payload['repo']}@{payload['sha']}`",
        f"- Timestamp (UTC): `{payload['timestamp_utc']}`",
        "",
        "## Missing contexts",
    ]

    missing = payload.get("missing") or []
    if missing:
        lines.extend(f"- `{name}`" for name in missing)
    else:
        lines.append(NONE_MARKER)

    lines.extend(["", "## Failed contexts"])
    failed = payload.get("failed") or []
    if failed:
        lines.extend(f"- {entry}" for entry in failed)
    else:
        lines.append(NONE_MARKER)

    lines.extend(["", "## Pending contexts"])
    pending = payload.get("pending") or []
    if pending:
        lines.extend(f"- {entry}" for entry in pending)
    else:
        lines.append(NONE_MARKER)

    lines.extend(["", f"- Timed out: `{payload.get('timed_out', False)}`"])

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


def _build_payload(
    *,
    status: str,
    args: argparse.Namespace,
    required: List[str],
    missing: List[str],
    failed: List[str],
    pending: List[str],
    contexts: Dict[str, Dict[str, Any]],
    timed_out: bool,
) -> Dict[str, Any]:
    return {
        "status": status,
        "repo": args.repo,
        "sha": args.sha,
        "required": required,
        "missing": missing,
        "failed": failed,
        "pending": pending,
        "contexts": contexts,
        "timestamp_utc": datetime.now(timezone.utc).isoformat(),
        "timed_out": timed_out,
    }


def _fetch_context_snapshot(args: argparse.Namespace, token: str) -> Dict[str, Dict[str, Any]]:
    check_runs = _api_get(args.repo, f"commits/{args.sha}/check-runs?per_page=100", token)
    statuses = _api_get(args.repo, f"commits/{args.sha}/status", token)
    return _collect_contexts(check_runs, statuses)


def _should_keep_waiting(
    *,
    status: str,
    missing: List[str],
    contexts: Dict[str, Dict[str, Any]],
) -> bool:
    if status == "pass":
        return False

    if missing:
        return True

    return any(
        value.get("state") != "completed"
        for value in contexts.values()
        if value.get("source") == "check_run"
    )


def main() -> int:
    args = _parse_args()
    token = (os.environ.get("GITHUB_TOKEN", "") or os.environ.get("GH_TOKEN", "")).strip()
    required = [item.strip() for item in args.required_context if item.strip()]

    if not required:
        raise SystemExit("At least one --required-context is required")
    if not token:
        raise SystemExit("GITHUB_TOKEN or GH_TOKEN is required")

    deadline = time.time() + max(args.timeout_seconds, 1)

    final_payload: Optional[Dict[str, Any]] = None
    timed_out = False
    while time.time() <= deadline:
        contexts = _fetch_context_snapshot(args, token)
        status, missing, failed, pending = _evaluate(required, contexts, timed_out=False)

        final_payload = _build_payload(
            status=status,
            args=args,
            required=required,
            missing=missing,
            failed=failed,
            pending=pending,
            contexts=contexts,
            timed_out=False,
        )

        if not _should_keep_waiting(status=status, missing=missing, contexts=contexts):
            break
        time.sleep(max(args.poll_seconds, 1))
    else:
        timed_out = True

    if final_payload and timed_out and final_payload["status"] != "pass":
        status, missing, failed, pending = _evaluate(required, final_payload["contexts"], timed_out=True)
        final_payload = _build_payload(
            status=status,
            args=args,
            required=required,
            missing=missing,
            failed=failed,
            pending=pending,
            contexts=final_payload["contexts"],
            timed_out=True,
        )

    if final_payload is None:
        raise SystemExit("No payload collected")

    try:
        out_json = _safe_output_path(args.out_json, "quality-zero-gate/required-checks.json")
        out_md = _safe_output_path(args.out_md, "quality-zero-gate/required-checks.md")
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 1

    out_json.parent.mkdir(parents=True, exist_ok=True)
    out_md.parent.mkdir(parents=True, exist_ok=True)
    out_json.write_text(json.dumps(final_payload, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    out_md.write_text(_render_md(final_payload), encoding="utf-8")
    print(out_md.read_text(encoding="utf-8"), end="")

    return 0 if final_payload["status"] == "pass" else 1


if __name__ == "__main__":
    raise SystemExit(main())
