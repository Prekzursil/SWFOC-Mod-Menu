#!/usr/bin/env python3
"""Detect SWFOC launch context and emit normalized recommendation JSON.

Examples:
  python tools/detect-launch-context.py --process-name StarWarsG \
    --process-path "D:/.../corruption/StarWarsG.exe" \
    --command-line "StarWarsG.exe STEAMMOD=3447786229" --pretty
  python tools/detect-launch-context.py --from-process-json \
    tools/fixtures/launch_context_cases.json --pretty
"""

from __future__ import annotations

import argparse
import datetime as dt
import glob
import json
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


EXE_HINT_MATCHERS: tuple[tuple[str, tuple[str, ...]], ...] = (
    ("sweaw", ("sweaw", "sweaw.exe")),
    ("swfoc", ("swfoc", "swfoc.exe")),
    ("starwarsg", ("starwarsg", "starwarsg.exe")),
)


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


def parse_forced_workshop_ids(raw: str | None) -> list[str]:
    if not raw:
        return []
    ids: set[str] = set()
    for token in raw.split(","):
        value = token.strip()
        if value:
            ids.add(value)
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

    manifest_profile_ids: set[str] | None = None
    manifest_path = profile_root / "manifest.json"
    if manifest_path.exists():
        with open(manifest_path, "r", encoding="utf-8") as f:
            manifest_payload = json.load(f)
        manifest_profiles = manifest_payload.get("profiles") if isinstance(manifest_payload, dict) else None
        if isinstance(manifest_profiles, list):
            ids: set[str] = set()
            for item in manifest_profiles:
                if not isinstance(item, dict):
                    continue
                profile_id = str(item.get("id") or "").strip()
                if profile_id:
                    ids.add(profile_id)
            manifest_profile_ids = ids

    out: dict[str, ProfileInfo] = {}
    for json_file in sorted(glob.glob(str(profiles_dir / "*.json"))):
        with open(json_file, "r", encoding="utf-8") as f:
            data = json.load(f)
        profile_id = str(data.get("id", "")).strip()
        if not profile_id:
            continue
        if manifest_profile_ids is not None and profile_id not in manifest_profile_ids:
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


def _text_or_empty(value: str | None) -> str:
    return (value or "").lower()


def _contains_any_token(haystack: str, needles: tuple[str, ...]) -> bool:
    return any(needle in haystack for needle in needles)


def _matches_exe_hint(fields: tuple[str, str, str], needles: tuple[str, ...]) -> bool:
    return any(_contains_any_token(field, needles) for field in fields)


def detect_exe_hint(process_name: str | None, process_path: str | None, command_line: str | None) -> str:
    fields = (
        _text_or_empty(process_name),
        _text_or_empty(process_path),
        _text_or_empty(command_line),
    )
    for exe_hint, needles in EXE_HINT_MATCHERS:
        if _matches_exe_hint(fields, needles):
            return exe_hint
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


def profile_priority_key(profile: ProfileInfo) -> tuple[bool, bool, str]:
    return (profile_sort_priority(profile.profile_id), profile.profile_id)


def profile_sort_priority(profile_id: str) -> int:
    pid = profile_id.lower()
    if "roe_" in pid:
        return 0
    if "aotr_" in pid:
        return 1
    return 2


def required_workshop_ids(profile: ProfileInfo) -> list[str]:
    ids: list[str] = []
    if profile.steam_workshop_id:
        ids.append(profile.steam_workshop_id)
    ids.extend(parse_csv(profile.metadata, "requiredWorkshopIds"))
    ids.extend(parse_csv(profile.metadata, "requiredWorkshopId"))
    return sorted(set(ids))


def score_workshop_match(profile: ProfileInfo, steam_ids: set[str]) -> int:
    score = 0
    if profile.steam_workshop_id and profile.steam_workshop_id in steam_ids:
        score = max(score, 1000)

    required_ids = required_workshop_ids(profile)
    if not required_ids:
        return score

    overlap = sum(1 for required_id in required_ids if required_id in steam_ids)
    if overlap == len(required_ids):
        return max(score, 900 + len(required_ids))
    if overlap > 0:
        return max(score, 700 + overlap)
    return score


def steam_profile_match(profiles: dict[str, ProfileInfo], steam_ids: list[str]) -> ProfileInfo | None:
    if not steam_ids:
        return None

    steam_set = set(steam_ids)
    best_profile: ProfileInfo | None = None
    best_score = 0
    best_required_count = -1
    for profile in profiles.values():
        score = score_workshop_match(profile, steam_set)
        if score <= 0:
            continue
        required_count = len(required_workshop_ids(profile))
        if (
            best_profile is None
            or score > best_score
            or (score == best_score and required_count > best_required_count)
            or (score == best_score and required_count == best_required_count and profile_priority_key(profile) < profile_priority_key(best_profile))
        ):
            best_profile = profile
            best_score = score
            best_required_count = required_count

    return best_profile


def best_modpath_match(profiles: dict[str, ProfileInfo], modpath_norm: str) -> ProfileInfo | None:
    hint_matches: list[tuple[int, ProfileInfo]] = []
    for profile in profiles.values():
        hints = gather_hints(profile)
        score = 0
        for hint in hints:
            if hint and hint in modpath_norm:
                score = max(score, len(hint))
        if score > 0:
            hint_matches.append((score, profile))

    if not hint_matches:
        return None

    hint_matches.sort(key=lambda pair: (-pair[0],) + profile_priority_key(pair[1]))
    return hint_matches[0][1]


def fallback_profile_for_exe(exe_hint: str, profiles: dict[str, ProfileInfo]) -> dict[str, Any] | None:
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

    return None


def recommend_profile(
    profiles: dict[str, ProfileInfo],
    steam_ids: list[str],
    modpath_norm: str | None,
    exe_hint: str,
) -> dict[str, Any]:
    # 1) Exact workshop-id match.
    best_steam_match = steam_profile_match(profiles, steam_ids)
    if best_steam_match:
        confidence = 1.0 if best_steam_match.steam_workshop_id and best_steam_match.steam_workshop_id in set(steam_ids) else 0.97
        return {
            "profileId": best_steam_match.profile_id,
            "reasonCode": reason_code_for_profile(best_steam_match.profile_id, "steam"),
            "confidence": confidence,
        }

    # 2) MODPATH hint match from profile metadata.
    if modpath_norm:
        best = best_modpath_match(profiles, modpath_norm)
        if best:
            return {
                "profileId": best.profile_id,
                "reasonCode": reason_code_for_profile(best.profile_id, "modpath"),
                "confidence": 0.95,
            }

    # 3) Exe fallback.
    fallback = fallback_profile_for_exe(exe_hint, profiles)
    if fallback:
        return fallback

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


def detect_one(
    process_input: dict[str, Any],
    profiles: dict[str, ProfileInfo],
    forced_workshop_ids: list[str] | None = None,
    forced_profile_id: str | None = None,
) -> dict[str, Any]:
    case_name = process_input.get("name")
    case_name = str(case_name) if case_name is not None else None
    process_name = str(process_input.get("processName", "") or "")
    process_path = str(process_input.get("processPath", "") or "")
    command_line = process_input.get("commandLine")
    command_line = str(command_line) if command_line is not None else None

    steam_ids = parse_steammod_ids(command_line)
    forced_ids = sorted(set(forced_workshop_ids or []))
    forced_profile = forced_profile_id.strip() if forced_profile_id and forced_profile_id.strip() else None
    modpath_raw = parse_modpath(command_line)
    modpath_norm = normalize_token(modpath_raw)
    exe_hint = detect_exe_hint(process_name, process_path, command_line)
    source = "detected"
    if not steam_ids and not modpath_norm and (forced_ids or forced_profile):
        source = "forced"
        if forced_ids:
            steam_ids = forced_ids

    launch_kind = infer_launch_kind(steam_ids, modpath_norm, exe_hint)
    if source == "forced" and forced_profile:
        recommendation = {
            "profileId": forced_profile,
            "reasonCode": "forced_profile_id",
            "confidence": 1.0,
        }
    else:
        recommendation = recommend_profile(profiles, steam_ids, modpath_norm, exe_hint)

    launch_context = {
        "launchKind": launch_kind,
        "commandLineAvailable": bool(command_line and command_line.strip()),
        "steamModIds": steam_ids,
        "modPathRaw": modpath_raw,
        "modPathNormalized": modpath_norm,
        "detectedVia": "script_input",
        "source": source,
        "forcedWorkshopIds": forced_ids,
        "forcedProfileId": forced_profile,
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


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Detect SWFOC launch context and profile recommendation")
    parser.add_argument("--command-line", dest="command_line", default=None)
    parser.add_argument("--process-name", dest="process_name", default=None)
    parser.add_argument("--process-path", dest="process_path", default=None)
    parser.add_argument("--profile-root", default="profiles/default")
    parser.add_argument("--from-process-json", dest="from_process_json", default=None)
    parser.add_argument("--force-workshop-ids", dest="force_workshop_ids", default=None)
    parser.add_argument("--force-profile-id", dest="force_profile_id", default=None)
    parser.add_argument("--pretty", action="store_true")
    return parser.parse_args()


def _load_profiles_or_none(profile_root: str) -> dict[str, ProfileInfo] | None:
    try:
        return load_profiles(Path(profile_root))
    except Exception as exc:
        print(f"profile-load-error: {exc}", file=sys.stderr)
        return None


def _load_cases_payload(input_path: str) -> dict[str, Any] | None:
    try:
        with open(input_path, "r", encoding="utf-8") as f:
            payload = json.load(f)
    except Exception as exc:
        print(f"input-read-error: {exc}", file=sys.stderr)
        return None

    cases = payload.get("cases") if isinstance(payload, dict) else None
    if not isinstance(cases, list):
        print("invalid-input: expected JSON object with 'cases' array", file=sys.stderr)
        return None
    return payload


def _build_multi_case_output(
    payload: dict[str, Any],
    profiles: dict[str, ProfileInfo],
    forced_workshop_ids: list[str],
    forced_profile_id: str | None,
) -> dict[str, Any]:
    cases = payload.get("cases", [])
    results = [
        detect_one(
            case if isinstance(case, dict) else {},
            profiles,
            forced_workshop_ids=forced_workshop_ids,
            forced_profile_id=forced_profile_id,
        )
        for case in cases
    ]
    return {
        "schemaVersion": SCHEMA_VERSION,
        "generatedAtUtc": dt.datetime.now(dt.timezone.utc).isoformat(),
        "results": results,
    }


def _single_process_input(args: argparse.Namespace) -> dict[str, str | None]:
    return {
        "processName": args.process_name,
        "processPath": args.process_path,
        "commandLine": args.command_line,
    }


def _emit_json(output: Any, pretty: bool) -> None:
    if pretty:
        print(json.dumps(output, indent=2, ensure_ascii=True))
    else:
        print(json.dumps(output, separators=(",", ":"), ensure_ascii=True))


def main() -> int:
    args = _parse_args()
    profiles = _load_profiles_or_none(args.profile_root)
    if profiles is None:
        return 3

    forced_workshop_ids = parse_forced_workshop_ids(args.force_workshop_ids)
    forced_profile_id = args.force_profile_id.strip() if args.force_profile_id and args.force_profile_id.strip() else None

    if args.from_process_json:
        payload = _load_cases_payload(args.from_process_json)
        if payload is None:
            return 2

        output: Any = _build_multi_case_output(payload, profiles, forced_workshop_ids, forced_profile_id)
    else:
        process_input = _single_process_input(args)
        if not any(process_input.values()):
            print("invalid-input: provide --from-process-json or process fields", file=sys.stderr)
            return 2

        output = detect_one(
            process_input,
            profiles,
            forced_workshop_ids=forced_workshop_ids,
            forced_profile_id=forced_profile_id,
        )

    _emit_json(output, args.pretty)
    return 0


if __name__ == "__main__":
    sys.exit(main())
