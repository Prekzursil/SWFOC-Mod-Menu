"""Tests for scripts/security_helpers.normalize_https_url."""

from __future__ import annotations

import pytest
from security_helpers import normalize_https_url


def test_accepts_simple_https_url() -> None:
    assert normalize_https_url("https://example.com/path") == "https://example.com/path"


def test_strips_fragment_and_params() -> None:
    out = normalize_https_url("https://example.com/a;p=1?q=2#frag")
    assert "#frag" not in out
    assert ";p=1" not in out
    assert "q=2" in out


def test_strip_query_removes_query() -> None:
    out = normalize_https_url("https://example.com/a?q=2", strip_query=True)
    assert "q=2" not in out


def test_rejects_non_https() -> None:
    with pytest.raises(ValueError, match="Only https"):
        normalize_https_url("http://example.com")


def test_rejects_missing_hostname() -> None:
    with pytest.raises(ValueError, match="missing a hostname"):
        normalize_https_url("https:///path")


def test_rejects_credentials() -> None:
    with pytest.raises(ValueError, match="credentials are not allowed"):
        normalize_https_url("https://user:pass@example.com")


def test_host_allowlist_pass_and_fail() -> None:
    assert normalize_https_url("https://example.com", allowed_hosts={"example.com"})
    with pytest.raises(ValueError, match="not in allowlist"):
        normalize_https_url("https://evil.com", allowed_hosts={"example.com"})


def test_host_suffix_allowlist_pass_exact_and_subdomain() -> None:
    assert normalize_https_url("https://api.example.com", allowed_host_suffixes={"example.com"})
    assert normalize_https_url("https://example.com", allowed_host_suffixes={"example.com"})


def test_host_suffix_allowlist_fail() -> None:
    with pytest.raises(ValueError, match="suffix allowlist"):
        normalize_https_url("https://evil.com", allowed_host_suffixes={"example.com"})


def test_host_suffix_allowlist_empty_set_is_noop() -> None:
    # Suffixes that strip down to empty -> validation is skipped.
    assert normalize_https_url("https://anything.com", allowed_host_suffixes={"."})


def test_rejects_private_ip() -> None:
    with pytest.raises(ValueError, match="Private or local"):
        normalize_https_url("https://10.0.0.1")


def test_rejects_reserved_or_multicast_ip() -> None:
    with pytest.raises(ValueError, match="Private or local"):
        normalize_https_url("https://224.0.0.1")  # multicast


def test_public_ip_is_allowed() -> None:
    assert normalize_https_url("https://8.8.8.8/") == "https://8.8.8.8/"


def test_rejects_localhost_name() -> None:
    with pytest.raises(ValueError, match="Localhost"):
        normalize_https_url("https://localhost")
