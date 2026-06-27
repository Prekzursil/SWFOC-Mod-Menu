// ExportRTTIToJSON.java — Ghidra postScript
// Runs after RecoverClassesFromRTTIScript.java to export all recovered RTTI data to JSON.
// Output: <projectDir>/rtti_export_raw.json

import ghidra.app.script.GhidraScript;
import ghidra.program.model.symbol.*;
import ghidra.program.model.listing.*;
import ghidra.program.model.address.*;
import ghidra.program.model.data.*;
import ghidra.program.model.mem.*;
import ghidra.util.task.TaskMonitor;

import java.io.*;
import java.util.*;

public class ExportRTTIToJSON extends GhidraScript {

    private long imageBase;

    @Override
    public void run() throws Exception {
        imageBase = currentProgram.getImageBase().getOffset();
        String outputPath = System.getProperty("rtti.export.path",
            "C:/Users/Prekzursil/Downloads/swfoc_memory/rtti_export_raw.json");

        println("=== ExportRTTIToJSON starting ===");
        println("Image base: 0x" + Long.toHexString(imageBase));
        println("Output: " + outputPath);

        StringBuilder json = new StringBuilder();
        json.append("{\n");
        json.append("  \"_meta\": {\n");
        json.append("    \"program\": \"").append(esc(currentProgram.getName())).append("\",\n");
        json.append("    \"image_base\": \"0x").append(Long.toHexString(imageBase)).append("\",\n");
        json.append("    \"analyzer\": \"Ghidra ").append(ghidra.framework.Application.getApplicationVersion()).append("\",\n");
        json.append("    \"script\": \"ExportRTTIToJSON\",\n");
        json.append("    \"export_date\": \"").append(new java.text.SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss").format(new Date())).append("\"\n");
        json.append("  },\n");

        // 1. Export all namespaces (recovered classes)
        json.append("  \"namespaces\": [\n");
        exportNamespaces(json);
        json.append("  ],\n");

        // 2. Export all vftable symbols
        json.append("  \"vftables\": [\n");
        exportVftables(json);
        json.append("  ],\n");

        // 3. Export RTTI TypeDescriptor data
        json.append("  \"type_descriptors\": [\n");
        exportTypeDescriptors(json);
        json.append("  ],\n");

        // 4. Export all labeled functions with their namespace
        json.append("  \"functions\": [\n");
        exportFunctions(json);
        json.append("  ],\n");

        // 5. Export strings that contain class-related keywords
        json.append("  \"class_strings\": [\n");
        exportClassStrings(json);
        json.append("  ]\n");

        json.append("}\n");

        // Write output
        File outFile = new File(outputPath);
        try (PrintWriter pw = new PrintWriter(new FileWriter(outFile))) {
            pw.print(json.toString());
        }
        println("=== Export complete: " + outFile.getAbsolutePath() + " ===");
    }

    private void exportNamespaces(StringBuilder json) throws Exception {
        SymbolTable symTable = currentProgram.getSymbolTable();
        NamespaceManager nsMgr = currentProgram.getNamespaceManager();
        boolean first = true;

        // Get all non-global, non-library namespaces
        for (Symbol sym : symTable.getAllSymbols(false)) {
            Namespace ns = sym.getParentNamespace();
            if (ns == null || ns.isGlobal() || ns.isLibrary()) continue;

            // We want class-level namespaces that contain vftable or RTTI symbols
            String nsName = ns.getName(true); // fully qualified
            if (nsName == null || nsName.isEmpty()) continue;

            // Check if this namespace has RTTI-related content
            String symName = sym.getName();
            if (symName != null && (symName.contains("vftable") || symName.contains("RTTI") ||
                symName.contains("typeDescriptor") || symName.contains("TypeDescriptor"))) {
                if (!first) json.append(",\n");
                first = false;
                json.append("    {\n");
                json.append("      \"namespace\": \"").append(esc(nsName)).append("\",\n");
                json.append("      \"symbol\": \"").append(esc(symName)).append("\",\n");
                json.append("      \"address\": \"0x").append(Long.toHexString(sym.getAddress().getOffset())).append("\",\n");
                json.append("      \"rva\": \"0x").append(Long.toHexString(sym.getAddress().getOffset() - imageBase)).append("\",\n");
                json.append("      \"type\": \"").append(sym.getSymbolType().toString()).append("\"\n");
                json.append("    }");
            }
        }
        if (!first) json.append("\n");
    }

    private void exportVftables(StringBuilder json) throws Exception {
        SymbolTable symTable = currentProgram.getSymbolTable();
        boolean first = true;

        // Find all symbols containing "vftable"
        SymbolIterator iter = symTable.getAllSymbols(false);
        while (iter.hasNext()) {
            Symbol sym = iter.next();
            String name = sym.getName();
            if (name == null) continue;

            if (name.contains("vftable") || name.equals("vftable")) {
                if (!first) json.append(",\n");
                first = false;

                Address addr = sym.getAddress();
                String nsName = sym.getParentNamespace() != null ?
                    sym.getParentNamespace().getName(true) : "global";

                json.append("    {\n");
                json.append("      \"class\": \"").append(esc(nsName)).append("\",\n");
                json.append("      \"label\": \"").append(esc(name)).append("\",\n");
                json.append("      \"address\": \"0x").append(Long.toHexString(addr.getOffset())).append("\",\n");
                json.append("      \"rva\": \"0x").append(Long.toHexString(addr.getOffset() - imageBase)).append("\",\n");

                // Try to read vtable entries (function pointers)
                json.append("      \"entries\": [");
                try {
                    List<String> entries = readVtableEntries(addr);
                    for (int i = 0; i < entries.size(); i++) {
                        if (i > 0) json.append(", ");
                        json.append("\"").append(entries.get(i)).append("\"");
                    }
                } catch (Exception e) {
                    // vtable read failed, skip entries
                }
                json.append("]\n");
                json.append("    }");
            }
        }
        if (!first) json.append("\n");
    }

    private List<String> readVtableEntries(Address vtableAddr) throws Exception {
        List<String> entries = new ArrayList<>();
        Memory mem = currentProgram.getMemory();
        AddressSpace space = vtableAddr.getAddressSpace();
        int maxEntries = 100; // safety limit

        for (int i = 0; i < maxEntries; i++) {
            Address entryAddr = vtableAddr.add(i * 8L); // x64: 8-byte pointers
            if (!mem.contains(entryAddr)) break;

            long funcPtr = mem.getLong(entryAddr);
            if (funcPtr == 0) break;

            Address funcAddr = space.getAddress(funcPtr);
            Function func = currentProgram.getFunctionManager().getFunctionAt(funcAddr);
            if (func == null) {
                // Not pointing to a known function — end of vtable
                // But check if it's in a code section first
                if (!currentProgram.getMemory().getBlock(funcAddr).isExecute()) break;
                entries.add("0x" + Long.toHexString(funcPtr - imageBase));
            } else {
                String funcName = func.getName(true);
                entries.add(funcName + "@0x" + Long.toHexString(funcPtr - imageBase));
            }
        }
        return entries;
    }

    private void exportTypeDescriptors(StringBuilder json) throws Exception {
        SymbolTable symTable = currentProgram.getSymbolTable();
        Memory mem = currentProgram.getMemory();
        boolean first = true;

        // Find TypeDescriptor symbols and also scan for RTTI mangled names
        SymbolIterator iter = symTable.getAllSymbols(false);
        while (iter.hasNext()) {
            Symbol sym = iter.next();
            String name = sym.getName();
            if (name == null) continue;

            if (name.contains("TypeDescriptor") || name.contains("typeDescriptor") ||
                name.contains("type_info") || name.contains("RTTI_Type_Descriptor")) {
                if (!first) json.append(",\n");
                first = false;

                Address addr = sym.getAddress();
                String nsName = sym.getParentNamespace() != null ?
                    sym.getParentNamespace().getName(true) : "global";

                json.append("    {\n");
                json.append("      \"class\": \"").append(esc(nsName)).append("\",\n");
                json.append("      \"label\": \"").append(esc(name)).append("\",\n");
                json.append("      \"address\": \"0x").append(Long.toHexString(addr.getOffset())).append("\",\n");
                json.append("      \"rva\": \"0x").append(Long.toHexString(addr.getOffset() - imageBase)).append("\",\n");

                // Try to read the mangled name string from TypeDescriptor
                // MSVC TypeDescriptor layout: [8 bytes vfptr][8 bytes spare][N bytes name]
                String mangledName = "";
                try {
                    Address nameAddr = addr.add(16); // skip vfptr + spare (8+8 for x64)
                    byte[] nameBytes = new byte[256];
                    int bytesRead = mem.getBytes(nameAddr, nameBytes);
                    int end = 0;
                    for (int i = 0; i < bytesRead; i++) {
                        if (nameBytes[i] == 0) { end = i; break; }
                    }
                    if (end > 0) {
                        mangledName = new String(nameBytes, 0, end, "ASCII");
                    }
                } catch (Exception e) {
                    mangledName = "<read_error>";
                }

                json.append("      \"mangled_name\": \"").append(esc(mangledName)).append("\"\n");
                json.append("    }");
            }
        }
        if (!first) json.append("\n");
    }

    private void exportFunctions(StringBuilder json) throws Exception {
        FunctionManager funcMgr = currentProgram.getFunctionManager();
        boolean first = true;
        int count = 0;

        FunctionIterator iter = funcMgr.getFunctions(true);
        while (iter.hasNext()) {
            Function func = iter.next();
            Namespace ns = func.getParentNamespace();

            // Only export functions in non-global namespaces (class methods)
            // or functions with meaningful names (not FUN_xxxxx)
            String name = func.getName();
            boolean isClassMethod = (ns != null && !ns.isGlobal());
            boolean hasRealName = (name != null && !name.startsWith("FUN_"));

            if (isClassMethod || hasRealName) {
                if (!first) json.append(",\n");
                first = false;

                Address addr = func.getEntryPoint();
                json.append("    {\n");
                json.append("      \"name\": \"").append(esc(func.getName(true))).append("\",\n");
                json.append("      \"short_name\": \"").append(esc(name)).append("\",\n");
                json.append("      \"namespace\": \"").append(esc(ns != null ? ns.getName(true) : "global")).append("\",\n");
                json.append("      \"address\": \"0x").append(Long.toHexString(addr.getOffset())).append("\",\n");
                json.append("      \"rva\": \"0x").append(Long.toHexString(addr.getOffset() - imageBase)).append("\",\n");
                json.append("      \"signature\": \"").append(esc(func.getSignature().getPrototypeString(false))).append("\",\n");
                json.append("      \"is_thunk\": ").append(func.isThunk()).append(",\n");
                json.append("      \"calling_convention\": \"").append(esc(func.getCallingConventionName())).append("\",\n");
                json.append("      \"body_size\": ").append(func.getBody().getNumAddresses()).append("\n");
                json.append("    }");
                count++;
            }

            if (count > 50000) break; // safety limit
        }
        if (!first) json.append("\n");
        println("Exported " + count + " named/class functions");
    }

    private void exportClassStrings(StringBuilder json) throws Exception {
        // Export strings that match common Alamo engine / RTTI patterns
        DataIterator dataIter = currentProgram.getListing().getDefinedData(true);
        boolean first = true;
        int count = 0;
        Set<String> seen = new HashSet<>();

        while (dataIter.hasNext() && count < 10000) {
            Data data = dataIter.next();
            if (!data.hasStringValue()) continue;

            String val = data.getDefaultValueRepresentation();
            if (val == null) continue;

            // Remove quotes
            if (val.startsWith("\"")) val = val.substring(1);
            if (val.endsWith("\"")) val = val.substring(0, val.length() - 1);

            // Filter for interesting strings
            boolean interesting = false;
            if (val.startsWith(".?AV") || val.startsWith(".?AU")) interesting = true; // RTTI mangled names
            else if (val.contains("Class") || val.contains("Object") || val.contains("Manager"))
                interesting = true;
            else if (val.contains("Lua") || val.contains("lua_")) interesting = true;
            else if (val.contains("Game") || val.contains("Player") || val.contains("Unit"))
                interesting = true;
            else if (val.contains("Damage") || val.contains("Health") || val.contains("Shield"))
                interesting = true;
            else if (val.contains("AI_") || val.contains("Behavior") || val.contains("State"))
                interesting = true;
            else if (val.contains("XML") || val.contains("Property") || val.contains("Component"))
                interesting = true;
            else if (val.contains("Hardpoint") || val.contains("Weapon") || val.contains("Projectile"))
                interesting = true;
            else if (val.contains("Faction") || val.contains("Team") || val.contains("Alliance"))
                interesting = true;
            else if (val.contains("Map") || val.contains("Planet") || val.contains("Fleet"))
                interesting = true;
            else if (val.contains("Spawn") || val.contains("Reinforce") || val.contains("Transport"))
                interesting = true;
            else if (val.contains("Galactic") || val.contains("Tactical") || val.contains("Space"))
                interesting = true;

            if (interesting && !seen.contains(val)) {
                seen.add(val);
                if (!first) json.append(",\n");
                first = false;

                Address addr = data.getAddress();
                json.append("    {\n");
                json.append("      \"value\": \"").append(esc(val)).append("\",\n");
                json.append("      \"address\": \"0x").append(Long.toHexString(addr.getOffset())).append("\",\n");
                json.append("      \"rva\": \"0x").append(Long.toHexString(addr.getOffset() - imageBase)).append("\"\n");
                json.append("    }");
                count++;
            }
        }
        if (!first) json.append("\n");
        println("Exported " + count + " interesting strings");
    }

    private String esc(String s) {
        if (s == null) return "";
        return s.replace("\\", "\\\\")
                .replace("\"", "\\\"")
                .replace("\n", "\\n")
                .replace("\r", "\\r")
                .replace("\t", "\\t");
    }
}
