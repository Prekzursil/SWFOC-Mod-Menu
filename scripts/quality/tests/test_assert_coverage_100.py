from __future__ import absolute_import, division

import importlib.util
import sys
import tempfile
import textwrap
import unittest
from importlib.machinery import ModuleSpec
from pathlib import Path
from types import ModuleType


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "assert_coverage_100.py"


def _load_module() -> ModuleType:
    spec = importlib.util.spec_from_file_location("assert_coverage_100", SCRIPT_PATH)
    if spec is None or not isinstance(spec, ModuleSpec):
        raise RuntimeError(f"Failed to create module spec for {SCRIPT_PATH}")

    module = importlib.util.module_from_spec(spec)
    if not isinstance(module, ModuleType):
        raise RuntimeError(f"Failed to create module for {SCRIPT_PATH}")

    if spec.loader is None:
        raise RuntimeError(f"Module loader unavailable for {SCRIPT_PATH}")

    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


MODULE = _load_module()


class AssertCoverage100Tests(unittest.TestCase):
    def write_temp_xml(self, content: str) -> Path:
        temp_dir = Path(tempfile.mkdtemp(prefix="assert-coverage-100-"))
        path = temp_dir / "coverage.cobertura.xml"
        with path.open("w", encoding="utf-8") as handle:
            handle.write(textwrap.dedent(content).strip() + "\n")
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
