#!/usr/bin/env python3
"""Enrich workshop discovery output into generated profile seeds."""

from __future__ import annotations

import argparse
import datetime as dt
import json
import sys
from pathlib import Path
from typing import Any

DEFAULT_REQUIRED_CAPABILITIES = [
    "set_credits",
    "freeze_timer",
    "toggle_fog_reveal",
    "toggle_ai",
    "set_unit_cap",
    "toggle_instant_build_patch",
]

DEFAULT_ANCHOR_HINTS = {
    "set_credits": ["credits"],
    "freeze_timer": ["game_timer_freeze"],
    "toggle_fog_reveal": ["fog_reveal"],
    "toggle_ai": ["ai_enabled"],
    "set_unit_cap": ["unit_cap"],
    "toggle_instant_build_patch": ["instant_build_patch"],
}


def utc_now_iso() -> str:
    return dt.datetime.now(dt.timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def normalize_seed(top_mod: dict[str, Any], source_run_id: str) -> dict[str, Any]:
    workshop_id = str(top_mod.get("workshopId") or "").strip()
    launch_hints = top_mod.get("launchHints") if isinstance(top_mod.get("launchHints"), dict) else {}
    steam_mod_ids = [str(item) for item in launch_hints.get("steamModIds", []) if str(item).isdigit()]
    if workshop_id and workshop_id not in steam_mod_ids:
        steam_mod_ids.insert(0, workshop_id)

    mod_path_hints = [str(item).strip() for item in launch_hints.get("modPathHints", []) if str(item).strip()]

    anchor_hints = top_mod.get("anchorHints")
    if not isinstance(anchor_hints, dict) or len(anchor_hints) == 0:
        anchor_hints = DEFAULT_ANCHOR_HINTS

    required_capabilities = top_mod.get("requiredCapabilities")
    if not isinstance(required_capabilities, list) or len(required_capabilities) == 0:
        required_capabilities = DEFAULT_REQUIRED_CAPABILITIES

    confidence = float(top_mod.get("confidence", 0.5))
    confidence = max(0.0, min(1.0, confidence))

    risk_level = str(top_mod.get("riskLevel") or "medium").lower()
    if risk_level not in {"low", "medium", "high"}:
        risk_level = "medium"

    dependencies = [
        str(dep) for dep in top_mod.get("parentDependencies", []) if str(dep).isdigit() and str(dep) != workshop_id
    ]

    return {
        "workshopId": workshop_id,
        "title": str(top_mod.get("title") or f"workshop_{workshop_id}"),
        "parentDependencies": dependencies,
        "launchHints": {
            "steamModIds": steam_mod_ids,
            "modPathHints": mod_path_hints,
        },
        "candidateBaseProfile": str(top_mod.get("candidateBaseProfile") or "base_swfoc"),
        "requiredCapabilities": [str(cap) for cap in required_capabilities if str(cap).strip()],
        "anchorHints": {
            str(key): [str(value) for value in values if str(value).strip()]
            for key, values in anchor_hints.items()
            if isinstance(values, list)
        },
        "riskLevel": risk_level,
        "confidence": confidence,
        "sourceRunId": source_run_id,
    }


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Convert workshop top-mods output into generated profile seed artifacts.")
    parser.add_argument("--input", required=True, help="Input workshop top-mods JSON")
    parser.add_argument("--output", required=True, help="Output generated-profile-seeds JSON")
    parser.add_argument("--source-run-id", default="", help="Override source run id")
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    input_path = Path(args.input)
    if not input_path.exists():
        raise FileNotFoundError(f"input file not found: {input_path}")

    payload = json.loads(input_path.read_text(encoding="utf-8"))
    if not isinstance(payload, dict):
        raise ValueError("input must be a JSON object")

    source_run_id = args.source_run_id.strip() or str(payload.get("sourceRunId") or "mod-discovery-unknown")

    top_mods = payload.get("topMods")
    if not isinstance(top_mods, list):
        raise ValueError("input payload must contain a topMods array")

    seeds = [normalize_seed(item, source_run_id) for item in top_mods if isinstance(item, dict)]
    seeds = [seed for seed in seeds if seed.get("workshopId")]

    output_payload = {
        "schemaVersion": "1.0",
        "sourceRunId": source_run_id,
        "generatedAtUtc": utc_now_iso(),
        "seedCount": len(seeds),
        "seeds": seeds,
    }

    output_path = Path(args.output)
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(output_payload, indent=2), encoding="utf-8")

    print(f"generated profile seeds json: {output_path}")
    print(f"seed count: {len(seeds)}")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except Exception as exc:  # pragma: no cover - command-line error path
        print(f"error: {exc}", file=sys.stderr)
        sys.exit(1)
