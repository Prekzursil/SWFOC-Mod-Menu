from __future__ import absolute_import

import argparse
import importlib.util
import sys
import unittest
from importlib.machinery import ModuleSpec
from pathlib import Path
from types import ModuleType


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "check_required_checks.py"


def _load_module() -> ModuleType:
    spec = importlib.util.spec_from_file_location("check_required_checks", SCRIPT_PATH)
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


class CheckRequiredChecksTests(unittest.TestCase):
    def test_should_keep_waiting_returns_true_for_missing_contexts(self) -> None:
        result = MODULE._should_keep_waiting(
            status="pending",
            missing=["Codecov Analytics"],
            contexts={"build-test": {"source": "status", "state": "success"}},
        )

        self.assertTrue(result)

    def test_should_keep_waiting_returns_true_for_in_progress_check_runs(self) -> None:
        result = MODULE._should_keep_waiting(
            status="pending",
            missing=[],
            contexts={"Coverage 100 Gate": {"source": "check_run", "state": "queued"}},
        )

        self.assertTrue(result)

    def test_should_keep_waiting_returns_false_when_only_completed_contexts_remain(self) -> None:
        result = MODULE._should_keep_waiting(
            status="fail",
            missing=[],
            contexts={
                "Codacy Static Code Analysis": {"source": "check_run", "state": "completed"},
                "build-test": {"source": "status", "state": "failure"},
            },
        )

        self.assertFalse(result)

    def test_build_payload_includes_expected_fields(self) -> None:
        args = argparse.Namespace(repo="Prekzursil/SWFOC-Mod-Menu", sha="abc123")

        payload = MODULE._build_payload(
            status="fail",
            args=args,
            required=["build-test"],
            missing=["Coverage 100 Gate"],
            failed=["Codacy Static Code Analysis"],
            pending=["Coverage 100 Gate"],
            contexts={"build-test": {"state": "success", "source": "status"}},
            timed_out=True,
        )

        self.assertEqual("fail", payload["status"])
        self.assertEqual("Prekzursil/SWFOC-Mod-Menu", payload["repo"])
        self.assertEqual("abc123", payload["sha"])
        self.assertEqual(["build-test"], payload["required"])
        self.assertEqual(["Coverage 100 Gate"], payload["missing"])
        self.assertEqual(["Codacy Static Code Analysis"], payload["failed"])
        self.assertEqual(["Coverage 100 Gate"], payload["pending"])
        self.assertTrue(payload["timed_out"])
        self.assertIn("timestamp_utc", payload)


if __name__ == "__main__":
    unittest.main()
