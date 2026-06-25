"""Tests for tools/ghidra/check-determinism.py."""

from __future__ import annotations

import json
from pathlib import Path

import pytest
from conftest import REPO_ROOT, load_script_module

mod = load_script_module("tools/ghidra/check-determinism.py", "check_determinism")

EMITTER = REPO_ROOT / "tools" / "ghidra" / "emit-symbol-pack.py"


def _make_inputs(tmp_path: Path) -> tuple[Path, Path]:
    raw = tmp_path / "raw.json"
    raw.write_text(
        json.dumps(
            {
                "symbols": [
                    {"name": "credits_value", "address": "0x10", "kind": "function"},
                    {"name": "fog_reveal_toggle", "address": "0x20", "kind": "label"},
                ]
            }
        ),
        encoding="utf-8",
    )
    binary = tmp_path / "bin.exe"
    binary.write_text("bytes", encoding="utf-8")
    return raw, binary


def test_validate_arg_text() -> None:
    assert mod._validate_arg_text("ok", "lbl") == "ok"
    with pytest.raises(ValueError, match="invalid-command-arg"):
        mod._validate_arg_text("", "lbl")
    with pytest.raises(ValueError, match="invalid-command-arg"):
        mod._validate_arg_text("a\x00b", "lbl")


def test_validated_path_text_missing(tmp_path: Path) -> None:
    with pytest.raises(FileNotFoundError, match="missing-path"):
        mod._validated_path_text(tmp_path / "no.bin", "lbl", must_exist=True)


def test_trusted_emitter_path_rejects_wrong_name(tmp_path: Path) -> None:
    bad = tmp_path / "other.py"
    bad.write_text("x", encoding="utf-8")
    with pytest.raises(ValueError, match="unexpected-emitter-script"):
        mod._trusted_emitter_path(bad)


def test_validate_emitter_command_length() -> None:
    with pytest.raises(ValueError, match="length"):
        mod._validate_emitter_command(("a", "b"))


def test_validate_emitter_command_flags() -> None:
    command = ("py", "emit", "--wrong", "v", "--x", "v", "--y", "v", "--z", "v", "--w", "v")
    with pytest.raises(ValueError, match="flags"):
        mod._validate_emitter_command(command)


def test_supports_zero_arg_call() -> None:
    assert mod._supports_zero_arg_call(lambda: None) is True
    assert mod._supports_zero_arg_call(lambda x: None) is False
    assert mod._supports_zero_arg_call(lambda *a: None) is True


def test_supports_zero_arg_call_unsignaturable() -> None:
    # A callable whose signature cannot be introspected (inspect.signature raises)
    # falls into the except branch and returns False.
    assert mod._supports_zero_arg_call(dict.update) is False


def test_load_emitter_main_bad_spec(tmp_path: Path) -> None:
    missing = tmp_path / "ghost.py"
    with pytest.raises((RuntimeError, FileNotFoundError)):
        mod._load_emitter_main(missing)


def test_load_emitter_main_missing_main(tmp_path: Path) -> None:
    script = tmp_path / "nomain.py"
    script.write_text("X = 1\n", encoding="utf-8")
    with pytest.raises(RuntimeError, match="missing main"):
        mod._load_emitter_main(script)


def test_load_emitter_main_main_needs_args(tmp_path: Path) -> None:
    script = tmp_path / "argmain.py"
    script.write_text("def main(x):\n    return 0\n", encoding="utf-8")
    with pytest.raises(RuntimeError, match="zero arguments"):
        mod._load_emitter_main(script)


def test_load_emitter_main_ok() -> None:
    fn = mod._load_emitter_main(EMITTER)
    assert callable(fn)


def test_run_emitter_main_nonzero_exit(tmp_path: Path, monkeypatch) -> None:
    script = tmp_path / "emit-symbol-pack.py"
    script.write_text("def main():\n    return 5\n", encoding="utf-8")
    raw = tmp_path / "raw.json"
    raw.write_text("{}", encoding="utf-8")
    binary = tmp_path / "bin.exe"
    binary.write_text("b", encoding="utf-8")
    with pytest.raises(RuntimeError, match="exit code 5"):
        mod._run_emitter(script, raw, binary, "rid", tmp_path / "p.json", tmp_path / "s.json")


def test_normalize_pack_for_compare_strips_volatile() -> None:
    pack = {"buildMetadata": {"analysisRunId": "x", "generatedAtUtc": "t", "keep": 1}}
    out = mod._normalize_pack_for_compare(pack)
    assert out["buildMetadata"] == {"keep": 1}


def test_is_pack_match(tmp_path: Path) -> None:
    a = tmp_path / "a.json"
    b = tmp_path / "b.json"
    a.write_text(json.dumps({"buildMetadata": {"analysisRunId": "1"}, "x": 1}), encoding="utf-8")
    b.write_text(json.dumps({"buildMetadata": {"analysisRunId": "2"}, "x": 1}), encoding="utf-8")
    assert mod._is_pack_match(a, b) is True


def test_main_deterministic_pass(tmp_path: Path, monkeypatch, capsys) -> None:
    raw, binary = _make_inputs(tmp_path)
    out_dir = tmp_path / "out"
    monkeypatch.setattr(
        "sys.argv",
        [
            "check-determinism.py",
            "--raw-symbols",
            str(raw),
            "--binary-path",
            str(binary),
            "--analysis-run-id-base",
            "base",
            "--output-dir",
            str(out_dir),
        ],
    )
    assert mod.main() == 0
    report = json.loads((out_dir / "determinism-report.json").read_text(encoding="utf-8"))
    assert report["deterministic"] is True
    assert "passed" in capsys.readouterr().out


def test_main_mismatch_raises(tmp_path: Path, monkeypatch) -> None:
    raw, binary = _make_inputs(tmp_path)
    out_dir = tmp_path / "out"
    monkeypatch.setattr(mod, "_is_pack_match", lambda *a: False)
    monkeypatch.setattr(
        "sys.argv",
        [
            "check-determinism.py",
            "--raw-symbols",
            str(raw),
            "--binary-path",
            str(binary),
            "--analysis-run-id-base",
            "base",
            "--output-dir",
            str(out_dir),
        ],
    )
    with pytest.raises(SystemExit, match="determinism check failed"):
        mod.main()
