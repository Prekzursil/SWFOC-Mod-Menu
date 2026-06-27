#pragma once
// fake_memory.h -- Process memory mock for offline bridge testing.
// Provides a simple address-to-byte-vector map so that the bridge's memory
// reads and writes (player objects, player array, globals) can be exercised
// without the game process.

#include <map>
#include <vector>
#include <cstdint>
#include <cstring>
#include <string>

class FakeMemory {
public:
    // Sparse memory: keyed by page-aligned base, value is byte vector.
    // For simplicity we store flat regions at exact addresses.
    std::map<uintptr_t, std::vector<uint8_t>> regions;

    // Ensure a region of at least `len` bytes exists at `addr`.
    void ensure_region(uintptr_t addr, size_t len);

    void write_bytes(uintptr_t addr, const void* data, size_t len);
    void read_bytes(uintptr_t addr, void* out, size_t len) const;

    void write_float(uintptr_t addr, float val);
    float read_float(uintptr_t addr) const;

    void write_int32(uintptr_t addr, int32_t val);
    int32_t read_int32(uintptr_t addr) const;

    void write_uint8(uintptr_t addr, uint8_t val);
    uint8_t read_uint8(uintptr_t addr) const;

    void write_qword(uintptr_t addr, uint64_t val);
    uint64_t read_qword(uintptr_t addr) const;

    // Write a null-terminated C string starting at addr.
    void write_string(uintptr_t addr, const char* s);

    // Set up a fake PlayerObject at the given address with specified fields.
    void setup_player(uintptr_t addr, int slot, bool isLocal,
                      float credits, int tech, const char* faction);

    // Set up the PlayerArray global: write player pointers into an array
    // at `arrayAddr`, and set PlayerCount at `countAddr`.
    void setup_player_array(uintptr_t arrayAddr, uintptr_t countAddr,
                            const std::vector<uintptr_t>& players, int count);

    // Reset all memory regions.
    void clear();
};

extern FakeMemory g_fakeMem;
