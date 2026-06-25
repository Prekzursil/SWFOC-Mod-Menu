"""Tests for scripts/quality/check_required_checks.py."""

from __future__ import annotations

import json
from pathlib import Path

import pytest
from conftest import load_script_module

mod = load_script_module("scripts/quality/check_required_checks.py", "check_required_checks")


def test_collect_contexts_merges_runs_and_statuses() -> None:
    runs = {
        "check_runs": [
            {"name": "build", "status": "completed", "conclusion": "success"},
            {"name": "", "status": "x"},
        ]
    }
    statuses = {"statuses": [{"context": "lint", "state": "success"}, {"context": ""}]}
    contexts = mod._collect_contexts(runs, statuses)
    assert contexts["build"]["source"] == "check_run"
    assert contexts["lint"]["source"] == "status"
    assert "" not in contexts


def test_collect_contexts_empty() -> None:
    assert mod._collect_contexts({}, {}) == {}


def test_evaluate_all_pass() -> None:
    contexts = {
        "build": {"state": "completed", "conclusion": "success", "source": "check_run"},
        "lint": {"state": "success", "conclusion": "success", "source": "status"},
    }
    status, missing, failed = mod._evaluate(["build", "lint"], contexts)
    assert status == "pass" and missing == [] and failed == []


def test_evaluate_missing() -> None:
    status, missing, failed = mod._evaluate(["gone"], {})
    assert status == "fail" and missing == ["gone"]


def test_evaluate_check_run_incomplete() -> None:
    contexts = {"build": {"state": "in_progress", "conclusion": "", "source": "check_run"}}
    status, missing, failed = mod._evaluate(["build"], contexts)
    assert any("status=in_progress" in f for f in failed)


def test_evaluate_check_run_bad_conclusion() -> None:
    contexts = {"build": {"state": "completed", "conclusion": "failure", "source": "check_run"}}
    status, missing, failed = mod._evaluate(["build"], contexts)
    assert any("conclusion=failure" in f for f in failed)


def test_evaluate_status_bad_state() -> None:
    contexts = {"lint": {"state": "failure", "conclusion": "failure", "source": "status"}}
    status, missing, failed = mod._evaluate(["lint"], contexts)
    assert any("state=failure" in f for f in failed)


def test_render_md_with_and_without() -> None:
    md = mod._render_md(
        {
            "status": "fail",
            "repo": "o/r",
            "sha": "abc",
            "timestamp_utc": "t",
            "missing": ["m"],
            "failed": ["f: bad"],
        }
    )
    assert "`m`" in md and "- f: bad" in md
    md2 = mod._render_md(
        {
            "status": "pass",
            "repo": "o/r",
            "sha": "abc",
            "timestamp_utc": "t",
            "missing": [],
            "failed": [],
        }
    )
    assert md2.count("- None") == 2


def test_safe_output_path_escape(tmp_path: Path) -> None:
    with pytest.raises(ValueError):
        mod._safe_output_path("../x", "fb", base=tmp_path)


def test_safe_output_path_absolute_inside_root(tmp_path: Path) -> None:
    abs_target = tmp_path / "sub" / "a.json"
    assert mod._safe_output_path(str(abs_target), "fb", base=tmp_path) == abs_target.resolve()


def test_main_requires_context(monkeypatch) -> None:
    monkeypatch.setenv("GITHUB_TOKEN", "t")
    monkeypatch.setattr("sys.argv", ["c.py", "--repo", "o/r", "--sha", "abc"])
    with pytest.raises(SystemExit, match="required-context"):
        mod.main()


def test_main_requires_token(monkeypatch) -> None:
    monkeypatch.delenv("GITHUB_TOKEN", raising=False)
    monkeypatch.delenv("GH_TOKEN", raising=False)
    monkeypatch.setattr(
        "sys.argv", ["c.py", "--repo", "o/r", "--sha", "abc", "--required-context", "build"]
    )
    with pytest.raises(SystemExit, match="GITHUB_TOKEN"):
        mod.main()


def test_main_pass_first_poll(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setenv("GITHUB_TOKEN", "t")

    def fake_api(repo, path, token):
        if "check-runs" in path:
            return {
                "check_runs": [{"name": "build", "status": "completed", "conclusion": "success"}]
            }
        return {"statuses": []}

    monkeypatch.setattr(mod, "_api_get", fake_api)
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(
        "sys.argv", ["c.py", "--repo", "o/r", "--sha", "abc", "--required-context", "build"]
    )
    assert mod.main() == 0
    payload = json.loads(
        (tmp_path / "quality-zero-gate" / "required-checks.json").read_text("utf-8")
    )
    assert payload["status"] == "pass"


def test_main_fail_terminal_no_inprogress(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setenv("GITHUB_TOKEN", "t")

    def fake_api(repo, path, token):
        if "check-runs" in path:
            return {
                "check_runs": [{"name": "build", "status": "completed", "conclusion": "failure"}]
            }
        return {"statuses": []}

    monkeypatch.setattr(mod, "_api_get", fake_api)
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(
        "sys.argv", ["c.py", "--repo", "o/r", "--sha", "abc", "--required-context", "build"]
    )
    # status is fail, but not missing and not in-progress -> loop breaks immediately
    assert mod.main() == 1


def test_main_waits_then_passes(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setenv("GITHUB_TOKEN", "t")
    state = {"n": 0}

    def fake_api(repo, path, token):
        if "check-runs" in path:
            if state["n"] == 0:
                return {
                    "check_runs": [{"name": "build", "status": "in_progress", "conclusion": ""}]
                }
            return {
                "check_runs": [{"name": "build", "status": "completed", "conclusion": "success"}]
            }
        return {"statuses": []}

    def fake_sleep(_):
        state["n"] += 1

    monkeypatch.setattr(mod, "_api_get", fake_api)
    monkeypatch.setattr(mod.time, "sleep", fake_sleep)
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(
        "sys.argv",
        [
            "c.py",
            "--repo",
            "o/r",
            "--sha",
            "abc",
            "--required-context",
            "build",
            "--poll-seconds",
            "0",
        ],
    )
    assert mod.main() == 0
    assert state["n"] == 1


def test_main_timeout_no_payload(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setenv("GITHUB_TOKEN", "t")
    # First time() call sets the deadline; the second (loop guard) is past it,
    # so the while body never runs and final_payload stays None.
    ticks = iter([0.0, 1_000_000.0])
    monkeypatch.setattr(mod.time, "time", lambda: next(ticks))
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(
        "sys.argv",
        [
            "c.py",
            "--repo",
            "o/r",
            "--sha",
            "abc",
            "--required-context",
            "build",
            "--timeout-seconds",
            "1",
        ],
    )
    with pytest.raises(SystemExit, match="No payload collected"):
        mod.main()


def test_main_bad_output(tmp_path: Path, monkeypatch, capsys) -> None:
    monkeypatch.setenv("GITHUB_TOKEN", "t")

    def fake_api(repo, path, token):
        if "check-runs" in path:
            return {
                "check_runs": [{"name": "build", "status": "completed", "conclusion": "success"}]
            }
        return {"statuses": []}

    monkeypatch.setattr(mod, "_api_get", fake_api)
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(
        "sys.argv",
        [
            "c.py",
            "--repo",
            "o/r",
            "--sha",
            "abc",
            "--required-context",
            "build",
            "--out-md",
            "../bad.md",
        ],
    )
    assert mod.main() == 1
    assert "escapes workspace root" in capsys.readouterr().err
