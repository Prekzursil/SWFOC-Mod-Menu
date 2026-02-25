# @category SWFOC
# Exports discovered symbols from currentProgram to a JSON file.

import json
import sys

from ghidra.program.model.symbol import SymbolType

try:
    currentProgram
except NameError:
    currentProgram = None


def _hex_address(addr):
    return "0x{0:x}".format(int(addr.getOffset()))


def _collect_function_symbols(program):
    manager = program.getFunctionManager()
    symbols = []
    for function in manager.getFunctions(True):
        symbols.append(
            {
                "name": function.getName(),
                "address": _hex_address(function.getEntryPoint()),
                "kind": "function",
            }
        )
    return symbols


def _collect_label_symbols(program):
    table = program.getSymbolTable()
    symbols = []
    iterator = table.getAllSymbols(True)
    while iterator.hasNext():
        symbol = iterator.next()
        if symbol.getSymbolType() != SymbolType.LABEL:
            continue
        symbols.append(
            {
                "name": symbol.getName(),
                "address": _hex_address(symbol.getAddress()),
                "kind": "label",
            }
        )
    return symbols


def run():
    if len(sys.argv) < 1:
        raise RuntimeError("expected output path argument")

    if currentProgram is None:
        raise RuntimeError(
            "currentProgram is only available in a Ghidra script runtime"
        )

    out_path = sys.argv[0]
    symbols = []
    symbols.extend(_collect_function_symbols(currentProgram))
    symbols.extend(_collect_label_symbols(currentProgram))
    symbols.sort(key=lambda item: (item["name"], item["address"]))

    payload = {
        "schemaVersion": "1.0",
        "programName": currentProgram.getName(),
        "generatedAtUtc": str(currentProgram.getCreationDate()),
        "symbols": symbols,
    }

    with open(out_path, "w") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)

    print("exported symbols to {}".format(out_path))


run()
