#!/usr/bin/env python3
"""
Emit machine-readable artifact metadata for headless Ghidra runs.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from datetime import datetime, timezone
from pathlib import Path
from typing import Any


SCHEMA_VERSION = "1.0"
DEFAULT_CLASSIFICATION_CODE = "GHIDRA_ARTIFACT_INDEX_READY"


def _now_iso() -> str:
    return datetime.now(timezone.utc).replace(microsecond=0).isoformat().replace("+00:00", "Z")


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _normalize_path(path: Path | str) -> str:
    return str(path).replace("\\", "/")


def _read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8"))


def _build_fingerprint_id(module_name: str, file_sha256: str) -> str:
    return f"{Path(module_name).stem.lower().replace(' ', '_')}_{file_sha256[:16]}"


def _load_symbol_pack_fingerprint(symbol_pack_path: Path) -> dict[str, str] | None:
    if not symbol_pack_path.exists():
        return None

    try:
        symbol_pack = _read_json(symbol_pack_path)
    except (OSError, TypeError, ValueError):
        return None

    raw = symbol_pack.get("binaryFingerprint", {})
    if not isinstance(raw, dict):
        return None

    fingerprint_id = str(raw.get("fingerprintId", "")).strip()
    module_name = str(raw.get("moduleName", "")).strip()
    file_sha256 = str(raw.get("fileSha256", "")).strip()
    if not (fingerprint_id and module_name and file_sha256):
        return None

    return {
        "fingerprintId": fingerprint_id,
        "moduleName": module_name,
        "fileSha256": file_sha256,
    }


def _resolve_binary_fingerprint(symbol_pack_path: Path, binary_path: Path) -> dict[str, str]:
    fingerprint = _load_symbol_pack_fingerprint(symbol_pack_path)
    if fingerprint is not None:
        return fingerprint

    module_name = binary_path.name
    file_sha256 = _sha256(binary_path)
    return {
        "fingerprintId": _build_fingerprint_id(module_name, file_sha256),
        "moduleName": module_name,
        "fileSha256": file_sha256,
    }


def _resolve_hash(path: Path) -> str | None:
    if not path.exists():
        return None
    return _sha256(path)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--analysis-run-id", required=True)
    parser.add_argument("--binary-path", required=True)
    parser.add_argument("--raw-symbols", required=True)
    parser.add_argument("--symbol-pack", required=True)
    parser.add_argument("--summary", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--decompile-archive", default="")
    parser.add_argument("--classification-code", default=DEFAULT_CLASSIFICATION_CODE)
    args = parser.parse_args()

    binary_path = Path(args.binary_path).resolve()
    raw_symbols_path = Path(args.raw_symbols).resolve()
    symbol_pack_path = Path(args.symbol_pack).resolve()
    summary_path = Path(args.summary).resolve()
    output_path = Path(args.output).resolve()
    decompile_archive_path = Path(args.decompile_archive).resolve() if args.decompile_archive else None

    fingerprint = _resolve_binary_fingerprint(symbol_pack_path, binary_path)
    pointers = {
        "rawSymbolsPath": _normalize_path(raw_symbols_path),
        "symbolPackPath": _normalize_path(symbol_pack_path),
        "analysisSummaryPath": _normalize_path(summary_path),
        "decompileArchivePath": _normalize_path(decompile_archive_path) if decompile_archive_path else None,
    }
    file_hashes = {
        "rawSymbolsSha256": _resolve_hash(raw_symbols_path),
        "symbolPackSha256": _resolve_hash(symbol_pack_path),
        "analysisSummarySha256": _resolve_hash(summary_path),
        "decompileArchiveSha256": _resolve_hash(decompile_archive_path) if decompile_archive_path else None,
    }

    payload = {
        "schemaVersion": SCHEMA_VERSION,
        "analysisRunId": args.analysis_run_id,
        "generatedAtUtc": _now_iso(),
        "classificationCode": args.classification_code,
        "binaryFingerprint": fingerprint,
        "artifactPointers": pointers,
        "fileHashes": file_hashes,
    }

    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text(json.dumps(payload, indent=2, sort_keys=True), encoding="utf-8")
    print(f"artifact index emitted: {_normalize_path(output_path)}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
