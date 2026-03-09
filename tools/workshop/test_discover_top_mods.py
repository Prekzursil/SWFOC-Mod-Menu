#!/usr/bin/env python3
"""Focused tests for workshop discovery URL validation."""

from __future__ import annotations

import importlib.util
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).resolve().parent / "discover-top-mods.py"
SPEC = importlib.util.spec_from_file_location("discover_top_mods", SCRIPT_PATH)
if SPEC is None or SPEC.loader is None:
    raise RuntimeError(f"Unable to load {SCRIPT_PATH}")

MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class ValidateRemoteUrlTests(unittest.TestCase):
    def test_accepts_steam_workshop_browse_endpoint(self) -> None:
        url = (
            "https://steamcommunity.com/workshop/browse/"
            "?appid=32470&browsesort=trend&section=readytouseitems&actualsort=trend&p=1"
        )

        self.assertEqual(url, MODULE.validate_remote_url(url))

    def test_accepts_steam_details_api_endpoint(self) -> None:
        url = "https://api.steampowered.com/ISteamRemoteStorage/GetPublishedFileDetails/v1/"

        self.assertEqual(url, MODULE.validate_remote_url(url))

    def test_rejects_unexpected_steamcommunity_path(self) -> None:
        with self.assertRaisesRegex(ValueError, "Unsupported remote path"):
            MODULE.validate_remote_url("https://steamcommunity.com/sharedfiles/filedetails/?id=123")

    def test_rejects_unexpected_api_path(self) -> None:
        with self.assertRaisesRegex(ValueError, "Unsupported remote path"):
            MODULE.validate_remote_url("https://api.steampowered.com/ISteamRemoteStorage/DeletePublishedFile/v1/")

    def test_rejects_non_default_ports(self) -> None:
        with self.assertRaisesRegex(ValueError, "Unsupported remote port"):
            MODULE.validate_remote_url(
                "https://api.steampowered.com:444/ISteamRemoteStorage/GetPublishedFileDetails/v1/"
            )

    def test_rejects_embedded_userinfo(self) -> None:
        with self.assertRaisesRegex(ValueError, "Unsupported credentials"):
            MODULE.validate_remote_url(
                "https://user@steamcommunity.com/workshop/browse/?appid=32470&browsesort=trend"
            )


if __name__ == "__main__":
    unittest.main()
