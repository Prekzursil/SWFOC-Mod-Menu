"""Tests for tools/generate-synthetic-save.py."""

from __future__ import annotations

import struct
from pathlib import Path

from conftest import load_script_module

mod = load_script_module("tools/generate-synthetic-save.py", "generate_synthetic_save")


def test_write_i32_and_i64() -> None:
    buf = bytearray(32)
    mod.write_i32(buf, 0, 7)
    mod.write_i64(buf, 8, 12345)
    assert struct.unpack_from("<i", buf, 0)[0] == 7
    assert struct.unpack_from("<q", buf, 8)[0] == 12345


def test_main_writes_save(tmp_path: Path, monkeypatch, capsys) -> None:
    out = tmp_path / "nested" / "save.dat"
    monkeypatch.setattr(
        "sys.argv",
        [
            "generate-synthetic-save.py",
            "--out",
            str(out),
            "--size",
            "7000",
            "--ticks",
            "42",
        ],
    )
    mod.main()
    data = out.read_bytes()
    assert len(data) == 7000
    assert data[0:8] == b"PGSAVE01"
    assert struct.unpack_from("<q", data, 16)[0] == 42
    assert struct.unpack_from("<i", data, 6144)[0] == 5000  # default credits-empire
    assert "Wrote synthetic save" in capsys.readouterr().out
