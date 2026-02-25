#!/usr/bin/env python3
"""
Determinism smoke for headless Ghidra symbol-pack emission.

Runs the emitter twice with symbol lists in different order and verifies that
anchor/capability outputs remain identical.
"""

from __future__ import annotations

import argparse
import json
import subprocess
from pathlib import Path


def _run_emitter(
    emitter_path: Path,
    raw_symbols: Path,
    binary_path: Path,
    analysis_run_id: str,
    output_pack: Path,
    output_summary: Path,
) -> None:
    subprocess.run(
        [
            "python3",
            str(emitter_path),
            "--raw-symbols",
            str(raw_symbols),
            "--binary-path",
            str(binary_path),
            "--analysis-run-id",
            analysis_run_id,
            "--output-pack",
            str(output_pack),
            "--output-summary",
            str(output_summary),
        ],
        check=True,
    )


def _load_json(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def _normalize_pack_for_compare(pack: dict) -> dict:
    normalized = json.loads(json.dumps(pack))
    build_metadata = normalized.get("buildMetadata", {})
    build_metadata.pop("analysisRunId", None)
    build_metadata.pop("generatedAtUtc", None)
    normalized["buildMetadata"] = build_metadata
    return normalized


def _write_report(path: Path, payload: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--raw-symbols", required=True)
    parser.add_argument("--binary-path", required=True)
    parser.add_argument("--analysis-run-id-base", required=True)
    parser.add_argument("--output-dir", required=True)
    args = parser.parse_args()

    script_dir = Path(__file__).resolve().parent
    emitter_path = script_dir / "emit-symbol-pack.py"
    raw_symbols_path = Path(args.raw_symbols).resolve()
    binary_path = Path(args.binary_path).resolve()
    output_dir = Path(args.output_dir).resolve()
    output_dir.mkdir(parents=True, exist_ok=True)

    raw_payload = _load_json(raw_symbols_path)
    symbols = raw_payload.get("symbols", [])
    reversed_payload = dict(raw_payload)
    reversed_payload["symbols"] = list(reversed(symbols))
    reversed_raw_path = output_dir / "raw-symbols.reversed.json"
    reversed_raw_path.write_text(json.dumps(reversed_payload, indent=2, sort_keys=True), encoding="utf-8")

    first_pack = output_dir / "symbol-pack.first.json"
    first_summary = output_dir / "analysis-summary.first.json"
    second_pack = output_dir / "symbol-pack.second.json"
    second_summary = output_dir / "analysis-summary.second.json"

    _run_emitter(
        emitter_path,
        raw_symbols_path,
        binary_path,
        f"{args.analysis_run_id_base}-a",
        first_pack,
        first_summary,
    )
    _run_emitter(
        emitter_path,
        reversed_raw_path,
        binary_path,
        f"{args.analysis_run_id_base}-b",
        second_pack,
        second_summary,
    )

    first = _normalize_pack_for_compare(_load_json(first_pack))
    second = _normalize_pack_for_compare(_load_json(second_pack))
    matches = first == second

    report = {
        "deterministic": matches,
        "firstPackPath": str(first_pack).replace("\\", "/"),
        "secondPackPath": str(second_pack).replace("\\", "/"),
    }
    _write_report(output_dir / "determinism-report.json", report)

    if not matches:
        raise SystemExit("ghidra symbol-pack determinism check failed")

    print("ghidra symbol-pack determinism check passed")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
