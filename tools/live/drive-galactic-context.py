#!/usr/bin/env python3
"""Best-effort galactic context driver for live SWFOC validation.

The script has two modes:

1. Fixture mode (`--from-fixture-json`) used by deterministic smoke tests to
   validate save-selection and receipt classification logic.
2. Live mode (`--profile-root` + `--profile-id`) used by the PowerShell live
   validation orchestrator to:
     - select or materialize a deterministic galactic save
     - locate and foreground the active FoC window
     - optionally run a calibrated UI recipe
     - verify galactic telemetry/autoload evidence from `_LogFile.txt`

Live mode is intentionally fail-closed: if the script cannot select a save,
find a window, load a recipe, or observe galactic telemetry, it emits a
machine-readable blocked receipt and exits non-zero.
"""

from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import re
import shutil
import struct
import subprocess
import sys
import time
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Iterable

SCHEMA_VERSION = "1.0"
SAVE_EXTENSIONS = (".petroglyphfoc64save", ".petroglyphfocsave")
TELEMETRY_LINE = re.compile(
    r"SWFOC_TRAINER_TELEMETRY\s+timestamp=(?P<timestamp>\S+)\s+mode=(?P<mode>[A-Za-z0-9_]+)",
    flags=re.IGNORECASE,
)
WINDOW_TITLE_HINTS = ("empire at war", "forces of corruption", "star wars")
FIXTURE_STEMS = {
    "aotr_1397421866_swfoc": "swfoc_trainer_live_aotr_galactic",
    "roe_3447786229_swfoc": "swfoc_trainer_live_roe_galactic",
}


@dataclass(frozen=True)
class ProfileInfo:
    profile_id: str
    save_schema_id: str | None
    metadata: dict[str, str]


@dataclass(frozen=True)
class SaveInspection:
    file_magic: str
    campaign_mode: int | None
    size_bytes: int


def utc_now() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def normalize_token(value: str | None) -> str | None:
    if value is None:
        return None
    normalized = value.strip().strip('"').replace("\\", "/").lower()
    while "//" in normalized:
        normalized = normalized.replace("//", "/")
    return normalized or None


def parse_csv(metadata: dict[str, str], key: str) -> list[str]:
    raw = metadata.get(key, "")
    if not raw:
        return []
    return [token.strip() for token in raw.split(",") if token.strip()]


def load_profiles(profile_root: Path) -> dict[str, ProfileInfo]:
    profiles_dir = profile_root / "profiles"
    if not profiles_dir.exists():
        raise FileNotFoundError(f"Missing profiles directory: {profiles_dir}")

    profiles: dict[str, ProfileInfo] = {}
    for path in sorted(profiles_dir.glob("*.json")):
        data = json.loads(path.read_text(encoding="utf-8"))
        profile_id = str(data.get("id") or "").strip()
        if not profile_id:
            continue
        metadata_raw = data.get("metadata")
        metadata = metadata_raw if isinstance(metadata_raw, dict) else {}
        profiles[profile_id] = ProfileInfo(
            profile_id=profile_id,
            save_schema_id=str(data.get("saveSchemaId") or "").strip() or None,
            metadata={str(key): str(value) for key, value in metadata.items()},
        )
    return profiles


def load_profile(profile_root: Path, profile_id: str) -> ProfileInfo:
    profiles = load_profiles(profile_root)
    try:
        return profiles[profile_id]
    except KeyError as exc:
        raise KeyError(f"Unknown profile id: {profile_id}") from exc


def resolve_default_save_root() -> Path:
    home = Path(os.environ.get("USERPROFILE") or str(Path.home()))
    return home / "Saved Games" / "Petroglyph" / "Empire At War - Forces of Corruption" / "Save"


def resolve_fixture_stem(profile_id: str) -> str:
    fixture = FIXTURE_STEMS.get(profile_id)
    if fixture:
        return fixture
    normalized = profile_id.replace("/", "_").replace("\\", "_")
    return f"swfoc_trainer_live_{normalized}_galactic"


def resolve_profile_tokens(profile: ProfileInfo) -> list[str]:
    tokens: set[str] = {profile.profile_id.lower()}
    for key in ("localPathHints", "profileAliases", "requiredWorkshopIds"):
        for value in parse_csv(profile.metadata, key):
            normalized = normalize_token(value)
            if normalized:
                tokens.add(normalized)

    return sorted(tokens, key=len, reverse=True)


def load_schema(profile_root: Path, profile: ProfileInfo) -> dict[str, Any]:
    if not profile.save_schema_id:
        raise ValueError(f"Profile '{profile.profile_id}' does not define saveSchemaId.")
    schema_path = profile_root / "schemas" / f"{profile.save_schema_id}.json"
    if not schema_path.exists():
        raise FileNotFoundError(f"Missing schema file: {schema_path}")
    return json.loads(schema_path.read_text(encoding="utf-8"))


def resolve_campaign_mode_offset(schema: dict[str, Any]) -> int:
    for field in schema.get("fieldDefs", []):
        if str(field.get("id") or "").strip() == "campaign_mode":
            return int(field.get("offset"))
    raise KeyError("campaign_mode field not present in schema")


def inspect_real_save(path: Path, campaign_mode_offset: int) -> SaveInspection:
    data = path.read_bytes()
    file_magic = data[:8].decode("ascii", errors="ignore")
    campaign_mode: int | None = None
    if len(data) >= campaign_mode_offset + 4:
        campaign_mode = struct.unpack_from("<i", data, campaign_mode_offset)[0]
    return SaveInspection(file_magic=file_magic, campaign_mode=campaign_mode, size_bytes=len(data))


def inspect_fixture_save(entry: dict[str, Any]) -> SaveInspection:
    return SaveInspection(
        file_magic=str(entry.get("fileMagic") or "PGSAVE01"),
        campaign_mode=int(entry["campaignMode"]) if entry.get("campaignMode") is not None else None,
        size_bytes=int(entry.get("sizeBytes") or 0),
    )


def is_save_extension(path: Path) -> bool:
    return path.suffix.lower() in SAVE_EXTENSIONS


def is_galactic_campaign(campaign_mode: int | None) -> bool:
    return campaign_mode is not None and campaign_mode > 0


def is_valid_save_magic(file_magic: str) -> bool:
    normalized = (file_magic or "").upper()
    return normalized == "PGSAVE01" or normalized.startswith("RGMH")


def build_save_descriptor(profile: ProfileInfo, fixture_stem: str, path: Path, inspection: SaveInspection) -> dict[str, Any]:
    stem = path.stem.lower()
    tokens = resolve_profile_tokens(profile)
    token_hits = sorted({token for token in tokens if token and token in stem}, key=len, reverse=True)

    exact_fixture_match = stem == fixture_stem.lower()
    valid_magic = is_valid_save_magic(inspection.file_magic)
    galactic = is_galactic_campaign(inspection.campaign_mode)
    score = 0
    reason = ""
    if exact_fixture_match and valid_magic and galactic:
        score = 10000
        reason = "exact_fixture_name"
    elif valid_magic and galactic and token_hits:
        score = 500 + min(50, len(token_hits) * 10)
        if "campaign" in stem or "galactic" in stem:
            score += 15
        reason = "filename_profile_hint"

    return {
        "path": str(path),
        "name": path.name,
        "stem": path.stem,
        "fileMagic": inspection.file_magic,
        "campaignMode": inspection.campaign_mode,
        "sizeBytes": inspection.size_bytes,
        "tokenHits": token_hits,
        "exactFixtureMatch": exact_fixture_match,
        "validMagic": valid_magic,
        "galactic": galactic,
        "score": score,
        "reasonCode": reason,
    }


def build_selection_receipt(profile: ProfileInfo, save_root: Path, descriptor: dict[str, Any] | None, source: str | None, status: str, reason_code: str, fixture_stem: str, fixture_path: Path | None = None) -> dict[str, Any]:
    return {
        "profileId": profile.profile_id,
        "saveRoot": str(save_root),
        "fixtureStem": fixture_stem,
        "fixturePath": str(fixture_path) if fixture_path is not None else "",
        "saveSelection": None if descriptor is None else {
            "selectedSavePath": str(fixture_path) if fixture_path is not None else descriptor["path"],
            "selectedSaveName": fixture_path.name if fixture_path is not None else descriptor["name"],
            "source": source,
            "reasonCode": descriptor["reasonCode"],
            "campaignMode": descriptor["campaignMode"],
            "fileMagic": descriptor["fileMagic"],
            "tokenHits": descriptor["tokenHits"],
            **({
                "materializeSourcePath": descriptor["path"],
                "materializeSourceName": descriptor["name"],
            } if fixture_path is not None and source == "fixture_created_from_existing" else {}),
        },
        "result": {
            "status": status,
            "reasonCode": reason_code,
            "phaseReached": "save_selected" if status == "ready" else "save_selection_blocked",
        },
    }


def select_save_from_descriptors(profile: ProfileInfo, save_root: Path, descriptors: list[dict[str, Any]], materialize_fixture: bool, fixture_suffix: str = ".PetroglyphFoC64Save") -> dict[str, Any]:
    fixture_stem = resolve_fixture_stem(profile.profile_id)
    fixture_descriptor = next((item for item in descriptors if item["exactFixtureMatch"]), None)
    fixture_path = save_root / f"{fixture_stem}{fixture_suffix}"

    if fixture_descriptor is not None:
        if fixture_descriptor["validMagic"] and fixture_descriptor["galactic"]:
            return build_selection_receipt(profile, save_root, fixture_descriptor, "existing_fixture", "ready", "save_selected_existing_fixture", fixture_stem)
        return build_selection_receipt(profile, save_root, None, None, "blocked", "fixture_not_galactic", fixture_stem)

    candidates = [item for item in descriptors if item["score"] > 0]
    candidates.sort(key=lambda item: (-int(item["score"]), item["name"].lower()))
    if not candidates:
        return build_selection_receipt(profile, save_root, None, None, "blocked", "fixture_required", fixture_stem, fixture_path=fixture_path)

    best = candidates[0]
    if materialize_fixture:
        return build_selection_receipt(profile, save_root, best, "fixture_created_from_existing", "ready", "fixture_materialized_from_existing", fixture_stem, fixture_path=fixture_path)

    return build_selection_receipt(profile, save_root, best, "existing_compatible", "ready", "save_selected_existing_compatible", fixture_stem)


def enumerate_real_saves(profile_root: Path, profile: ProfileInfo, save_root: Path) -> list[dict[str, Any]]:
    schema = load_schema(profile_root, profile)
    campaign_mode_offset = resolve_campaign_mode_offset(schema)
    descriptors: list[dict[str, Any]] = []
    if not save_root.exists():
        return descriptors
    fixture_stem = resolve_fixture_stem(profile.profile_id)
    for path in sorted(save_root.iterdir(), key=lambda entry: entry.name.lower()):
        if not path.is_file() or not is_save_extension(path):
            continue
        inspection = inspect_real_save(path, campaign_mode_offset)
        descriptors.append(build_save_descriptor(profile, fixture_stem, path, inspection))
    return descriptors


def materialize_fixture_copy(selection: dict[str, Any]) -> dict[str, Any]:
    save_selection = selection.get("saveSelection")
    if selection["result"]["status"] != "ready" or save_selection is None:
        return selection
    if save_selection["source"] != "fixture_created_from_existing":
        return selection

    source_candidate = save_selection.get("materializeSourcePath") or save_selection["selectedSavePath"]
    source_path = Path(str(source_candidate))
    if source_path.name == Path(selection["fixturePath"]).name:
        return selection

    destination = Path(selection["fixturePath"])
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source_path, destination)
    save_selection["selectedSavePath"] = str(destination)
    save_selection["selectedSaveName"] = destination.name
    return selection


def load_fixture_cases(path: Path) -> list[dict[str, Any]]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    cases = payload.get("cases")
    if not isinstance(cases, list):
        raise ValueError("Fixture payload must contain a 'cases' array.")
    return cases


def run_fixture_cases(profile_root: Path, fixture_json: Path) -> dict[str, Any]:
    results: list[dict[str, Any]] = []
    for case in load_fixture_cases(fixture_json):
        profile_id = str(case.get("profileId") or "").strip()
        if not profile_id:
            raise ValueError("Fixture case missing profileId.")
        profile = load_profile(profile_root, profile_id)
        save_root = resolve_default_save_root()
        fixture_stem = resolve_fixture_stem(profile.profile_id)
        descriptors = [
            build_save_descriptor(profile, fixture_stem, Path(str(entry["name"])), inspect_fixture_save(entry))
            for entry in case.get("saves", [])
        ]
        selection = select_save_from_descriptors(profile, save_root, descriptors, materialize_fixture=bool(case.get("materializeFixture", False)))
        results.append({"caseName": str(case.get("name") or profile_id), **selection})

    return {"schemaVersion": SCHEMA_VERSION, "generatedAtUtc": utc_now(), "results": results}


def parse_key_value_tokens(tokens: Iterable[str]) -> dict[str, str]:
    values: dict[str, str] = {}
    for token in tokens:
        if "=" not in token:
            continue
        key, value = token.split("=", 1)
        if key and value:
            values[key] = value
    return values


def resolve_log_path(process_path: str) -> Path | None:
    base = Path(process_path).resolve()
    process_dir = base.parent
    candidates = (
        process_dir / "_LogFile.txt",
        process_dir / "LogFile.txt",
        process_dir / "corruption" / "LogFile.txt",
        process_dir.parent / "corruption" / "LogFile.txt",
    )
    existing = [candidate for candidate in candidates if candidate.exists()]
    if not existing:
        return None
    existing.sort(key=lambda item: item.stat().st_mtime, reverse=True)
    return existing[0]


def read_recent_nonblank_lines(path: Path, limit: int = 512) -> list[str]:
    lines = [line.strip() for line in path.read_text(encoding="utf-8", errors="ignore").splitlines() if line.strip()]
    return lines[-limit:]


def read_latest_mode(log_path: Path) -> tuple[str | None, str | None]:
    for line in reversed(read_recent_nonblank_lines(log_path, limit=512)):
        match = TELEMETRY_LINE.search(line)
        if not match:
            continue
        return match.group("mode"), line
    return None, None


def read_latest_autoload(log_path: Path, profile_id: str) -> tuple[bool | None, str | None]:
    for line in reversed(read_recent_nonblank_lines(log_path, limit=512)):
        tokens = line.split()
        if len(tokens) < 2:
            continue
        status_token = tokens[0]
        if status_token.upper() not in {"SWFOC_TRAINER_HELPER_AUTOLOAD_READY", "SWFOC_TRAINER_HELPER_AUTOLOAD_FAILED"}:
            continue
        values = parse_key_value_tokens(tokens[1:])
        if values.get("profile", "").lower() != profile_id.lower():
            continue
        return status_token.upper().endswith("_READY"), line
    return None, None


def wait_for_galactic_telemetry(process_path: str, profile_id: str, timeout_seconds: int, require_helper_autoload: bool) -> dict[str, Any]:
    log_path = resolve_log_path(process_path)
    if log_path is None:
        return {"status": "blocked", "reasonCode": "telemetry_log_missing", "phaseReached": "recipe_executed"}

    deadline = time.time() + max(timeout_seconds, 1)
    last_mode: str | None = None
    last_mode_line: str | None = None
    last_autoload_line: str | None = None
    while time.time() < deadline:
        mode, raw_mode_line = read_latest_mode(log_path)
        if mode:
            last_mode = mode
            last_mode_line = raw_mode_line

        autoload_ready, raw_autoload_line = read_latest_autoload(log_path, profile_id)
        if raw_autoload_line:
            last_autoload_line = raw_autoload_line

        if mode and mode.lower() == "galactic":
            if not require_helper_autoload:
                return {"status": "succeeded", "reasonCode": "galactic_mode_verified", "phaseReached": "galactic_mode_verified", "telemetryLogPath": str(log_path), "telemetryLine": last_mode_line}
            if autoload_ready is True:
                return {"status": "succeeded", "reasonCode": "galactic_mode_and_helper_autoload_verified", "phaseReached": "galactic_mode_verified", "telemetryLogPath": str(log_path), "telemetryLine": last_mode_line, "autoloadLine": last_autoload_line}
        time.sleep(1.0)

    blocked = {"status": "blocked", "reasonCode": "telemetry_not_galactic", "phaseReached": "recipe_executed", "telemetryLogPath": str(log_path)}
    if last_mode is not None:
        blocked["lastObservedMode"] = last_mode
        blocked["telemetryLine"] = last_mode_line
    if last_autoload_line is not None:
        blocked["autoloadLine"] = last_autoload_line
    if require_helper_autoload and last_mode and last_mode.lower() == "galactic":
        blocked["reasonCode"] = "helper_autoload_not_ready"
    return blocked


def import_optional(module_name: str) -> Any | None:
    try:
        return __import__(module_name, fromlist=["*"])
    except Exception:
        return None


def query_process_path(process_id: int) -> str | None:
    command = ["powershell", "-NoProfile", "-Command", f"(Get-Process -Id {process_id} -ErrorAction Stop).Path"]
    try:
        completed = subprocess.run(command, capture_output=True, text=True, check=False, timeout=10)
    except Exception:
        return None
    if completed.returncode != 0:
        return None
    output = completed.stdout.strip()
    return output or None


def resolve_recipe_path(args: argparse.Namespace, profile_id: str) -> Path | None:
    if args.recipe_path:
        candidate = Path(args.recipe_path)
        return candidate if candidate.exists() else None

    default_recipe = Path(args.profile_root) / "live" / "recipes" / f"{profile_id}.json"
    if default_recipe.exists():
        return default_recipe

    repo_default = Path(args.profile_root).parent.parent / "tools" / "live" / "recipes" / f"{profile_id}.json"
    if repo_default.exists():
        return repo_default
    return None


def find_game_window(process_id: int | None) -> tuple[Any | None, str]:
    pywinauto = import_optional("pywinauto")
    if pywinauto is None:
        return None, "pywinauto_missing"
    try:
        desktop = pywinauto.Desktop(backend="uia")
        windows = desktop.windows()
    except Exception:
        return None, "pywinauto_window_enumeration_failed"

    best = None
    best_title = ""
    for window in windows:
        try:
            title = window.window_text() or ""
            if not title:
                continue
            if not window.is_visible():
                continue
            if process_id is not None and int(window.process_id()) != process_id:
                continue
            if process_id is None and not any(hint in title.lower() for hint in WINDOW_TITLE_HINTS):
                continue
            if len(title) > len(best_title):
                best = window
                best_title = title
        except Exception:
            continue

    if best is None:
        return None, "game_window_not_found"
    return best, "game_window_found"


def focus_window(window: Any) -> str:
    try:
        if hasattr(window, "restore"):
            window.restore()
    except Exception:
        pass
    try:
        wrapper = window.wrapper_object() if hasattr(window, "wrapper_object") else window
        wrapper.set_focus()
        return "window_focused"
    except Exception:
        return "window_focus_failed"


def load_recipe(recipe_path: Path) -> dict[str, Any]:
    payload = json.loads(recipe_path.read_text(encoding="utf-8"))
    actions = payload.get("actions")
    if not isinstance(actions, list):
        raise ValueError(f"Recipe '{recipe_path}' must contain an actions array.")
    return payload


def _window_rect(window: Any) -> tuple[int, int, int, int]:
    rect = window.rectangle()
    return int(rect.left), int(rect.top), int(rect.right), int(rect.bottom)


def _send_key_sequence(sequence: str) -> None:
    keyboard = import_optional("pywinauto.keyboard")
    if keyboard is None:
        raise RuntimeError("pywinauto.keyboard is not available")
    keyboard.send_keys(sequence, pause=0.05)


def run_recipe_actions(window: Any, recipe: dict[str, Any], context: dict[str, str]) -> None:
    pyautogui = import_optional("pyautogui")
    for action in recipe.get("actions", []):
        action_type = str(action.get("type") or "").strip()
        if not action_type:
            raise ValueError("Recipe action missing type.")

        if action_type == "sleep":
            time.sleep(float(action.get("seconds") or 0))
            continue
        if action_type == "focus_window":
            focus_result = focus_window(window)
            if focus_result != "window_focused":
                raise RuntimeError(focus_result)
            time.sleep(float(action.get("afterSeconds") or 0))
            continue
        if action_type == "press":
            key = str(action.get("key") or "").strip()
            count = int(action.get("count") or 1)
            if not key:
                raise ValueError("press action requires key")
            for _ in range(max(count, 1)):
                _send_key_sequence(f"{{{key}}}")
                time.sleep(float(action.get("intervalSeconds") or 0.15))
            continue
        if action_type == "send_keys":
            keys = str(action.get("keys") or "")
            if not keys:
                raise ValueError("send_keys action requires keys")
            _send_key_sequence(keys.format(**context))
            time.sleep(float(action.get("afterSeconds") or 0.15))
            continue
        if action_type in {"click_ratio", "double_click_ratio"}:
            if pyautogui is None:
                raise RuntimeError("pyautogui_missing")
            left, top, right, bottom = _window_rect(window)
            x_ratio = float(action.get("x") or 0)
            y_ratio = float(action.get("y") or 0)
            x = int(left + ((right - left) * x_ratio))
            y = int(top + ((bottom - top) * y_ratio))
            clicks = 2 if action_type == "double_click_ratio" else int(action.get("clicks") or 1)
            pyautogui.click(x=x, y=y, clicks=clicks, interval=float(action.get("intervalSeconds") or 0.1))
            time.sleep(float(action.get("afterSeconds") or 0.2))
            continue
        raise ValueError(f"Unsupported recipe action type: {action_type}")


def build_live_receipt(profile: ProfileInfo, selection: dict[str, Any], automation: dict[str, Any], recipe_path: Path | None, process_id: int | None, process_path: str | None, window_title: str) -> dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "generatedAtUtc": utc_now(),
        "profileId": profile.profile_id,
        "saveRoot": selection["saveRoot"],
        "fixtureStem": selection["fixtureStem"],
        "fixturePath": selection["fixturePath"],
        "saveSelection": selection["saveSelection"],
        "automation": {"recipePath": str(recipe_path) if recipe_path is not None else "", "processId": process_id, "processPath": process_path or "", "windowTitle": window_title, **automation},
        "result": automation,
    }


def run_live_mode(args: argparse.Namespace) -> dict[str, Any]:
    profile_root = Path(args.profile_root).resolve()
    profile = load_profile(profile_root, args.profile_id)
    save_root = Path(args.save_root).resolve() if args.save_root else resolve_default_save_root()
    descriptors = enumerate_real_saves(profile_root, profile, save_root)
    selection = select_save_from_descriptors(profile, save_root, descriptors, materialize_fixture=args.materialize_fixture)
    if args.materialize_fixture and selection["result"]["status"] == "ready":
        selection = materialize_fixture_copy(selection)

    if selection["result"]["status"] != "ready":
        return {"schemaVersion": SCHEMA_VERSION, "generatedAtUtc": utc_now(), **selection}
    if args.selection_only:
        return {"schemaVersion": SCHEMA_VERSION, "generatedAtUtc": utc_now(), **selection}

    process_id = int(args.process_id) if args.process_id else None
    window, window_reason = find_game_window(process_id)
    if window is None:
        return build_live_receipt(profile, selection, {"status": "blocked", "reasonCode": window_reason, "phaseReached": "window_detection"}, None, process_id, None, "")

    window_title = ""
    try:
        window_title = window.window_text() or ""
    except Exception:
        window_title = ""

    focus_result = focus_window(window)
    if focus_result != "window_focused":
        return build_live_receipt(profile, selection, {"status": "blocked", "reasonCode": focus_result, "phaseReached": "window_focus"}, None, process_id, None, window_title)

    recipe_path = resolve_recipe_path(args, profile.profile_id)
    if recipe_path is None:
        return build_live_receipt(profile, selection, {"status": "blocked", "reasonCode": "automation_recipe_missing", "phaseReached": "window_focused"}, None, process_id, None, window_title)

    process_id = process_id or int(window.process_id())
    process_path = query_process_path(process_id)
    if not process_path:
        return build_live_receipt(profile, selection, {"status": "blocked", "reasonCode": "process_path_unavailable", "phaseReached": "window_focused"}, recipe_path, process_id, None, window_title)

    recipe = load_recipe(recipe_path)
    save_selection = selection["saveSelection"] or {}
    action_context = {
        "fixture_path": selection.get("fixturePath") or "",
        "selected_save_path": save_selection.get("selectedSavePath") or "",
        "selected_save_name": save_selection.get("selectedSaveName") or "",
        "profile_id": profile.profile_id,
    }

    try:
        run_recipe_actions(window, recipe, action_context)
    except Exception as exc:
        return build_live_receipt(profile, selection, {"status": "blocked", "reasonCode": "automation_recipe_failed", "phaseReached": "recipe_execution", "message": str(exc)}, recipe_path, process_id, process_path, window_title)

    verification = wait_for_galactic_telemetry(process_path, profile.profile_id, int(args.verify_timeout_seconds), bool(args.require_helper_autoload))
    return build_live_receipt(profile, selection, verification, recipe_path, process_id, process_path, window_title)


def write_receipt(payload: dict[str, Any], receipt_path: Path | None, pretty: bool) -> None:
    text = json.dumps(payload, indent=2 if pretty else None, sort_keys=False)
    if receipt_path is not None:
        receipt_path.parent.mkdir(parents=True, exist_ok=True)
        receipt_path.write_text(text + ("\n" if not text.endswith("\n") else ""), encoding="utf-8")
    sys.stdout.write(text)
    if not text.endswith("\n"):
        sys.stdout.write("\n")


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Drive SWFOC into a galactic validation context.")
    parser.add_argument("--profile-root", help="Profile root (for example profiles/default)")
    parser.add_argument("--profile-id", help="Target profile id")
    parser.add_argument("--save-root", default="", help="Save root override")
    parser.add_argument("--recipe-path", default="", help="Optional calibrated UI automation recipe JSON")
    parser.add_argument("--receipt-path", default="", help="Optional JSON receipt output path")
    parser.add_argument("--process-id", type=int, default=0, help="Optional target process id")
    parser.add_argument("--selection-only", action="store_true", help="Only resolve/materialize a compatible save; do not drive UI")
    parser.add_argument("--materialize-fixture", action="store_true", help="Copy a compatible existing save to the deterministic fixture name when needed")
    parser.add_argument("--verify-timeout-seconds", type=int, default=90, help="Seconds to wait for galactic telemetry after UI automation")
    parser.add_argument("--require-helper-autoload", action="store_true", help="Require helper autoload ready proof in addition to galactic mode")
    parser.add_argument("--from-fixture-json", default="", help="Run deterministic fixture cases instead of live automation")
    parser.add_argument("--pretty", action="store_true", help="Pretty-print JSON output")
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    if args.from_fixture_json:
        if not args.profile_root:
            parser.error("--profile-root is required with --from-fixture-json")
        payload = run_fixture_cases(Path(args.profile_root).resolve(), Path(args.from_fixture_json).resolve())
        write_receipt(payload, Path(args.receipt_path).resolve() if args.receipt_path else None, args.pretty)
        return 0

    if not args.profile_root or not args.profile_id:
        parser.error("--profile-root and --profile-id are required in live mode")
    payload = run_live_mode(args)
    write_receipt(payload, Path(args.receipt_path).resolve() if args.receipt_path else None, args.pretty)
    result = payload.get("result", {})
    return 0 if result.get("status") in {"ready", "succeeded"} else 2


if __name__ == "__main__":
    sys.exit(main())
