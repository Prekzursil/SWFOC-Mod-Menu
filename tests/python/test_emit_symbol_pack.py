"""Tests for tools/ghidra/emit-symbol-pack.py."""

from __future__ import annotations

import json
from pathlib import Path

from conftest import load_script_module

mod = load_script_module("tools/ghidra/emit-symbol-pack.py", "emit_symbol_pack")


def _raw(path: Path, symbols: list[dict]) -> Path:
    path.write_text(json.dumps({"symbols": symbols}), encoding="utf-8")
    return path


def test_load_raw_symbols_filters_incomplete(tmp_path: Path) -> None:
    raw = _raw(
        tmp_path / "r.json",
        [
            {"name": "a", "address": "0x10", "kind": "function"},
            {"name": "", "address": "0x20"},  # no name -> skipped
            {"name": "b", "address": ""},  # no address -> skipped
            {"name": "c", "address": "0x30"},  # no kind -> "unknown"
        ],
    )
    out = mod._load_raw_symbols(raw)
    assert [s.name for s in out] == ["a", "c"]
    assert out[1].kind == "unknown"


def test_parse_address_variants() -> None:
    assert mod._parse_address("0x1F") == 31
    assert mod._parse_address("10") == 16
    assert mod._parse_address("") is None
    assert mod._parse_address("0x") is None
    assert mod._parse_address("zzz") is None


def test_normalized_address() -> None:
    assert mod._normalized_address("0x1F") == "0x1f"
    assert mod._normalized_address("nothex") == "nothex"


def test_normalize_anchor_id() -> None:
    assert mod._normalize_anchor_id("Set Credits!") == "set_credits"
    assert mod._normalize_anchor_id("___") == ""


def test_fingerprint_id() -> None:
    assert mod._fingerprint_id("Mod Name.exe", "abcdef0123456789xyz") == "mod_name_abcdef0123456789"


def test_symbol_choice_rank_unparseable() -> None:
    sym = mod.RawSymbol(name="X", address="zzz", kind="k")
    assert mod._symbol_choice_rank(sym) == (1, 0, "x")


def test_build_anchors_prefers_lowest_address_and_skips_empty() -> None:
    symbols = [
        mod.RawSymbol(name="credits_value", address="0x20", kind="function"),
        mod.RawSymbol(
            name="Credits Value", address="0x10", kind="label"
        ),  # same anchor id, lower addr
        mod.RawSymbol(name="___", address="0x5", kind="label"),  # empty anchor id -> skipped
    ]
    anchors = mod._build_anchors("mod", symbols)
    assert len(anchors) == 1
    assert anchors[0]["address"] == "0x10"


def test_build_anchors_keeps_first_when_new_rank_not_lower() -> None:
    # Second symbol has the SAME anchor id but a HIGHER address -> not replaced
    # (exercises the "rank not lower" side of the choice branch).
    symbols = [
        mod.RawSymbol(name="credits_value", address="0x10", kind="label"),
        mod.RawSymbol(name="Credits Value", address="0x20", kind="function"),
    ]
    anchors = mod._build_anchors("mod", symbols)
    assert len(anchors) == 1
    assert anchors[0]["address"] == "0x10"


def test_build_capabilities_available_and_unavailable() -> None:
    caps = mod._build_capabilities({"credits_value"})
    by_id = {c["featureId"]: c for c in caps}
    assert by_id["set_credits"]["available"] is True
    assert by_id["set_credits"]["state"] == "Verified"
    assert by_id["freeze_timer"]["available"] is False
    assert by_id["freeze_timer"]["reasonCode"] == "CAPABILITY_REQUIRED_MISSING"


def test_main_end_to_end(tmp_path: Path, monkeypatch) -> None:
    raw = _raw(
        tmp_path / "r.json",
        [{"name": "credits_value", "address": "0x10", "kind": "function"}],
    )
    binary = tmp_path / "bin.exe"
    binary.write_text("bytes", encoding="utf-8")
    pack = tmp_path / "pack.json"
    summary = tmp_path / "summary.json"
    monkeypatch.setenv("GHIDRA_VERSION", "11.0")
    monkeypatch.setattr(
        "sys.argv",
        [
            "emit-symbol-pack.py",
            "--raw-symbols",
            str(raw),
            "--binary-path",
            str(binary),
            "--analysis-run-id",
            "rid",
            "--output-pack",
            str(pack),
            "--output-summary",
            str(summary),
            "--decompile-archive-path",
            str(tmp_path / "arc.zip"),
        ],
    )
    assert mod.main() == 0
    pack_data = json.loads(pack.read_text(encoding="utf-8"))
    assert pack_data["binaryFingerprint"]["moduleName"] == "bin.exe"
    summary_data = json.loads(summary.read_text(encoding="utf-8"))
    assert summary_data["toolVersions"]["ghidra"] == "11.0"
    assert summary_data["coverageStats"]["anchorCount"] == 1
    assert any("unavailable" in w for w in summary_data["warnings"])
    assert summary_data["artifactPointers"]["decompileArchivePath"].endswith("arc.zip")


def test_main_without_decompile_archive(tmp_path: Path, monkeypatch) -> None:
    raw = _raw(tmp_path / "r.json", [])
    binary = tmp_path / "bin.exe"
    binary.write_text("b", encoding="utf-8")
    pack = tmp_path / "pack.json"
    summary = tmp_path / "summary.json"
    monkeypatch.delenv("GHIDRA_VERSION", raising=False)
    monkeypatch.setattr(
        "sys.argv",
        [
            "emit-symbol-pack.py",
            "--raw-symbols",
            str(raw),
            "--binary-path",
            str(binary),
            "--analysis-run-id",
            "rid",
            "--output-pack",
            str(pack),
            "--output-summary",
            str(summary),
        ],
    )
    assert mod.main() == 0
    summary_data = json.loads(summary.read_text(encoding="utf-8"))
    assert summary_data["toolVersions"]["ghidra"] == "unknown"
    assert summary_data["artifactPointers"]["decompileArchivePath"] == ""
