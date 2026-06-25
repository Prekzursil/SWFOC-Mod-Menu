"""Cover the urlopen-backed request helpers in the SaaS-zero scripts.

These functions issue a single validated HTTPS GET/POST. We exercise them with a
stubbed ``urllib.request.urlopen`` so the real request body (header assembly,
JSON decode, response handling) is measured without network access.
"""

from __future__ import annotations

import json

import pytest
from conftest import load_script_module

codacy = load_script_module("scripts/quality/check_codacy_zero.py", "check_codacy_zero_net")
deepscan = load_script_module("scripts/quality/check_deepscan_zero.py", "check_deepscan_zero_net")
sentry = load_script_module("scripts/quality/check_sentry_zero.py", "check_sentry_zero_net")
sonar = load_script_module("scripts/quality/check_sonar_zero.py", "check_sonar_zero_net")
required = load_script_module(
    "scripts/quality/check_required_checks.py", "check_required_checks_net"
)


class _Resp:
    def __init__(self, body: bytes, headers: dict | None = None) -> None:
        self._body = body
        self.headers = headers or {}

    def read(self) -> bytes:
        return self._body

    def __enter__(self) -> "_Resp":
        return self

    def __exit__(self, *a: object) -> None:
        return None


def _patch_urlopen(monkeypatch, module, resp: _Resp) -> list:
    captured: list = []

    def fake(req, timeout):  # noqa: ANN001
        captured.append(req)
        return resp

    monkeypatch.setattr(module.urllib.request, "urlopen", fake)
    return captured


def test_codacy_request_json_get(monkeypatch) -> None:
    _patch_urlopen(monkeypatch, codacy, _Resp(json.dumps({"total": 0}).encode()))
    out = codacy._request_json("https://api.codacy.com/x", "tok")
    assert out == {"total": 0}


def test_codacy_request_json_post_with_data(monkeypatch) -> None:
    captured = _patch_urlopen(monkeypatch, codacy, _Resp(json.dumps({"ok": 1}).encode()))
    out = codacy._request_json("https://api.codacy.com/x", "tok", method="POST", data={"a": 1})
    assert out == {"ok": 1}
    assert captured[0].data is not None


def test_deepscan_request_json(monkeypatch) -> None:
    _patch_urlopen(monkeypatch, deepscan, _Resp(json.dumps({"count": 0}).encode()))
    assert deepscan._request_json("https://api.deepscan.io/x", "tok") == {"count": 0}


def test_sonar_request_json(monkeypatch) -> None:
    _patch_urlopen(monkeypatch, sonar, _Resp(json.dumps({"paging": {"total": 0}}).encode()))
    assert sonar._request_json("https://sonarcloud.io/api/x", "auth") == {"paging": {"total": 0}}


def test_sentry_request_list_ok(monkeypatch) -> None:
    _patch_urlopen(monkeypatch, sentry, _Resp(json.dumps([{"id": 1}]).encode(), {"X-Hits": "1"}))
    body, headers = sentry._request("https://sentry.io/api/0/x", "tok")
    assert body == [{"id": 1}]
    assert headers["x-hits"] == "1"


def test_sentry_request_non_list_raises(monkeypatch) -> None:
    _patch_urlopen(monkeypatch, sentry, _Resp(json.dumps({"not": "list"}).encode()))
    with pytest.raises(RuntimeError, match="Unexpected Sentry"):
        sentry._request("https://sentry.io/api/0/x", "tok")


def test_required_api_get(monkeypatch) -> None:
    _patch_urlopen(monkeypatch, required, _Resp(json.dumps({"check_runs": []}).encode()))
    assert required._api_get("o/r", "commits/sha/check-runs", "tok") == {"check_runs": []}
