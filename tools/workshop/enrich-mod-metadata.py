#!/usr/bin/env python3
"""Enrich discovered top mods into generated profile seed records."""

from __future__ import annotations

import argparse
import datetime as dt
import json
import re
from pathlib import Path
from typing import Any


SCHEMA_VERSION = "1.0"


def utc_now_iso() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def clamp_confidence(value: Any) -> float:
    try:
        numeric = float(value)
    except (TypeError, ValueError):
        numeric = 0.5

    if numeric < 0.0:
        return 0.0
    if numeric > 1.0:
        return 1.0
    return round(numeric, 2)


def unique_ordered(values: list[str]) -> list[str]:
    seen: set[str] = set()
    out: list[str] = []
    for value in values:
        if value in seen:
            continue
        seen.add(value)
        out.append(value)
    return out


def infer_required_capabilities(
    candidate_base_profile: str,
    launch_hints: list[str],
    normalized_tags: list[str],
    parent_dependencies: list[str],
) -> list[str]:
    capabilities = ["set_credits", "freeze_timer", "toggle_fog_reveal", "toggle_ai"]

    if candidate_base_profile != "base_sweaw":
        capabilities.extend(["set_unit_cap", "toggle_instant_build_patch"])

    if candidate_base_profile == "aotr_1397421866_swfoc":
        capabilities.extend(["spawn_unit_helper", "set_hero_state_helper"])

    if candidate_base_profile == "roe_3447786229_swfoc":
        capabilities.extend(["spawn_unit_helper", "toggle_roe_respawn_helper"])

    if parent_dependencies:
        capabilities.append("spawn_unit_helper")

    tags = set(normalized_tags)
    hints = set(launch_hints)
    if "tactical" in tags or "tactical_profile" in hints:
        capabilities.append("set_selected_hp")

    if "galactic_campaign" in hints or "campaign" in tags:
        capabilities.append("set_hero_respawn_timer")

    return unique_ordered(capabilities)


def build_anchor_hints(
    title: str,
    normalized_tags: list[str],
    workshop_id: str,
    candidate_base_profile: str,
    parent_dependencies: list[str],
) -> list[str]:
    title_tokens = re.findall(r"[a-z0-9]+", title.lower())
    filtered_tokens = [token for token in title_tokens if len(token) >= 4]

    anchors: list[str] = []
    anchors.extend([f"title:{token}" for token in filtered_tokens[:4]])
    anchors.extend([f"tag:{tag}" for tag in normalized_tags[:4]])
    anchors.append(f"workshop:{workshop_id}")
    anchors.append(f"base:{candidate_base_profile}")
    anchors.extend([f"dep:{dep}" for dep in parent_dependencies[:3]])

    return unique_ordered(anchors)


def normalize_dependency_ids(raw_dependencies: Any) -> list[str]:
    deps: list[str] = []
    if isinstance(raw_dependencies, list):
        for item in raw_dependencies:
            dep = str(item or "").strip()
            if dep.isdigit():
                deps.append(dep)
    return unique_ordered(deps)


def normalize_text_list(raw_values: Any) -> list[str]:
    values: list[str] = []
    if isinstance(raw_values, list):
        for item in raw_values:
            value = str(item or "").strip()
            if value:
                values.append(value)
    return unique_ordered(values)


def ensure_risk_level(raw: Any) -> str:
    value = str(raw or "").strip().lower()
    if value in {"low", "medium", "high"}:
        return value
    return "medium"


def to_seed(mod: dict[str, Any], source_run_id: str) -> dict[str, Any] | None:
    workshop_id = str(mod.get("workshopId") or "").strip()
    if not workshop_id.isdigit():
        return None

    title = str(mod.get("title") or f"Workshop Mod {workshop_id}").strip()
    parent_dependencies = normalize_dependency_ids(mod.get("parentDependencies"))
    launch_hints = normalize_text_list(mod.get("launchHints"))
    normalized_tags = normalize_text_list(mod.get("normalizedTags"))

    candidate_base_profile = str(mod.get("candidateBaseProfile") or "base_swfoc").strip() or "base_swfoc"
    required_capabilities = infer_required_capabilities(
        candidate_base_profile=candidate_base_profile,
        launch_hints=launch_hints,
        normalized_tags=normalized_tags,
        parent_dependencies=parent_dependencies,
    )

    return {
        "workshopId": workshop_id,
        "title": title,
        "parentDependencies": parent_dependencies,
        "launchHints": launch_hints,
        "candidateBaseProfile": candidate_base_profile,
        "requiredCapabilities": required_capabilities,
        "anchorHints": build_anchor_hints(
            title=title,
            normalized_tags=normalized_tags,
            workshop_id=workshop_id,
            candidate_base_profile=candidate_base_profile,
            parent_dependencies=parent_dependencies,
        ),
        "riskLevel": ensure_risk_level(mod.get("riskLevel")),
        "confidence": clamp_confidence(mod.get("confidence")),
        "sourceRunId": source_run_id,
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Enrich workshop top mods into generated profile seed JSON")
    parser.add_argument("--input", required=True, help="Input top mods JSON path")
    parser.add_argument("--output", required=True, help="Output generated profile seeds JSON path")
    parser.add_argument("--source-run-id", required=True, help="Run id to stamp into each generated seed")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    input_path = Path(args.input)
    output_path = Path(args.output)

    with input_path.open("r", encoding="utf-8") as handle:
        payload = json.load(handle)

    top_mods = payload.get("topMods")
    if not isinstance(top_mods, list):
        raise ValueError("Input payload must include topMods[]")

    seeds = [to_seed(mod, args.source_run_id) for mod in top_mods if isinstance(mod, dict)]
    normalized_seeds = [seed for seed in seeds if seed is not None]

    output = {
        "schemaVersion": SCHEMA_VERSION,
        "generatedAtUtc": utc_now_iso(),
        "sourceRunId": args.source_run_id,
        "appId": payload.get("appId", 32470),
        "seeds": normalized_seeds,
    }

    output_path.parent.mkdir(parents=True, exist_ok=True)
    with output_path.open("w", encoding="utf-8") as handle:
        json.dump(output, handle, indent=2)
        handle.write("\n")

    print(f"wrote {len(normalized_seeds)} generated seed entries to {output_path}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
