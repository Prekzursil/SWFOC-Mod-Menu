// cppcheck-suppress-file missingIncludeSystem
#pragma once

#include <charconv>
#include <cstdint>
#include <sstream>
#include <string>
#include <string_view>
#include <vector>

#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#endif

namespace swfoc::extender::plugins::process_mutation {

enum class WriteMutationMode
{
    Data,
    Patch
};

struct WriteOperationDiagnostics
{
    std::string writeMode {"data"};
    std::string oldProtect {"n/a"};
    std::string len {"0"};
    std::string restoreProtectOk {"n/a"};
};

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

inline bool TryReadBytes(
    std::int32_t processId,
    std::uintptr_t address,
    SIZE_T length,
    std::vector<std::uint8_t>& output,
    std::string& error) {
#if defined(_WIN32)
    output.clear();
    if (processId <= 0 || address == 0 || length == 0) {
        error = "invalid process id, address, or read length";
        return false;
    }

    HANDLE process = OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, FALSE, static_cast<DWORD>(processId));
    if (process == nullptr) {
        error = "OpenProcess failed (" + std::to_string(GetLastError()) + ")";
        return false;
    }

    output.resize(length);
    SIZE_T bytesRead = 0;
    const auto ok = ReadProcessMemory(
        process,
        reinterpret_cast<LPCVOID>(address),
        output.data(),
        length,
        &bytesRead);
    const auto lastError = ok ? ERROR_SUCCESS : GetLastError();
    CloseHandle(process);

    if (!ok || bytesRead != length) {
        output.clear();
        error = "ReadProcessMemory failed (" + std::to_string(lastError) + ")";
        return false;
    }

    return true;
#else
    (void)processId;
    (void)address;
    (void)length;
    output.clear();
    error = "process reads are only supported on Windows hosts";
    return false;
#endif
}

inline bool TryWriteBytesPatchSafe(
    std::int32_t processId,
    std::uintptr_t address,
    const std::uint8_t* bytes,
    SIZE_T length,
    std::string& error,
    WriteOperationDiagnostics* diagnostics = nullptr) {
#if defined(_WIN32)
    if (diagnostics != nullptr) {
        diagnostics->writeMode = "patch";
        diagnostics->oldProtect = "n/a";
        diagnostics->len = std::to_string(length);
        diagnostics->restoreProtectOk = "false";
    }

    if (processId <= 0 || address == 0 || bytes == nullptr || length == 0) {
        error = "invalid process id, address, bytes, or write length";
        return false;
    }

    HANDLE process = OpenProcess(PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, FALSE, static_cast<DWORD>(processId));
    if (process == nullptr) {
        error = "OpenProcess failed (" + std::to_string(GetLastError()) + ")";
        return false;
    }

    DWORD oldProtect = 0;
    if (!VirtualProtectEx(process, reinterpret_cast<LPVOID>(address), length, PAGE_EXECUTE_READWRITE, &oldProtect)) {
        const auto protectError = GetLastError();
        CloseHandle(process);
        error = "VirtualProtectEx failed (" + std::to_string(protectError) + ")";
        return false;
    }

    if (diagnostics != nullptr) {
        std::ostringstream oldProtectHex;
        oldProtectHex << "0x" << std::hex << static_cast<unsigned long long>(oldProtect);
        diagnostics->oldProtect = oldProtectHex.str();
    }

    SIZE_T written = 0;
    const auto writeOk = WriteProcessMemory(
        process,
        reinterpret_cast<LPVOID>(address),
        bytes,
        length,
        &written);
    const auto writeError = writeOk ? ERROR_SUCCESS : GetLastError();

    DWORD ignoredProtect = 0;
    const auto restoreOk = VirtualProtectEx(process, reinterpret_cast<LPVOID>(address), length, oldProtect, &ignoredProtect);
    const auto restoreError = restoreOk ? ERROR_SUCCESS : GetLastError();
    if (diagnostics != nullptr) {
        diagnostics->restoreProtectOk = restoreOk ? "true" : "false";
    }
    CloseHandle(process);

    if (!writeOk || written != length) {
        error = "WriteProcessMemory failed (" + std::to_string(writeError) + ")";
        return false;
    }

    if (!restoreOk) {
        error = "VirtualProtectEx restore failed (" + std::to_string(restoreError) + ")";
        return false;
    }

    return true;
#else
    (void)processId;
    (void)address;
    (void)bytes;
    (void)length;
    if (diagnostics != nullptr) {
        diagnostics->writeMode = "patch";
        diagnostics->oldProtect = "n/a";
        diagnostics->len = std::to_string(length);
        diagnostics->restoreProtectOk = "false";
    }

    error = "process mutation is only supported on Windows hosts";
    return false;
#endif
}

template <typename TValue>
inline bool TryWriteValue(
    std::int32_t processId,
    std::uintptr_t address,
    TValue value,
    std::string& error,
    WriteMutationMode mode = WriteMutationMode::Data,
    WriteOperationDiagnostics* diagnostics = nullptr) {
#if defined(_WIN32)
    if (mode == WriteMutationMode::Patch) {
        return TryWriteBytesPatchSafe(
            processId,
            address,
            reinterpret_cast<const std::uint8_t*>(&value),
            sizeof(TValue),
            error,
            diagnostics);
    }

    if (diagnostics != nullptr) {
        diagnostics->writeMode = "data";
        diagnostics->oldProtect = "n/a";
        diagnostics->len = std::to_string(sizeof(TValue));
        diagnostics->restoreProtectOk = "n/a";
    }

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
    (void)mode;
    if (diagnostics != nullptr) {
        diagnostics->writeMode = mode == WriteMutationMode::Patch ? "patch" : "data";
        diagnostics->oldProtect = "n/a";
        diagnostics->len = std::to_string(sizeof(TValue));
        diagnostics->restoreProtectOk = "n/a";
    }

    error = "process mutation is only supported on Windows hosts";
    return false;
#endif
}

} // namespace swfoc::extender::plugins::process_mutation
