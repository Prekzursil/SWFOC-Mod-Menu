#pragma once
#include <windows.h>
#include <atomic>
#include <cstdint>

#define SHMEM_CMD_NAME  "Local\\SWFOC_Bridge_Cmd"
#define SHMEM_CMD_SIZE  8192

struct SharedCmdBuffer {
    std::atomic<uint32_t> cmd_seq;      // CE increments when writing command
    std::atomic<uint32_t> result_seq;   // DLL increments when result ready
    uint32_t cmd_len;                   // Length of command string
    uint32_t result_len;                // Length of result string
    char cmd[4096];                     // Command text (null-terminated)
    char result[4096];                  // Result text (null-terminated)
};

// Event ring buffer for high-frequency data (Wave 1D)
#define SHMEM_EVT_NAME "Local\\SWFOC_Bridge_Events"
#define SHMEM_EVT_SIZE (64 * 1024)

struct SharedEvtBuffer {
    std::atomic<uint32_t> write_pos;
    std::atomic<uint32_t> read_pos;
    std::atomic<uint32_t> event_count;
    std::atomic<uint32_t> flags;        // Bit 0: events enabled
    uint8_t ring[SHMEM_EVT_SIZE - 16];
};

enum EventType : uint16_t {
    EVT_HP_CHANGE   = 0x01,
    EVT_UNIT_DIED   = 0x02,
    EVT_PRODUCTION  = 0x03,
    EVT_STORY       = 0x04,
    EVT_POSITION    = 0x10,
    EVT_SELECTION   = 0x20,
};
