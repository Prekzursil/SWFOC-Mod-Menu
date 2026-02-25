#!/usr/bin/env python3
"""
Extract actionable calibration intelligence from a Cheat Engine .CT file.

This is intentionally not a direct "table importer". We distill patterns and
techniques (AOB hooks, branch bypasses, constant overrides) and map them to the
trainer's broader action model.
"""

from __future__ import annotations

import argparse
import json
import re
from dataclasses import asdict, dataclass, field
from pathlib import Path
from typing import Iterable

from defusedxml import ElementTree as element_tree


AOB_SCAN_RE = re.compile(
    r"aobscanmodule\(\s*([^,\s]+)\s*,\s*([^,\s]+)\s*,\s*([^)]+?)\s*\)",
    re.IGNORECASE,
)
INJECTION_RE = re.compile(r"INJECTION POINT:\s*([^\s]+)", re.IGNORECASE)
WRITE_RE = re.compile(
    r"\bmov\s+\[([^\]]+)\]\s*,\s*\((float|int)\)\s*([-+]?\d+(?:\.\d+)?)",
    re.IGNORECASE,
)
DB_RE = re.compile(r"^\s*db\s+([0-9A-Fa-f? ]+)\s*$")


@dataclass
class AobScan:
    symbol: str
    module: str
    pattern: str


@dataclass
class ConstantWrite:
    target: str
    value_type: str
    value: str


@dataclass
class ScriptIntel:
    group: str
    description: str
    technique: str
    aob_scans: list[AobScan] = field(default_factory=list)
    injection_points: list[str] = field(default_factory=list)
    constant_writes: list[ConstantWrite] = field(default_factory=list)
    disable_restore_bytes: list[str] = field(default_factory=list)
    trainer_mapping: str = "unmapped"
    notes: list[str] = field(default_factory=list)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Extract trainer-facing intel from Cheat Engine .CT")
    parser.add_argument(
        "--ct",
        default="StarWarsG.CT",
        help="Path to Cheat Table file (default: StarWarsG.CT)",
    )
    parser.add_argument(
        "--out-json",
        default="artifacts/cheattable_intel/starwarsg.intel.json",
        help="Output JSON path",
    )
    parser.add_argument(
        "--out-md",
        default="docs/CHEATTABLE_INTEL.md",
        help="Output markdown summary path",
    )
    return parser.parse_args()


def normalize_description(raw: str | None) -> str:
    if not raw:
        return ""
    value = raw.strip()
    if value.startswith('"') and value.endswith('"') and len(value) >= 2:
        value = value[1:-1]
    return value.strip()


def technique_from_script(script: str, writes: list[ConstantWrite]) -> tuple[str, list[str]]:
    notes: list[str] = []
    lowered = script.lower()
    has_code_cave = "alloc(newmem" in lowered and "jmp newmem" in lowered
    bypass_jump_hint = ("remove the jump" in lowered) or ("kill the jump" in lowered)
    has_nop = re.search(r"^\s*nop\s*$", script, flags=re.IGNORECASE | re.MULTILINE) is not None

    if has_code_cave and writes:
        notes.append("Uses code-cave trampoline with immediate writes.")
        return "code_cave_override", notes

    if bypass_jump_hint or has_nop:
        notes.append("Branch behavior is bypassed/patched.")
        return "branch_bypass_patch", notes

    if has_code_cave:
        notes.append("Uses code-cave trampoline.")
        return "code_cave_patch", notes

    return "direct_patch", notes


def mapping_from_description(description: str) -> str:
    d = description.lower()
    if "build" in d:
        return "set_instant_build_multiplier (or patch-mode feature)"
    if "credit" in d:
        return "set_credits (+ mirror sync)"
    if "maphack" in d:
        return "toggle_fog_reveal (or code-patch fallback)"
    if "unit cap" in d:
        return "future:set_unit_cap"
    return "unmapped"


def parse_restore_bytes(script: str) -> list[str]:
    lines = script.splitlines()
    in_disable = False
    values: list[str] = []
    for line in lines:
        stripped = line.strip()
        if stripped.upper() == "[DISABLE]":
            in_disable = True
            continue
        if stripped.upper() == "[ENABLE]":
            in_disable = False
            continue
        if not in_disable:
            continue
        m = DB_RE.match(stripped)
        if m:
            values.append(" ".join(m.group(1).split()).upper())
    return values


def extract_intel_from_script(group: str, description: str, script: str) -> ScriptIntel:
    scans = [
        AobScan(symbol=symbol.strip(), module=module.strip(), pattern=" ".join(pattern.strip().split()))
        for symbol, module, pattern in AOB_SCAN_RE.findall(script)
    ]

    writes = [
        ConstantWrite(target=target.strip(), value_type=value_type.lower(), value=value.strip())
        for target, value_type, value in WRITE_RE.findall(script)
    ]

    injection_points = sorted(set(INJECTION_RE.findall(script)))
    restore_bytes = parse_restore_bytes(script)
    technique, notes = technique_from_script(script, writes)
    mapping = mapping_from_description(description)

    if not scans:
        notes.append("No aobscanmodule() pattern found.")
    if "donate" in description.lower() or "paypal" in description.lower():
        notes.append("Non-gameplay utility entry.")

    return ScriptIntel(
        group=group,
        description=description,
        technique=technique,
        aob_scans=scans,
        injection_points=injection_points,
        constant_writes=writes,
        disable_restore_bytes=restore_bytes,
        trainer_mapping=mapping,
        notes=notes,
    )


def iter_cheat_entries(root: element_tree.Element) -> Iterable[tuple[str, element_tree.Element]]:
    top = root.find("CheatEntries")
    if top is None:
        return
    for entry in top.findall("CheatEntry"):
        group_name = normalize_description(entry.findtext("Description"))
        if entry.findtext("GroupHeader") != "1":
            yield ("", entry)
            continue

        nested = entry.find("CheatEntries")
        if nested is None:
            continue

        for child in nested.findall("CheatEntry"):
            yield (group_name, child)


def dedupe_intel(records: list[ScriptIntel]) -> list[ScriptIntel]:
    seen: set[tuple[str, str, str]] = set()
    result: list[ScriptIntel] = []
    for item in records:
        primary_pattern = item.aob_scans[0].pattern if item.aob_scans else ""
        key = (item.group, item.description.lower(), primary_pattern)
        if key in seen:
            continue
        seen.add(key)
        result.append(item)
    return result


def render_markdown(records: list[ScriptIntel], source_path: Path) -> str:
    lines: list[str] = []
    append_intro(lines, source_path)
    append_summary_table(lines, records)
    append_actionable_notes(lines)
    append_detailed_entries(lines, records)
    return "\n".join(lines).rstrip() + "\n"


def append_intro(lines: list[str], source_path: Path) -> None:
    lines.append("# Cheat Table Intelligence")
    lines.append("")
    lines.append(f"Source: `{source_path.name}`")
    lines.append("")
    lines.append("This is an extracted intelligence summary, not a direct CE table import.")
    lines.append("It keeps AOB/patch techniques that are useful for trainer calibration and ignores unrelated table noise.")
    lines.append("")


def append_summary_table(lines: list[str], records: list[ScriptIntel]) -> None:
    lines.append("## Extracted Scripts")
    lines.append("")
    lines.append("| Group | Entry | Technique | Trainer Mapping | Primary AOB |")
    lines.append("|---|---|---|---|---|")
    for item in records:
        primary = item.aob_scans[0].pattern if item.aob_scans else "-"
        lines.append(
            f"| {item.group or '-'} | {item.description} | {item.technique} | {item.trainer_mapping} | `{primary}` |"
        )
    lines.append("")


def append_actionable_notes(lines: list[str]) -> None:
    lines.append("## Actionable Notes")
    lines.append("")
    lines.append("1. `Infinite Credits` scripts confirm a dual-path flow (`float -> int convert`), matching the trainer's mirror-sync model.")
    lines.append("2. `Maphack` scripts are branch-bypass patches, so they are an optional fallback path if symbol-based fog toggles regress.")
    lines.append("3. `1 Sec/1 Cred Build` scripts are code-cave overrides with hardcoded values; useful as behavior anchors, not as final trainer behavior.")
    lines.append("4. `Max Unit Cap` suggests a future patch-mode feature (`set_unit_cap`) if desired.")
    lines.append("")


def append_detailed_entries(lines: list[str], records: list[ScriptIntel]) -> None:
    lines.append("## Detailed Entries")
    lines.append("")
    for item in records:
        append_detailed_entry(lines, item)


def append_detailed_entry(lines: list[str], item: ScriptIntel) -> None:
    lines.append(f"### {item.group} / {item.description}")
    lines.append(f"- Technique: `{item.technique}`")
    lines.append(f"- Trainer mapping: `{item.trainer_mapping}`")
    append_injection_points(lines, item.injection_points)
    append_aob_scans(lines, item.aob_scans)
    append_constant_writes(lines, item.constant_writes)
    append_restore_bytes(lines, item.disable_restore_bytes)
    append_notes(lines, item.notes)
    lines.append("")


def append_injection_points(lines: list[str], injection_points: list[str]) -> None:
    if not injection_points:
        return

    points = ", ".join(f"`{point}`" for point in injection_points)
    lines.append(f"- Injection points: {points}")


def append_aob_scans(lines: list[str], aob_scans: list[AobScan]) -> None:
    if not aob_scans:
        return

    lines.append("- AOB scans:")
    for scan in aob_scans:
        lines.append(
            f"  - `{scan.symbol}` on `{scan.module}` with pattern `{scan.pattern}`"
        )


def append_constant_writes(lines: list[str], constant_writes: list[ConstantWrite]) -> None:
    if not constant_writes:
        return

    lines.append("- Constant writes:")
    for write in constant_writes:
        lines.append(f"  - `[{write.target}] <- ({write.value_type}){write.value}`")


def append_restore_bytes(lines: list[str], restore_bytes: list[str]) -> None:
    if not restore_bytes:
        return

    lines.append("- Disable restore bytes:")
    for blob in restore_bytes:
        lines.append(f"  - `db {blob}`")


def append_notes(lines: list[str], notes: list[str]) -> None:
    if not notes:
        return

    lines.append("- Notes:")
    for note in notes:
        lines.append(f"  - {note}")


def main() -> int:
    args = parse_args()
    ct_path = Path(args.ct)
    if not ct_path.exists():
        raise FileNotFoundError(f"Cheat table not found: {ct_path}")

    tree = element_tree.parse(ct_path)
    root = tree.getroot()

    records: list[ScriptIntel] = []
    for group, entry in iter_cheat_entries(root):
        description = normalize_description(entry.findtext("Description"))
        variable_type = (entry.findtext("VariableType") or "").strip()
        script = entry.findtext("AssemblerScript") or ""

        if not description:
            continue

        if "donate" in description.lower() or "paypal" in description.lower():
            continue

        if variable_type.lower() != "auto assembler script":
            continue

        intel = extract_intel_from_script(group=group, description=description, script=script)
        records.append(intel)

    records = dedupe_intel(records)

    payload = {
        "source": str(ct_path),
        "entryCount": len(records),
        "entries": [asdict(r) for r in records],
    }

    out_json = Path(args.out_json)
    out_md = Path(args.out_md)
    out_json.parent.mkdir(parents=True, exist_ok=True)
    out_md.parent.mkdir(parents=True, exist_ok=True)

    out_json.write_text(json.dumps(payload, indent=2), encoding="utf-8")
    out_md.write_text(render_markdown(records, ct_path), encoding="utf-8")

    print(f"Wrote {len(records)} extracted entries:")
    print(f" - JSON: {out_json}")
    print(f" - Markdown: {out_md}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
