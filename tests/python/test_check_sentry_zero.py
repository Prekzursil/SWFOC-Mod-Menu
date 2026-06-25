"""Tests for scripts/quality/check_sentry_zero.py."""

from __future__ import annotations

from pathlib import Path

import pytest
from conftest import load_script_module

mod = load_script_module("scripts/quality/check_sentry_zero.py", "check_sentry_zero")


def test_hits_from_headers() -> None:
    assert mod._hits_from_headers({"x-hits": "5"}) == 5
    assert mod._hits_from_headers({}) is None
    assert mod._hits_from_headers({"x-hits": "abc"}) is None


def test_resolve_projects_from_args() -> None:
    assert mod._resolve_projects(["P1", "P1", "p2"]) == ["p1", "p2"]


def test_resolve_projects_from_env(monkeypatch) -> None:
    monkeypatch.setenv("SENTRY_PROJECT", "main")
    monkeypatch.setenv("SENTRY_PROJECT_BACKEND", "be")
    monkeypatch.setenv("SENTRY_PROJECT_WEB", "")
    assert mod._resolve_projects([]) == ["main", "be"]


def test_resolve_projects_empty(monkeypatch) -> None:
    for n in ("SENTRY_PROJECT", "SENTRY_PROJECT_BACKEND", "SENTRY_PROJECT_WEB"):
        monkeypatch.delenv(n, raising=False)
    assert mod._resolve_projects([]) == []


def test_validate_sentry_config() -> None:
    findings = mod._validate_sentry_config("", "", [])
    assert len(findings) == 3
    assert mod._validate_sentry_config("t", "o", ["p"]) == []


def test_render_md_full() -> None:
    md = mod._render_md(
        {
            "status": "fail",
            "org": "o",
            "timestamp_utc": "t",
            "projects": [{"project": "p", "unresolved": 2}],
            "findings": ["f"],
        }
    )
    assert "`p`" in md and "- f" in md


def test_render_md_empty() -> None:
    md = mod._render_md(
        {"status": "pass", "org": "o", "timestamp_utc": "t", "projects": [], "findings": []}
    )
    assert md.count("- None") == 2


def test_query_project_with_hits(monkeypatch) -> None:
    monkeypatch.setattr(mod, "_request", lambda *a, **k: ([], {"x-hits": "0"}))
    unresolved, findings = mod._query_project("https://sentry.io/api/0", "o", "p", "t")
    assert unresolved == 0 and findings == []


def test_query_project_nonzero_hits(monkeypatch) -> None:
    monkeypatch.setattr(mod, "_request", lambda *a, **k: ([], {"x-hits": "3"}))
    unresolved, findings = mod._query_project("https://sentry.io/api/0", "o", "p", "t")
    assert unresolved == 3
    assert any("3 unresolved" in f for f in findings)


def test_query_project_no_hits_header_falls_back_to_len(monkeypatch) -> None:
    monkeypatch.setattr(mod, "_request", lambda *a, **k: ([{"id": 1}], {}))
    unresolved, findings = mod._query_project("https://sentry.io/api/0", "o", "p", "t")
    assert unresolved == 1
    assert any("no X-Hits header" in f for f in findings)


def test_query_all_projects_pass(monkeypatch) -> None:
    monkeypatch.setattr(mod, "_query_project", lambda *a: (0, []))
    status, results, findings = mod._query_all_projects("b", "o", ["p1", "p2"], "t")
    assert status == "pass" and len(results) == 2


def test_query_all_projects_fail(monkeypatch) -> None:
    monkeypatch.setattr(mod, "_query_project", lambda *a: (1, ["bad"]))
    status, results, findings = mod._query_all_projects("b", "o", ["p1"], "t")
    assert status == "fail"


def test_safe_output_path_escape(tmp_path: Path) -> None:
    with pytest.raises(ValueError):
        mod._safe_output_path("../x", "fb", base=tmp_path)


def test_safe_output_path_absolute_inside_root(tmp_path: Path) -> None:
    abs_target = tmp_path / "sub" / "a.json"
    assert mod._safe_output_path(str(abs_target), "fb", base=tmp_path) == abs_target.resolve()


def test_query_project_no_hits_empty_issues(monkeypatch) -> None:
    # No X-Hits header and zero issues -> unresolved stays 0 (>=1 branch skipped).
    monkeypatch.setattr(mod, "_request", lambda *a, **k: ([], {}))
    unresolved, findings = mod._query_project("https://sentry.io/api/0", "o", "p", "t")
    assert unresolved == 0 and findings == []


def test_main_config_fail(tmp_path: Path, monkeypatch) -> None:
    for n in (
        "SENTRY_AUTH_TOKEN",
        "SENTRY_ORG",
        "SENTRY_PROJECT",
        "SENTRY_PROJECT_BACKEND",
        "SENTRY_PROJECT_WEB",
    ):
        monkeypatch.delenv(n, raising=False)
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["check_sentry_zero.py"])
    assert mod.main() == 1


def test_main_pass(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setenv("SENTRY_AUTH_TOKEN", "t")
    monkeypatch.setenv("SENTRY_ORG", "o")
    monkeypatch.setattr(mod, "_request", lambda *a, **k: ([], {"x-hits": "0"}))
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["check_sentry_zero.py", "--project", "p"])
    assert mod.main() == 0


def test_main_bad_output(tmp_path: Path, monkeypatch, capsys) -> None:
    monkeypatch.setenv("SENTRY_AUTH_TOKEN", "t")
    monkeypatch.setenv("SENTRY_ORG", "o")
    monkeypatch.setattr(mod, "_request", lambda *a, **k: ([], {"x-hits": "0"}))
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(
        "sys.argv", ["check_sentry_zero.py", "--project", "p", "--out-md", "../bad.md"]
    )
    assert mod.main() == 1
    assert "escapes workspace root" in capsys.readouterr().err
