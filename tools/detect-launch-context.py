#!/usr/bin/env python3
"""Detect SWFOC launch context and emit normalized recommendation JSON.

Examples:
  python tools/detect-launch-context.py --process-name StarWarsG --process-path "D:/.../corruption/StarWarsG.exe" --command-line "StarWarsG.exe STEAMMOD=3447786229" --pretty
  python tools/detect-launch-context.py --from-process-json tools/fixtures/launch_context_cases.json --pretty
"""

from __future__ import annotations

import argparse
import datetime as dt
import glob
import json
import os
import re
import sys
from dataclasses import dataclass
from pathlib import Path
from typing import Any

SCHEMA_VERSION = "1.0"


@dataclass
class ProfileInfo:
    profile_id: str
    exe_target: str
    steam_workshop_id: str | None
    metadata: dict[str, str]


def normalize_token(value: str | None) -> str | None:
    if not value:
        return None
    out = value.strip().strip('"').replace("\\", "/")
    while "//" in out:
        out = out.replace("//", "/")
    return out.lower()


def parse_steammod_ids(command_line: str | None) -> list[str]:
    if not command_line:
        return []
    ids = set(re.findall(r"steammod\s*=\s*(\d+)", command_line, flags=re.IGNORECASE))
    return sorted(ids)


def parse_modpath(command_line: str | None) -> str | None:
    if not command_line:
        return None
    match = re.search(r'modpath\s*=\s*(?:"([^"]+)"|([^\s]+))', command_line, flags=re.IGNORECASE)
    if not match:
        return None
    value = match.group(1) or match.group(2)
    if not value:
        return None
    return value.strip().strip('"')


def parse_csv(metadata: dict[str, str], key: str) -> list[str]:
    raw = metadata.get(key, "")
    if not raw:
        return []
    return [x.strip() for x in raw.split(",") if x.strip()]


def load_profiles(profile_root: Path) -> dict[str, ProfileInfo]:
    profiles_dir = profile_root / "profiles"
    if not profiles_dir.exists():
        raise FileNotFoundError(f"Missing profiles directory: {profiles_dir}")

    out: dict[str, ProfileInfo] = {}
    for json_file in sorted(glob.glob(str(profiles_dir / "*.json"))):
        with open(json_file, "r", encoding="utf-8") as f:
            data = json.load(f)
        profile_id = str(data.get("id", "")).strip()
        if not profile_id:
            continue
        metadata_raw = data.get("metadata")
        metadata = metadata_raw if isinstance(metadata_raw, dict) else {}
        out[profile_id] = ProfileInfo(
            profile_id=profile_id,
            exe_target=str(data.get("exeTarget", "Unknown")),
            steam_workshop_id=(str(data["steamWorkshopId"]) if data.get("steamWorkshopId") else None),
            metadata={str(k): str(v) for k, v in metadata.items()},
        )
    return out


def infer_launch_kind(steam_ids: list[str], modpath_norm: str | None, exe_hint: str) -> str:
    if steam_ids and modpath_norm:
        return "Mixed"
    if steam_ids:
        return "Workshop"
    if modpath_norm:
        return "LocalModPath"
    if exe_hint in {"sweaw", "swfoc", "starwarsg"}:
        return "BaseGame"
    return "Unknown"


def detect_exe_hint(process_name: str | None, process_path: str | None, command_line: str | None) -> str:
    name = (process_name or "").lower()
    path = (process_path or "").lower()
    cmd = (command_line or "").lower()
    if "sweaw" in name or "sweaw.exe" in path or "sweaw.exe" in cmd:
        return "sweaw"
    if "swfoc" in name or "swfoc.exe" in path or "swfoc.exe" in cmd:
        return "swfoc"
    if "starwarsg" in name or "starwarsg.exe" in path or "starwarsg.exe" in cmd:
        return "starwarsg"
    return "unknown"


def gather_hints(profile: ProfileInfo) -> list[str]:
    hints: set[str] = set()
    hints.add(profile.profile_id.lower())
    if profile.steam_workshop_id:
        hints.add(profile.steam_workshop_id)
    for key in ("localPathHints", "profileAliases"):
        for value in parse_csv(profile.metadata, key):
            norm = normalize_token(value)
            if norm:
                hints.add(norm)
    return sorted(hints)


def reason_code_for_profile(profile_id: str, source: str) -> str:
    pid = profile_id.lower()
    if source == "steam":
        if "roe_" in pid:
            return "steammod_exact_roe"
        if "aotr_" in pid:
            return "steammod_exact_aotr"
        return "steammod_exact_profile"
    if source == "modpath":
        if "roe_" in pid:
            return "modpath_hint_roe"
        if "aotr_" in pid:
            return "modpath_hint_aotr"
        return "modpath_hint_profile"
    return "unknown"


def recommend_profile(
    profiles: dict[str, ProfileInfo],
    steam_ids: list[str],
    modpath_norm: str | None,
    exe_hint: str,
) -> dict[str, Any]:
    # 1) Exact workshop-id match.
    steam_matches: list[ProfileInfo] = []
    for profile in profiles.values():
        if profile.steam_workshop_id and profile.steam_workshop_id in steam_ids:
            steam_matches.append(profile)

    if steam_matches:
        steam_matches.sort(key=lambda p: ("roe_" not in p.profile_id.lower(), "aotr_" not in p.profile_id.lower(), p.profile_id))
        best = steam_matches[0]
        return {
            "profileId": best.profile_id,
            "reasonCode": reason_code_for_profile(best.profile_id, "steam"),
            "confidence": 1.0,
        }

    # 2) MODPATH hint match from profile metadata.
    if modpath_norm:
        hint_matches: list[tuple[int, ProfileInfo]] = []
        for profile in profiles.values():
            hints = gather_hints(profile)
            score = 0
            for hint in hints:
                if hint and hint in modpath_norm:
                    score = max(score, len(hint))
            if score > 0:
                hint_matches.append((score, profile))

        if hint_matches:
            hint_matches.sort(key=lambda pair: (-pair[0], "roe_" not in pair[1].profile_id.lower(), "aotr_" not in pair[1].profile_id.lower(), pair[1].profile_id))
            best = hint_matches[0][1]
            return {
                "profileId": best.profile_id,
                "reasonCode": reason_code_for_profile(best.profile_id, "modpath"),
                "confidence": 0.95,
            }

    # 3) Exe fallback.
    if exe_hint == "sweaw" and "base_sweaw" in profiles:
        return {
            "profileId": "base_sweaw",
            "reasonCode": "exe_target_sweaw",
            "confidence": 0.80,
        }

    if exe_hint in {"swfoc", "starwarsg"} and "base_swfoc" in profiles:
        return {
            "profileId": "base_swfoc",
            "reasonCode": "foc_safe_starwarsg_fallback",
            "confidence": 0.55 if exe_hint == "starwarsg" else 0.65,
        }

    return {
        "profileId": None,
        "reasonCode": "unknown",
        "confidence": 0.20,
    }


def dependency_hints(profiles: dict[str, ProfileInfo], profile_id: str | None) -> dict[str, Any]:
    if not profile_id or profile_id not in profiles:
        return {
            "requiredWorkshopIds": [],
            "requiredMarkerFile": None,
            "dependencySensitiveActions": [],
            "localPathHints": [],
            "localParentPathHints": [],
            "profileAliases": [],
        }

    profile = profiles[profile_id]
    required_ids = []
    if profile.steam_workshop_id:
        required_ids.append(profile.steam_workshop_id)
    required_ids.extend(parse_csv(profile.metadata, "requiredWorkshopIds"))
    required_ids.extend(parse_csv(profile.metadata, "requiredWorkshopId"))

    required_ids = sorted(set(required_ids))

    return {
        "requiredWorkshopIds": required_ids,
        "requiredMarkerFile": profile.metadata.get("requiredMarkerFile"),
        "dependencySensitiveActions": parse_csv(profile.metadata, "dependencySensitiveActions"),
        "localPathHints": parse_csv(profile.metadata, "localPathHints"),
        "localParentPathHints": parse_csv(profile.metadata, "localParentPathHints"),
        "profileAliases": parse_csv(profile.metadata, "profileAliases"),
    }


def detect_one(process_input: dict[str, Any], profiles: dict[str, ProfileInfo]) -> dict[str, Any]:
    case_name = process_input.get("name")
    case_name = str(case_name) if case_name is not None else None
    process_name = str(process_input.get("processName", "") or "")
    process_path = str(process_input.get("processPath", "") or "")
    command_line = process_input.get("commandLine")
    command_line = str(command_line) if command_line is not None else None

    steam_ids = parse_steammod_ids(command_line)
    modpath_raw = parse_modpath(command_line)
    modpath_norm = normalize_token(modpath_raw)
    exe_hint = detect_exe_hint(process_name, process_path, command_line)
    launch_kind = infer_launch_kind(steam_ids, modpath_norm, exe_hint)
    recommendation = recommend_profile(profiles, steam_ids, modpath_norm, exe_hint)

    launch_context = {
        "launchKind": launch_kind,
        "commandLineAvailable": bool(command_line and command_line.strip()),
        "steamModIds": steam_ids,
        "modPathRaw": modpath_raw,
        "modPathNormalized": modpath_norm,
        "detectedVia": "script_input",
    }

    return {
        "schemaVersion": SCHEMA_VERSION,
        "generatedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "input": {
            "name": case_name,
            "processName": process_name,
            "processPath": process_path,
            "commandLine": command_line,
        },
        "launchContext": launch_context,
        "profileRecommendation": recommendation,
        "dependencyHints": dependency_hints(profiles, recommendation.get("profileId")),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Detect SWFOC launch context and profile recommendation")
    parser.add_argument("--command-line", dest="command_line", default=None)
    parser.add_argument("--process-name", dest="process_name", default=None)
    parser.add_argument("--process-path", dest="process_path", default=None)
    parser.add_argument("--profile-root", default="profiles/default")
    parser.add_argument("--from-process-json", dest="from_process_json", default=None)
    parser.add_argument("--pretty", action="store_true")
    args = parser.parse_args()

    try:
        profiles = load_profiles(Path(args.profile_root))
    except Exception as exc:
        print(f"profile-load-error: {exc}", file=sys.stderr)
        return 3

    if args.from_process_json:
        try:
            with open(args.from_process_json, "r", encoding="utf-8") as f:
                payload = json.load(f)
        except Exception as exc:
            print(f"input-read-error: {exc}", file=sys.stderr)
            return 2

        cases = payload.get("cases") if isinstance(payload, dict) else None
        if not isinstance(cases, list):
            print("invalid-input: expected JSON object with 'cases' array", file=sys.stderr)
            return 2

        results = [detect_one(case if isinstance(case, dict) else {}, profiles) for case in cases]
        output: Any = {
            "schemaVersion": SCHEMA_VERSION,
            "generatedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
            "results": results,
        }
    else:
        process_input = {
            "processName": args.process_name,
            "processPath": args.process_path,
            "commandLine": args.command_line,
        }

        if not any(process_input.values()):
            print("invalid-input: provide --from-process-json or process fields", file=sys.stderr)
            return 2

        output = detect_one(process_input, profiles)

    if args.pretty:
        print(json.dumps(output, indent=2, ensure_ascii=True))
    else:
        print(json.dumps(output, separators=(",", ":"), ensure_ascii=True))

    return 0


if __name__ == "__main__":
    sys.exit(main())
