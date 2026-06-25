"""Tests for scripts/quality/check_codacy_zero.py."""

from __future__ import annotations

import json
import urllib.error
from pathlib import Path

import pytest
from conftest import load_script_module

mod = load_script_module("scripts/quality/check_codacy_zero.py", "check_codacy_zero")


def test_extract_total_open_direct_key() -> None:
    assert mod.extract_total_open({"total": 5}) == 5


def test_extract_total_open_nested() -> None:
    # Nested candidates (pagination/page/meta) are searched via a DFS stack.
    assert mod.extract_total_open({"pagination": {"count": 3}}) == 3
    assert mod.extract_total_open({"page": {"total": 0}}) == 0
    assert mod.extract_total_open({"meta": {"hits": 1}}) == 1


def test_extract_total_open_in_list() -> None:
    assert mod.extract_total_open([{"hits": 0}]) == 0


def test_extract_total_open_none() -> None:
    assert mod.extract_total_open({"a": "b"}) is None
    assert mod.extract_total_open(42) is None


def test_find_total_ignores_non_numeric() -> None:
    assert mod._find_total_in_keys({"total": "x"}) is None


def test_render_md() -> None:
    md = mod._render_md(
        {
            "status": "fail",
            "owner": "o",
            "repo": "r",
            "open_issues": 2,
            "timestamp_utc": "t",
            "findings": ["bad"],
        }
    )
    assert "`o/r`" in md and "- bad" in md
    md2 = mod._render_md(
        {"status": "pass", "owner": "o", "repo": "r", "timestamp_utc": "t", "findings": []}
    )
    assert "- None" in md2


def test_safe_output_path_escape(tmp_path: Path) -> None:
    with pytest.raises(ValueError):
        mod._safe_output_path("../x.json", "fb", base=tmp_path)


def test_safe_output_path_absolute_inside_root(tmp_path: Path) -> None:
    abs_target = tmp_path / "sub" / "a.json"
    out = mod._safe_output_path(str(abs_target), "fb", base=tmp_path)
    assert out == abs_target.resolve()


def test_query_codacy_issues_pass(monkeypatch) -> None:
    monkeypatch.setattr(mod, "_request_json", lambda *a, **k: {"total": 0})
    status, issues, findings = mod._query_codacy_issues(
        "https://api.codacy.com", "t", "o", "r", "gh"
    )
    assert status == "pass" and issues == 0 and findings == []


def test_query_codacy_issues_nonzero(monkeypatch) -> None:
    monkeypatch.setattr(mod, "_request_json", lambda *a, **k: {"total": 4})
    status, issues, findings = mod._query_codacy_issues(
        "https://api.codacy.com", "t", "o", "r", "gh"
    )
    assert status == "fail" and issues == 4


def test_query_codacy_issues_unparseable(monkeypatch) -> None:
    monkeypatch.setattr(mod, "_request_json", lambda *a, **k: {"nope": 1})
    status, issues, findings = mod._query_codacy_issues(
        "https://api.codacy.com", "t", "o", "r", "gh"
    )
    assert status == "fail" and issues is None
    assert any("parseable" in f for f in findings)


def _http_error(code: int) -> urllib.error.HTTPError:
    return urllib.error.HTTPError("u", code, "msg", {}, None)  # type: ignore[arg-type]


def test_query_codacy_issues_404_then_success(monkeypatch) -> None:
    calls = {"n": 0}

    def fake(*a, **k):
        calls["n"] += 1
        if calls["n"] == 1:
            raise _http_error(404)
        return {"total": 0}

    monkeypatch.setattr(mod, "_request_json", fake)
    status, _, _ = mod._query_codacy_issues("https://api.codacy.com", "t", "o", "r", "gh")
    assert status == "pass"
    assert calls["n"] == 2


def test_query_codacy_issues_http_500(monkeypatch) -> None:
    monkeypatch.setattr(
        mod, "_request_json", lambda *a, **k: (_ for _ in ()).throw(_http_error(500))
    )
    status, issues, findings = mod._query_codacy_issues(
        "https://api.codacy.com", "t", "o", "r", "gh"
    )
    assert status == "fail"
    assert any("HTTP 500" in f for f in findings)


def test_query_codacy_issues_all_404(monkeypatch) -> None:
    monkeypatch.setattr(
        mod, "_request_json", lambda *a, **k: (_ for _ in ()).throw(_http_error(404))
    )
    status, issues, findings = mod._query_codacy_issues(
        "https://api.codacy.com", "t", "o", "r", "gh"
    )
    assert status == "fail"
    assert any("was not found" in f for f in findings)
    assert any("Last Codacy API error" in f for f in findings)


def test_main_missing_token(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.delenv("CODACY_API_TOKEN", raising=False)
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["check_codacy_zero.py", "--owner", "o", "--repo", "r"])
    assert mod.main() == 1
    payload = json.loads((tmp_path / "codacy-zero" / "codacy.json").read_text("utf-8"))
    assert "CODACY_API_TOKEN is missing." in payload["findings"]


def test_main_pass_with_token(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setenv("CODACY_API_TOKEN", "tok")
    monkeypatch.setattr(mod, "_request_json", lambda *a, **k: {"total": 0})
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["check_codacy_zero.py", "--owner", "o", "--repo", "r"])
    assert mod.main() == 0


def test_main_bad_output(tmp_path: Path, monkeypatch, capsys) -> None:
    monkeypatch.setenv("CODACY_API_TOKEN", "tok")
    monkeypatch.setattr(mod, "_request_json", lambda *a, **k: {"total": 0})
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(
        "sys.argv",
        ["check_codacy_zero.py", "--owner", "o", "--repo", "r", "--out-md", "../bad.md"],
    )
    assert mod.main() == 1
    assert "escapes workspace root" in capsys.readouterr().err
