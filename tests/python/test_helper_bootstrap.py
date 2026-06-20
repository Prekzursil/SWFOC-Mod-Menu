"""Exercise the self-bootstrap (sys.path insertion) in each SaaS-zero script.

Each ``scripts/quality/check_*_zero.py`` inserts the repo's ``scripts`` dir onto
``sys.path`` so it can ``from security_helpers import ...`` when run as a
stand-alone script. Because the conftest already triggers one such bootstrap, the
*other* modules find the path present and skip their insert. To genuinely exercise
each module's insert branch we re-execute it with the scripts dir removed from
``sys.path`` and confirm the module restores it.
"""

from __future__ import annotations

import importlib.util
import sys

import pytest
from conftest import REPO_ROOT

SCRIPTS_DIR = str(REPO_ROOT / "scripts")

MODULES = [
    "scripts/quality/check_codacy_zero.py",
    "scripts/quality/check_deepscan_zero.py",
    "scripts/quality/check_sentry_zero.py",
    "scripts/quality/check_sonar_zero.py",
]


@pytest.mark.parametrize("relative_path", MODULES)
def test_module_self_bootstraps_scripts_dir(relative_path: str, monkeypatch) -> None:
    # Remove the scripts dir from sys.path so the module's own insert runs.
    cleaned = [p for p in sys.path if p != SCRIPTS_DIR]
    monkeypatch.setattr(sys, "path", cleaned)

    spec = importlib.util.spec_from_file_location("_bootstrap_probe", REPO_ROOT / relative_path)
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)

    # The module must have re-inserted the scripts dir to resolve security_helpers.
    assert SCRIPTS_DIR in sys.path
    assert hasattr(module, "normalize_https_url")
