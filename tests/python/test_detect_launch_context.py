"""Tests for tools/detect-launch-context.py."""

from __future__ import annotations

import json
from pathlib import Path

import pytest
from conftest import load_script_module

mod = load_script_module("tools/detect-launch-context.py", "detect_launch_context")


def _profile(profile_id: str, **kw) -> "mod.ProfileInfo":
    return mod.ProfileInfo(
        profile_id=profile_id,
        exe_target=kw.get("exe_target", "swfoc"),
        steam_workshop_id=kw.get("steam_workshop_id"),
        metadata=kw.get("metadata", {}),
    )


def test_normalize_token() -> None:
    assert mod.normalize_token(None) is None
    assert mod.normalize_token("") is None
    assert mod.normalize_token('  "D:\\\\A//B"  ') == "d:/a/b"


def test_parse_steammod_ids() -> None:
    assert mod.parse_steammod_ids(None) == []
    assert mod.parse_steammod_ids("STEAMMOD=123 steammod = 456") == ["123", "456"]


def test_parse_forced_workshop_ids() -> None:
    assert mod.parse_forced_workshop_ids(None) == []
    assert mod.parse_forced_workshop_ids("1, 2 ,,3") == ["1", "2", "3"]


def test_parse_modpath() -> None:
    assert mod.parse_modpath(None) is None
    assert mod.parse_modpath("nomod here") is None
    assert mod.parse_modpath('MODPATH="My Mod/Dir"') == "My Mod/Dir"
    assert mod.parse_modpath("modpath=Mods/X") == "Mods/X"


def test_parse_modpath_empty_value() -> None:
    # The unquoted alternative matches the bare "" token; stripping quotes yields "".
    assert mod.parse_modpath('modpath=""') == ""


def test_parse_csv() -> None:
    assert mod.parse_csv({"k": "a, b ,,c"}, "k") == ["a", "b", "c"]
    assert mod.parse_csv({}, "k") == []


def test_infer_launch_kind() -> None:
    assert mod.infer_launch_kind(["1"], "p", "x") == "Mixed"
    assert mod.infer_launch_kind(["1"], None, "x") == "Workshop"
    assert mod.infer_launch_kind([], "p", "x") == "LocalModPath"
    assert mod.infer_launch_kind([], None, "swfoc") == "BaseGame"
    assert mod.infer_launch_kind([], None, "other") == "Unknown"


def test_detect_exe_hint() -> None:
    assert mod.detect_exe_hint("sweaw.exe", None, None) == "sweaw"
    assert mod.detect_exe_hint(None, "C:/swfoc.exe", None) == "swfoc"
    assert mod.detect_exe_hint(None, None, "run StarWarsG.exe") == "starwarsg"
    assert mod.detect_exe_hint("game", None, None) == "unknown"


def test_gather_hints() -> None:
    p = _profile(
        "ROE_X",
        steam_workshop_id="999",
        metadata={"localPathHints": "Mods/RoE", "profileAliases": "roe"},
    )
    hints = mod.gather_hints(p)
    assert "roe_x" in hints
    assert "999" in hints
    assert "mods/roe" in hints


def test_gather_hints_skips_unnormalizable() -> None:
    # '""' normalizes to '' (falsy) and is skipped; only the profile id remains.
    p = _profile("p", metadata={"localPathHints": '""'})
    hints = mod.gather_hints(p)
    assert hints == ["p"]


def test_reason_code_for_profile() -> None:
    assert mod.reason_code_for_profile("roe_x", "steam") == "steammod_exact_roe"
    assert mod.reason_code_for_profile("aotr_x", "steam") == "steammod_exact_aotr"
    assert mod.reason_code_for_profile("base_x", "steam") == "steammod_exact_profile"
    assert mod.reason_code_for_profile("roe_x", "modpath") == "modpath_hint_roe"
    assert mod.reason_code_for_profile("aotr_x", "modpath") == "modpath_hint_aotr"
    assert mod.reason_code_for_profile("base_x", "modpath") == "modpath_hint_profile"
    assert mod.reason_code_for_profile("x", "other") == "unknown"


def test_profile_sort_priority() -> None:
    assert mod.profile_sort_priority("roe_x") == 0
    assert mod.profile_sort_priority("aotr_x") == 1
    assert mod.profile_sort_priority("base_x") == 2


def test_required_workshop_ids() -> None:
    p = _profile(
        "x",
        steam_workshop_id="100",
        metadata={"requiredWorkshopIds": "200,300", "requiredWorkshopId": "100"},
    )
    assert mod.required_workshop_ids(p) == ["100", "200", "300"]


def test_score_workshop_match() -> None:
    direct = _profile("x", steam_workshop_id="100")
    assert mod.score_workshop_match(direct, {"100"}) == 1000
    no_required = _profile("x")
    assert mod.score_workshop_match(no_required, {"999"}) == 0
    full = _profile("x", metadata={"requiredWorkshopIds": "10,20"})
    assert mod.score_workshop_match(full, {"10", "20"}) == 902
    partial = _profile("x", metadata={"requiredWorkshopIds": "10,20"})
    assert mod.score_workshop_match(partial, {"10"}) == 701
    none_overlap = _profile("x", metadata={"requiredWorkshopIds": "10"})
    assert mod.score_workshop_match(none_overlap, {"99"}) == 0


def test_steam_profile_match_none_when_no_ids() -> None:
    assert mod.steam_profile_match({"a": _profile("a")}, []) is None


def test_steam_profile_match_picks_best() -> None:
    profiles = {
        "low": _profile("low", metadata={"requiredWorkshopIds": "10"}),
        "high": _profile("high", steam_workshop_id="10"),
    }
    best = mod.steam_profile_match(profiles, ["10"])
    assert best is not None and best.profile_id == "high"


def test_steam_profile_match_skips_zero_score() -> None:
    # A non-matching profile (score 0) is skipped both before AND after a match is
    # found, exercising the continue-loops-back branch in either iteration order.
    profiles = {
        "nomatch1": _profile("nomatch1", steam_workshop_id="999"),
        "match": _profile("match", steam_workshop_id="10"),
        "nomatch2": _profile("nomatch2", steam_workshop_id="888"),
    }
    best = mod.steam_profile_match(profiles, ["10"])
    assert best is not None and best.profile_id == "match"


def test_steam_profile_match_worse_candidate_not_chosen() -> None:
    # Two scoring profiles; the second is strictly worse, so the selection `if`
    # is False and the loop continues without updating best_profile.
    profiles = {
        "best": _profile("aotr_best", steam_workshop_id="10"),  # direct match, score 1000
        "worse": _profile("aotr_worse", metadata={"requiredWorkshopIds": "10"}),  # score 902
    }
    best = mod.steam_profile_match(profiles, ["10"])
    assert best is not None and best.profile_id == "aotr_best"


def test_steam_profile_match_tiebreak_required_count() -> None:
    # Same direct-match score (1000), but "b" declares more required ids -> wins
    # the required_count tiebreak.
    profiles = {
        "a": _profile("aotr_a", steam_workshop_id="10"),
        "b": _profile("aotr_b", steam_workshop_id="10", metadata={"requiredWorkshopIds": "20,30"}),
    }
    best = mod.steam_profile_match(profiles, ["10"])
    assert best is not None and best.profile_id == "aotr_b"


def test_steam_profile_match_tiebreak_priority_key() -> None:
    # Equal score and required_count -> lower profile_priority_key wins (roe < aotr).
    profiles = {
        "z": _profile("aotr_z", steam_workshop_id="10"),
        "a": _profile("roe_a", steam_workshop_id="10"),
    }
    best = mod.steam_profile_match(profiles, ["10"])
    assert best is not None and best.profile_id == "roe_a"


def test_best_modpath_match() -> None:
    profiles = {
        "roe": _profile("roe_mod", metadata={"localPathHints": "RoE"}),
        "other": _profile("zzz"),
    }
    best = mod.best_modpath_match(profiles, "c:/games/roe_mod/data")
    assert best is not None and best.profile_id == "roe_mod"


def test_best_modpath_match_none() -> None:
    assert mod.best_modpath_match({"x": _profile("zzz")}, "nomatch") is None


def test_fallback_profile_for_exe() -> None:
    profiles = {"base_sweaw": _profile("base_sweaw"), "base_swfoc": _profile("base_swfoc")}
    assert mod.fallback_profile_for_exe("sweaw", profiles)["profileId"] == "base_sweaw"
    swfoc = mod.fallback_profile_for_exe("swfoc", profiles)
    assert swfoc["confidence"] == 0.65
    starwars = mod.fallback_profile_for_exe("starwarsg", profiles)
    assert starwars["confidence"] == 0.55
    assert mod.fallback_profile_for_exe("unknown", profiles) is None
    assert mod.fallback_profile_for_exe("sweaw", {}) is None


def test_recommend_profile_steam_exact() -> None:
    profiles = {"p": _profile("roe_p", steam_workshop_id="10")}
    rec = mod.recommend_profile(profiles, ["10"], None, "unknown")
    assert rec["confidence"] == 1.0
    assert rec["reasonCode"] == "steammod_exact_roe"


def test_recommend_profile_steam_required_only() -> None:
    profiles = {"p": _profile("aotr_p", metadata={"requiredWorkshopIds": "10"})}
    rec = mod.recommend_profile(profiles, ["10"], None, "unknown")
    assert rec["confidence"] == 0.97


def test_recommend_profile_modpath() -> None:
    profiles = {"p": _profile("roe_p", metadata={"localPathHints": "RoE"})}
    rec = mod.recommend_profile(profiles, [], "c:/roe_p/data", "unknown")
    assert rec["confidence"] == 0.95


def test_recommend_profile_fallback() -> None:
    profiles = {"base_swfoc": _profile("base_swfoc")}
    rec = mod.recommend_profile(profiles, [], None, "swfoc")
    assert rec["profileId"] == "base_swfoc"


def test_recommend_profile_modpath_no_match_falls_to_fallback() -> None:
    # modpath_norm is set but no profile matches it -> branch falls through to exe fallback.
    profiles = {"base_swfoc": _profile("base_swfoc")}
    rec = mod.recommend_profile(profiles, [], "c:/unknown/path", "swfoc")
    assert rec["profileId"] == "base_swfoc"


def test_dependency_hints_no_steam_id() -> None:
    # Profile without a steam_workshop_id -> the steam-id append branch is skipped.
    profiles = {"p": _profile("p", metadata={"requiredWorkshopIds": "20"})}
    hints = mod.dependency_hints(profiles, "p")
    assert hints["requiredWorkshopIds"] == ["20"]


def test_recommend_profile_unknown() -> None:
    rec = mod.recommend_profile({}, [], None, "unknown")
    assert rec["profileId"] is None and rec["confidence"] == 0.20


def test_dependency_hints_missing() -> None:
    hints = mod.dependency_hints({}, None)
    assert hints["requiredWorkshopIds"] == []


def test_dependency_hints_present() -> None:
    profiles = {
        "p": _profile(
            "p",
            steam_workshop_id="10",
            metadata={
                "requiredWorkshopIds": "20",
                "requiredMarkerFile": "marker.txt",
                "dependencySensitiveActions": "spawn",
                "localPathHints": "Mods/P",
                "localParentPathHints": "Mods",
                "profileAliases": "p1",
            },
        )
    }
    hints = mod.dependency_hints(profiles, "p")
    assert hints["requiredWorkshopIds"] == ["10", "20"]
    assert hints["requiredMarkerFile"] == "marker.txt"
    assert hints["profileAliases"] == ["p1"]


def test_detect_one_forced_profile() -> None:
    result = mod.detect_one(
        {"name": "case", "processName": "game.exe"},
        {},
        forced_profile_id="forced_x",
    )
    assert result["profileRecommendation"]["profileId"] == "forced_x"
    assert result["launchContext"]["source"] == "forced"


def test_detect_one_forced_workshop_ids() -> None:
    profiles = {"p": _profile("roe_p", steam_workshop_id="55")}
    result = mod.detect_one({"processName": "game.exe"}, profiles, forced_workshop_ids=["55"])
    assert result["launchContext"]["source"] == "forced"
    assert result["launchContext"]["steamModIds"] == ["55"]


def test_detect_one_detected() -> None:
    profiles = {"p": _profile("roe_p", steam_workshop_id="10")}
    result = mod.detect_one({"commandLine": "StarWarsG.exe STEAMMOD=10"}, profiles)
    assert result["launchContext"]["source"] == "detected"
    assert result["profileRecommendation"]["profileId"] == "roe_p"


def test_detect_one_none_command_line() -> None:
    result = mod.detect_one({"commandLine": None, "name": None}, {})
    assert result["input"]["commandLine"] is None
    assert result["input"]["name"] is None


# ---------- profile loading + main() ----------


def _write_profiles(root: Path, profiles: list[dict], manifest: dict | None = None) -> None:
    pdir = root / "profiles"
    pdir.mkdir(parents=True)
    for idx, p in enumerate(profiles):
        # Use a stable, always-valid filename (the id field may legitimately be
        # empty to exercise the "skip empty id" branch).
        name = p.get("id") or f"_anon_{idx}"
        (pdir / f"{name}.json").write_text(json.dumps(p), encoding="utf-8")
    if manifest is not None:
        (root / "manifest.json").write_text(json.dumps(manifest), encoding="utf-8")


def test_load_profiles_missing_dir(tmp_path: Path) -> None:
    with pytest.raises(FileNotFoundError):
        mod.load_profiles(tmp_path)


def test_load_profiles_basic(tmp_path: Path) -> None:
    _write_profiles(
        tmp_path,
        [
            {
                "id": "roe_p",
                "exeTarget": "swfoc",
                "steamWorkshopId": "10",
                "metadata": {"localPathHints": "RoE"},
            },
            {"id": "", "exeTarget": "x"},  # no id -> skipped
        ],
    )
    profiles = mod.load_profiles(tmp_path)
    assert "roe_p" in profiles
    assert profiles["roe_p"].steam_workshop_id == "10"


def test_load_profiles_with_manifest_filter(tmp_path: Path) -> None:
    _write_profiles(
        tmp_path,
        [
            {"id": "keep", "exeTarget": "x"},
            {"id": "drop", "exeTarget": "x"},
        ],
        manifest={"profiles": [{"id": "keep"}, {"id": ""}, "bad"]},
    )
    profiles = mod.load_profiles(tmp_path)
    assert "keep" in profiles and "drop" not in profiles


def test_load_profiles_manifest_not_list(tmp_path: Path) -> None:
    _write_profiles(
        tmp_path,
        [{"id": "p", "exeTarget": "x", "metadata": "notadict"}],
        manifest={"profiles": "notalist"},
    )
    profiles = mod.load_profiles(tmp_path)
    assert profiles["p"].metadata == {}


def test_load_profiles_or_none_error(tmp_path: Path, capsys) -> None:
    assert mod._load_profiles_or_none(str(tmp_path / "nope")) is None
    assert "profile-load-error" in capsys.readouterr().err


def test_load_cases_payload_ok(tmp_path: Path) -> None:
    f = tmp_path / "cases.json"
    f.write_text(json.dumps({"cases": [{"name": "c1"}]}), encoding="utf-8")
    payload = mod._load_cases_payload(str(f))
    assert payload is not None and len(payload["cases"]) == 1


def test_load_cases_payload_bad_json(tmp_path: Path, capsys) -> None:
    f = tmp_path / "cases.json"
    f.write_text("{bad", encoding="utf-8")
    assert mod._load_cases_payload(str(f)) is None
    assert "input-read-error" in capsys.readouterr().err


def test_load_cases_payload_no_cases(tmp_path: Path, capsys) -> None:
    f = tmp_path / "cases.json"
    f.write_text(json.dumps({"notcases": 1}), encoding="utf-8")
    assert mod._load_cases_payload(str(f)) is None
    assert "invalid-input" in capsys.readouterr().err


def test_emit_json_pretty_and_compact(capsys) -> None:
    mod._emit_json({"a": 1}, True)
    assert "\n" in capsys.readouterr().out
    mod._emit_json({"a": 1}, False)
    assert capsys.readouterr().out.strip() == '{"a":1}'


def test_main_profiles_load_fail(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setattr(
        "sys.argv", ["d.py", "--profile-root", str(tmp_path / "missing"), "--process-name", "g"]
    )
    assert mod.main() == 3


def test_main_single_process(tmp_path: Path, monkeypatch, capsys) -> None:
    root = tmp_path / "prof"
    _write_profiles(root, [{"id": "roe_p", "exeTarget": "swfoc", "steamWorkshopId": "10"}])
    monkeypatch.setattr(
        "sys.argv",
        ["d.py", "--profile-root", str(root), "--command-line", "g STEAMMOD=10", "--pretty"],
    )
    assert mod.main() == 0
    out = json.loads(capsys.readouterr().out)
    assert out["profileRecommendation"]["profileId"] == "roe_p"


def test_main_no_process_fields(tmp_path: Path, monkeypatch, capsys) -> None:
    root = tmp_path / "prof"
    _write_profiles(root, [{"id": "p", "exeTarget": "x"}])
    monkeypatch.setattr("sys.argv", ["d.py", "--profile-root", str(root)])
    assert mod.main() == 2
    assert "invalid-input" in capsys.readouterr().err


def test_main_from_json(tmp_path: Path, monkeypatch, capsys) -> None:
    root = tmp_path / "prof"
    _write_profiles(root, [{"id": "p", "exeTarget": "x"}])
    cases = tmp_path / "cases.json"
    cases.write_text(json.dumps({"cases": [{"name": "c", "commandLine": "g"}, "bad"]}), "utf-8")
    monkeypatch.setattr(
        "sys.argv",
        [
            "d.py",
            "--profile-root",
            str(root),
            "--from-process-json",
            str(cases),
            "--force-profile-id",
            "forced",
            "--force-workshop-ids",
            "1,2",
        ],
    )
    assert mod.main() == 0
    out = json.loads(capsys.readouterr().out)
    assert len(out["results"]) == 2


def test_main_from_json_invalid(tmp_path: Path, monkeypatch) -> None:
    root = tmp_path / "prof"
    _write_profiles(root, [{"id": "p", "exeTarget": "x"}])
    cases = tmp_path / "cases.json"
    cases.write_text("{bad", encoding="utf-8")
    monkeypatch.setattr(
        "sys.argv",
        ["d.py", "--profile-root", str(root), "--from-process-json", str(cases)],
    )
    assert mod.main() == 2
