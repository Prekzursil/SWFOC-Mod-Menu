# export_rtti.py — Ghidra Python postScript (PyGhidra / Jython compatible)
# Runs on an already-analyzed Ghidra database to export RTTI-recovered class data to JSON.
# Output: C:/Users/Prekzursil/Downloads/swfoc_memory/rtti_export_raw.json

import json
import os
from datetime import datetime

OUTPUT_PATH = "C:/Users/Prekzursil/Downloads/swfoc_memory/rtti_export_raw.json"

def get_image_base():
    return currentProgram.getImageBase().getOffset()

def get_rva(addr):
    return addr.getOffset() - get_image_base()

def hex_rva(addr):
    return "0x{:X}".format(get_rva(addr))

def hex_addr(addr):
    return "0x{:X}".format(addr.getOffset())

def safe_str(s):
    if s is None:
        return ""
    return str(s)

def export_namespaces_and_classes():
    """Find all class namespaces created by RTTI recovery."""
    sym_table = currentProgram.getSymbolTable()
    classes = {}  # namespace_name -> {info}

    # Iterate all symbols looking for vftable, RTTI, TypeDescriptor labels
    sym_iter = sym_table.getAllSymbols(False)
    count = 0
    while sym_iter.hasNext():
        sym = sym_iter.next()
        name = sym.getName()
        if name is None:
            continue

        ns = sym.getParentNamespace()
        if ns is None or ns.isGlobal():
            continue

        ns_name = ns.getName(True)

        # Track class-related symbols
        is_vftable = "vftable" in name.lower()
        is_rtti = "RTTI" in name or "TypeDescriptor" in name or "type_info" in name
        is_col = "CompleteObjectLocator" in name or "BaseClassArray" in name or "ClassHierarchyDescriptor" in name

        if is_vftable or is_rtti or is_col:
            if ns_name not in classes:
                classes[ns_name] = {
                    "name": ns_name,
                    "vftables": [],
                    "type_descriptors": [],
                    "rtti_symbols": [],
                    "methods": []
                }

            entry = {
                "symbol": safe_str(name),
                "address": hex_addr(sym.getAddress()),
                "rva": hex_rva(sym.getAddress()),
                "type": str(sym.getSymbolType())
            }

            if is_vftable:
                classes[ns_name]["vftables"].append(entry)
            elif is_rtti:
                classes[ns_name]["type_descriptors"].append(entry)
            else:
                classes[ns_name]["rtti_symbols"].append(entry)

        count += 1
        if count % 50000 == 0:
            println("  Processed {} symbols...".format(count))

    println("Found {} class namespaces from {} symbols".format(len(classes), count))
    return classes


def export_vftable_entries(classes):
    """Read vtable function pointer entries for each class."""
    mem = currentProgram.getMemory()
    func_mgr = currentProgram.getFunctionManager()
    addr_factory = currentProgram.getAddressFactory()
    image_base = get_image_base()

    for cls_name, cls_info in classes.items():
        for vft in cls_info["vftables"]:
            entries = []
            try:
                addr = addr_factory.getDefaultAddressSpace().getAddress(int(vft["address"], 16))
                for i in range(200):  # safety limit
                    entry_addr = addr.add(i * 8)
                    if not mem.contains(entry_addr):
                        break
                    func_ptr = mem.getLong(entry_addr)
                    if func_ptr == 0:
                        break

                    func_addr = addr_factory.getDefaultAddressSpace().getAddress(func_ptr)
                    func = func_mgr.getFunctionAt(func_addr)

                    if func is not None:
                        func_rva = func_ptr - image_base
                        entries.append({
                            "index": i,
                            "name": func.getName(True),
                            "rva": "0x{:X}".format(func_rva),
                            "signature": safe_str(func.getSignature().getPrototypeString(False))
                        })
                    else:
                        # Check if it's in a code section
                        block = mem.getBlock(func_addr)
                        if block is None or not block.isExecute():
                            break
                        entries.append({
                            "index": i,
                            "name": None,
                            "rva": "0x{:X}".format(func_ptr - image_base)
                        })
            except Exception as e:
                entries.append({"error": str(e)})

            vft["entries"] = entries
            vft["entry_count"] = len(entries)


def export_type_descriptor_names(classes):
    """Read mangled names from TypeDescriptor structures."""
    mem = currentProgram.getMemory()
    addr_factory = currentProgram.getAddressFactory()

    for cls_name, cls_info in classes.items():
        for td in cls_info["type_descriptors"]:
            try:
                addr = addr_factory.getDefaultAddressSpace().getAddress(int(td["address"], 16))
                # MSVC x64 TypeDescriptor: [8 vfptr][8 spare][N name]
                name_addr = addr.add(16)
                name_bytes = bytearray(256)
                actual = mem.getBytes(name_addr, name_bytes)
                end = 0
                for j in range(actual):
                    if name_bytes[j] == 0:
                        end = j
                        break
                if end > 0:
                    mangled = bytes(name_bytes[:end]).decode('ascii', errors='replace')
                    td["mangled_name"] = mangled
                    td["demangled_name"] = demangle_msvc(mangled)
            except Exception as e:
                td["mangled_name_error"] = str(e)


def demangle_msvc(mangled):
    """Basic MSVC RTTI name demangling: .?AVFoo@Bar@@ -> Bar::Foo"""
    if not mangled:
        return ""
    s = mangled
    # Remove leading .?AV or .?AU (class vs struct)
    for prefix in [".?AV", ".?AU", ".?AW4"]:
        if s.startswith(prefix):
            s = s[len(prefix):]
            break
    # Remove trailing @@
    while s.endswith("@@"):
        s = s[:-2]
    # Split on @ and reverse (MSVC stores inner-to-outer)
    parts = s.split("@")
    parts = [p for p in parts if p]
    parts.reverse()
    return "::".join(parts)


def export_class_functions(classes):
    """Find all functions within class namespaces."""
    func_mgr = currentProgram.getFunctionManager()
    func_iter = func_mgr.getFunctions(True)
    count = 0

    while func_iter.hasNext():
        func = func_iter.next()
        ns = func.getParentNamespace()
        if ns is None or ns.isGlobal():
            continue

        ns_name = ns.getName(True)
        if ns_name in classes:
            addr = func.getEntryPoint()
            method = {
                "name": func.getName(),
                "full_name": func.getName(True),
                "rva": hex_rva(addr),
                "signature": safe_str(func.getSignature().getPrototypeString(False)),
                "is_thunk": func.isThunk(),
                "body_size": func.getBody().getNumAddresses()
            }
            classes[ns_name]["methods"].append(method)
            count += 1

    println("Found {} class methods".format(count))


def export_all_named_functions():
    """Export all functions with real names (not FUN_xxx)."""
    func_mgr = currentProgram.getFunctionManager()
    func_iter = func_mgr.getFunctions(True)
    functions = []
    count = 0

    while func_iter.hasNext():
        func = func_iter.next()
        name = func.getName()
        if name is None or name.startswith("FUN_"):
            continue

        ns = func.getParentNamespace()
        addr = func.getEntryPoint()

        functions.append({
            "name": name,
            "full_name": func.getName(True),
            "namespace": safe_str(ns.getName(True)) if ns and not ns.isGlobal() else "global",
            "rva": hex_rva(addr),
            "signature": safe_str(func.getSignature().getPrototypeString(False)),
            "is_thunk": func.isThunk(),
            "body_size": func.getBody().getNumAddresses()
        })
        count += 1
        if count > 100000:
            break

    println("Found {} named functions".format(count))
    return functions


def export_interesting_strings():
    """Export strings containing game-engine keywords."""
    keywords = [
        ".?AV", ".?AU", ".?AW4",
        "Class", "Object", "Manager", "Lua", "lua_",
        "Game", "Player", "Unit", "Damage", "Health", "Shield",
        "AI_", "Behavior", "State", "Property", "Component",
        "Hardpoint", "Weapon", "Projectile", "Faction", "Team",
        "Map", "Planet", "Fleet", "Spawn", "Reinforce",
        "Galactic", "Tactical", "Space", "Transport",
        "alamo", "Alamo", "Petroglyph"
    ]

    listing = currentProgram.getListing()
    data_iter = listing.getDefinedData(True)
    strings = []
    seen = set()
    count = 0

    while data_iter.hasNext() and count < 15000:
        data = data_iter.next()
        if not data.hasStringValue():
            continue

        val = data.getDefaultValueRepresentation()
        if val is None:
            continue

        # Remove quotes
        if val.startswith('"'):
            val = val[1:]
        if val.endswith('"'):
            val = val[:-1]

        if len(val) < 3 or val in seen:
            continue

        interesting = False
        for kw in keywords:
            if kw in val:
                interesting = True
                break

        if interesting:
            seen.add(val)
            addr = data.getAddress()
            strings.append({
                "value": val,
                "rva": hex_rva(addr)
            })
            count += 1

    println("Found {} interesting strings".format(count))
    return strings


def get_function_stats():
    """Get overview statistics."""
    func_mgr = currentProgram.getFunctionManager()
    total = 0
    named = 0
    thunks = 0
    in_namespace = 0

    func_iter = func_mgr.getFunctions(True)
    while func_iter.hasNext():
        func = func_iter.next()
        total += 1
        if not func.getName().startswith("FUN_"):
            named += 1
        if func.isThunk():
            thunks += 1
        ns = func.getParentNamespace()
        if ns and not ns.isGlobal():
            in_namespace += 1

    return {
        "total_functions": total,
        "named_functions": named,
        "unnamed_functions": total - named,
        "thunks": thunks,
        "in_class_namespace": in_namespace
    }


# === MAIN ===
println("=== ExportRTTI Python Script Starting ===")
println("Program: " + currentProgram.getName())
println("Image base: 0x{:X}".format(get_image_base()))

# 1. Collect class namespaces
println("[1/6] Collecting class namespaces...")
classes = export_namespaces_and_classes()

# 2. Read vtable entries
println("[2/6] Reading vtable entries...")
export_vftable_entries(classes)

# 3. Read type descriptor mangled names
println("[3/6] Reading type descriptor names...")
export_type_descriptor_names(classes)

# 4. Collect class methods
println("[4/6] Collecting class methods...")
export_class_functions(classes)

# 5. Collect all named functions
println("[5/6] Collecting all named functions...")
all_functions = export_all_named_functions()

# 6. Collect interesting strings
println("[6/6] Collecting interesting strings...")
strings = export_interesting_strings()

# 7. Get statistics
println("Computing statistics...")
stats = get_function_stats()

# Build final output
output = {
    "_meta": {
        "program": currentProgram.getName(),
        "image_base": "0x{:X}".format(get_image_base()),
        "analyzer": "Ghidra (RecoverClassesFromRTTIScript + export_rtti.py)",
        "export_date": datetime.now().strftime("%Y-%m-%dT%H:%M:%S"),
        "binary_path": "D:/SteamLibrary/steamapps/common/Star Wars Empire at War/corruption/StarWarsG.exe"
    },
    "stats": stats,
    "classes": classes,
    "named_functions": all_functions,
    "interesting_strings": strings
}

# Write JSON
println("Writing output to: " + OUTPUT_PATH)
with open(OUTPUT_PATH, 'w') as f:
    json.dump(output, f, indent=2, default=str)

println("=== Export complete! ===")
println("Classes: {}".format(len(classes)))
println("Named functions: {}".format(len(all_functions)))
println("Interesting strings: {}".format(len(strings)))
println("Output: " + OUTPUT_PATH)
