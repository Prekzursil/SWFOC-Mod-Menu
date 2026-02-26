#!/usr/bin/env python3
"""Discover top Steam workshop mods for SWFOC and emit normalized top-mod artifacts."""

from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import sys
import urllib.parse
import urllib.request
from dataclasses import dataclass
from pathlib import Path
from typing import Any

STEAM_BROWSE_URL = "https://steamcommunity.com/workshop/browse/"
STEAM_DETAILS_URL = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/"
WORKSHOP_URL_TEMPLATE = "https://steamcommunity.com/sharedfiles/filedetails/?id={workshop_id}"
ALLOWED_FETCH_SCHEMES = {"https"}

PROMOTED_REQUIRED_CAPABILITIES = [
    "set_credits",
    "freeze_timer",
    "toggle_fog_reveal",
    "toggle_ai",
    "set_unit_cap",
    "toggle_instant_build_patch",
]

ID_PATTERN = re.compile(r"/sharedfiles/filedetails/\?id=(\d+)", re.IGNORECASE)
DEPENDENCY_PATTERN = re.compile(r"(?:STEAMMOD\s*=\s*|\?id=)(\d{4,})", re.IGNORECASE)
TOKEN_PATTERN = re.compile(r"[^a-z0-9]+")


@dataclass(frozen=True)
class DiscoverySource:
    url: str
    source_type: str


def utc_now_iso() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def ensure_allowed_fetch_url(url: str) -> None:
    parsed = urllib.parse.urlparse(url)
    if parsed.scheme.lower() not in ALLOWED_FETCH_SCHEMES or not parsed.netloc:
        raise ValueError(f"unsupported fetch url scheme: {url}")


def fetch_text(url: str, data: bytes | None = None) -> str:
    ensure_allowed_fetch_url(url)
    request = urllib.request.Request(
        url,
        data=data,
        headers={
            "User-Agent": "SWFOC-Mod-Discovery/1.0 (+https://github.com/Prekzursil/SWFOC-Mod-Menu)",
            "Accept": "application/json, text/html;q=0.9, */*;q=0.1",
        },
        method="POST" if data is not None else "GET",
    )
    opener = urllib.request.build_opener(urllib.request.HTTPSHandler())
    with opener.open(request, timeout=30) as response:
        return response.read().decode("utf-8", errors="replace")


def extract_ids_from_browse_html(html_text: str) -> list[str]:
    ids: list[str] = []
    seen: set[str] = set()
    for match in ID_PATTERN.finditer(html_text):
        workshop_id = match.group(1)
        if workshop_id in seen:
            continue
        seen.add(workshop_id)
        ids.append(workshop_id)
    return ids


def fetch_browse_ids(app_id: int, pages: int, browse_sort: str, section: str) -> tuple[list[str], list[DiscoverySource]]:
    collected: list[str] = []
    seen: set[str] = set()
    sources: list[DiscoverySource] = []

    for page in range(1, pages + 1):
        query = urllib.parse.urlencode(
            {
                "appid": str(app_id),
                "browsesort": browse_sort,
                "section": section,
                "p": str(page),
            }
        )
        url = f"{STEAM_BROWSE_URL}?{query}"
        html = fetch_text(url)
        sources.append(DiscoverySource(url=url, source_type="steam_workshop_browse"))

        for workshop_id in extract_ids_from_browse_html(html):
            if workshop_id in seen:
                continue
            seen.add(workshop_id)
            collected.append(workshop_id)

    return collected, sources


def chunked(values: list[str], chunk_size: int) -> list[list[str]]:
    return [values[index : index + chunk_size] for index in range(0, len(values), chunk_size)]


def fetch_published_file_details(workshop_ids: list[str]) -> dict[str, dict[str, Any]]:
    details_by_id: dict[str, dict[str, Any]] = {}
    for group in chunked(workshop_ids, 80):
        payload: dict[str, str] = {"itemcount": str(len(group))}
        for index, workshop_id in enumerate(group):
            payload[f"publishedfileids[{index}]"] = workshop_id

        encoded = urllib.parse.urlencode(payload).encode("utf-8")
        raw = fetch_text(STEAM_DETAILS_URL, data=encoded)
        parsed = json.loads(raw)
        details = parsed.get("response", {}).get("publishedfiledetails", [])
        for entry in details:
            if str(entry.get("result", 0)) != "1":
                continue
            details_by_id[str(entry.get("publishedfileid", ""))] = entry

    return details_by_id


def parse_dependency_ids(description: str, workshop_id: str) -> list[str]:
    dependencies: list[str] = []
    seen: set[str] = set()

    for match in DEPENDENCY_PATTERN.finditer(description or ""):
        candidate = match.group(1)
        if candidate == workshop_id:
            continue
        if candidate in seen:
            continue
        seen.add(candidate)
        dependencies.append(candidate)

    return dependencies


def normalize_tag(value: str) -> str:
    normalized = TOKEN_PATTERN.sub("_", (value or "").strip().lower()).strip("_")
    return normalized


def build_mod_path_hints(title: str, description: str) -> list[str]:
    hints: list[str] = []
    seen: set[str] = set()

    title_hint = normalize_tag(title)
    if title_hint:
        seen.add(title_hint)
        hints.append(title_hint)

    alias_tokens = [
        "aotr",
        "roe",
        "remake",
        "republic_at_war",
        "thrawns_revenge",
        "fall_of_the_republic",
    ]
    content = f"{title} {description}".lower()
    for alias in alias_tokens:
        if alias.replace("_", " ") in content or alias in content:
            if alias not in seen:
                seen.add(alias)
                hints.append(alias)

    return hints[:8]


def resolve_dependency_profile(workshop_id: str, dependencies: list[str]) -> tuple[str, float] | None:
    profile_by_dependency = {
        "1397421866": ("aotr_1397421866_swfoc", 0.98),
        "3447786229": ("roe_3447786229_swfoc", 0.98),
    }
    for dependency, profile in profile_by_dependency.items():
        if workshop_id == dependency or dependency in dependencies:
            return profile

    return None


def has_any_keyword(content: str, keywords: tuple[str, ...]) -> bool:
    return any(keyword in content for keyword in keywords)


def resolve_base_profile(workshop_id: str, title: str, description: str, dependencies: list[str]) -> tuple[str, float]:
    dependency_profile = resolve_dependency_profile(workshop_id, dependencies)
    if dependency_profile is not None:
        return dependency_profile

    content = f"{title} {description}".lower()
    if has_any_keyword(content, ("awakening of the rebellion", "aotr")):
        return "aotr_1397421866_swfoc", 0.90

    if has_any_keyword(content, ("roe", "rise of the empire")):
        return "roe_3447786229_swfoc", 0.85

    return "base_swfoc", 0.62


def resolve_risk_level(file_size: int, dependencies: list[str]) -> str:
    if file_size >= 5_000_000_000 or len(dependencies) >= 3:
        return "high"
    if file_size >= 1_000_000_000 or len(dependencies) >= 1:
        return "medium"
    return "low"


def to_iso_utc(epoch_seconds: int | str | None) -> str:
    value = int(epoch_seconds or 0)
    if value <= 0:
        return "1970-01-01T00:00:00Z"
    return dt.datetime.fromtimestamp(value, tz=dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def normalize_tags(raw_tags: Any) -> list[str]:
    if not isinstance(raw_tags, list):
        return []

    normalized_tags: list[str] = []
    for tag in raw_tags:
        value = tag.get("tag") if isinstance(tag, dict) else tag
        normalized = normalize_tag(str(value or ""))
        if normalized:
            normalized_tags.append(normalized)

    return sorted(set(normalized_tags))


def resolve_dependencies(source: dict[str, Any], description: str, workshop_id: str) -> list[str]:
    dependencies = source.get("parentDependencies")
    if not isinstance(dependencies, list):
        dependencies = parse_dependency_ids(description, workshop_id)

    return [str(dep) for dep in dependencies if str(dep).isdigit() and str(dep) != workshop_id]


def build_default_launch_hints(
    workshop_id: str,
    dependencies: list[str],
    title: str,
    description: str,
) -> dict[str, list[str]]:
    return {
        "steamModIds": [workshop_id] + dependencies,
        "modPathHints": build_mod_path_hints(title, description),
    }


def normalize_launch_hints(raw_hints: dict[str, Any]) -> dict[str, list[str]]:
    return {
        "steamModIds": [str(item) for item in raw_hints.get("steamModIds", []) if str(item).isdigit()],
        "modPathHints": [str(item).strip() for item in raw_hints.get("modPathHints", []) if str(item).strip()],
    }


def ensure_workshop_hint(launch_hints: dict[str, list[str]], workshop_id: str) -> dict[str, list[str]]:
    if workshop_id and workshop_id not in launch_hints["steamModIds"]:
        launch_hints["steamModIds"].insert(0, workshop_id)
    return launch_hints


def resolve_launch_hints(
    source: dict[str, Any],
    workshop_id: str,
    dependencies: list[str],
    title: str,
    description: str,
) -> dict[str, list[str]]:
    launch_hints = source.get("launchHints") if isinstance(source.get("launchHints"), dict) else None
    if launch_hints is None:
        normalized = build_default_launch_hints(workshop_id, dependencies, title, description)
    else:
        normalized = normalize_launch_hints(launch_hints)

    return ensure_workshop_hint(normalized, workshop_id)


def resolve_record_risk_level(source: dict[str, Any], dependencies: list[str]) -> str:
    file_size = int(source.get("file_size") or source.get("fileSize") or 0)
    risk_level = str(source.get("riskLevel") or resolve_risk_level(file_size, dependencies)).lower()
    if risk_level not in {"low", "medium", "high"}:
        return resolve_risk_level(file_size, dependencies)

    return risk_level


def resolve_time_updated_iso(source: dict[str, Any]) -> str:
    time_updated = source.get("timeUpdated")
    if isinstance(time_updated, str) and "T" in time_updated:
        return time_updated

    return to_iso_utc(source.get("time_updated") or source.get("timeUpdated") or source.get("timeUpdatedEpoch"))


def resolve_record_confidence(source: dict[str, Any], profile_confidence: float) -> float:
    confidence = source.get("confidence")
    if confidence is None:
        confidence = profile_confidence

    return max(0.0, min(1.0, float(confidence)))


def first_non_empty_string(*candidates: Any) -> str:
    for candidate in candidates:
        value = str(candidate or "").strip()
        if value:
            return value

    return ""


def resolve_workshop_id(source: dict[str, Any], fallback_workshop_id: str | None) -> str:
    return first_non_empty_string(
        source.get("workshopId"),
        source.get("publishedfileid"),
        fallback_workshop_id,
    )


def resolve_title(source: dict[str, Any]) -> str:
    return first_non_empty_string(source.get("title"), "unknown_mod")


def resolve_lifetime_subscriptions(source: dict[str, Any], subscriptions: int) -> int:
    lifetime_value = source.get("lifetimeSubscriptions")
    if lifetime_value is None:
        lifetime_value = source.get("lifetime_subscriptions")
    lifetime_subscriptions = int(lifetime_value or 0)
    return max(lifetime_subscriptions, subscriptions)


def normalize_top_mod_record(source: dict[str, Any], fallback_workshop_id: str | None = None) -> dict[str, Any]:
    workshop_id = resolve_workshop_id(source, fallback_workshop_id)
    title = resolve_title(source)
    description = str(source.get("description") or "")
    normalized_tags = normalize_tags(source.get("normalizedTags") or source.get("tags") or [])
    dependencies = resolve_dependencies(source, description, workshop_id)
    base_profile, profile_confidence = resolve_base_profile(workshop_id, title, description, dependencies)
    launch_hints = resolve_launch_hints(source, workshop_id, dependencies, title, description)
    risk_level = resolve_record_risk_level(source, dependencies)
    subscriptions = int(source.get("subscriptions") or 0)
    lifetime_subscriptions = resolve_lifetime_subscriptions(source, subscriptions)
    time_updated_iso = resolve_time_updated_iso(source)
    confidence = resolve_record_confidence(source, profile_confidence)

    return {
        "workshopId": workshop_id,
        "title": title,
        "url": str(source.get("url") or WORKSHOP_URL_TEMPLATE.format(workshop_id=workshop_id)),
        "subscriptions": subscriptions,
        "lifetimeSubscriptions": max(lifetime_subscriptions, subscriptions),
        "timeUpdated": time_updated_iso,
        "parentDependencies": dependencies,
        "launchHints": launch_hints,
        "candidateBaseProfile": str(source.get("candidateBaseProfile") or base_profile),
        "confidence": confidence,
        "riskLevel": risk_level,
        "normalizedTags": normalized_tags,
        "requiredCapabilities": PROMOTED_REQUIRED_CAPABILITIES,
    }


def collect_live_top_mods(app_id: int, limit: int, pages: int, browse_sort: str, section: str) -> tuple[list[dict[str, Any]], list[DiscoverySource]]:
    workshop_ids, sources = fetch_browse_ids(app_id, pages, browse_sort, section)
    if not workshop_ids:
        return [], sources

    details_by_id = fetch_published_file_details(workshop_ids)
    records: list[dict[str, Any]] = []
    for workshop_id in workshop_ids:
        details = details_by_id.get(workshop_id)
        if details is None:
            continue
        normalized = normalize_top_mod_record(details, fallback_workshop_id=workshop_id)
        records.append(normalized)
        if len(records) >= limit:
            break

    sources.append(DiscoverySource(url=STEAM_DETAILS_URL, source_type="steam_remote_storage_api"))
    return records, sources


def parse_fixture_payload(payload: Any) -> tuple[int, list[Any]]:
    app_id = 32470
    if isinstance(payload, dict):
        app_id = int(payload.get("appId") or app_id)
        top_mods = payload.get("topMods")
        entries = top_mods if isinstance(top_mods, list) else []
        return app_id, entries

    if isinstance(payload, list):
        return app_id, payload

    raise ValueError("Unsupported source-file shape. Expected JSON object or array.")


def collect_fixture_top_mods(source_file: Path, limit: int) -> tuple[list[dict[str, Any]], list[DiscoverySource], int]:
    payload = json.loads(source_file.read_text(encoding="utf-8"))
    app_id, entries = parse_fixture_payload(payload)

    normalized = [normalize_top_mod_record(item if isinstance(item, dict) else {}) for item in entries]
    normalized = [item for item in normalized if item.get("workshopId")]
    return normalized[:limit], [DiscoverySource(url=str(source_file), source_type="fixture_file")], app_id


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Discover and normalize top SWFOC workshop mods.")
    parser.add_argument("--output", required=True, help="Output JSON path")
    parser.add_argument("--limit", type=int, default=10, help="Max number of mods to emit")
    parser.add_argument("--appid", type=int, default=32470, help="Steam app id")
    parser.add_argument("--pages", type=int, default=2, help="Browse pages to scan in live mode")
    parser.add_argument("--browsesort", default="trend", help="Workshop browse sort key")
    parser.add_argument("--section", default="readytouseitems", help="Workshop browse section")
    parser.add_argument("--run-id", default="", help="Optional deterministic source run id")
    parser.add_argument("--source-file", default="", help="Optional fixture JSON path (no network)")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)

    run_id = args.run_id.strip() or f"mod-discovery-{dt.datetime.now(dt.timezone.utc).strftime('%Y%m%d-%H%M%S')}"

    if args.source_file:
        source_path = Path(args.source_file)
        if not source_path.exists():
            raise FileNotFoundError(f"source-file not found: {source_path}")
        top_mods, sources, app_id = collect_fixture_top_mods(source_path, max(args.limit, 0))
    else:
        top_mods, sources = collect_live_top_mods(
            app_id=args.appid,
            limit=max(args.limit, 0),
            pages=max(args.pages, 1),
            browse_sort=args.browsesort,
            section=args.section,
        )
        app_id = args.appid

    payload = {
        "schemaVersion": "1.0",
        "appId": int(app_id),
        "sourceRunId": run_id,
        "generatedAtUtc": utc_now_iso(),
        "retrievalTimestampUtc": utc_now_iso(),
        "rankingBasis": f"steam_workshop_browse:{args.browsesort}",
        "sources": [source.url for source in sources] or ["unknown"],
        "topMods": top_mods[: max(args.limit, 0)],
    }

    output_path.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    print(f"workshop top-mods json: {output_path}")
    print(f"discovered entries: {len(payload['topMods'])}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:  # pragma: no cover - command-line error path
        print(f"error: {exc}", file=sys.stderr)
        sys.exit(1)
