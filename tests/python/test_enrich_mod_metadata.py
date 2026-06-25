"""Tests for tools/workshop/enrich-mod-metadata.py."""

from __future__ import annotations

import json
from pathlib import Path

import pytest
from conftest import load_script_module

mod = load_script_module("tools/workshop/enrich-mod-metadata.py", "enrich_mod_metadata")


def test_utc_now_iso() -> None:
    assert mod.utc_now_iso().endswith("Z")


def test_clamp_confidence() -> None:
    assert mod.clamp_confidence("bad") == 0.5
    assert mod.clamp_confidence(-1) == 0.0
    assert mod.clamp_confidence(2) == 1.0
    assert mod.clamp_confidence(0.123) == 0.12


def test_unique_ordered() -> None:
    assert mod.unique_ordered(["a", "b", "a"]) == ["a", "b"]


def test_infer_required_capabilities_base_swfoc() -> None:
    caps = mod.infer_required_capabilities("base_swfoc", [], [], [])
    assert "set_unit_cap" in caps  # not sweaw -> adds unit cap


def test_infer_required_capabilities_sweaw() -> None:
    caps = mod.infer_required_capabilities("base_sweaw", [], [], [])
    assert "set_unit_cap" not in caps


def test_infer_required_capabilities_aotr_and_roe() -> None:
    aotr = mod.infer_required_capabilities("aotr_1397421866_swfoc", [], [], [])
    assert "set_hero_state_helper" in aotr
    roe = mod.infer_required_capabilities("roe_3447786229_swfoc", [], [], [])
    assert "toggle_roe_respawn_helper" in roe


def test_infer_required_capabilities_deps_and_tags() -> None:
    caps = mod.infer_required_capabilities(
        "base_swfoc",
        ["galactic_campaign"],
        ["tactical", "campaign"],
        ["3447786229"],
    )
    assert "spawn_unit_helper" in caps  # parent deps
    assert "set_selected_hp" in caps  # tactical tag
    assert "set_hero_respawn_timer" in caps  # campaign


def test_build_anchor_hints() -> None:
    anchors = mod.build_anchor_hints(
        "Awakening of the Rebellion Mod",
        ["overhaul", "campaign"],
        "123",
        "aotr_1397421866_swfoc",
        ["456"],
    )
    assert "workshop:123" in anchors
    assert "base:aotr_1397421866_swfoc" in anchors
    assert "dep:456" in anchors
    assert any(a.startswith("title:") for a in anchors)
    assert any(a.startswith("tag:") for a in anchors)


def test_normalize_dependency_ids() -> None:
    assert mod.normalize_dependency_ids(["1", "abc", "2", "1"]) == ["1", "2"]
    assert mod.normalize_dependency_ids("notlist") == []


def test_normalize_text_list() -> None:
    assert mod.normalize_text_list(["a", "", "b", "a"]) == ["a", "b"]
    assert mod.normalize_text_list(None) == []


def test_ensure_risk_level() -> None:
    assert mod.ensure_risk_level("HIGH") == "high"
    assert mod.ensure_risk_level("nonsense") == "medium"


def test_to_seed_valid() -> None:
    seed = mod.to_seed(
        {
            "workshopId": "123",
            "title": "Cool Mod",
            "parentDependencies": ["456"],
            "launchHints": ["workshop"],
            "normalizedTags": ["overhaul"],
            "candidateBaseProfile": "base_swfoc",
            "riskLevel": "low",
            "confidence": 0.8,
        },
        "run-1",
    )
    assert seed is not None
    assert seed["workshopId"] == "123"
    assert seed["sourceRunId"] == "run-1"


def test_to_seed_invalid_id() -> None:
    assert mod.to_seed({"workshopId": "notdigit"}, "r") is None


def test_to_seed_defaults_base_profile() -> None:
    seed = mod.to_seed({"workshopId": "1", "candidateBaseProfile": ""}, "r")
    assert seed is not None
    assert seed["candidateBaseProfile"] == "base_swfoc"


def test_main_ok(tmp_path: Path, monkeypatch, capsys) -> None:
    in_path = tmp_path / "in.json"
    in_path.write_text(
        json.dumps(
            {
                "appId": 999,
                "topMods": [{"workshopId": "1", "title": "M"}, "skip", {"workshopId": "x"}],
            }
        ),
        encoding="utf-8",
    )
    out_path = tmp_path / "out" / "seeds.json"
    monkeypatch.setattr(
        "sys.argv",
        ["e.py", "--input", str(in_path), "--output", str(out_path), "--source-run-id", "r1"],
    )
    assert mod.main() == 0
    data = json.loads(out_path.read_text(encoding="utf-8"))
    assert data["appId"] == 999
    assert len(data["seeds"]) == 1
    assert "wrote 1" in capsys.readouterr().out


def test_main_bad_topmods(tmp_path: Path, monkeypatch) -> None:
    in_path = tmp_path / "in.json"
    in_path.write_text(json.dumps({"topMods": "notlist"}), encoding="utf-8")
    monkeypatch.setattr(
        "sys.argv",
        [
            "e.py",
            "--input",
            str(in_path),
            "--output",
            str(tmp_path / "o.json"),
            "--source-run-id",
            "r",
        ],
    )
    with pytest.raises(ValueError, match="topMods"):
        mod.main()
