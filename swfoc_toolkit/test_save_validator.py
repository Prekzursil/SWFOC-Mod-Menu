#!/usr/bin/env python3
"""Unit tests for the SWFOC Save File Validator."""

import struct
import tempfile
import unittest
import zlib
from pathlib import Path

import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parent))
from save_validator import (
    CHUNK_HEADER_SIZE,
    MAX_NESTING_DEPTH,
    MICRO_CHUNK_HEADER_SIZE,
    SUB_CHUNK_FLAG,
    SaveValidator,
    ValidationResult,
    load_save_data,
    validate_file,
)


def make_chunk(chunk_id: int, payload: bytes, has_sub_chunks: bool = False) -> bytes:
    """Build a binary chunk with the correct header."""
    size = len(payload)
    if has_sub_chunks:
        size |= SUB_CHUNK_FLAG
    header = struct.pack("<II", chunk_id, size)
    return header + payload


def make_micro_chunk(mc_id: int, payload: bytes) -> bytes:
    """Build a binary micro-chunk with 1-byte id + 1-byte size header."""
    assert len(payload) <= 255
    return struct.pack("BB", mc_id, len(payload)) + payload


class TestWellFormedChunks(unittest.TestCase):
    """Test parsing well-formed chunk sequences."""

    def test_single_leaf_chunk(self):
        payload = b"\x01\x02\x03\x04"
        data = make_chunk(0x0001, payload)
        v = SaveValidator(data, "test.sav")
        result = v.validate()

        self.assertTrue(result.is_valid)
        self.assertEqual(result.total_chunks, 1)
        self.assertEqual(result.max_depth, 0)
        self.assertIn(0x0001, result.chunks_by_id)
        self.assertEqual(result.chunks_by_id[0x0001], 1)

    def test_multiple_sequential_chunks(self):
        data = make_chunk(0x0001, b"\xAA" * 8) + make_chunk(0x0002, b"\xBB" * 4)
        v = SaveValidator(data, "test.sav")
        result = v.validate()

        self.assertTrue(result.is_valid)
        self.assertEqual(result.total_chunks, 2)
        self.assertEqual(result.chunks_by_id[0x0001], 1)
        self.assertEqual(result.chunks_by_id[0x0002], 1)

    def test_nested_sub_chunks(self):
        inner = make_chunk(0x0010, b"\xFF" * 4)
        outer = make_chunk(0x0001, inner, has_sub_chunks=True)
        v = SaveValidator(outer, "test.sav")
        result = v.validate()

        self.assertTrue(result.is_valid)
        self.assertEqual(result.total_chunks, 2)
        self.assertEqual(result.max_depth, 1)

    def test_multiple_sub_chunks(self):
        inner1 = make_chunk(0x0010, b"\xAA" * 2)
        inner2 = make_chunk(0x0011, b"\xBB" * 6)
        outer = make_chunk(0x0001, inner1 + inner2, has_sub_chunks=True)
        v = SaveValidator(outer, "test.sav")
        result = v.validate()

        self.assertTrue(result.is_valid)
        self.assertEqual(result.total_chunks, 3)
        self.assertEqual(result.max_depth, 1)

    def test_deeply_nested_chunks(self):
        # Build 5 levels of nesting
        payload = b"\x42" * 4
        for depth in range(5):
            chunk_id = 0x0100 + depth
            payload = make_chunk(chunk_id, payload, has_sub_chunks=(depth > 0))
        # Outermost wrapper
        data = make_chunk(0x0001, payload, has_sub_chunks=True)
        v = SaveValidator(data, "test.sav")
        result = v.validate()

        self.assertTrue(result.is_valid)
        self.assertEqual(result.total_chunks, 6)  # 1 root + 5 wrapped layers
        self.assertEqual(result.max_depth, 5)


class TestSubChunkFlag(unittest.TestCase):
    """Test detection of the sub-chunk flag (bit 31)."""

    def test_flag_set(self):
        inner = make_chunk(0x0010, b"\x00" * 4)
        outer = make_chunk(0x0001, inner, has_sub_chunks=True)
        v = SaveValidator(outer, "test.sav")
        result = v.validate()

        root = result.root_chunks[0]
        self.assertTrue(root.has_sub_chunks)
        self.assertEqual(len(root.children), 1)
        self.assertFalse(root.children[0].has_sub_chunks)

    def test_flag_not_set(self):
        data = make_chunk(0x0001, b"\xDE\xAD\xBE\xEF")
        v = SaveValidator(data, "test.sav")
        result = v.validate()

        root = result.root_chunks[0]
        self.assertFalse(root.has_sub_chunks)
        self.assertEqual(len(root.children), 0)


class TestMicroChunks(unittest.TestCase):
    """Test micro-chunk parsing within leaf chunks."""

    def test_single_micro_chunk(self):
        mc = make_micro_chunk(0x01, b"\xAA\xBB\xCC\xDD")
        data = make_chunk(0x0001, mc)
        v = SaveValidator(data, "test.sav")
        result = v.validate()

        self.assertTrue(result.is_valid)
        self.assertEqual(result.total_micro_chunks, 1)
        root = result.root_chunks[0]
        self.assertEqual(len(root.micro_chunks), 1)
        self.assertEqual(root.micro_chunks[0]["micro_chunk_id"], 0x01)
        self.assertEqual(root.micro_chunks[0]["size"], 4)

    def test_multiple_micro_chunks(self):
        mc1 = make_micro_chunk(0x01, b"\x11\x22")
        mc2 = make_micro_chunk(0x02, b"\x33\x44\x55")
        mc3 = make_micro_chunk(0x03, b"\x66")
        data = make_chunk(0x0001, mc1 + mc2 + mc3)
        v = SaveValidator(data, "test.sav")
        result = v.validate()

        self.assertTrue(result.is_valid)
        self.assertEqual(result.total_micro_chunks, 3)

    def test_micro_chunk_max_size(self):
        mc = make_micro_chunk(0xFF, b"\x00" * 255)
        data = make_chunk(0x0001, mc)
        v = SaveValidator(data, "test.sav")
        result = v.validate()

        self.assertTrue(result.is_valid)
        self.assertEqual(result.total_micro_chunks, 1)
        self.assertEqual(result.root_chunks[0].micro_chunks[0]["size"], 255)

    def test_non_tiling_payload_skips_micro_chunks(self):
        # Payload that doesn't tile as micro-chunks (leftover bytes)
        payload = b"\x01\x04\xAA\xBB\xCC\xDD\xFF"  # mc(1,4) + 1 leftover
        data = make_chunk(0x0001, payload)
        v = SaveValidator(data, "test.sav")
        result = v.validate()

        self.assertTrue(result.is_valid)
        # Should NOT detect micro-chunks because tiling doesn't match
        self.assertEqual(result.total_micro_chunks, 0)


class TestTruncatedChunks(unittest.TestCase):
    """Test detection of truncated/corrupt chunks."""

    def test_truncated_header(self):
        # Only 4 bytes -- not enough for an 8-byte header
        data = b"\x01\x00\x00\x00"
        v = SaveValidator(data, "test.sav")
        result = v.validate()

        self.assertFalse(result.is_valid)
        self.assertTrue(any("Truncated" in e or "too small" in e for e in result.errors))

    def test_chunk_size_overflows_file(self):
        # Declare 1000 bytes of payload but only provide 4
        header = struct.pack("<II", 0x0001, 1000)
        data = header + b"\xAA" * 4
        v = SaveValidator(data, "test.sav")
        result = v.validate()

        self.assertFalse(result.is_valid)
        self.assertTrue(any("overflows" in e for e in result.errors))

    def test_truncated_sub_chunk(self):
        # Parent claims sub-chunks but inner data is too short
        inner_partial = b"\x10\x00\x00\x00\x20\x00"  # 6 bytes, not enough
        outer = make_chunk(0x0001, inner_partial, has_sub_chunks=True)
        v = SaveValidator(outer, "test.sav")
        result = v.validate()

        self.assertFalse(result.is_valid)
        self.assertTrue(any("Truncated" in e or "overflows" in e for e in result.errors))


class TestZeroSizeChunks(unittest.TestCase):
    """Test zero-size chunk detection."""

    def test_zero_size_warning(self):
        data = make_chunk(0x0001, b"")
        v = SaveValidator(data, "test.sav")
        result = v.validate()

        self.assertTrue(result.is_valid)  # Warning, not error
        self.assertTrue(any("Zero-size" in w for w in result.warnings))


class TestZlibDecompression(unittest.TestCase):
    """Test zlib decompression fallback."""

    def test_compressed_file(self):
        # Build a valid chunk, compress it, write to temp file
        payload = b"\x42" * 64
        raw_data = make_chunk(0x0001, payload)
        compressed = zlib.compress(raw_data)

        with tempfile.NamedTemporaryFile(suffix=".sav", delete=False) as f:
            f.write(compressed)
            tmp_path = f.name

        try:
            data, was_compressed = load_save_data(tmp_path)
            self.assertTrue(was_compressed)
            self.assertEqual(data, raw_data)

            result = validate_file(tmp_path)
            self.assertTrue(result.is_valid)
            self.assertTrue(result.was_compressed)
            self.assertEqual(result.total_chunks, 1)
        finally:
            Path(tmp_path).unlink(missing_ok=True)

    def test_uncompressed_file(self):
        payload = b"\x42" * 16
        raw_data = make_chunk(0x0001, payload)

        with tempfile.NamedTemporaryFile(suffix=".sav", delete=False) as f:
            f.write(raw_data)
            tmp_path = f.name

        try:
            data, was_compressed = load_save_data(tmp_path)
            self.assertFalse(was_compressed)
            self.assertEqual(data, raw_data)
        finally:
            Path(tmp_path).unlink(missing_ok=True)


class TestEmptyAndMissingFiles(unittest.TestCase):
    """Test edge cases for empty and missing files."""

    def test_empty_file(self):
        with tempfile.NamedTemporaryFile(suffix=".sav", delete=False) as f:
            tmp_path = f.name

        try:
            result = validate_file(tmp_path)
            self.assertFalse(result.is_valid)
            self.assertTrue(any("empty" in e.lower() for e in result.errors))
        finally:
            Path(tmp_path).unlink(missing_ok=True)

    def test_missing_file(self):
        result = validate_file("/nonexistent/path/fake.sav")
        self.assertFalse(result.is_valid)
        self.assertTrue(any("not found" in e.lower() for e in result.errors))


class TestOutputFormats(unittest.TestCase):
    """Test text and JSON output generation."""

    def test_to_dict_keys(self):
        data = make_chunk(0x0001, b"\x00" * 4)
        v = SaveValidator(data, "test.sav")
        result = v.validate()
        d = result.to_dict()

        expected_keys = {
            "file_path", "file_size", "was_compressed", "valid",
            "total_chunks", "total_micro_chunks", "max_depth",
            "chunks_by_id", "error_count", "errors",
            "warning_count", "warnings",
        }
        self.assertEqual(set(d.keys()), expected_keys)

    def test_to_text_contains_status(self):
        data = make_chunk(0x0001, b"\x00" * 4)
        v = SaveValidator(data, "test.sav")
        result = v.validate()
        text = result.to_text()

        self.assertIn("VALID", text)
        self.assertIn("Total chunks: 1", text)


class TestRealWorldPatterns(unittest.TestCase):
    """Test patterns that mimic real save file structure."""

    def test_predicted_save_structure(self):
        """Simulate the predicted SWFOC save structure from the format spec."""
        # SAVE_HEADER chunk (leaf with micro-chunks)
        header_mcs = (
            make_micro_chunk(0x01, b"\x01\x00\x00\x00")  # version
            + make_micro_chunk(0x02, b"TestSave\x00")     # save name
            + make_micro_chunk(0x03, b"\x00\x00\x00\x00") # timestamp
        )
        save_header = make_chunk(0x0001, header_mcs)

        # PLAYER_INFO inside PLAYER_DATA
        player_info = make_chunk(0x0011, b"\x00" * 16)
        ai_state = make_chunk(0x0012, b"\xFF" * 8)
        player_data = make_chunk(
            0x0010, player_info + ai_state, has_sub_chunks=True
        )

        # PLANET inside GALACTIC_MAP
        planet_info = make_chunk(0x0021, b"\x00" * 12)
        ground_units = make_chunk(0x0022, b"\x00" * 8)
        planet = make_chunk(
            0x0020, planet_info + ground_units, has_sub_chunks=True
        )
        galactic_map = make_chunk(0x0002, planet, has_sub_chunks=True)

        # ROOT chunk
        root_payload = save_header + player_data + galactic_map
        root = make_chunk(0xFFFF, root_payload, has_sub_chunks=True)

        v = SaveValidator(root, "campaign.sav")
        result = v.validate()

        self.assertTrue(result.is_valid)
        self.assertEqual(result.total_chunks, 9)
        self.assertEqual(result.max_depth, 3)
        self.assertEqual(result.total_micro_chunks, 3)


if __name__ == "__main__":
    unittest.main()
