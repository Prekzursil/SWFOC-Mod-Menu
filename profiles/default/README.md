# Default Profile Pack

This folder is copied with the portable app and contains:

- `manifest.json`: profile index and release metadata.
- `profiles/*.json`: trainer profile definitions.
- `schemas/*.json`: save schema definitions.
- `catalog/*/catalog.json`: prebuilt catalogs.
- `helper/scripts/**`: helper-mod scripts + hash-checked deployment payload.

To publish profile updates independently from the app:

1. Build profile zip packages with `tools/build-profile-pack.ps1`.
2. Upload zips to GitHub Releases.
3. Update `manifest.json` with version, URLs, and SHA256 hashes.
