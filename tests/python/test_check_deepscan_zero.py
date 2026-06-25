"""Tests for scripts/quality/check_deepscan_zero.py."""

from __future__ import annotations

from pathlib import Path

from conftest import load_script_module

mod = load_script_module("scripts/quality/check_deepscan_zero.py", "check_deepscan_zero")


def test_extract_total_open_dict_direct() -> None:
    assert mod.extract_total_open({"count": 7}) == 7


def test_extract_total_open_nested_dict() -> None:
    assert mod.extract_total_open({"a": {"b": {"total": 0}}}) == 0


def test_extract_total_open_list() -> None:
    assert mod.extract_total_open([{"x": 1}, {"hits": 3}]) == 3


def test_extract_total_open_none() -> None:
    assert mod.extract_total_open("string") is None
    assert mod.extract_total_open({"k": "v"}) is None


def test_find_total_in_dict_non_numeric() -> None:
    assert mod._find_total_in_dict({"total": "x"}) is None


def test_render_md_with_findings() -> None:
    md = mod._render_md(
        {
            "status": "fail",
            "open_issues": 1,
            "open_issues_url": "https://x.deepscan.io",
            "timestamp_utc": "t",
            "findings": ["f"],
        }
    )
    assert "deepscan.io" in md and "- f" in md


def test_render_md_no_url_no_findings() -> None:
    md = mod._render_md(
        {"status": "pass", "open_issues": 0, "open_issues_url": "", "timestamp_utc": "t"}
    )
    assert "n/a" in md and "- None" in md


def test_validate_config_missing_token_and_url() -> None:
    url, findings = mod._validate_config("", "")
    assert any("DEEPSCAN_API_TOKEN" in f for f in findings)
    assert any("DEEPSCAN_OPEN_ISSUES_URL" in f for f in findings)


def test_validate_config_bad_url() -> None:
    url, findings = mod._validate_config("tok", "http://insecure.deepscan.io")
    assert any("https" in f for f in findings)


def test_validate_config_ok() -> None:
    url, findings = mod._validate_config("tok", "https://api.deepscan.io/issues")
    assert findings == []
    assert url.startswith("https://")


def test_query_and_evaluate_pass(monkeypatch) -> None:
    monkeypatch.setattr(mod, "_request_json", lambda *a, **k: {"total": 0})
    status, issues, findings = mod._query_and_evaluate("https://api.deepscan.io", "tok")
    assert status == "pass" and issues == 0


def test_query_and_evaluate_nonzero(monkeypatch) -> None:
    monkeypatch.setattr(mod, "_request_json", lambda *a, **k: {"total": 9})
    status, issues, findings = mod._query_and_evaluate("https://api.deepscan.io", "tok")
    assert status == "fail" and issues == 9


def test_query_and_evaluate_unparseable(monkeypatch) -> None:
    monkeypatch.setattr(mod, "_request_json", lambda *a, **k: {"nope": 1})
    status, issues, findings = mod._query_and_evaluate("https://api.deepscan.io", "tok")
    assert status == "fail" and issues is None


def test_safe_output_path_absolute_inside_root(tmp_path: Path) -> None:
    abs_target = tmp_path / "sub" / "a.json"
    out = mod._safe_output_path(str(abs_target), "fb", base=tmp_path)
    assert out == abs_target.resolve()


def test_main_config_error(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.delenv("DEEPSCAN_API_TOKEN", raising=False)
    monkeypatch.delenv("DEEPSCAN_OPEN_ISSUES_URL", raising=False)
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["check_deepscan_zero.py"])
    assert mod.main() == 1


def test_main_pass(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setenv("DEEPSCAN_API_TOKEN", "tok")
    monkeypatch.setenv("DEEPSCAN_OPEN_ISSUES_URL", "https://api.deepscan.io/issues")
    monkeypatch.setattr(mod, "_request_json", lambda *a, **k: {"total": 0})
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["check_deepscan_zero.py"])
    assert mod.main() == 0


def test_main_bad_output(tmp_path: Path, monkeypatch, capsys) -> None:
    monkeypatch.setenv("DEEPSCAN_API_TOKEN", "tok")
    monkeypatch.setenv("DEEPSCAN_OPEN_ISSUES_URL", "https://api.deepscan.io/issues")
    monkeypatch.setattr(mod, "_request_json", lambda *a, **k: {"total": 0})
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["check_deepscan_zero.py", "--out-json", "../bad.json"])
    assert mod.main() == 1
    assert "escapes workspace root" in capsys.readouterr().err
