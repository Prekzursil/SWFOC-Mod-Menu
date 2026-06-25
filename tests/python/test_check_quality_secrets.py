"""Tests for scripts/quality/check_quality_secrets.py."""

from __future__ import annotations

import json
from pathlib import Path

import pytest
from conftest import load_script_module

mod = load_script_module("scripts/quality/check_quality_secrets.py", "check_quality_secrets")


def test_dedupe() -> None:
    assert mod._dedupe(["A", " A ", "", "B", "A"]) == ["A", "B"]


def test_evaluate_env_all_present(monkeypatch) -> None:
    monkeypatch.setenv("S1", "v")
    monkeypatch.setenv("V1", "v")
    result = mod.evaluate_env(["S1"], ["V1"])
    assert result["missing_secrets"] == []
    assert result["present_secrets"] == ["S1"]
    assert result["present_vars"] == ["V1"]


def test_evaluate_env_missing(monkeypatch) -> None:
    monkeypatch.delenv("S2", raising=False)
    monkeypatch.setenv("S2", "   ")  # whitespace counts as missing
    result = mod.evaluate_env(["S2"], ["V2"])
    assert result["missing_secrets"] == ["S2"]
    assert result["missing_vars"] == ["V2"]


def test_render_md_with_and_without_missing() -> None:
    md = mod._render_md(
        {
            "status": "fail",
            "timestamp_utc": "t",
            "missing_secrets": ["X"],
            "missing_vars": ["Y"],
        }
    )
    assert "`X`" in md and "`Y`" in md
    md2 = mod._render_md(
        {"status": "pass", "timestamp_utc": "t", "missing_secrets": [], "missing_vars": []}
    )
    assert md2.count("- None") == 2


def test_safe_output_path_ok(tmp_path: Path) -> None:
    out = mod._safe_output_path("sub/a.json", "fb.json", base=tmp_path)
    assert str(out).startswith(str(tmp_path.resolve()))


def test_safe_output_path_default_fallback(tmp_path: Path) -> None:
    out = mod._safe_output_path("", "fallback.json", base=tmp_path)
    assert out.name == "fallback.json"


def test_safe_output_path_escape(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="escapes workspace root"):
        mod._safe_output_path("../escape.json", "fb.json", base=tmp_path)


def test_safe_output_path_absolute_inside_root(tmp_path: Path) -> None:
    abs_target = tmp_path / "sub" / "a.json"
    assert mod._safe_output_path(str(abs_target), "fb", base=tmp_path) == abs_target.resolve()


def test_main_pass(tmp_path: Path, monkeypatch, capsys) -> None:
    for name in mod.DEFAULT_REQUIRED_SECRETS:
        monkeypatch.setenv(name, "x")
    for name in mod.DEFAULT_REQUIRED_VARS:
        monkeypatch.setenv(name, "x")
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["check_quality_secrets.py"])
    assert mod.main() == 0
    assert "`pass`" in capsys.readouterr().out


def test_main_fail_missing(tmp_path: Path, monkeypatch) -> None:
    for name in mod.DEFAULT_REQUIRED_SECRETS + mod.DEFAULT_REQUIRED_VARS:
        monkeypatch.delenv(name, raising=False)
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["check_quality_secrets.py", "--required-secret", "EXTRA"])
    assert mod.main() == 1
    payload = json.loads((tmp_path / "quality-secrets" / "secrets.json").read_text("utf-8"))
    assert "EXTRA" in payload["missing_secrets"]


def test_main_bad_output_path(tmp_path: Path, monkeypatch, capsys) -> None:
    for name in mod.DEFAULT_REQUIRED_SECRETS + mod.DEFAULT_REQUIRED_VARS:
        monkeypatch.setenv(name, "x")
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["check_quality_secrets.py", "--out-json", "../outside.json"])
    assert mod.main() == 1
    assert "escapes workspace root" in capsys.readouterr().err
