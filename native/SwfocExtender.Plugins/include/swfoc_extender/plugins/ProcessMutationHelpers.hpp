// cppcheck-suppress-file missingIncludeSystem
#pragma once

#include <charconv>
#include <cstdint>
#include <string>
#include <string_view>

#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#endif

namespace swfoc::extender::plugins::process_mutation {

inline bool TryParseAddress(std::string_view raw, std::uintptr_t& address) {
    address = 0;
    if (raw.empty()) {
        return false;
    }

    std::string normalized(raw);
    if (normalized.rfind("0x", 0) == 0 || normalized.rfind("0X", 0) == 0) {
        normalized = normalized.substr(2);
    }

    if (normalized.empty()) {
        return false;
    }

    unsigned long long parsed = 0;
    const auto* begin = normalized.data();
    const auto* end = begin + normalized.size();
    const auto [ptr, ec] = std::from_chars(begin, end, parsed, 16);
    if (ec != std::errc() || ptr != end) {
        return false;
    }

    address = static_cast<std::uintptr_t>(parsed);
    return true;
}

template <typename TValue>
inline bool TryWriteValue(std::int32_t processId, std::uintptr_t address, TValue value, std::string& error) {
#if defined(_WIN32)
    if (processId <= 0 || address == 0) {
        error = "invalid process id or target address";
        return false;
    }

    HANDLE process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, FALSE, static_cast<DWORD>(processId));
    if (process == nullptr) {
        error = "OpenProcess failed (" + std::to_string(GetLastError()) + ")";
        return false;
    }

    SIZE_T written = 0;
    const auto ok = WriteProcessMemory(
        process,
        reinterpret_cast<LPVOID>(address),
        &value,
        sizeof(TValue),
        &written);
    const auto lastError = ok ? ERROR_SUCCESS : GetLastError();
    CloseHandle(process);

    if (!ok || written != sizeof(TValue)) {
        error = "WriteProcessMemory failed (" + std::to_string(lastError) + ")";
        return false;
    }

    return true;
#else
    (void)processId;
    (void)address;
    (void)value;
    error = "process mutation is only supported on Windows hosts";
    return false;
#endif
}

} // namespace swfoc::extender::plugins::process_mutation
