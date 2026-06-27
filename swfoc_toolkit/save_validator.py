#!/usr/bin/env python3
"""
SWFOC Save File Validator
Parses .sav files using the Westwood chunk/micro-chunk format.
Reports structural errors, chunk counts, and statistics.

Chunk format (Westwood/Petroglyph):
  - Chunk header: 4-byte chunk_id (uint32 LE) + 4-byte chunk_size (uint32 LE)
  - Bit 31 of chunk_size (0x80000000) means "contains sub-chunks"
  - Data size = chunk_size & 0x7FFFFFFF
  - Micro-chunk header: 1-byte id + 1-byte size (max 255 bytes)
  - No checksum, no file magic -- file starts directly with root chunk
"""

import argparse
import json
import os
import struct
import sys
import zlib
from dataclasses import dataclass, field
from pathlib import Path
from typing import List, Optional, Tuple

CHUNK_HEADER_SIZE = 8
MICRO_CHUNK_HEADER_SIZE = 2
SUB_CHUNK_FLAG = 0x80000000
SIZE_MASK = 0x7FFFFFFF
MAX_NESTING_DEPTH = 256

DEFAULT_SAVE_DIR = os.path.join(
    os.environ.get("APPDATA", ""),
    "Petroglyph",
    "Empire at War - Forces of Corruption",
    "Save",
)


@dataclass
class ChunkInfo:
    """Record of a single parsed chunk."""
    chunk_id: int
    offset: int
    data_size: int
    has_sub_chunks: bool
    depth: int
    children: List["ChunkInfo"] = field(default_factory=list)
    micro_chunks: List[dict] = field(default_factory=list)


@dataclass
class ValidationResult:
    """Aggregated validation output."""
    file_path: str
    file_size: int
    was_compressed: bool
    total_chunks: int
    total_micro_chunks: int
    max_depth: int
    chunks_by_id: dict = field(default_factory=dict)
    errors: List[str] = field(default_factory=list)
    warnings: List[str] = field(default_factory=list)
    root_chunks: List[ChunkInfo] = field(default_factory=list)

    @property
    def is_valid(self) -> bool:
        return len(self.errors) == 0

    def to_dict(self) -> dict:
        return {
            "file_path": self.file_path,
            "file_size": self.file_size,
            "was_compressed": self.was_compressed,
            "valid": self.is_valid,
            "total_chunks": self.total_chunks,
            "total_micro_chunks": self.total_micro_chunks,
            "max_depth": self.max_depth,
            "chunks_by_id": {
                f"0x{k:08X}": v for k, v in sorted(self.chunks_by_id.items())
            },
            "error_count": len(self.errors),
            "errors": self.errors,
            "warning_count": len(self.warnings),
            "warnings": self.warnings,
        }

    def to_text(self) -> str:
        lines = []
        lines.append(f"=== SWFOC Save File Validation Report ===")
        lines.append(f"File: {self.file_path}")
        lines.append(f"Size: {self.file_size} bytes")
        lines.append(f"Compressed: {'yes' if self.was_compressed else 'no'}")
        lines.append(f"Status: {'VALID' if self.is_valid else 'ERRORS FOUND'}")
        lines.append("")
        lines.append(f"Total chunks: {self.total_chunks}")
        lines.append(f"Total micro-chunks: {self.total_micro_chunks}")
        lines.append(f"Max nesting depth: {self.max_depth}")
        lines.append("")

        if self.chunks_by_id:
            lines.append("Chunks by type ID:")
            for cid in sorted(self.chunks_by_id):
                count = self.chunks_by_id[cid]
                lines.append(f"  0x{cid:08X}: {count}")
            lines.append("")

        if self.errors:
            lines.append(f"Errors ({len(self.errors)}):")
            for err in self.errors:
                lines.append(f"  [ERROR] {err}")
            lines.append("")

        if self.warnings:
            lines.append(f"Warnings ({len(self.warnings)}):")
            for warn in self.warnings:
                lines.append(f"  [WARN] {warn}")
            lines.append("")

        return "\n".join(lines)


class SaveValidator:
    """Parses and validates a SWFOC save file chunk tree."""

    def __init__(self, data: bytes, file_path: str, was_compressed: bool = False):
        self._data = data
        self._file_path = file_path
        self._was_compressed = was_compressed
        self._result = ValidationResult(
            file_path=file_path,
            file_size=len(data),
            was_compressed=was_compressed,
            total_chunks=0,
            total_micro_chunks=0,
            max_depth=0,
        )

    def validate(self) -> ValidationResult:
        """Run full validation and return results."""
        if len(self._data) == 0:
            self._result.errors.append("File is empty (0 bytes)")
            return self._result

        if len(self._data) < CHUNK_HEADER_SIZE:
            self._result.errors.append(
                f"File too small for even one chunk header "
                f"({len(self._data)} < {CHUNK_HEADER_SIZE} bytes)"
            )
            return self._result

        offset = 0
        data_len = len(self._data)

        while offset < data_len:
            remaining = data_len - offset
            if remaining < CHUNK_HEADER_SIZE:
                if remaining > 0:
                    self._result.errors.append(
                        f"Truncated chunk header at offset 0x{offset:X} "
                        f"({remaining} bytes remaining, need {CHUNK_HEADER_SIZE})"
                    )
                break

            chunk = self._parse_chunk(offset, depth=0)
            if chunk is None:
                break
            self._result.root_chunks.append(chunk)
            offset += CHUNK_HEADER_SIZE + chunk.data_size

        return self._result

    def _parse_chunk(self, offset: int, depth: int) -> Optional[ChunkInfo]:
        """Parse a single chunk and recurse into sub-chunks if flagged."""
        data_len = len(self._data)

        if offset + CHUNK_HEADER_SIZE > data_len:
            self._result.errors.append(
                f"Chunk header overflows file at offset 0x{offset:X}"
            )
            return None

        chunk_id, raw_size = struct.unpack_from("<II", self._data, offset)
        has_sub_chunks = bool(raw_size & SUB_CHUNK_FLAG)
        data_size = raw_size & SIZE_MASK

        chunk = ChunkInfo(
            chunk_id=chunk_id,
            offset=offset,
            data_size=data_size,
            has_sub_chunks=has_sub_chunks,
            depth=depth,
        )

        self._result.total_chunks += 1
        self._result.chunks_by_id[chunk_id] = (
            self._result.chunks_by_id.get(chunk_id, 0) + 1
        )
        if depth > self._result.max_depth:
            self._result.max_depth = depth

        # Validate size bounds
        chunk_end = offset + CHUNK_HEADER_SIZE + data_size
        if chunk_end > data_len:
            self._result.errors.append(
                f"Chunk 0x{chunk_id:08X} at offset 0x{offset:X} overflows file: "
                f"declared size {data_size}, but only "
                f"{data_len - offset - CHUNK_HEADER_SIZE} bytes available"
            )
            # Clamp to available data so we can continue parsing
            chunk.data_size = data_len - offset - CHUNK_HEADER_SIZE
            return chunk

        # Zero-size chunk warning
        if data_size == 0:
            self._result.warnings.append(
                f"Zero-size chunk 0x{chunk_id:08X} at offset 0x{offset:X}"
            )
            return chunk

        # Depth guard
        if depth >= MAX_NESTING_DEPTH:
            self._result.errors.append(
                f"Nesting depth {depth} exceeds maximum {MAX_NESTING_DEPTH} "
                f"at offset 0x{offset:X}"
            )
            return chunk

        payload_start = offset + CHUNK_HEADER_SIZE
        payload_end = payload_start + data_size

        if has_sub_chunks:
            sub_offset = payload_start
            while sub_offset < payload_end:
                remaining = payload_end - sub_offset
                if remaining < CHUNK_HEADER_SIZE:
                    if remaining > 0:
                        self._result.errors.append(
                            f"Truncated sub-chunk header inside chunk "
                            f"0x{chunk_id:08X} at offset 0x{sub_offset:X} "
                            f"({remaining} bytes remaining)"
                        )
                    break

                sub_chunk = self._parse_chunk(sub_offset, depth + 1)
                if sub_chunk is None:
                    break
                chunk.children.append(sub_chunk)
                sub_offset += CHUNK_HEADER_SIZE + sub_chunk.data_size
        else:
            # Leaf chunk -- try parsing micro-chunks
            self._parse_micro_chunks(chunk, payload_start, payload_end)

        return chunk

    def _parse_micro_chunks(
        self, parent: ChunkInfo, start: int, end: int
    ) -> None:
        """Attempt to parse micro-chunks within a leaf chunk's payload.

        Micro-chunks are heuristic: we validate that the declared sizes
        tile the payload exactly. If they don't, we silently skip (the
        payload might be raw data, not micro-chunks).
        """
        offset = start
        micro_chunks = []
        has_nonzero_size = False
        while offset < end:
            remaining = end - offset
            if remaining < MICRO_CHUNK_HEADER_SIZE:
                # Not enough for another micro-chunk -- leftover raw bytes
                break

            mc_id = self._data[offset]
            mc_size = self._data[offset + 1]

            mc_data_end = offset + MICRO_CHUNK_HEADER_SIZE + mc_size
            if mc_data_end > end:
                # Micro-chunk overflows parent -- not micro-chunk data
                break

            if mc_size > 0:
                has_nonzero_size = True

            micro_chunks.append({
                "micro_chunk_id": mc_id,
                "offset": offset,
                "size": mc_size,
            })
            offset = mc_data_end

        # Only record micro-chunks if they tile the payload exactly
        # and at least one has a non-zero data size (avoids false positives
        # on payloads like all-zeros that happen to tile as mc(0,0) repeats).
        if offset == end and micro_chunks and has_nonzero_size:
            parent.micro_chunks = micro_chunks
            self._result.total_micro_chunks += len(micro_chunks)


def load_save_data(file_path: str) -> Tuple[bytes, bool]:
    """Load save file data, trying raw first, then zlib decompression.

    Returns (data, was_compressed).
    """
    with open(file_path, "rb") as f:
        raw = f.read()

    if len(raw) == 0:
        return raw, False

    # Try raw chunk parsing first: check if the first 8 bytes look like a
    # valid chunk header (reasonable chunk_id and size).
    if len(raw) >= CHUNK_HEADER_SIZE:
        _, raw_size = struct.unpack_from("<II", raw, 0)
        data_size = raw_size & SIZE_MASK
        # Heuristic: if declared size is within file bounds, treat as raw
        if data_size <= len(raw):
            return raw, False

    # Raw doesn't look right -- try zlib decompression
    try:
        decompressed = zlib.decompress(raw)
        return decompressed, True
    except zlib.error:
        pass

    # Also try skipping potential header bytes (some formats prepend
    # uncompressed size or other metadata before the zlib stream)
    for skip in (4, 8):
        if len(raw) > skip:
            try:
                decompressed = zlib.decompress(raw[skip:])
                return decompressed, True
            except zlib.error:
                pass

    # Fall back to raw data and let the validator report errors
    return raw, False


def validate_file(file_path: str) -> ValidationResult:
    """High-level entry point: load file, parse, validate, return result."""
    path = Path(file_path)
    if not path.exists():
        result = ValidationResult(
            file_path=file_path,
            file_size=0,
            was_compressed=False,
            total_chunks=0,
            total_micro_chunks=0,
            max_depth=0,
        )
        result.errors.append(f"File not found: {file_path}")
        return result

    if not path.is_file():
        result = ValidationResult(
            file_path=file_path,
            file_size=0,
            was_compressed=False,
            total_chunks=0,
            total_micro_chunks=0,
            max_depth=0,
        )
        result.errors.append(f"Path is not a file: {file_path}")
        return result

    try:
        data, was_compressed = load_save_data(file_path)
    except OSError as e:
        result = ValidationResult(
            file_path=file_path,
            file_size=0,
            was_compressed=False,
            total_chunks=0,
            total_micro_chunks=0,
            max_depth=0,
        )
        result.errors.append(f"Failed to read file: {e}")
        return result

    validator = SaveValidator(data, file_path, was_compressed)
    return validator.validate()


def find_save_files(directory: Optional[str] = None) -> List[str]:
    """Find .sav files in the given directory (or default save location)."""
    search_dir = directory or DEFAULT_SAVE_DIR
    search_path = Path(search_dir)

    if not search_path.is_dir():
        return []

    return sorted(str(p) for p in search_path.glob("*.sav"))


def main() -> int:
    parser = argparse.ArgumentParser(
        description="SWFOC Save File Validator -- parses and validates "
        ".sav files using the Westwood/Petroglyph chunk format."
    )
    parser.add_argument(
        "files",
        nargs="*",
        help="Save file(s) to validate. If none given, searches the "
        "default save directory.",
    )
    parser.add_argument(
        "--json",
        action="store_true",
        help="Output results as JSON instead of human-readable text.",
    )
    parser.add_argument(
        "--dir",
        default=None,
        help="Directory to search for .sav files (overrides default).",
    )

    args = parser.parse_args()

    files = args.files
    if not files:
        files = find_save_files(args.dir)
        if not files:
            search_dir = args.dir or DEFAULT_SAVE_DIR
            print(f"No .sav files found in: {search_dir}", file=sys.stderr)
            return 1

    all_results = []
    any_errors = False

    for fpath in files:
        result = validate_file(fpath)
        all_results.append(result)
        if not result.is_valid:
            any_errors = True

    if args.json:
        if len(all_results) == 1:
            print(json.dumps(all_results[0].to_dict(), indent=2))
        else:
            print(json.dumps([r.to_dict() for r in all_results], indent=2))
    else:
        for i, result in enumerate(all_results):
            if i > 0:
                print()
            print(result.to_text())

    return 1 if any_errors else 0


if __name__ == "__main__":
    sys.exit(main())
