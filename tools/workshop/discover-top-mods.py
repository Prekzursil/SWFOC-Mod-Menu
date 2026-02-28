#!/usr/bin/env python3
"""Discover top SWFOC workshop mods with live and fixture-backed modes."""

from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import sys
from pathlib import Path
from typing import Any
from urllib import parse, request


SCHEMA_VERSION = "1.0"
DEFAULT_APP_ID = 32470
DETAILS_API_URL = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/"


def utc_now_iso() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def as_int(value: Any, default: int = 0) -> int:
    try:
        return int(value)
    except (TypeError, ValueError):
        return default


def clamp_confidence(value: float) -> float:
    if value < 0.0:
        return 0.0
    if value > 1.0:
        return 1.0
    return round(value, 2)


def normalize_tag(raw: str) -> str:
    value = raw.strip().lower()
    value = re.sub(r"[^a-z0-9]+", "_", value)
    value = re.sub(r"_+", "_", value).strip("_")
    return value


def unique_ordered(values: list[str]) -> list[str]:
    seen: set[str] = set()
    out: list[str] = []
    for value in values:
        if value in seen:
            continue
        seen.add(value)
        out.append(value)
    return out


def parse_timestamp_to_iso(value: Any) -> str:
    if isinstance(value, str):
        stripped = value.strip()
        if re.match(r"^\d{4}-\d{2}-\d{2}T", stripped):
            return stripped
        if stripped.isdigit():
            value = int(stripped)

    if isinstance(value, (int, float)):
        if int(value) <= 0:
            return utc_now_iso()
        return dt.datetime.fromtimestamp(int(value), tz=dt.timezone.utc).replace(microsecond=0).isoformat().replace(
            "+00:00", "Z"
        )

    return utc_now_iso()


def parse_dependency_ids(raw_items: Any) -> list[str]:
    deps: list[str] = []
    if isinstance(raw_items, list):
        for item in raw_items:
            if isinstance(item, dict):
                raw_id = item.get("publishedfileid") or item.get("id") or item.get("workshopId")
            else:
                raw_id = item
            dep = str(raw_id or "").strip()
            if dep.isdigit():
                deps.append(dep)
    return unique_ordered(deps)


def parse_normalized_tags(raw_tags: Any) -> list[str]:
    tags: list[str] = []
    if isinstance(raw_tags, list):
        for item in raw_tags:
            raw_tag = item.get("tag") if isinstance(item, dict) else item
            if raw_tag is None:
                continue
            normalized = normalize_tag(str(raw_tag))
            if normalized:
                tags.append(normalized)
    return unique_ordered(tags)


def infer_candidate_base_profile(title: str, tags: list[str], parent_dependencies: list[str]) -> str:
    title_lc = title.lower()
    tag_set = set(tags)
    dep_set = set(parent_dependencies)

    if "3447786229" in dep_set:
        return "roe_3447786229_swfoc"

    if "1397421866" in dep_set:
        return "aotr_1397421866_swfoc"

    if "order 66" in title_lc or "roe" in title_lc:
        return "roe_3447786229_swfoc"

    if "awakening" in title_lc or "aotr" in title_lc:
        return "aotr_1397421866_swfoc"

    if "eaw" in tag_set and "foc" not in tag_set:
        return "base_sweaw"

    return "base_swfoc"


def infer_launch_hints(base_profile: str, parent_dependencies: list[str], tags: list[str]) -> list[str]:
    hints = ["workshop"]
    hints.append("launch_sweaw" if base_profile == "base_sweaw" else "launch_swfoc")

    tag_set = set(tags)
    if parent_dependencies:
        hints.append("requires_parent_mod")
    if "campaign" in tag_set:
        hints.append("galactic_campaign")
    if "tactical" in tag_set:
        hints.append("tactical_profile")
    if "multiplayer" in tag_set:
        hints.append("manual_smoke_required")

    return unique_ordered(hints)


def infer_risk_level(tags: list[str], parent_dependencies: list[str], subscriptions: int) -> str:
    tag_set = set(tags)
    if tag_set.intersection({"beta", "experimental", "unstable"}):
        return "high"
    if parent_dependencies:
        return "medium"
    if subscriptions < 5000 and "multiplayer" in tag_set:
        return "medium"
    return "low"


def infer_confidence(
    title: str,
    tags: list[str],
    parent_dependencies: list[str],
    base_profile: str,
    subscriptions: int,
) -> float:
    score = 0.55
    if tags:
        score += 0.1
    if parent_dependencies:
        score += 0.1
    if subscriptions >= 10000:
        score += 0.1
    if base_profile != "base_swfoc":
        score += 0.08

    title_lc = title.lower()
    if any(keyword in title_lc for keyword in ("aotr", "awakening", "roe", "order 66")):
        score += 0.12

    return clamp_confidence(score)


def canonical_mod_url(workshop_id: str) -> str:
    return f"https://steamcommunity.com/sharedfiles/filedetails/?id={workshop_id}"


def normalize_mod_from_detail(detail: dict[str, Any]) -> dict[str, Any] | None:  # NOSONAR
    workshop_id = str(detail.get("publishedfileid") or detail.get("workshopId") or detail.get("id") or "").strip()
    if not workshop_id.isdigit():
        return None

    title = str(detail.get("title") or f"Workshop Mod {workshop_id}").strip()
    subscriptions = as_int(detail.get("subscriptions"))
    lifetime_subscriptions = max(subscriptions, as_int(detail.get("lifetime_subscriptions")))
    parent_dependencies = parse_dependency_ids(detail.get("children") or detail.get("parentDependencies"))
    normalized_tags = parse_normalized_tags(detail.get("tags") or detail.get("normalizedTags"))
    candidate_base_profile = infer_candidate_base_profile(title, normalized_tags, parent_dependencies)

    launch_hints_raw = detail.get("launchHints")
    if isinstance(launch_hints_raw, list) and launch_hints_raw:
        launch_hints = unique_ordered([str(item).strip() for item in launch_hints_raw if str(item).strip()])
    else:
        launch_hints = infer_launch_hints(candidate_base_profile, parent_dependencies, normalized_tags)

    risk_level = str(detail.get("riskLevel") or "").strip().lower()
    if risk_level not in {"low", "medium", "high"}:
        risk_level = infer_risk_level(normalized_tags, parent_dependencies, subscriptions)

    raw_confidence = detail.get("confidence")
    if raw_confidence is None:
        confidence = infer_confidence(title, normalized_tags, parent_dependencies, candidate_base_profile, subscriptions)
    else:
        try:
            confidence = clamp_confidence(float(raw_confidence))
        except (TypeError, ValueError):
            confidence = infer_confidence(title, normalized_tags, parent_dependencies, candidate_base_profile, subscriptions)

    return {
        "workshopId": workshop_id,
        "title": title,
        "url": str(detail.get("url") or canonical_mod_url(workshop_id)),
        "subscriptions": subscriptions,
        "lifetimeSubscriptions": lifetime_subscriptions,
        "timeUpdated": parse_timestamp_to_iso(detail.get("time_updated") or detail.get("timeUpdated")),
        "parentDependencies": parent_dependencies,
        "launchHints": launch_hints,
        "candidateBaseProfile": candidate_base_profile,
        "confidence": confidence,
        "riskLevel": risk_level,
        "normalizedTags": normalized_tags,
    }


def scrape_workshop_ids(app_id: int, pages: int, timeout_sec: float) -> tuple[list[str], list[dict[str, str]]]:
    pattern = re.compile(r"sharedfiles/filedetails/\?id=(\d+)")
    workshop_ids: list[str] = []
    sources: list[dict[str, str]] = []

    for page in range(1, pages + 1):
        browse_url = (
            "https://steamcommunity.com/workshop/browse/"
            f"?appid={app_id}&browsesort=trend&section=readytouseitems&actualsort=trend&p={page}"
        )
        sources.append({"type": "workshop_browse", "uri": browse_url})
        req = request.Request(browse_url, headers={"User-Agent": "swfoc-discovery/1.0"})
        try:
            with request.urlopen(req, timeout=timeout_sec) as response:
                html = response.read().decode("utf-8", errors="ignore")
            for match in pattern.findall(html):
                if match not in workshop_ids:
                    workshop_ids.append(match)
        except Exception as exc:  # noqa: BLE001
            print(f"warning: failed to scrape {browse_url}: {exc}", file=sys.stderr)

    return workshop_ids, sources


def fetch_published_file_details(workshop_ids: list[str], timeout_sec: float) -> list[dict[str, Any]]:
    all_details: list[dict[str, Any]] = []

    for start in range(0, len(workshop_ids), 100):
        batch = workshop_ids[start : start + 100]
        payload: dict[str, str] = {"itemcount": str(len(batch))}
        for index, workshop_id in enumerate(batch):
            payload[f"publishedfileids[{index}]"] = workshop_id

        body = parse.urlencode(payload).encode("utf-8")
        req = request.Request(
            DETAILS_API_URL,
            data=body,
            headers={"Content-Type": "application/x-www-form-urlencoded", "User-Agent": "swfoc-discovery/1.0"},
            method="POST",
        )

        try:
            with request.urlopen(req, timeout=timeout_sec) as response:
                raw_payload = json.loads(response.read().decode("utf-8"))
            details = raw_payload.get("response", {}).get("publishedfiledetails", [])
            if isinstance(details, list):
                all_details.extend([item for item in details if isinstance(item, dict)])
        except Exception as exc:  # noqa: BLE001
            print(f"warning: failed to fetch file details for batch starting at {start}: {exc}", file=sys.stderr)

    return all_details


def sort_mods(top_mods: list[dict[str, Any]], ranking_basis: str) -> list[dict[str, Any]]:
    if ranking_basis == "lifetime_subscriptions_desc":
        return sorted(
            top_mods,
            key=lambda mod: (mod["lifetimeSubscriptions"], mod["subscriptions"], mod["timeUpdated"]),
            reverse=True,
        )

    return sorted(
        top_mods,
        key=lambda mod: (mod["subscriptions"], mod["lifetimeSubscriptions"], mod["timeUpdated"]),
        reverse=True,
    )


def build_output(
    app_id: int,
    ranking_basis: str,
    sources: list[dict[str, str]],
    top_mods: list[dict[str, Any]],
    generated_at_utc: str,
    retrieval_timestamp_utc: str,
) -> dict[str, Any]:
    return {
        "schemaVersion": SCHEMA_VERSION,
        "generatedAtUtc": generated_at_utc,
        "retrievalTimestampUtc": retrieval_timestamp_utc,
        "rankingBasis": ranking_basis,
        "sources": sources,
        "appId": app_id,
        "topMods": top_mods,
    }


def load_source_payload(path: Path) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    with path.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)

    if isinstance(payload, dict) and isinstance(payload.get("topMods"), list):
        mods = [normalize_mod_from_detail(item) for item in payload["topMods"] if isinstance(item, dict)]
        return [item for item in mods if item is not None], payload

    if isinstance(payload, dict) and isinstance(payload.get("response", {}).get("publishedfiledetails"), list):
        details = payload["response"]["publishedfiledetails"]
        mods = [normalize_mod_from_detail(item) for item in details if isinstance(item, dict)]
        return [item for item in mods if item is not None], payload

    if isinstance(payload, list):
        mods = [normalize_mod_from_detail(item) for item in payload if isinstance(item, dict)]
        return [item for item in mods if item is not None], {}

    raise ValueError(f"Unsupported source payload shape in {path}")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Discover top SWFOC workshop mods")
    parser.add_argument("--app-id", type=int, default=DEFAULT_APP_ID, help="Steam app id (default: 32470)")
    parser.add_argument("--limit", type=int, default=25, help="Maximum number of mods in output")
    parser.add_argument("--output", required=True, help="Output JSON path")
    parser.add_argument(
        "--ranking-basis",
        default="subscriptions_desc",
        choices=("subscriptions_desc", "lifetime_subscriptions_desc"),
        help="Ranking strategy for ordering topMods",
    )
    parser.add_argument("--source-file", help="Optional fixture JSON path for deterministic mode")
    parser.add_argument("--pages", type=int, default=2, help="Browse pages to scrape in live mode")
    parser.add_argument("--timeout", type=float, default=20.0, help="Network timeout in seconds")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    output_path = Path(args.output)

    if args.limit <= 0:
        raise ValueError("--limit must be greater than zero")

    if args.source_file:
        source_path = Path(args.source_file)
        top_mods, original_payload = load_source_payload(source_path)
        top_mods = sort_mods(top_mods, args.ranking_basis)[: args.limit]

        generated_at_utc = str(original_payload.get("generatedAtUtc") or utc_now_iso())
        retrieval_timestamp_utc = str(original_payload.get("retrievalTimestampUtc") or generated_at_utc)

        sources = list(original_payload.get("sources", [])) if isinstance(original_payload, dict) else []
        sources.append({"type": "fixture", "uri": str(source_path)})

        output = build_output(
            app_id=as_int(original_payload.get("appId"), args.app_id),
            ranking_basis=str(original_payload.get("rankingBasis") or args.ranking_basis),
            sources=sources,
            top_mods=top_mods,
            generated_at_utc=generated_at_utc,
            retrieval_timestamp_utc=retrieval_timestamp_utc,
        )
    else:
        workshop_ids, browse_sources = scrape_workshop_ids(args.app_id, args.pages, args.timeout)
        if not workshop_ids:
            raise RuntimeError("No workshop IDs discovered from browse pages")

        details = fetch_published_file_details(workshop_ids, args.timeout)
        top_mods = [normalize_mod_from_detail(detail) for detail in details]
        normalized_mods = [item for item in top_mods if item is not None]
        ranked_mods = sort_mods(normalized_mods, args.ranking_basis)[: args.limit]

        timestamp = utc_now_iso()
        sources = browse_sources + [{"type": "get_published_file_details", "uri": DETAILS_API_URL}]
        output = build_output(
            app_id=args.app_id,
            ranking_basis=args.ranking_basis,
            sources=sources,
            top_mods=ranked_mods,
            generated_at_utc=timestamp,
            retrieval_timestamp_utc=timestamp,
        )

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", encoding="utf-8") as handle:
        json.dump(output, handle, indent=2)
        handle.write("\n")

    print(f"wrote {len(output['topMods'])} top mod entries to {output_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
