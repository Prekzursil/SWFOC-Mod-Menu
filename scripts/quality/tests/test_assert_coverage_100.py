from __future__ import annotations

import importlib.util
import sys
import tempfile
import textwrap
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "assert_coverage_100.py"
SPEC = importlib.util.spec_from_file_location("assert_coverage_100", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


class AssertCoverage100Tests(unittest.TestCase):
    def write_temp_xml(self, content: str) -> Path:
        temp_dir = Path(tempfile.mkdtemp(prefix="assert-coverage-100-"))
        path = temp_dir / "coverage.cobertura.xml"
        path.write_text(textwrap.dedent(content).strip() + "\n", encoding="utf-8")
        return path

    def test_parse_coverage_xml_counts_line_hits_when_only_root_lines_exist(self) -> None:
        coverage_path = self.write_temp_xml(
            """
            <coverage line-rate="0.5" branch-rate="0.0">
              <packages />
              <line number="1" hits="1" />
              <line number="2" hits="0" />
            </coverage>
            """
        )

        stats = MODULE.parse_coverage_xml("dotnet", coverage_path, include_generated=False)

        self.assertEqual(stats.line_covered, 1)
        self.assertEqual(stats.line_total, 2)
        self.assertEqual(stats.branch_covered, 0)
        self.assertEqual(stats.branch_total, 0)

    def test_parse_coverage_xml_rejects_unparseable_zero_totals(self) -> None:
        coverage_path = self.write_temp_xml(
            """
            <coverage line-rate="1.0" branch-rate="1.0">
              <packages />
            </coverage>
            """
        )

        with self.assertRaises(ValueError):
            MODULE.parse_coverage_xml("dotnet", coverage_path, include_generated=False)


if __name__ == "__main__":
    unittest.main()
