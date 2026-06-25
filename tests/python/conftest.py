"""Shared fixtures/helpers for the auxiliary-tooling Python test suite.

Several scripts under ``tools/`` use hyphenated filenames (e.g.
``detect-launch-context.py``) which are not importable via the normal ``import``
statement. ``load_script_module`` loads them by path with a deterministic module
name so coverage attributes lines to the real source file.
"""

from __future__ import annotations

import importlib.util
import sys
from pathlib import Path
from types import ModuleType

REPO_ROOT = Path(__file__).resolve().parents[2]


def load_script_module(relative_path: str, module_name: str) -> ModuleType:
    """Load a (possibly hyphen-named) script file as an importable module."""
    source = REPO_ROOT / relative_path
    spec = importlib.util.spec_from_file_location(module_name, source)
    if spec is None or spec.loader is None:  # pragma: no cover - defensive
        raise ImportError(f"cannot load {relative_path}")
    module = importlib.util.module_from_spec(spec)
    sys.modules[module_name] = module
    spec.loader.exec_module(module)
    return module


# Loading one scripts/quality module triggers its self-bootstrap, which inserts
# the repo's ``scripts`` dir onto sys.path so ``from security_helpers import ...``
# resolves for every test (including the direct import in test_security_helpers).
# Doing it via the real bootstrap (rather than a hand-rolled sys.path.insert)
# keeps that bootstrap line genuinely exercised instead of bypassed.
load_script_module("scripts/quality/check_sonar_zero.py", "_bootstrap_sonar")
