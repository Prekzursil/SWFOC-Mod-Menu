"""
SWFOC Knowledge Base Documentation Generator

Reads the reverse-engineering knowledge base (JSON + Markdown) and generates
interlinked MkDocs-compatible Markdown pages in docs/.

Usage:
    python -m swfoc_toolkit.kb_generator [--output-dir docs]

No external dependencies -- uses only Python 3 stdlib.
"""

from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path
from typing import Any


# ---------------------------------------------------------------------------
# Paths (relative to project root)
# ---------------------------------------------------------------------------

_PROJECT_ROOT = Path(__file__).resolve().parent.parent

KB_JSON_PATH = _PROJECT_ROOT / "knowledge-base" / "alamo_engine_kb_v3.json"
RE_FINDINGS_DIR = _PROJECT_ROOT / "re-findings"
VERIFIED_RVAS_PATH = _PROJECT_ROOT / "knowledge-base" / "VERIFIED_RVAS_v3.md"
LUA_API_PATH = _PROJECT_ROOT / "knowledge-base" / "GAME_LUA_API.md"

DEFAULT_OUTPUT_DIR = _PROJECT_ROOT / "docs"


# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

def _load_json(path: Path) -> dict | None:
    """Load a JSON file, returning None on any error."""
    try:
        with open(path, "r", encoding="utf-8") as fh:
            return json.load(fh)
    except (OSError, json.JSONDecodeError, UnicodeDecodeError):
        return None


def _read_text(path: Path) -> str:
    """Read a text file, returning empty string on error."""
    try:
        return path.read_text(encoding="utf-8")
    except OSError:
        return ""


def _slugify(name: str) -> str:
    """Turn a struct/system name into a filename-safe slug."""
    return re.sub(r"[^a-z0-9]+", "-", name.lower()).strip("-")


def _struct_link(name: str) -> str:
    """Return a relative Markdown link to a struct page (from any docs/ page)."""
    slug = _slugify(name)
    return f"[{name}](structs/{slug}.md)"


def _write(path: Path, content: str) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding="utf-8")


# ---------------------------------------------------------------------------
# Struct name cross-link replacer
# ---------------------------------------------------------------------------

def _crosslink(text: str, known_structs: set[str], current_struct: str = "") -> str:
    """Replace known struct names in *text* with markdown links.

    Avoids self-links when *current_struct* matches.
    Only replaces whole-word occurrences that are NOT already inside a link.
    """
    for name in sorted(known_structs, key=len, reverse=True):
        if name == current_struct:
            continue
        # Match whole word, but not if preceded by [ or followed by ]
        pattern = rf"(?<!\[)\b{re.escape(name)}\b(?!\])"
        replacement = f"[{name}](../structs/{_slugify(name)}.md)"
        text = re.sub(pattern, replacement, text, count=1)
    return text


# ---------------------------------------------------------------------------
# Page generators
# ---------------------------------------------------------------------------

def generate_struct_page(
    name: str,
    data: dict[str, Any],
    known_structs: set[str],
) -> str:
    """Generate a full Markdown page for one struct."""
    lines: list[str] = []
    lines.append(f"# {name}\n")

    # Metadata
    if rtti := data.get("rtti_mangled_name"):
        lines.append(f"**RTTI**: `{rtti}`\n")
    if vtable := data.get("vtable_rva"):
        lines.append(f"**VTable RVA**: `{vtable}`\n")
    if inherits := data.get("inherits"):
        linked = [_struct_link(c) if c in known_structs else f"`{c}`" for c in inherits]
        lines.append(f"**Inherits**: {', '.join(linked)}\n")
    if size := data.get("estimated_size") or data.get("minimum_size"):
        hex_size = data.get("estimated_size_hex", "")
        size_str = f"{size}" + (f" ({hex_size})" if hex_size else "")
        lines.append(f"**Size**: {size_str} bytes\n")
    if desc := data.get("description"):
        lines.append(f"\n{desc}\n")

    # Notes
    if notes := data.get("notes"):
        lines.append("\n## Notes\n")
        for note in notes:
            lines.append(f"- {note}")
        lines.append("")

    # Fields table
    fields = data.get("fields", {})
    if fields:
        lines.append("\n## Fields\n")
        lines.append("| Offset | Type | Name | Status / Confidence | Notes |")
        lines.append("|--------|------|------|---------------------|-------|")

        field_items = fields
        # Handle both dict-of-dicts (KB) and list-of-dicts (RE findings)
        if isinstance(fields, list):
            field_items = {f.get("offset", "?"): f for f in fields}

        for offset, fdata in field_items.items():
            if isinstance(fdata, dict):
                ftype = fdata.get("type", "?")
                fname = fdata.get("name", "?")
                status = fdata.get("status", fdata.get("confidence", ""))
                note = fdata.get("note", fdata.get("description", ""))
                if values := fdata.get("values"):
                    note += " Values: " + ", ".join(
                        f"{k}={v}" for k, v in values.items()
                    )
                # Cross-link struct references in type
                note = _crosslink(str(note), known_structs, name)
                lines.append(f"| `{offset}` | `{ftype}` | {fname} | {status} | {note} |")
            else:
                lines.append(f"| `{offset}` | | {fdata} | | |")

    # Nested sub-objects (e.g. CameraClass has outer + sub_object)
    for section_key in ("outer", "sub_object"):
        if section_key in (fields or {}):
            section = fields[section_key]
            if isinstance(section, dict):
                lines.append(f"\n### {section_key}\n")
                lines.append("| Offset | Type | Name | Notes |")
                lines.append("|--------|------|------|-------|")
                for off, fd in section.items():
                    if isinstance(fd, dict):
                        lines.append(
                            f"| `{off}` | `{fd.get('type', '?')}` "
                            f"| {fd.get('name', '?')} "
                            f"| {fd.get('note', fd.get('default', ''))} |"
                        )

    # Key offsets (alt format used by some structs)
    if key_offsets := data.get("key_offsets"):
        if isinstance(key_offsets, dict):
            lines.append("\n## Key Offsets\n")
            lines.append("| Offset | Type | Name |")
            lines.append("|--------|------|------|")
            for off, odata in key_offsets.items():
                if isinstance(odata, dict):
                    lines.append(
                        f"| `{off}` | `{odata.get('type', '?')}` | {odata.get('name', '?')} |"
                    )
                else:
                    lines.append(f"| `{off}` | | {odata} |")

    # Lua methods
    if lua_methods := data.get("lua_methods"):
        lines.append("\n## Lua Methods\n")
        lines.append("| Method | Wrapper RVA | Engine RVA |")
        lines.append("|--------|-------------|------------|")
        for method_name, mdata in lua_methods.items():
            wrapper = mdata.get("wrapper_rva", "")
            engine = mdata.get("engine_rva", "")
            lines.append(f"| `{method_name}` | `{wrapper}` | `{engine}` |")

    # QueryInterface types
    if qi := data.get("query_interface_types"):
        lines.append("\n## QueryInterface Types\n")
        lines.append("| ID | Class |")
        lines.append("|----|-------|")
        for qid, qclass in qi.items():
            lines.append(f"| `{qid}` | {qclass} |")

    lines.append("")
    return "\n".join(lines)


def generate_system_page(name: str, kb_data: dict, re_json: dict | None) -> str:
    """Generate a Markdown page for one game subsystem."""
    lines: list[str] = []
    pretty_name = name.replace("_", " ").title()
    lines.append(f"# {pretty_name}\n")

    # From KB subsystems section
    if kb_data:
        for key, val in kb_data.items():
            if isinstance(val, str):
                lines.append(f"**{key.replace('_', ' ').title()}**: {val}\n")
            elif isinstance(val, (int, float)):
                lines.append(f"**{key.replace('_', ' ').title()}**: {val}\n")
            elif isinstance(val, list):
                lines.append(f"\n### {key.replace('_', ' ').title()}\n")
                for item in val:
                    if isinstance(item, dict):
                        parts = [f"{k}: {v}" for k, v in item.items()]
                        lines.append(f"- {'; '.join(parts)}")
                    else:
                        lines.append(f"- {item}")
                lines.append("")
            elif isinstance(val, dict):
                lines.append(f"\n### {key.replace('_', ' ').title()}\n")
                lines.append("| Key | Value |")
                lines.append("|-----|-------|")
                for k, v in val.items():
                    lines.append(f"| {k} | {v} |")
                lines.append("")

    # From RE findings JSON
    if re_json:
        lines.append("\n## RE Findings Detail\n")
        title = re_json.get("title", "")
        desc = re_json.get("description", "")
        if title:
            lines.append(f"**Analysis**: {title}\n")
        if desc:
            lines.append(f"{desc}\n")

        meta = re_json.get("_meta", {})
        if meta:
            date = meta.get("analysis_date", meta.get("date", ""))
            analyst = meta.get("analyst", "")
            if date:
                lines.append(f"- **Date**: {date}")
            if analyst:
                lines.append(f"- **Analyst**: {analyst}")
            lines.append("")

        # Dump top-level sections (skip meta and schema)
        for key, val in re_json.items():
            if key.startswith("$") or key.startswith("_"):
                continue
            if key in ("title", "description"):
                continue
            lines.append(f"\n### {key.replace('_', ' ').title()}\n")
            if isinstance(val, dict):
                _render_nested_dict(lines, val, depth=0)
            elif isinstance(val, list):
                for item in val:
                    if isinstance(item, dict):
                        parts = [f"**{k}**: {v}" for k, v in item.items() if not str(v).startswith("{")]
                        lines.append(f"- {'; '.join(parts)}")
                    else:
                        lines.append(f"- {item}")
            else:
                lines.append(f"{val}")
            lines.append("")

    lines.append("")
    return "\n".join(lines)


def _render_nested_dict(lines: list[str], d: dict, depth: int) -> None:
    """Recursively render a dict as nested markdown lists."""
    indent = "  " * depth
    for key, val in d.items():
        if isinstance(val, dict):
            lines.append(f"{indent}- **{key}**:")
            _render_nested_dict(lines, val, depth + 1)
        elif isinstance(val, list):
            lines.append(f"{indent}- **{key}**: {', '.join(str(v) for v in val)}")
        else:
            lines.append(f"{indent}- **{key}**: {val}")


def generate_lua_api_page(lua_md_content: str) -> str:
    """Generate the Lua API docs page (mostly pass-through of the existing MD)."""
    lines: list[str] = []
    lines.append("# SWFOC Lua API Reference\n")
    lines.append(
        "This page documents all 405 Lua functions available in Star Wars: "
        "Empire at War -- Forces of Corruption.\n"
    )
    lines.append("See also: [RVA Table](rvas.md) | [Struct Reference](../index.md)\n")
    lines.append("---\n")

    # Strip the original title/header (first two lines) and insert the rest
    content_lines = lua_md_content.split("\n")
    skip = 0
    for i, line in enumerate(content_lines):
        if line.startswith("# "):
            skip = i + 1
            # Skip subtitle line too
            if skip < len(content_lines) and content_lines[skip].startswith("## "):
                skip += 1
            break
    lines.extend(content_lines[skip:])
    lines.append("")
    return "\n".join(lines)


def generate_rvas_page(rvas_md_content: str) -> str:
    """Generate the RVA reference page (pass-through with added nav)."""
    lines: list[str] = []
    lines.append("# Complete RVA Reference\n")
    lines.append("See also: [Lua API](lua-api.md) | [Struct Reference](index.md)\n")
    lines.append("---\n")

    # Strip original title
    content_lines = rvas_md_content.split("\n")
    skip = 0
    for i, line in enumerate(content_lines):
        if line.startswith("# "):
            skip = i + 1
            break
    lines.extend(content_lines[skip:])
    lines.append("")
    return "\n".join(lines)


def generate_index_page(
    struct_names: list[str],
    system_names: list[str],
    meta: dict,
) -> str:
    """Generate the top-level index.md."""
    lines: list[str] = []
    lines.append("# SWFOC Reverse Engineering Knowledge Base\n")
    lines.append(
        "Documentation for the Alamo engine as used in Star Wars: Empire at War "
        "-- Forces of Corruption (64-bit Steam build).\n"
    )

    # Meta
    if meta:
        lines.append("## Binary Info\n")
        module = meta.get("module", {})
        lines.append(f"- **Module**: {module.get('name', 'StarWarsG.exe')}")
        lines.append(f"- **Architecture**: {module.get('architecture', 'x86_64')}")
        lines.append(f"- **Compiler**: {module.get('compiler', 'MSVC')}")
        lines.append(f"- **Ghidra Base**: `{module.get('base_address_ghidra', '0x140000000')}`")
        lines.append(f"- **Lua Version**: {meta.get('lua_version', '5.0.2')}")
        lines.append(f"- **RTTI Classes**: {meta.get('rtti_total_classes', '?')}")
        lines.append("")

    # Structs
    lines.append("## Structs\n")
    for name in sorted(struct_names):
        slug = _slugify(name)
        lines.append(f"- [{name}](structs/{slug}.md)")
    lines.append("")

    # Systems
    lines.append("## Game Systems\n")
    for name in sorted(system_names):
        slug = _slugify(name)
        pretty = name.replace("_", " ").title()
        lines.append(f"- [{pretty}](systems/{slug}.md)")
    lines.append("")

    # Reference pages
    lines.append("## Reference\n")
    lines.append("- [Lua API (405 functions)](lua-api.md)")
    lines.append("- [RVA Table (280+ addresses)](rvas.md)")
    lines.append("")

    return "\n".join(lines)


def generate_mkdocs_yml(
    struct_names: list[str],
    system_names: list[str],
    output_dir: Path,
) -> str:
    """Generate mkdocs.yml configuration."""
    lines: list[str] = []
    lines.append("site_name: SWFOC RE Knowledge Base")
    lines.append("site_description: Reverse engineering documentation for Star Wars Empire at War - Forces of Corruption")
    lines.append("theme:")
    lines.append("  name: material")
    lines.append("  palette:")
    lines.append("    scheme: slate")
    lines.append("    primary: deep purple")
    lines.append("    accent: amber")
    lines.append("  features:")
    lines.append("    - navigation.sections")
    lines.append("    - navigation.expand")
    lines.append("    - search.highlight")
    lines.append("    - content.code.copy")
    lines.append("")
    lines.append("markdown_extensions:")
    lines.append("  - tables")
    lines.append("  - toc:")
    lines.append("      permalink: true")
    lines.append("  - pymdownx.highlight")
    lines.append("  - pymdownx.superfences")
    lines.append("")
    lines.append("nav:")
    lines.append("  - Home: index.md")
    lines.append("  - Structs:")

    for name in sorted(struct_names):
        slug = _slugify(name)
        lines.append(f"    - {name}: structs/{slug}.md")

    lines.append("  - Game Systems:")
    for name in sorted(system_names):
        slug = _slugify(name)
        pretty = name.replace("_", " ").title()
        lines.append(f"    - {pretty}: systems/{slug}.md")

    lines.append("  - Reference:")
    lines.append("    - Lua API: lua-api.md")
    lines.append("    - RVA Table: rvas.md")
    lines.append("")

    return "\n".join(lines)


# ---------------------------------------------------------------------------
# Orchestrator
# ---------------------------------------------------------------------------

def _match_re_findings(
    system_name: str,
    re_files: dict[str, dict],
) -> tuple[str | None, dict | None]:
    """Find the best matching RE findings JSON for a subsystem name.

    Returns (matched_key, data) so the caller can track which keys were consumed.
    """
    # Direct match
    if system_name in re_files:
        return system_name, re_files[system_name]

    # Try common suffixes
    candidates = [
        f"{system_name}_system",
        f"{system_name}_complete",
        system_name.replace("_", ""),
    ]
    for c in candidates:
        if c in re_files:
            return c, re_files[c]

    # Fuzzy: system name appears in filename
    for fname, data in re_files.items():
        if system_name.replace("_", "") in fname.replace("_", ""):
            return fname, data

    return None, None


def generate_docs(output_dir: Path | None = None) -> dict[str, Any]:
    """Main entry point. Returns a summary dict."""
    out = output_dir or DEFAULT_OUTPUT_DIR

    # Load KB
    kb = _load_json(KB_JSON_PATH)
    if kb is None:
        print(f"ERROR: Could not load {KB_JSON_PATH}", file=sys.stderr)
        return {"error": "KB JSON not found"}

    meta = kb.get("_meta", {})
    structs = kb.get("structs", {})
    globals_data = kb.get("globals", {})
    functions_data = kb.get("functions", {})
    subsystems = kb.get("subsystems", {})

    # Load RE findings
    re_files: dict[str, dict] = {}
    if RE_FINDINGS_DIR.is_dir():
        for f in RE_FINDINGS_DIR.glob("*.json"):
            data = _load_json(f)
            if data is not None:
                re_files[f.stem] = data

    # Load markdown sources
    rvas_md = _read_text(VERIFIED_RVAS_PATH)
    lua_md = _read_text(LUA_API_PATH)

    known_structs = set(structs.keys())

    # Also load struct data from RE findings complete JSONs
    re_struct_data: dict[str, dict] = {}
    for fname, data in re_files.items():
        if fname.endswith("_complete"):
            # These have a "struct" or top-level struct info
            if "struct" in data:
                sname = data["struct"].get("name", "")
                if sname:
                    re_struct_data[sname] = data["struct"]
                    known_structs.add(sname)
            elif "struct_name" in data:
                sname = data["struct_name"]
                re_struct_data[sname] = data
                known_structs.add(sname)

    # ---- Generate struct pages ----
    structs_dir = out / "structs"
    struct_names = []
    for name, sdata in structs.items():
        # Merge RE findings data if available
        merged = dict(sdata)
        if name in re_struct_data:
            re = re_struct_data[name]
            # Prefer RE findings fields list if richer
            if "fields" in re and isinstance(re["fields"], list) and len(re["fields"]) > len(sdata.get("fields", {})):
                merged["fields"] = {f.get("offset", "?"): f for f in re["fields"]}
        page = generate_struct_page(name, merged, known_structs)
        _write(structs_dir / f"{_slugify(name)}.md", page)
        struct_names.append(name)

    # Structs only in RE findings (not in main KB)
    for name, sdata in re_struct_data.items():
        if name not in structs:
            page = generate_struct_page(name, sdata, known_structs)
            _write(structs_dir / f"{_slugify(name)}.md", page)
            struct_names.append(name)

    # ---- Generate system pages ----
    systems_dir = out / "systems"
    system_names = []
    consumed_re_keys: set[str] = set()
    for name, sys_data in subsystems.items():
        matched_key, re_data = _match_re_findings(name, re_files)
        if matched_key is not None:
            consumed_re_keys.add(matched_key)
        page = generate_system_page(name, sys_data, re_data)
        _write(systems_dir / f"{_slugify(name)}.md", page)
        system_names.append(name)

    # Systems only in RE findings (not already consumed by a KB subsystem)
    for fname, data in re_files.items():
        # Skip struct files, the map file, and already-consumed files
        if fname.endswith("_complete") or fname == "game_systems_map":
            continue
        if fname in consumed_re_keys:
            continue
        page = generate_system_page(fname, {}, data)
        _write(systems_dir / f"{_slugify(fname)}.md", page)
        system_names.append(fname)

    # ---- Generate Lua API page ----
    if lua_md:
        page = generate_lua_api_page(lua_md)
        _write(out / "lua-api.md", page)
    else:
        _write(out / "lua-api.md", "# Lua API\n\nSource file not found.\n")

    # ---- Generate RVA page ----
    if rvas_md:
        page = generate_rvas_page(rvas_md)
        _write(out / "rvas.md", page)
    else:
        _write(out / "rvas.md", "# RVA Reference\n\nSource file not found.\n")

    # ---- Generate index ----
    index = generate_index_page(struct_names, system_names, meta)
    _write(out / "index.md", index)

    # ---- Generate mkdocs.yml ----
    mkdocs_yml = generate_mkdocs_yml(struct_names, system_names, out)
    _write(out.parent / "mkdocs.yml", mkdocs_yml)

    summary = {
        "output_dir": str(out),
        "struct_pages": len(struct_names),
        "system_pages": len(system_names),
        "has_lua_api": bool(lua_md),
        "has_rvas": bool(rvas_md),
        "re_findings_loaded": len(re_files),
    }
    return summary


# ---------------------------------------------------------------------------
# CLI
# ---------------------------------------------------------------------------

def main() -> None:
    output_dir = None
    if "--output-dir" in sys.argv:
        idx = sys.argv.index("--output-dir")
        if idx + 1 < len(sys.argv):
            output_dir = Path(sys.argv[idx + 1])

    print("SWFOC KB Documentation Generator")
    print("=================================")
    result = generate_docs(output_dir)

    if "error" in result:
        print(f"FAILED: {result['error']}", file=sys.stderr)
        sys.exit(1)

    print(f"Output directory: {result['output_dir']}")
    print(f"Struct pages:     {result['struct_pages']}")
    print(f"System pages:     {result['system_pages']}")
    print(f"Lua API page:     {'yes' if result['has_lua_api'] else 'no'}")
    print(f"RVA table page:   {'yes' if result['has_rvas'] else 'no'}")
    print(f"RE findings:      {result['re_findings_loaded']} files loaded")
    print("Done.")


if __name__ == "__main__":
    main()
