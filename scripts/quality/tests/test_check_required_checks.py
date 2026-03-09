import argparse
import importlib.util
import sys
import unittest
from importlib.machinery import ModuleSpec
from pathlib import Path
from types import ModuleType


SCRIPT_PATH = Path(__file__).resolve().parents[1] / "check_required_checks.py"
SPEC = importlib.util.spec_from_file_location("check_required_checks", SCRIPT_PATH)
assert SPEC is not None
assert isinstance(SPEC, ModuleSpec)
MODULE = importlib.util.module_from_spec(SPEC)
assert isinstance(MODULE, ModuleType)
assert SPEC.loader is not None
sys.modules[SPEC.name] = MODULE
SPEC.loader.exec_module(MODULE)


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
