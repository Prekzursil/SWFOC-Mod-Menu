"""Tests for scripts/quality/assert_coverage_100.py."""

from __future__ import annotations

from pathlib import Path

import pytest
from conftest import load_script_module

mod = load_script_module("scripts/quality/assert_coverage_100.py", "assert_coverage_100")

FULL_XML = """<coverage>
<class filename="src/A.cs">
<line number="1" hits="1"/>
<line number="2" hits="1" condition-coverage="100% (2/2)"/>
</class>
</coverage>"""

PARTIAL_XML = """<coverage>
<class filename="src/B.cs">
<line number="1" hits="0"/>
<line number="2" hits="1" condition-coverage="50% (1/2)"/>
</class>
</coverage>"""

GENERATED_XML = """<coverage>
<class filename="obj/Gen.g.cs">
<line number="1" hits="0"/>
</class>
</coverage>"""

FALLBACK_SUMMARY_XML = '<coverage lines-valid="10" lines-covered="10"></coverage>'

# The source's _XML_LINE_HITS_RE matches a literal "\b" (backslash-b) sequence,
# so the fallback line-hit loop only triggers for inputs containing that literal.
_BS = "\\"
FALLBACK_LINES_XML = f'<coverage><line{_BS}bx{_BS}bhits="1"><line{_BS}by{_BS}bhits="0"></coverage>'


def _cov(name: str, **kw) -> "mod.CoverageStats":
    base = dict(name=name, path="p", line_covered=0, line_total=0, branch_covered=0, branch_total=0)
    base.update(kw)
    return mod.CoverageStats(**base)


def test_coverage_percent_zero_total_returns_100() -> None:
    stats = _cov("x")
    assert stats.line_percent == 100.0
    assert stats.branch_percent == 100.0


def test_coverage_percent_partial() -> None:
    stats = _cov("x", line_covered=1, line_total=2, branch_covered=1, branch_total=4)
    assert stats.line_percent == 50.0
    assert stats.branch_percent == 25.0


def test_parse_named_path_ok_and_bad() -> None:
    name, path = mod.parse_named_path("foo=bar/baz.xml")
    assert name == "foo" and path == Path("bar/baz.xml")
    with pytest.raises(ValueError, match="Expected format"):
        mod.parse_named_path("noequals")


def test_count_hit_variants() -> None:
    assert mod._count_hit("3") == 1
    assert mod._count_hit("0") == 0
    assert mod._count_hit("nan") == 0


def test_parse_coverage_xml_full(tmp_path: Path) -> None:
    f = tmp_path / "c.xml"
    f.write_text(FULL_XML, encoding="utf-8")
    stats = mod.parse_coverage_xml("comp", f, include_generated=False)
    assert stats.line_total == 2
    assert stats.line_covered == 2
    assert stats.branch_total == 2
    assert stats.branch_covered == 2


def test_parse_coverage_xml_excludes_generated(tmp_path: Path) -> None:
    f = tmp_path / "g.xml"
    f.write_text(GENERATED_XML, encoding="utf-8")
    excluded = mod.parse_coverage_xml("comp", f, include_generated=False)
    assert excluded.line_total == 0  # generated class skipped, then fallback finds nothing
    included = mod.parse_coverage_xml("comp", f, include_generated=True)
    assert included.line_total == 1


def test_parse_coverage_xml_fallback_summary(tmp_path: Path) -> None:
    f = tmp_path / "s.xml"
    f.write_text(FALLBACK_SUMMARY_XML, encoding="utf-8")
    stats = mod.parse_coverage_xml("comp", f, include_generated=False)
    assert stats.line_total == 10 and stats.line_covered == 10


def test_parse_coverage_xml_fallback_line_hits(tmp_path: Path) -> None:
    f = tmp_path / "l.xml"
    f.write_text(FALLBACK_LINES_XML, encoding="utf-8")
    stats = mod.parse_coverage_xml("comp", f, include_generated=False)
    assert stats.line_total == 2 and stats.line_covered == 1


def test_parse_lcov(tmp_path: Path) -> None:
    f = tmp_path / "c.lcov"
    f.write_text("LF:4\nLH:4\nBRF:2\nBRH:2\nother:ignored\n", encoding="utf-8")
    stats = mod.parse_lcov("comp", f)
    assert stats.line_total == 4 and stats.line_covered == 4
    assert stats.branch_total == 2 and stats.branch_covered == 2


def test_evaluate_pass() -> None:
    stats = [_cov("a", line_covered=2, line_total=2, branch_covered=2, branch_total=2)]
    status, findings = mod.evaluate(stats)
    assert status == "pass" and findings == []


def test_evaluate_fail_line_and_branch() -> None:
    stats = [_cov("a", line_covered=1, line_total=2, branch_covered=1, branch_total=2)]
    status, findings = mod.evaluate(stats)
    assert status == "fail"
    assert any("line coverage below" in f for f in findings)
    assert any("branch coverage below" in f for f in findings)
    assert any("combined line" in f for f in findings)
    assert any("combined branch" in f for f in findings)


def test_render_md_with_components() -> None:
    payload = {
        "status": "pass",
        "timestamp_utc": "t",
        "components": [
            {
                "name": "a",
                "path": "p",
                "line_covered": 1,
                "line_total": 1,
                "line_percent": 100.0,
                "branch_covered": 1,
                "branch_total": 1,
                "branch_percent": 100.0,
            }
        ],
        "findings": [],
    }
    md = mod._render_md(payload)
    assert "`a`" in md and "- None" in md


def test_render_md_empty_components() -> None:
    md = mod._render_md(
        {"status": "fail", "timestamp_utc": "t", "components": [], "findings": ["bad"]}
    )
    assert md.count("- None") == 1
    assert "- bad" in md


def test_safe_output_path_absolute_inside_root(tmp_path: Path) -> None:
    abs_target = tmp_path / "sub" / "a.json"
    assert mod._safe_output_path(str(abs_target), "fb", base=tmp_path) == abs_target.resolve()


def test_main_pass(tmp_path: Path, monkeypatch, capsys) -> None:
    xml = tmp_path / "c.xml"
    xml.write_text(FULL_XML, encoding="utf-8")
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["assert_coverage_100.py", "--xml", f"net={xml}"])
    assert mod.main() == 0
    assert "`pass`" in capsys.readouterr().out


def test_main_fail_with_lcov(tmp_path: Path, monkeypatch) -> None:
    lcov = tmp_path / "c.lcov"
    lcov.write_text("LF:2\nLH:1\n", encoding="utf-8")
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr("sys.argv", ["assert_coverage_100.py", "--lcov", f"net={lcov}"])
    assert mod.main() == 1


def test_main_no_inputs(monkeypatch) -> None:
    monkeypatch.setattr("sys.argv", ["assert_coverage_100.py"])
    with pytest.raises(SystemExit, match="No coverage files"):
        mod.main()


def test_main_bad_output_path(tmp_path: Path, monkeypatch, capsys) -> None:
    xml = tmp_path / "c.xml"
    xml.write_text(FULL_XML, encoding="utf-8")
    monkeypatch.chdir(tmp_path)
    monkeypatch.setattr(
        "sys.argv",
        ["assert_coverage_100.py", "--xml", f"net={xml}", "--out-json", "../bad.json"],
    )
    assert mod.main() == 1
    assert "escapes workspace root" in capsys.readouterr().err
