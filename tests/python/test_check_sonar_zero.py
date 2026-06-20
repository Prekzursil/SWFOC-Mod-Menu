"""Tests for scripts/quality/check_sonar_zero.py."""

from __future__ import annotations

import base64
from pathlib import Path

import pytest
from conftest import load_script_module

mod = load_script_module("scripts/quality/check_sonar_zero.py", "check_sonar_zero")


def test_auth_header() -> None:
    header = mod._auth_header("tok")
    assert header.startswith("Basic ")
    assert base64.b64decode(header[len("Basic ") :]) == b"tok:"


def test_scope_query() -> None:
    assert mod._scope_query("k", "", "") == {"projectKey": "k"}
    full = mod._scope_query("k", "main", "7")
    assert full == {"projectKey": "k", "branch": "main", "pullRequest": "7"}


def test_search_total(monkeypatch) -> None:
    monkeypatch.setattr(mod, "_request_json", lambda *a, **k: {"paging": {"total": 12}})
    assert mod._search_total("https://sonarcloud.io", "/api/x", {"a": "b"}, "auth") == 12


def test_search_total_missing_paging(monkeypatch) -> None:
    monkeypatch.setattr(mod, "_request_json", lambda *a, **k: {})
    assert mod._search_total("https://sonarcloud.io", "/api/x", {}, "auth") == 0


def test_evaluate_metrics_clean() -> None:
    assert mod._evaluate_metrics(0, 0, "OK") == []


def test_evaluate_metrics_all_bad() -> None:
    findings = mod._evaluate_metrics(2, 1, "ERROR")
    assert len(findings) == 3


def test_fetch_sonar_metrics(monkeypatch) -> None:
    calls = []

    def fake_request(url, auth):
        calls.append(url)
        if "qualitygates" in url:
            return {"projectStatus": {"status": "OK"}}
        return {"paging": {"total": 0}}

    monkeypatch.setattr(mod, "_request_json", fake_request)
    result = mod._fetch_sonar_metrics("https://sonarcloud.io", "auth", "key", "main", "5")
    assert result == (0, 0, 0, "OK")


def test_fetch_sonar_metrics_unknown_gate(monkeypatch) -> None:
    monkeypatch.setattr(
        mod,
        "_request_json",
        lambda url, auth: {} if "qualitygates" in url else {"paging": {"total": 0}},
    )
    result = mod._fetch_sonar_metrics("https://sonarcloud.io", "auth", "key", "", "")
    assert result[3] == "UNKNOWN"


def test_render_md() -> None:
    md = mod._render_md(
        {
            "status": "fail",
            "project_key": "k",
            "open_issues": 1,
            "security_hotspots_total": 2,
            "security_hotspots_to_review": 1,
            "quality_gate": "ERROR",
            "timestamp_utc": "t",
            "findings": ["bad"],
        }
    )
    assert "`k`" in md and "- bad" in md
    md2 = mod._render_md(
        {
            "status": "pass",
            "project_key": "k",
            "timestamp_utc": "t",
            "findings": [],
        }
    )
    assert "- None" in md2


def test_safe_output_path_escape(tmp_path: Path) -> None:
    with pytest.raises(ValueError):
        mod._safe_output_path("../x", "fb", base=tmp_path)


def test_safe_output_path_absolute_inside_root(tmp_path: Path) -> None:
    abs_target = tmp_path / "sub" / "a.json"
    assert mod._safe_output_path(str(abs_target), "fb", base=tmp_path) == abs_target.resolve()


def test_main_missing_token(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.delenv("SONAR_TOKEN", raising=False)
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["check_sonar_zero.py", "--project-key", "k"])
    assert mod.main() == 1


def test_main_pass(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setenv("SONAR_TOKEN", "tok")
    monkeypatch.setattr(mod, "_fetch_sonar_metrics", lambda *a: (0, 0, 0, "OK"))
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["check_sonar_zero.py", "--project-key", "k"])
    assert mod.main() == 0


def test_main_fail_metrics(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setenv("SONAR_TOKEN", "tok")
    monkeypatch.setattr(mod, "_fetch_sonar_metrics", lambda *a: (3, 0, 0, "OK"))
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["check_sonar_zero.py", "--project-key", "k"])
    assert mod.main() == 1


def test_main_bad_output(tmp_path: Path, monkeypatch, capsys) -> None:
    monkeypatch.setenv("SONAR_TOKEN", "tok")
    monkeypatch.setattr(mod, "_fetch_sonar_metrics", lambda *a: (0, 0, 0, "OK"))
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(
        "sys.argv", ["check_sonar_zero.py", "--project-key", "k", "--out-json", "../bad.json"]
    )
    assert mod.main() == 1
    assert "escapes workspace root" in capsys.readouterr().err
