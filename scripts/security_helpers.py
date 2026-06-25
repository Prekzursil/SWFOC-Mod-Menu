from __future__ import annotations

import ipaddress
from typing import Optional, Set
from urllib.parse import ParseResult, urlparse, urlunparse


def _validate_url_scheme_and_host(parsed: ParseResult, raw_url: str) -> str:
    """Validate scheme, hostname presence, and credentials."""
    if parsed.scheme != "https":
        raise ValueError(f"Only https URLs are allowed: {raw_url!r}")
    if not parsed.hostname:
        raise ValueError(f"URL is missing a hostname: {raw_url!r}")
    if parsed.username or parsed.password:
        raise ValueError(f"URL credentials are not allowed: {raw_url!r}")
    return parsed.hostname.lower().strip(".")


def _validate_host_allowlist(hostname: str, allowed_hosts: Optional[Set[str]]) -> None:
    """Check hostname against explicit allowlist."""
    if allowed_hosts is None:
        return
    normalized = {host.lower().strip(".") for host in allowed_hosts}
    if hostname not in normalized:
        raise ValueError(f"URL host is not in allowlist: {hostname}")


def _validate_host_suffix_allowlist(
    hostname: str, allowed_host_suffixes: Optional[Set[str]]
) -> None:
    """Check hostname against suffix allowlist."""
    if allowed_host_suffixes is None:
        return
    suffixes = {s.lower().strip(".") for s in allowed_host_suffixes if s.strip(".")}
    if not suffixes:
        return
    if not any(hostname == s or hostname.endswith(f".{s}") for s in suffixes):
        raise ValueError(f"URL host is not in suffix allowlist: {hostname}")


def _reject_private_ip(hostname: str) -> None:
    """Reject private, loopback, link-local, reserved, and multicast IPs."""
    try:
        ip_value = ipaddress.ip_address(hostname)
    except ValueError:
        return
    if ip_value.is_private or ip_value.is_loopback or ip_value.is_link_local:
        raise ValueError(f"Private or local addresses are not allowed: {hostname}")
    if ip_value.is_reserved or ip_value.is_multicast:
        raise ValueError(f"Private or local addresses are not allowed: {hostname}")


def normalize_https_url(
    raw_url: str,
    *,
    allowed_hosts: Optional[Set[str]] = None,
    allowed_host_suffixes: Optional[Set[str]] = None,
    strip_query: bool = False,
) -> str:
    """Validate user-provided URLs for CLI scripts."""
    parsed = urlparse((raw_url or "").strip())
    hostname = _validate_url_scheme_and_host(parsed, raw_url)
    _validate_host_allowlist(hostname, allowed_hosts)
    _validate_host_suffix_allowlist(hostname, allowed_host_suffixes)
    _reject_private_ip(hostname)
    if hostname in {"localhost", "localhost.localdomain"}:
        raise ValueError("Localhost URLs are not allowed.")
    sanitized = parsed._replace(fragment="", params="")
    if strip_query:
        sanitized = sanitized._replace(query="")
    return urlunparse(sanitized)
