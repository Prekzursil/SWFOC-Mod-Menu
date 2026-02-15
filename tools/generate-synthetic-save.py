#!/usr/bin/env python3
"""Generate synthetic SWFOC save files for schema mapping tests.

This utility does not try to mimic full game saves. It creates deterministic
binary payloads with controlled offsets so schema and checksum logic can be
verified in CI and local tests.
"""

from __future__ import annotations

import argparse
import os
import struct
from pathlib import Path


def write_i32(buf: bytearray, offset: int, value: int) -> None:
    buf[offset : offset + 4] = struct.pack("<i", value)


def write_i64(buf: bytearray, offset: int, value: int) -> None:
    buf[offset : offset + 8] = struct.pack("<q", value)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--out", required=True, help="output save path")
    parser.add_argument("--size", type=int, default=300_000, help="bytes")
    parser.add_argument("--credits-empire", type=int, default=5000)
    parser.add_argument("--credits-rebel", type=int, default=4500)
    parser.add_argument("--credits-underworld", type=int, default=3900)
    parser.add_argument("--ticks", type=int, default=123456789)
    args = parser.parse_args()

    data = bytearray(args.size)
    data[0:8] = b"PGSAVE01"
    write_i32(data, 8, 1)
    write_i64(data, 16, args.ticks)

    # base FoC economy offsets from schema sample
    write_i32(data, 6144, args.credits_empire)
    write_i32(data, 6148, args.credits_rebel)
    write_i32(data, 6152, args.credits_underworld)

    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_bytes(data)
    print(f"Wrote synthetic save: {out} ({len(data)} bytes)")


if __name__ == "__main__":
    main()
