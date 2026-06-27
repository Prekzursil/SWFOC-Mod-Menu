// fake_memory.cpp -- Process memory mock implementations.
// Manages sparse byte regions keyed by address for offline testing.

#include "fake_memory.h"
#include "rvas.h"
#include <algorithm>
#include <stdexcept>

FakeMemory g_fakeMem;

// Page size for region alignment (4KB).
static constexpr size_t PAGE_SIZE = 0x1000;

// Round addr down to page boundary.
static uintptr_t page_base(uintptr_t addr) {
    return addr & ~(uintptr_t)(PAGE_SIZE - 1);
}

void FakeMemory::ensure_region(uintptr_t addr, size_t len) {
    uintptr_t base = page_base(addr);
    size_t needed = (addr - base) + len;
    // Round up to next page
    size_t pages = (needed + PAGE_SIZE - 1) / PAGE_SIZE;
    size_t total = pages * PAGE_SIZE;

    auto it = regions.find(base);
    if (it == regions.end()) {
        regions[base].resize(total, 0);
    } else if (it->second.size() < total) {
        it->second.resize(total, 0);
    }
}

void FakeMemory::write_bytes(uintptr_t addr, const void* data, size_t len) {
    ensure_region(addr, len);
    uintptr_t base = page_base(addr);
    size_t offset = addr - base;
    memcpy(regions[base].data() + offset, data, len);
}

void FakeMemory::read_bytes(uintptr_t addr, void* out, size_t len) const {
    uintptr_t base = page_base(addr);
    auto it = regions.find(base);
    if (it == regions.end()) {
        memset(out, 0, len);
        return;
    }
    size_t offset = addr - base;
    if (offset + len <= it->second.size()) {
        memcpy(out, it->second.data() + offset, len);
    } else {
        memset(out, 0, len);
    }
}

void FakeMemory::write_float(uintptr_t addr, float val) {
    write_bytes(addr, &val, sizeof(float));
}

float FakeMemory::read_float(uintptr_t addr) const {
    float val = 0;
    read_bytes(addr, &val, sizeof(float));
    return val;
}

void FakeMemory::write_int32(uintptr_t addr, int32_t val) {
    write_bytes(addr, &val, sizeof(int32_t));
}

int32_t FakeMemory::read_int32(uintptr_t addr) const {
    int32_t val = 0;
    read_bytes(addr, &val, sizeof(int32_t));
    return val;
}

void FakeMemory::write_uint8(uintptr_t addr, uint8_t val) {
    write_bytes(addr, &val, sizeof(uint8_t));
}

uint8_t FakeMemory::read_uint8(uintptr_t addr) const {
    uint8_t val = 0;
    read_bytes(addr, &val, sizeof(uint8_t));
    return val;
}

void FakeMemory::write_qword(uintptr_t addr, uint64_t val) {
    write_bytes(addr, &val, sizeof(uint64_t));
}

uint64_t FakeMemory::read_qword(uintptr_t addr) const {
    uint64_t val = 0;
    read_bytes(addr, &val, sizeof(uint64_t));
    return val;
}

void FakeMemory::write_string(uintptr_t addr, const char* s) {
    size_t len = strlen(s) + 1;  // include null terminator
    write_bytes(addr, s, len);
}

void FakeMemory::setup_player(uintptr_t addr, int slot, bool isLocal,
                               float credits, int tech, const char* faction) {
    // Allocate enough for the biggest offset we use (TechLevel at 0x88 + 4 = 0x8C)
    ensure_region(addr, 0x100);

    write_int32(addr + RVA::PlayerObj::SlotIndex, slot);
    write_uint8(addr + RVA::PlayerObj::LocalPlayer, isLocal ? 1 : 0);
    write_float(addr + RVA::PlayerObj::Credits, credits);
    write_float(addr + RVA::PlayerObj::MaxCredits, 100000.0f);  // default max
    write_int32(addr + RVA::PlayerObj::TechLevel, tech);
    write_int32(addr + RVA::PlayerObj::MaxTechLevel, 5);

    // Faction name is a pointer to a string. We store the string at addr+0xF0
    // and write the pointer at FactionName offset.
    uintptr_t strAddr = addr + 0xF0;
    write_string(strAddr, faction);
    write_qword(addr + RVA::PlayerObj::FactionName, strAddr);
}

void FakeMemory::setup_player_array(uintptr_t arrayAddr, uintptr_t countAddr,
                                     const std::vector<uintptr_t>& players, int count) {
    ensure_region(arrayAddr, count * 8);
    for (int i = 0; i < count; i++) {
        write_qword(arrayAddr + i * 8, players[i]);
    }
    write_int32(countAddr, count);
}

void FakeMemory::clear() {
    regions.clear();
}
