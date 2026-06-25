"""Tests for tools/ghidra/emit-artifact-index.py."""

from __future__ import annotations

import json
from pathlib import Path

from conftest import load_script_module

mod = load_script_module("tools/ghidra/emit-artifact-index.py", "emit_artifact_index")


def _write(path: Path, text: str) -> Path:
    path.write_text(text, encoding="utf-8")
    return path


def test_now_iso_zulu() -> None:
    assert mod._now_iso().endswith("Z")


def test_sha256_matches_known(tmp_path: Path) -> None:
    f = _write(tmp_path / "f.bin", "abc")
    import hashlib

    assert mod._sha256(f) == hashlib.sha256(b"abc").hexdigest()


def test_normalize_path_backslashes() -> None:
    assert mod._normalize_path("a\\b\\c") == "a/b/c"


def test_build_fingerprint_id() -> None:
    fid = mod._build_fingerprint_id("My Mod.exe", "0123456789abcdef0123")
    assert fid == "my_mod_0123456789abcdef"


def test_load_symbol_pack_fingerprint_missing(tmp_path: Path) -> None:
    assert mod._load_symbol_pack_fingerprint(tmp_path / "nope.json") is None


def test_load_symbol_pack_fingerprint_bad_json(tmp_path: Path) -> None:
    f = _write(tmp_path / "p.json", "{not json")
    assert mod._load_symbol_pack_fingerprint(f) is None


def test_load_symbol_pack_fingerprint_raw_not_dict(tmp_path: Path) -> None:
    f = _write(tmp_path / "p.json", json.dumps({"binaryFingerprint": "x"}))
    assert mod._load_symbol_pack_fingerprint(f) is None


def test_load_symbol_pack_fingerprint_incomplete(tmp_path: Path) -> None:
    f = _write(
        tmp_path / "p.json",
        json.dumps(
            {"binaryFingerprint": {"fingerprintId": "a", "moduleName": "", "fileSha256": "c"}}
        ),
    )
    assert mod._load_symbol_pack_fingerprint(f) is None


def test_load_symbol_pack_fingerprint_ok(tmp_path: Path) -> None:
    f = _write(
        tmp_path / "p.json",
        json.dumps(
            {"binaryFingerprint": {"fingerprintId": "a", "moduleName": "b", "fileSha256": "c"}}
        ),
    )
    assert mod._load_symbol_pack_fingerprint(f) == {
        "fingerprintId": "a",
        "moduleName": "b",
        "fileSha256": "c",
    }


def test_resolve_binary_fingerprint_from_pack(tmp_path: Path) -> None:
    pack = _write(
        tmp_path / "p.json",
        json.dumps(
            {"binaryFingerprint": {"fingerprintId": "a", "moduleName": "b", "fileSha256": "c"}}
        ),
    )
    assert mod._resolve_binary_fingerprint(pack, tmp_path / "bin.exe")["fingerprintId"] == "a"


def test_resolve_binary_fingerprint_fallback(tmp_path: Path) -> None:
    binary = _write(tmp_path / "bin.exe", "data")
    fp = mod._resolve_binary_fingerprint(tmp_path / "missing.json", binary)
    assert fp["moduleName"] == "bin.exe"
    assert fp["fingerprintId"].startswith("bin_")


def test_resolve_hash_missing_and_present(tmp_path: Path) -> None:
    assert mod._resolve_hash(tmp_path / "no.bin") is None
    f = _write(tmp_path / "y.bin", "z")
    assert mod._resolve_hash(f) is not None


def test_main_without_decompile_archive(tmp_path: Path, monkeypatch, capsys) -> None:
    binary = _write(tmp_path / "bin.exe", "binary")
    raw = _write(tmp_path / "raw.json", "{}")
    pack = _write(tmp_path / "pack.json", "{}")
    summary = _write(tmp_path / "summary.json", "{}")
    out = tmp_path / "out" / "index.json"
    monkeypatch.setattr(
        "sys.argv",
        [
            "emit-artifact-index.py",
            "--analysis-run-id",
            "run-1",
            "--binary-path",
            str(binary),
            "--raw-symbols",
            str(raw),
            "--symbol-pack",
            str(pack),
            "--summary",
            str(summary),
            "--output",
            str(out),
        ],
    )
    assert mod.main() == 0
    payload = json.loads(out.read_text(encoding="utf-8"))
    assert payload["analysisRunId"] == "run-1"
    assert payload["artifactPointers"]["decompileArchivePath"] is None
    assert payload["fileHashes"]["decompileArchiveSha256"] is None
    assert "artifact index emitted" in capsys.readouterr().out


def test_main_with_decompile_archive(tmp_path: Path, monkeypatch) -> None:
    binary = _write(tmp_path / "bin.exe", "binary")
    raw = _write(tmp_path / "raw.json", "{}")
    pack = _write(tmp_path / "pack.json", "{}")
    summary = _write(tmp_path / "summary.json", "{}")
    archive = _write(tmp_path / "decompile.zip", "zip")
    out = tmp_path / "index.json"
    monkeypatch.setattr(
        "sys.argv",
        [
            "emit-artifact-index.py",
            "--analysis-run-id",
            "run-2",
            "--binary-path",
            str(binary),
            "--raw-symbols",
            str(raw),
            "--symbol-pack",
            str(pack),
            "--summary",
            str(summary),
            "--decompile-archive",
            str(archive),
            "--classification-code",
            "CUSTOM",
            "--output",
            str(out),
        ],
    )
    assert mod.main() == 0
    payload = json.loads(out.read_text(encoding="utf-8"))
    assert payload["classificationCode"] == "CUSTOM"
    assert payload["artifactPointers"]["decompileArchivePath"].endswith("decompile.zip")
    assert payload["fileHashes"]["decompileArchiveSha256"] is not None
