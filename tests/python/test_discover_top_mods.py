"""Tests for tools/workshop/discover-top-mods.py."""

from __future__ import annotations

import contextlib
import io
import json
from pathlib import Path

import pytest
from conftest import load_script_module

mod = load_script_module("tools/workshop/discover-top-mods.py", "discover_top_mods")


def test_utc_now_iso() -> None:
    assert mod.utc_now_iso().endswith("Z")


def test_as_int() -> None:
    assert mod.as_int("5") == 5
    assert mod.as_int(None, 7) == 7
    assert mod.as_int("x", 3) == 3


def test_clamp_confidence() -> None:
    assert mod.clamp_confidence(-1) == 0.0
    assert mod.clamp_confidence(2) == 1.0
    assert mod.clamp_confidence(0.555) == 0.56


def test_normalize_tag() -> None:
    assert mod.normalize_tag("  Total Conversion! ") == "total_conversion"


def test_unique_ordered() -> None:
    assert mod.unique_ordered(["a", "a", "b"]) == ["a", "b"]


def test_parse_timestamp_to_iso_variants() -> None:
    assert mod.parse_timestamp_to_iso("2020-01-01T00:00:00Z") == "2020-01-01T00:00:00Z"
    assert mod.parse_timestamp_to_iso("1577836800").startswith("2020-01-01")
    assert mod.parse_timestamp_to_iso(1577836800).startswith("2020-01-01")
    assert mod.parse_timestamp_to_iso(0).endswith("Z")  # <= 0 -> now
    assert mod.parse_timestamp_to_iso(None).endswith("Z")


def test_parse_timestamp_to_iso_nondigit_string() -> None:
    # A string that is neither ISO nor digit -> falls through to utc now.
    assert mod.parse_timestamp_to_iso("not-a-date").endswith("Z")


def test_parse_dependency_ids() -> None:
    assert mod.parse_dependency_ids([{"publishedfileid": "1"}, {"id": "2"}, "3", "x"]) == [
        "1",
        "2",
        "3",
    ]
    assert mod.parse_dependency_ids("notlist") == []


def test_parse_normalized_tags() -> None:
    assert mod.parse_normalized_tags([{"tag": "Overhaul"}, "Campaign", {"tag": None}]) == [
        "overhaul",
        "campaign",
    ]
    assert mod.parse_normalized_tags("notlist") == []


def test_parse_normalized_tags_skips_empty_normalized() -> None:
    # A tag that normalizes to empty string is skipped (normalized-falsy branch).
    assert mod.parse_normalized_tags(["!!!", "Real"]) == ["real"]


def test_infer_candidate_base_profile() -> None:
    assert mod.infer_candidate_base_profile("x", [], ["3447786229"]) == "roe_3447786229_swfoc"
    assert mod.infer_candidate_base_profile("x", [], ["1397421866"]) == "aotr_1397421866_swfoc"
    assert mod.infer_candidate_base_profile("Order 66 mod", [], []) == "roe_3447786229_swfoc"
    assert mod.infer_candidate_base_profile("Awakening", [], []) == "aotr_1397421866_swfoc"
    assert mod.infer_candidate_base_profile("x", ["eaw"], []) == "base_sweaw"
    assert mod.infer_candidate_base_profile("x", [], []) == "base_swfoc"


def test_infer_launch_hints() -> None:
    hints = mod.infer_launch_hints("base_sweaw", ["1"], ["campaign", "tactical", "multiplayer"])
    assert "launch_sweaw" in hints
    assert "requires_parent_mod" in hints
    assert "galactic_campaign" in hints
    assert "tactical_profile" in hints
    assert "manual_smoke_required" in hints
    assert "launch_swfoc" in mod.infer_launch_hints("base_swfoc", [], [])


def test_infer_risk_level() -> None:
    assert mod.infer_risk_level(["beta"], [], 0) == "high"
    assert mod.infer_risk_level([], ["dep"], 0) == "medium"
    assert mod.infer_risk_level(["multiplayer"], [], 100) == "medium"
    assert mod.infer_risk_level([], [], 100000) == "low"


def test_infer_confidence() -> None:
    score = mod.infer_confidence("AOTR", ["t"], ["d"], "aotr_1397421866_swfoc", 20000)
    assert score == 1.0  # capped
    low = mod.infer_confidence("Plain", [], [], "base_swfoc", 0)
    assert low == 0.55


def test_canonical_mod_url() -> None:
    assert mod.canonical_mod_url("99") == (
        "https://steamcommunity.com/sharedfiles/filedetails/?id=99"
    )


def test_normalize_mod_from_detail_minimal() -> None:
    out = mod.normalize_mod_from_detail({"publishedfileid": "123"})
    assert out is not None
    assert out["workshopId"] == "123"
    assert out["candidateBaseProfile"] == "base_swfoc"


def test_normalize_mod_from_detail_invalid() -> None:
    assert mod.normalize_mod_from_detail({"publishedfileid": "abc"}) is None


def test_normalize_mod_from_detail_explicit_overrides() -> None:
    out = mod.normalize_mod_from_detail(
        {
            "publishedfileid": "1",
            "title": "T",
            "launchHints": ["custom"],
            "riskLevel": "HIGH",
            "confidence": 0.4,
            "lifetime_subscriptions": 99,
            "subscriptions": 10,
        }
    )
    assert out is not None
    assert out["launchHints"] == ["custom"]
    assert out["riskLevel"] == "high"
    assert out["confidence"] == 0.4
    assert out["lifetimeSubscriptions"] == 99


def test_normalize_mod_from_detail_bad_confidence_falls_back() -> None:
    out = mod.normalize_mod_from_detail({"publishedfileid": "1", "confidence": "bad"})
    assert out is not None
    assert 0.0 <= out["confidence"] <= 1.0


def test_normalize_mod_from_detail_invalid_risk_recomputed() -> None:
    out = mod.normalize_mod_from_detail(
        {"publishedfileid": "1", "riskLevel": "weird", "tags": [{"tag": "beta"}]}
    )
    assert out is not None
    assert out["riskLevel"] == "high"


def test_sort_mods_both_bases() -> None:
    mods = [
        {"subscriptions": 1, "lifetimeSubscriptions": 5, "timeUpdated": "a"},
        {"subscriptions": 3, "lifetimeSubscriptions": 1, "timeUpdated": "b"},
    ]
    by_sub = mod.sort_mods(mods, "subscriptions_desc")
    assert by_sub[0]["subscriptions"] == 3
    by_life = mod.sort_mods(mods, "lifetime_subscriptions_desc")
    assert by_life[0]["lifetimeSubscriptions"] == 5


def test_build_output() -> None:
    out = mod.build_output(1, "rb", [], [], "g", "r")
    assert out["appId"] == 1 and out["rankingBasis"] == "rb"


def test_load_source_payload_topmods(tmp_path: Path) -> None:
    f = tmp_path / "s.json"
    f.write_text(json.dumps({"topMods": [{"publishedfileid": "1"}, "skip"]}), encoding="utf-8")
    mods, payload = mod.load_source_payload(f)
    assert len(mods) == 1


def test_load_source_payload_steam_response(tmp_path: Path) -> None:
    f = tmp_path / "s.json"
    f.write_text(
        json.dumps({"response": {"publishedfiledetails": [{"publishedfileid": "2"}]}}),
        encoding="utf-8",
    )
    mods, _ = mod.load_source_payload(f)
    assert mods[0]["workshopId"] == "2"


def test_load_source_payload_list(tmp_path: Path) -> None:
    f = tmp_path / "s.json"
    f.write_text(json.dumps([{"publishedfileid": "3"}, "skip"]), encoding="utf-8")
    mods, payload = mod.load_source_payload(f)
    assert mods[0]["workshopId"] == "3"
    assert payload == {}


def test_load_source_payload_unsupported(tmp_path: Path) -> None:
    f = tmp_path / "s.json"
    f.write_text(json.dumps({"weird": 1}), encoding="utf-8")
    with pytest.raises(ValueError, match="Unsupported source payload"):
        mod.load_source_payload(f)


class _FakeResp:
    def __init__(self, payload: bytes) -> None:
        self._payload = payload

    def read(self) -> bytes:
        return self._payload

    def __enter__(self) -> "_FakeResp":
        return self

    def __exit__(self, *a: object) -> None:
        return None


def test_scrape_workshop_ids_ok(monkeypatch) -> None:
    html = (
        b"sharedfiles/filedetails/?id=111 "
        b"sharedfiles/filedetails/?id=111 "  # duplicate is deduped
        b"sharedfiles/filedetails/?id=222"
    )
    monkeypatch.setattr(mod.request, "urlopen", lambda req, timeout: _FakeResp(html))
    ids, sources = mod.scrape_workshop_ids(32470, 1, 1.0)
    assert ids == ["111", "222"]
    assert sources[0]["type"] == "workshop_browse"


def test_scrape_workshop_ids_error(monkeypatch) -> None:
    def boom(req, timeout):
        raise OSError("net down")

    monkeypatch.setattr(mod.request, "urlopen", boom)
    err = io.StringIO()
    with contextlib.redirect_stderr(err):
        ids, sources = mod.scrape_workshop_ids(32470, 1, 1.0)
    assert ids == []
    assert "failed to scrape" in err.getvalue()


def test_fetch_published_file_details_ok(monkeypatch) -> None:
    payload = json.dumps(
        {"response": {"publishedfiledetails": [{"publishedfileid": "1"}, "skip"]}}
    ).encode("utf-8")
    monkeypatch.setattr(mod.request, "urlopen", lambda req, timeout: _FakeResp(payload))
    details = mod.fetch_published_file_details(["1", "2"], 1.0)
    assert details == [{"publishedfileid": "1"}]


def test_fetch_published_file_details_multibatch(monkeypatch) -> None:
    # >100 ids -> two batches, exercising the loop-continuation branch.
    payload = json.dumps(
        {"response": {"publishedfiledetails": [{"publishedfileid": "1"}]}}
    ).encode()
    calls = {"n": 0}

    def fake(req, timeout):
        calls["n"] += 1
        return _FakeResp(payload)

    monkeypatch.setattr(mod.request, "urlopen", fake)
    ids = [str(i) for i in range(150)]
    details = mod.fetch_published_file_details(ids, 1.0)
    assert calls["n"] == 2
    assert len(details) == 2


def test_fetch_published_file_details_non_list(monkeypatch) -> None:
    # publishedfiledetails not a list -> extend branch skipped, loop continues.
    payload = json.dumps({"response": {"publishedfiledetails": "notlist"}}).encode("utf-8")
    monkeypatch.setattr(mod.request, "urlopen", lambda req, timeout: _FakeResp(payload))
    assert mod.fetch_published_file_details(["1"], 1.0) == []


def test_fetch_published_file_details_error(monkeypatch) -> None:
    monkeypatch.setattr(
        mod.request, "urlopen", lambda req, timeout: (_ for _ in ()).throw(OSError("x"))
    )
    err = io.StringIO()
    with contextlib.redirect_stderr(err):
        details = mod.fetch_published_file_details(["1"], 1.0)
    assert details == []
    assert "failed to fetch" in err.getvalue()


def test_main_source_file_mode(tmp_path: Path, monkeypatch, capsys) -> None:
    src = tmp_path / "src.json"
    src.write_text(
        json.dumps(
            {
                "appId": 32470,
                "rankingBasis": "subscriptions_desc",
                "generatedAtUtc": "2020-01-01T00:00:00Z",
                "sources": [{"type": "x", "uri": "u"}],
                "topMods": [{"publishedfileid": "1", "subscriptions": 10}],
            }
        ),
        encoding="utf-8",
    )
    out = tmp_path / "out.json"
    monkeypatch.setattr(
        "sys.argv",
        ["d.py", "--output", str(out), "--source-file", str(src), "--limit", "5"],
    )
    assert mod.main() == 0
    data = json.loads(out.read_text(encoding="utf-8"))
    assert len(data["topMods"]) == 1
    assert any(s["type"] == "fixture" for s in data["sources"])
    assert "wrote 1" in capsys.readouterr().out


def test_main_bad_limit(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setattr("sys.argv", ["d.py", "--output", str(tmp_path / "o.json"), "--limit", "0"])
    with pytest.raises(ValueError, match="--limit"):
        mod.main()


def test_main_live_mode(tmp_path: Path, monkeypatch, capsys) -> None:
    monkeypatch.setattr(mod, "scrape_workshop_ids", lambda *a: (["1"], [{"type": "b", "uri": "u"}]))
    monkeypatch.setattr(
        mod,
        "fetch_published_file_details",
        lambda *a: [{"publishedfileid": "1", "subscriptions": 9}],
    )
    out = tmp_path / "out.json"
    monkeypatch.setattr("sys.argv", ["d.py", "--output", str(out)])
    assert mod.main() == 0
    data = json.loads(out.read_text(encoding="utf-8"))
    assert data["topMods"][0]["workshopId"] == "1"


def test_main_live_mode_no_ids(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setattr(mod, "scrape_workshop_ids", lambda *a: ([], []))
    monkeypatch.setattr("sys.argv", ["d.py", "--output", str(tmp_path / "o.json")])
    with pytest.raises(RuntimeError, match="No workshop IDs"):
        mod.main()
