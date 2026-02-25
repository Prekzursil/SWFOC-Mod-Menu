#!/usr/bin/env python3
"""
Normalize raw Ghidra headless symbol export into schema-backed symbol pack + summary.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from dataclasses import dataclass
from datetime import datetime, timezone
from pathlib import Path
from typing import Dict, List, Tuple


SCHEMA_VERSION = "1.0"

DEFAULT_FEATURE_REQUIREMENTS: Dict[str, List[str]] = {
    "set_credits": ["credits_value"],
    "freeze_timer": ["freeze_timer_patch"],
    "toggle_fog_reveal": ["fog_reveal_toggle"],
    "toggle_ai": ["ai_toggle_patch"],
    "set_unit_cap": ["unit_cap_value"],
    "toggle_instant_build_patch": ["instant_build_patch"],
}


@dataclass(frozen=True)
class RawSymbol:
    name: str
    address: str
    kind: str


def _load_raw_symbols(path: Path) -> List[RawSymbol]:
    payload = json.loads(path.read_text(encoding="utf-8"))
    symbols = payload.get("symbols", [])
    results: List[RawSymbol] = []
    for entry in symbols:
        name = str(entry.get("name", "")).strip()
        address = str(entry.get("address", "")).strip()
        kind = str(entry.get("kind", "")).strip() or "unknown"
        if not name or not address:
            continue
        results.append(RawSymbol(name=name, address=address, kind=kind))
    return results


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _fingerprint_id(module_name: str, file_sha256: str) -> str:
    module = Path(module_name).stem.lower().replace(" ", "_")
    return f"{module}_{file_sha256[:16]}"


def _normalize_anchor_id(symbol_name: str) -> str:
    normalized = "".join(ch.lower() if ch.isalnum() else "_" for ch in symbol_name)
    return "_".join(part for part in normalized.split("_") if part)


def _parse_address(address: str) -> int | None:
    cleaned = address.strip().lower()
    if cleaned.startswith("0x"):
        cleaned = cleaned[2:]
    if not cleaned:
        return None
    try:
        return int(cleaned, 16)
    except ValueError:
        return None


def _normalized_address(address: str) -> str:
    parsed = _parse_address(address)
    return f"0x{parsed:x}" if parsed is not None else address.strip().lower()


def _symbol_choice_rank(symbol: RawSymbol) -> Tuple[int, int, str]:
    parsed = _parse_address(symbol.address)
    if parsed is None:
        return (1, 0, symbol.name.lower())
    return (0, parsed, symbol.name.lower())


def _build_anchors(module_name: str, symbols: List[RawSymbol]) -> List[dict]:
    canonical_by_anchor_id: Dict[str, RawSymbol] = {}
    for symbol in symbols:
        anchor_id = _normalize_anchor_id(symbol.name)
        if not anchor_id:
            continue

        current = canonical_by_anchor_id.get(anchor_id)
        if current is None or _symbol_choice_rank(symbol) < _symbol_choice_rank(current):
            canonical_by_anchor_id[anchor_id] = symbol

    anchors = []
    for anchor_id in sorted(canonical_by_anchor_id.keys()):
        symbol = canonical_by_anchor_id[anchor_id]
        anchors.append(
            {
                "id": anchor_id,
                "address": _normalized_address(symbol.address),
                "module": module_name,
                "confidence": 0.95,
                "source": f"ghidra:{symbol.kind}",
                "valueType": "Int32",
            }
        )
    return anchors


def _build_capabilities(anchor_ids: set[str]) -> List[dict]:
    capabilities = []
    for feature_id, required in DEFAULT_FEATURE_REQUIREMENTS.items():
        missing = [anchor for anchor in required if anchor not in anchor_ids]
        available = len(missing) == 0
        capabilities.append(
            {
                "featureId": feature_id,
                "available": available,
                "state": "Verified" if available else "Unavailable",
                "reasonCode": "CAPABILITY_PROBE_PASS" if available else "CAPABILITY_REQUIRED_MISSING",
                "requiredAnchors": required,
            }
        )
    return capabilities


def _now_iso() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-symbols", required=True)
    parser.add_argument("--binary-path", required=True)
    parser.add_argument("--analysis-run-id", required=True)
    parser.add_argument("--output-pack", required=True)
    parser.add_argument("--output-summary", required=True)
    parser.add_argument("--decompile-archive-path", default="")
    args = parser.parse_args()

    raw_symbols_path = Path(args.raw_symbols).resolve()
    binary_path = Path(args.binary_path).resolve()
    output_pack = Path(args.output_pack).resolve()
    output_summary = Path(args.output_summary).resolve()

    symbols = _load_raw_symbols(raw_symbols_path)
    file_sha256 = _sha256(binary_path)
    module_name = binary_path.name
    fingerprint_id = _fingerprint_id(module_name, file_sha256)
    anchors = _build_anchors(module_name, symbols)
    anchor_ids = {item["id"] for item in anchors}
    capabilities = _build_capabilities(anchor_ids)

    output_pack.parent.mkdir(parents=True, exist_ok=True)
    output_summary.parent.mkdir(parents=True, exist_ok=True)

    symbol_pack = {
        "schemaVersion": SCHEMA_VERSION,
        "binaryFingerprint": {
            "fingerprintId": fingerprint_id,
            "moduleName": module_name,
            "fileSha256": file_sha256,
        },
        "buildMetadata": {
            "analysisRunId": args.analysis_run_id,
            "generatedAtUtc": _now_iso(),
            "toolchain": "ghidra-headless+emit-symbol-pack.py",
            "notes": "auto-generated by headless reverse-engineering pipeline",
        },
        "anchors": anchors,
        "capabilities": capabilities,
    }

    output_pack.write_text(json.dumps(symbol_pack, indent=2, sort_keys=True), encoding="utf-8")

    available_count = sum(1 for item in capabilities if item["available"])
    warnings = [
        f"capability {item['featureId']} unavailable: missing required anchors"
        for item in capabilities
        if not item["available"]
    ]
    summary = {
        "schemaVersion": SCHEMA_VERSION,
        "analysisRunId": args.analysis_run_id,
        "binaryPath": str(binary_path).replace("\\", "/"),
        "toolVersions": {
            "ghidra": os.environ.get("GHIDRA_VERSION", "unknown"),
            "emitter": "emit-symbol-pack.py@1.0",
        },
        "coverageStats": {
            "rawSymbolCount": len(symbols),
            "anchorCount": len(anchors),
            "availableCapabilityCount": available_count,
        },
        "warnings": warnings,
        "artifactPointers": {
            "rawSymbolsPath": str(raw_symbols_path).replace("\\", "/"),
            "symbolPackPath": str(output_pack).replace("\\", "/"),
            "decompileArchivePath": str(Path(args.decompile_archive_path).resolve()).replace("\\", "/")
            if args.decompile_archive_path
            else "",
        },
    }
    output_summary.write_text(json.dumps(summary, indent=2, sort_keys=True), encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
