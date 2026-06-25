"""Tests for tools/verify-codacy-ignore-scope.py."""

from __future__ import annotations

import json
from pathlib import Path

from conftest import load_script_module

mod = load_script_module("tools/verify-codacy-ignore-scope.py", "verify_codacy_ignore_scope")


def test_unquote() -> None:
    assert mod.unquote('"x"') == "x"
    assert mod.unquote("'x'") == "x"
    assert mod.unquote("x") == "x"


def test_update_quote_state() -> None:
    assert mod.update_quote_state("'", False, False) == (True, False)
    assert mod.update_quote_state('"', False, False) == (False, True)
    assert mod.update_quote_state("'", False, True) == (False, True)  # inside double quote
    assert mod.update_quote_state("a", False, False) == (False, False)


def test_is_comment_delimiter() -> None:
    assert mod.is_comment_delimiter("#", False, False) is True
    assert mod.is_comment_delimiter("#", True, False) is False


def test_strip_inline_comment() -> None:
    assert mod.strip_inline_comment("value  # note") == "value"
    assert mod.strip_inline_comment('"a # b"') == '"a # b"'
    assert mod.strip_inline_comment("clean") == "clean"


def test_is_top_level_key() -> None:
    assert mod.is_top_level_key("key:", "key:") is True
    assert mod.is_top_level_key("  indented:", "indented:") is False
    assert mod.is_top_level_key("- item", "- item") is False
    assert mod.is_top_level_key("", "") is False


def test_parse_exclude_item() -> None:
    assert mod.parse_exclude_item("- foo/**") == "foo/**"
    assert mod.parse_exclude_item("# comment") is None
    assert mod.parse_exclude_item("notalist") is None
    assert mod.parse_exclude_item("- # only comment") is None


def test_parse_exclude_paths(tmp_path: Path) -> None:
    cfg = tmp_path / ".codacy.yml"
    cfg.write_text(
        "exclude_paths:\n"
        "  - 'TestResults/**'\n"
        "  # a comment line inside the block (parses to None)\n"
        "  - artifacts/**\n"
        "other_key:\n"
        "  - ignored/**\n",
        encoding="utf-8",
    )
    paths = mod.parse_exclude_paths(cfg)
    assert paths == ["TestResults/**", "artifacts/**"]


def test_should_skip_scanned_path() -> None:
    assert mod.should_skip_scanned_path(".git/config") is True
    assert mod.should_skip_scanned_path("src/obj/x.cs") is True
    assert mod.should_skip_scanned_path("src/Main.cs") is False


def test_list_repository_files(tmp_path: Path) -> None:
    (tmp_path / "src").mkdir()
    (tmp_path / "src" / "a.cs").write_text("x", encoding="utf-8")
    (tmp_path / "obj").mkdir()
    (tmp_path / "obj" / "gen.cs").write_text("x", encoding="utf-8")
    files = mod.list_repository_files(tmp_path)
    assert "src/a.cs" in files
    assert "obj/gen.cs" not in files


def test_match_ignored_files() -> None:
    ignored, counts = mod.match_ignored_files(["TestResults/x.txt", "src/a.cs"], ["TestResults/**"])
    assert ignored == ["TestResults/x.txt"]
    assert counts == {"TestResults/**": 1}


def test_build_report(tmp_path: Path) -> None:
    (tmp_path / "TestResults").mkdir()
    (tmp_path / "TestResults" / "r.txt").write_text("x", encoding="utf-8")
    (tmp_path / "src").mkdir()
    (tmp_path / "src" / "a.cs").write_text("x", encoding="utf-8")
    report = mod.build_report(tmp_path, Path(".codacy.yml"), ["TestResults/**"])
    assert report["ignoredTrackedFilesTotal"] == 1
    assert report["trackedFilesTotal"] == 2


def test_strict_violations_broad_pattern() -> None:
    violations = mod.strict_violations(["src/**"], [])
    codes = {v["code"] for v in violations}
    assert "CODACY_SCOPE_DISALLOWED_BROAD_PATTERN" in codes


def test_strict_violations_unexpected_pattern() -> None:
    violations = mod.strict_violations(["weird/**"], [])
    codes = {v["code"] for v in violations}
    assert "CODACY_SCOPE_UNEXPECTED_PATTERN" in codes


def test_strict_violations_protected_ignored() -> None:
    violations = mod.strict_violations(["artifacts/**"], ["src/secret.cs"])
    codes = {v["code"] for v in violations}
    assert "CODACY_SCOPE_PROTECTED_PATH_IGNORED" in codes


def test_strict_violations_clean() -> None:
    assert mod.strict_violations(["artifacts/**"], ["artifacts/x"]) == []


def test_main_config_not_found(tmp_path: Path, monkeypatch, capsys) -> None:
    monkeypatch.setattr(
        "sys.argv", ["v.py", "--repo-root", str(tmp_path), "--codacy-file", "missing.yml"]
    )
    assert mod.main() == 1
    out = json.loads(capsys.readouterr().out)
    assert out["error"]["code"] == "CODACY_SCOPE_CONFIG_NOT_FOUND"


def test_main_non_strict_ok(tmp_path: Path, monkeypatch, capsys) -> None:
    (tmp_path / ".codacy.yml").write_text(
        "exclude_paths:\n  - 'TestResults/**'\n", encoding="utf-8"
    )
    monkeypatch.setattr("sys.argv", ["v.py", "--repo-root", str(tmp_path)])
    assert mod.main() == 0
    report = json.loads(capsys.readouterr().out)
    assert "strictViolations" not in report


def test_main_strict_violation_with_output(tmp_path: Path, monkeypatch, capsys) -> None:
    (tmp_path / ".codacy.yml").write_text("exclude_paths:\n  - 'src/**'\n", encoding="utf-8")
    (tmp_path / "src").mkdir()
    (tmp_path / "src" / "a.cs").write_text("x", encoding="utf-8")
    out_file = tmp_path / "out" / "report.json"
    monkeypatch.setattr(
        "sys.argv",
        ["v.py", "--repo-root", str(tmp_path), "--strict", "--output", str(out_file)],
    )
    assert mod.main() == 1
    assert out_file.exists()
    written = json.loads(out_file.read_text(encoding="utf-8"))
    assert written["strictViolations"]


def test_main_strict_relative_output(tmp_path: Path, monkeypatch) -> None:
    (tmp_path / ".codacy.yml").write_text(
        "exclude_paths:\n  - 'TestResults/**'\n", encoding="utf-8"
    )
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(
        "sys.argv",
        ["v.py", "--repo-root", str(tmp_path), "--strict", "--output", "rel.json"],
    )
    assert mod.main() == 0
    assert (tmp_path / "rel.json").exists()
