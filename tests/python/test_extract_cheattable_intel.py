"""Tests for tools/extract-cheattable-intel.py."""

from __future__ import annotations

import json
from pathlib import Path
from xml.etree.ElementTree import fromstring

import pytest
from conftest import load_script_module

mod = load_script_module("tools/extract-cheattable-intel.py", "extract_cheattable_intel")


def test_normalize_description() -> None:
    assert mod.normalize_description(None) == ""
    assert mod.normalize_description('  "Quoted"  ') == "Quoted"
    assert mod.normalize_description("plain") == "plain"


def test_technique_from_script_code_cave_override() -> None:
    script = "alloc(newmem,1024)\njmp newmem\n"
    writes = [mod.ConstantWrite("addr", "int", "5")]
    technique, notes = mod.technique_from_script(script, writes)
    assert technique == "code_cave_override"
    assert notes


def test_technique_from_script_branch_bypass() -> None:
    technique, _ = mod.technique_from_script("remove the jump\n", [])
    assert technique == "branch_bypass_patch"
    technique2, _ = mod.technique_from_script("xyz\nnop\n", [])
    assert technique2 == "branch_bypass_patch"


def test_technique_from_script_code_cave_patch() -> None:
    technique, _ = mod.technique_from_script("alloc(newmem,1)\njmp newmem\n", [])
    assert technique == "code_cave_patch"


def test_technique_from_script_direct() -> None:
    technique, notes = mod.technique_from_script("mov eax, ebx\n", [])
    assert technique == "direct_patch" and notes == []


def test_mapping_from_description() -> None:
    assert "build" in mod.mapping_from_description("Instant Build")
    assert "credits" in mod.mapping_from_description("Infinite Credits")
    assert "fog" in mod.mapping_from_description("Maphack")
    assert mod.mapping_from_description("Max Unit Cap") == "future:set_unit_cap"
    assert mod.mapping_from_description("random") == "unmapped"


def test_parse_restore_bytes() -> None:
    script = "[ENABLE]\ndb 90 90\n[DISABLE]\ndb 8B 45 FC\nmov eax,1\n"
    assert mod.parse_restore_bytes(script) == ["8B 45 FC"]


def test_extract_intel_from_script_full() -> None:
    script = (
        "aobscanmodule(sym,game.exe,8B 45 ?? FC)\n"
        "INJECTION POINT: 0x401000\n"
        "mov [eax],(int)5\n"
        "[DISABLE]\ndb 90\n"
    )
    intel = mod.extract_intel_from_script("grp", "Infinite Credits", script)
    assert intel.aob_scans[0].symbol == "sym"
    assert intel.injection_points == ["0x401000"]
    assert intel.constant_writes[0].value == "5"
    assert intel.disable_restore_bytes == ["90"]


def test_extract_intel_no_scan_adds_note() -> None:
    intel = mod.extract_intel_from_script("g", "Some Donate paypal entry", "mov eax,1\n")
    assert any("No aobscanmodule" in n for n in intel.notes)
    assert any("Non-gameplay" in n for n in intel.notes)


def test_iter_cheat_entries_group_and_flat() -> None:
    xml = """<CheatTable><CheatEntries>
      <CheatEntry><Description>"Group A"</Description><GroupHeader>1</GroupHeader>
        <CheatEntries>
          <CheatEntry><Description>"Child"</Description></CheatEntry>
        </CheatEntries>
      </CheatEntry>
      <CheatEntry><Description>"Flat"</Description></CheatEntry>
    </CheatEntries></CheatTable>"""
    root = fromstring(xml)
    entries = list(mod.iter_cheat_entries(root))
    groups = {g for g, _ in entries}
    assert "Group A" in groups
    assert "" in groups  # flat entry


def test_iter_cheat_entries_no_top() -> None:
    root = fromstring("<CheatTable></CheatTable>")
    assert list(mod.iter_cheat_entries(root)) == []


def test_iter_cheat_entries_group_without_nested() -> None:
    xml = """<CheatTable><CheatEntries>
      <CheatEntry><Description>"G"</Description><GroupHeader>1</GroupHeader></CheatEntry>
    </CheatEntries></CheatTable>"""
    root = fromstring(xml)
    assert list(mod.iter_cheat_entries(root)) == []


def test_dedupe_intel() -> None:
    a = mod.ScriptIntel(group="g", description="Same", technique="direct_patch")
    b = mod.ScriptIntel(group="g", description="same", technique="direct_patch")
    assert len(mod.dedupe_intel([a, b])) == 1


def test_should_skip_entry() -> None:
    assert mod.should_skip_entry("", "Auto Assembler Script") is True
    assert mod.should_skip_entry("Donate here", "Auto Assembler Script") is True
    assert mod.should_skip_entry("Real", "Other Type") is True
    assert mod.should_skip_entry("Real", "Auto Assembler Script") is False


def test_collect_script_intel() -> None:
    xml = """<CheatTable><CheatEntries>
      <CheatEntry><Description>"Infinite Credits"</Description>
        <VariableType>Auto Assembler Script</VariableType>
        <AssemblerScript>aobscanmodule(s,m,90)</AssemblerScript>
      </CheatEntry>
      <CheatEntry><Description>"Skip Me"</Description>
        <VariableType>Other</VariableType>
      </CheatEntry>
    </CheatEntries></CheatTable>"""
    root = fromstring(xml)
    records = mod.collect_script_intel(root)
    assert len(records) == 1
    assert records[0].description == "Infinite Credits"


def test_render_markdown_full() -> None:
    intel = mod.ScriptIntel(
        group="grp",
        description="Infinite Credits",
        technique="code_cave_override",
        aob_scans=[mod.AobScan("s", "m", "90 90")],
        injection_points=["0x1"],
        constant_writes=[mod.ConstantWrite("addr", "int", "5")],
        disable_restore_bytes=["90"],
        trainer_mapping="set_credits",
        notes=["a note"],
    )
    md = mod.render_markdown([intel], Path("StarWarsG.CT"))
    assert "Cheat Table Intelligence" in md
    assert "Injection points" in md
    assert "AOB scans" in md
    assert "Constant writes" in md
    assert "Disable restore bytes" in md
    assert "Notes" in md


def test_render_markdown_minimal() -> None:
    intel = mod.ScriptIntel(group="", description="X", technique="direct_patch")
    md = mod.render_markdown([intel], Path("f.CT"))
    assert "X" in md
    # optional sections omitted when empty
    assert "Injection points" not in md


def test_main_ok(tmp_path: Path, monkeypatch, capsys) -> None:
    ct = tmp_path / "table.CT"
    ct.write_text(
        """<CheatTable><CheatEntries>
          <CheatEntry><Description>"Infinite Credits"</Description>
            <VariableType>Auto Assembler Script</VariableType>
            <AssemblerScript>aobscanmodule(s,game.exe,90 90)</AssemblerScript>
          </CheatEntry>
        </CheatEntries></CheatTable>""",
        encoding="utf-8",
    )
    out_json = tmp_path / "out" / "intel.json"
    out_md = tmp_path / "out" / "intel.md"
    monkeypatch.setattr(
        "sys.argv",
        ["e.py", "--ct", str(ct), "--out-json", str(out_json), "--out-md", str(out_md)],
    )
    assert mod.main() == 0
    payload = json.loads(out_json.read_text(encoding="utf-8"))
    assert payload["entryCount"] == 1
    assert out_md.exists()
    assert "Wrote 1 extracted entries" in capsys.readouterr().out


def test_main_missing_ct(tmp_path: Path, monkeypatch) -> None:
    monkeypatch.setattr("sys.argv", ["e.py", "--ct", str(tmp_path / "nope.CT")])
    with pytest.raises(FileNotFoundError, match="Cheat table not found"):
        mod.main()


def test_main_no_root(tmp_path: Path, monkeypatch) -> None:
    ct = tmp_path / "empty.CT"
    ct.write_text("<CheatTable/>", encoding="utf-8")
    monkeypatch.setattr("sys.argv", ["e.py", "--ct", str(ct)])

    class _NoRootTree:
        def getroot(self):
            return None

    monkeypatch.setattr(mod.element_tree, "parse", lambda _p: _NoRootTree())
    with pytest.raises(ValueError, match="no root element"):
        mod.main()
