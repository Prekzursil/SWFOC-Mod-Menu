"""
Unit tests for kb_generator.py

Verifies:
- JSON parsing on sample data
- Output markdown files are created
- Cross-links are valid
- MkDocs config is well-formed
"""

import json
import os
import shutil
import tempfile
import unittest
from pathlib import Path

from swfoc_toolkit.kb_generator import (
    _crosslink,
    _load_json,
    _slugify,
    generate_docs,
    generate_index_page,
    generate_mkdocs_yml,
    generate_struct_page,
    generate_system_page,
    generate_lua_api_page,
    generate_rvas_page,
)


class TestSlugify(unittest.TestCase):
    def test_simple(self):
        self.assertEqual(_slugify("PlayerClass"), "playerclass")

    def test_special_chars(self):
        self.assertEqual(_slugify("GameObjectClass"), "gameobjectclass")

    def test_spaces_and_underscores(self):
        self.assertEqual(_slugify("camera_selection"), "camera-selection")


class TestCrosslink(unittest.TestCase):
    def test_links_known_struct(self):
        result = _crosslink(
            "References PlayerClass data",
            {"PlayerClass", "GameObjectClass"},
        )
        self.assertIn("[PlayerClass](../structs/playerclass.md)", result)

    def test_no_self_link(self):
        result = _crosslink(
            "PlayerClass references itself",
            {"PlayerClass"},
            current_struct="PlayerClass",
        )
        self.assertNotIn("[PlayerClass]", result)

    def test_no_match(self):
        text = "No known structs here"
        result = _crosslink(text, {"FooClass"})
        self.assertEqual(text, result)


class TestLoadJson(unittest.TestCase):
    def test_valid_json(self):
        tmp = tempfile.NamedTemporaryFile(
            mode="w", suffix=".json", delete=False, encoding="utf-8"
        )
        try:
            json.dump({"key": "value"}, tmp)
            tmp.close()
            data = _load_json(Path(tmp.name))
            self.assertIsNotNone(data)
            self.assertEqual(data["key"], "value")
        finally:
            os.unlink(tmp.name)

    def test_missing_file(self):
        result = _load_json(Path("/nonexistent/file.json"))
        self.assertIsNone(result)

    def test_malformed_json(self):
        tmp = tempfile.NamedTemporaryFile(
            mode="w", suffix=".json", delete=False, encoding="utf-8"
        )
        try:
            tmp.write("{invalid json")
            tmp.close()
            result = _load_json(Path(tmp.name))
            self.assertIsNone(result)
        finally:
            os.unlink(tmp.name)


class TestGenerateStructPage(unittest.TestCase):
    def test_basic_struct(self):
        data = {
            "rtti_mangled_name": ".?AVTestClass@@",
            "inherits": ["BaseClass"],
            "estimated_size": 128,
            "fields": {
                "0x00": {
                    "type": "pointer",
                    "name": "vtable_ptr",
                    "status": "CONFIRMED",
                },
                "0x08": {
                    "type": "float32",
                    "name": "health",
                    "status": "CONFIRMED",
                    "note": "Current HP",
                },
            },
        }
        page = generate_struct_page("TestClass", data, {"BaseClass"})
        self.assertIn("# TestClass", page)
        self.assertIn(".?AVTestClass@@", page)
        self.assertIn("vtable_ptr", page)
        self.assertIn("health", page)
        self.assertIn("CONFIRMED", page)
        # Inheritance link
        self.assertIn("[BaseClass]", page)

    def test_lua_methods(self):
        data = {
            "lua_methods": {
                "Give_Money": {
                    "wrapper_rva": "0x603130",
                    "engine_rva": "0x27F370",
                },
            },
        }
        page = generate_struct_page("Player", data, set())
        self.assertIn("Give_Money", page)
        self.assertIn("0x603130", page)

    def test_fields_as_list(self):
        """RE findings use list-of-dicts for fields."""
        data = {
            "fields": [
                {"offset": "0x00", "type": "pointer", "name": "vtable_ptr", "status": "CONFIRMED"},
                {"offset": "0x08", "type": "int32", "name": "id", "confidence": "high"},
            ],
        }
        page = generate_struct_page("ListStruct", data, set())
        self.assertIn("vtable_ptr", page)
        self.assertIn("id", page)


class TestGenerateSystemPage(unittest.TestCase):
    def test_kb_data_only(self):
        kb_data = {
            "pipeline_stages": 8,
            "validation_gates": 18,
            "can_produce_rva": "0x2804D0",
        }
        page = generate_system_page("production", kb_data, None)
        self.assertIn("# Production", page)
        self.assertIn("0x2804D0", page)

    def test_with_re_findings(self):
        re_data = {
            "title": "Combat Deep Dive",
            "description": "Full damage pipeline analysis.",
            "_meta": {"analysis_date": "2026-04-04", "analyst": "Agent 3C"},
            "stages": [{"stage": 1, "name": "Weapon Fire"}],
        }
        page = generate_system_page("combat", {}, re_data)
        self.assertIn("# Combat", page)
        self.assertIn("Combat Deep Dive", page)
        self.assertIn("Weapon Fire", page)


class TestGenerateLuaApiPage(unittest.TestCase):
    def test_strips_header(self):
        content = "# SWFOC Game Lua API Reference\n## Subtitle\n\nActual content here.\n"
        page = generate_lua_api_page(content)
        self.assertIn("# SWFOC Lua API Reference", page)
        self.assertIn("Actual content here.", page)
        # Should not duplicate original title
        self.assertNotIn("# SWFOC Game Lua API Reference", page)


class TestGenerateRvasPage(unittest.TestCase):
    def test_strips_header(self):
        content = "# StarWarsG.exe Complete RVA Reference v3\n\n| RVA | Function |\n"
        page = generate_rvas_page(content)
        self.assertIn("# Complete RVA Reference", page)
        self.assertIn("| RVA | Function |", page)


class TestGenerateIndexPage(unittest.TestCase):
    def test_contains_all_sections(self):
        page = generate_index_page(
            struct_names=["PlayerClass", "GameObjectClass"],
            system_names=["combat", "ai"],
            meta={"module": {"name": "StarWarsG.exe"}, "lua_version": "5.0.2"},
        )
        self.assertIn("PlayerClass", page)
        self.assertIn("GameObjectClass", page)
        self.assertIn("Combat", page)
        self.assertIn("Lua API", page)
        self.assertIn("RVA Table", page)
        self.assertIn("StarWarsG.exe", page)


class TestGenerateMkdocsYml(unittest.TestCase):
    def test_valid_yaml_structure(self):
        yml = generate_mkdocs_yml(
            struct_names=["PlayerClass"],
            system_names=["combat"],
            output_dir=Path("docs"),
        )
        self.assertIn("site_name:", yml)
        self.assertIn("theme:", yml)
        self.assertIn("nav:", yml)
        self.assertIn("PlayerClass", yml)
        self.assertIn("Combat", yml)
        # Check it has required MkDocs keys
        self.assertIn("markdown_extensions:", yml)


class TestFullGeneration(unittest.TestCase):
    """Integration test: run generate_docs against the real project data."""

    def test_generate_real_docs(self):
        """Verify docs are generated from the actual knowledge base."""
        tmp_dir = Path(tempfile.mkdtemp())
        try:
            result = generate_docs(tmp_dir)
            # Should not have errors
            self.assertNotIn("error", result)
            # Should have created struct pages
            self.assertGreater(result["struct_pages"], 0)
            # Should have created system pages
            self.assertGreater(result["system_pages"], 0)
            # Lua API and RVAs
            self.assertTrue(result["has_lua_api"])
            self.assertTrue(result["has_rvas"])

            # Verify files exist
            self.assertTrue((tmp_dir / "index.md").exists())
            self.assertTrue((tmp_dir / "lua-api.md").exists())
            self.assertTrue((tmp_dir / "rvas.md").exists())
            self.assertTrue((tmp_dir / "structs").is_dir())
            self.assertTrue((tmp_dir / "systems").is_dir())

            # Verify at least one struct file
            struct_files = list((tmp_dir / "structs").glob("*.md"))
            self.assertGreater(len(struct_files), 0)

            # Verify mkdocs.yml was created in parent
            mkdocs_path = tmp_dir.parent / "mkdocs.yml"
            self.assertTrue(mkdocs_path.exists())

            # Verify cross-links in index
            index_content = (tmp_dir / "index.md").read_text(encoding="utf-8")
            self.assertIn("structs/", index_content)
            self.assertIn("systems/", index_content)

            # Verify a struct page has valid content
            pc_path = tmp_dir / "structs" / "playerclass.md"
            if pc_path.exists():
                content = pc_path.read_text(encoding="utf-8")
                self.assertIn("# PlayerClass", content)
                self.assertIn("vtable_ptr", content)

        finally:
            shutil.rmtree(tmp_dir, ignore_errors=True)


class TestCrossLinkValidation(unittest.TestCase):
    """Verify that cross-links in generated pages point to files that exist."""

    def test_crosslinks_resolve(self):
        tmp_dir = Path(tempfile.mkdtemp())
        try:
            generate_docs(tmp_dir)

            # Collect all generated .md files
            all_md_files: set[str] = set()
            for md_file in tmp_dir.rglob("*.md"):
                rel = md_file.relative_to(tmp_dir).as_posix()
                all_md_files.add(rel)

            # Check links in index.md
            index = (tmp_dir / "index.md").read_text(encoding="utf-8")
            link_pattern = re.compile(r"\[.*?\]\((.*?\.md)\)")
            for match in link_pattern.finditer(index):
                target = match.group(1)
                self.assertIn(
                    target,
                    all_md_files,
                    f"Broken link in index.md: {target}",
                )
        finally:
            shutil.rmtree(tmp_dir, ignore_errors=True)


# Need re import at module level for the test
import re


if __name__ == "__main__":
    unittest.main()
