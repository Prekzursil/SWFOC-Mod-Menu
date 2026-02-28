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

namespace detail {

inline void SetBaseDiagnostics(WriteOperationDiagnostics* diagnostics, std::string mode, SIZE_T length, std::string restoreStatus)
{
    if (diagnostics == nullptr)
    {
        return;
    }

    diagnostics->writeMode = std::move(mode);
    diagnostics->oldProtect = "n/a";
    diagnostics->len = std::to_string(length);
    diagnostics->restoreProtectOk = std::move(restoreStatus);
}

inline bool IsInvalidReadRequest(std::int32_t processId, std::uintptr_t address, SIZE_T length)
{
    return processId <= 0 || address == 0 || length == 0;
}

inline bool IsInvalidWriteRequest(std::int32_t processId, std::uintptr_t address, const std::uint8_t* bytes, SIZE_T length)
{
    return processId <= 0 || address == 0 || bytes == nullptr || length == 0;
}

inline bool IsInvalidDataWriteRequest(std::int32_t processId, std::uintptr_t address)
{
    return processId <= 0 || address == 0;
}

#if defined(_WIN32)
inline std::string BuildWin32Error(std::string_view prefix, DWORD code)
{
    return std::string(prefix) + " (" + std::to_string(code) + ")";
}

inline std::string FormatProtect(DWORD protect)
{
    std::ostringstream oldProtectHex;
    oldProtectHex << "0x" << std::hex << static_cast<unsigned long long>(protect);
    return oldProtectHex.str();
}

inline HANDLE OpenProcessHandle(DWORD accessMask, std::int32_t processId, std::string& error)
{
    const auto process = OpenProcess(accessMask, FALSE, static_cast<DWORD>(processId));
    if (process == nullptr)
    {
        error = BuildWin32Error("OpenProcess failed", GetLastError());
    }

    return process;
}

inline bool TryReadProcessExact(HANDLE process, std::uintptr_t address, SIZE_T length, std::vector<std::uint8_t>& output, std::string& error)
{
    output.resize(length);
    SIZE_T bytesRead = 0;
    const auto ok = ReadProcessMemory(
        process,
        reinterpret_cast<LPCVOID>(address),
        output.data(),
        length,
        &bytesRead);
    if (!ok || bytesRead != length)
    {
        output.clear();
        error = BuildWin32Error("ReadProcessMemory failed", ok ? ERROR_SUCCESS : GetLastError());
        return false;
    }

    return true;
}

inline bool TryWriteProcessExact(HANDLE process, std::uintptr_t address, const void* bytes, SIZE_T length, std::string& error)
{
    SIZE_T written = 0;
    const auto ok = WriteProcessMemory(
        process,
        reinterpret_cast<LPVOID>(address),
        bytes,
        length,
        &written);
    if (!ok || written != length)
    {
        error = BuildWin32Error("WriteProcessMemory failed", ok ? ERROR_SUCCESS : GetLastError());
        return false;
    }

    return true;
}

inline bool TryEnablePatchProtection(HANDLE process, std::uintptr_t address, SIZE_T length, DWORD& oldProtect, std::string& error)
{
    if (VirtualProtectEx(process, reinterpret_cast<LPVOID>(address), length, PAGE_EXECUTE_READWRITE, &oldProtect))
    {
        return true;
    }

    error = BuildWin32Error("VirtualProtectEx failed", GetLastError());
    return false;
}

inline bool TryRestorePatchProtection(
    HANDLE process,
    std::uintptr_t address,
    SIZE_T length,
    DWORD oldProtect,
    std::string& error,
    WriteOperationDiagnostics* diagnostics)
{
    DWORD ignoredProtect = 0;
    const auto restoreOk = VirtualProtectEx(process, reinterpret_cast<LPVOID>(address), length, oldProtect, &ignoredProtect);
    if (diagnostics != nullptr)
    {
        diagnostics->restoreProtectOk = restoreOk ? "true" : "false";
    }

    if (restoreOk)
    {
        return true;
    }

    error = BuildWin32Error("VirtualProtectEx restore failed", GetLastError());
    return false;
}
#endif

} // namespace detail

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
    output.clear();
    if (detail::IsInvalidReadRequest(processId, address, length)) {
        error = "invalid process id, address, or read length";
        return false;
    }

#if defined(_WIN32)
    HANDLE process = detail::OpenProcessHandle(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, processId, error);
    if (process == nullptr)
    {
        return false;
    }

    const auto readOk = detail::TryReadProcessExact(process, address, length, output, error);
    CloseHandle(process);
    return readOk;
#else
    (void)processId;
    (void)address;
    (void)length;
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
    detail::SetBaseDiagnostics(diagnostics, "patch", length, "false");
    if (detail::IsInvalidWriteRequest(processId, address, bytes, length)) {
        error = "invalid process id, address, bytes, or write length";
        return false;
    }

#if defined(_WIN32)
    HANDLE process = detail::OpenProcessHandle(PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, processId, error);
    if (process == nullptr)
    {
        return false;
    }

    DWORD oldProtect = 0;
    if (!detail::TryEnablePatchProtection(process, address, length, oldProtect, error))
    {
        CloseHandle(process);
        return false;
    }

    if (diagnostics != nullptr)
    {
        diagnostics->oldProtect = detail::FormatProtect(oldProtect);
    }

    const auto writeOk = detail::TryWriteProcessExact(process, address, bytes, length, error);
    const auto restoreOk = detail::TryRestorePatchProtection(process, address, length, oldProtect, error, diagnostics);
    CloseHandle(process);

    if (!writeOk)
    {
        return false;
    }

    return restoreOk;
#else
    (void)processId;
    (void)address;
    (void)bytes;
    (void)length;
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

    detail::SetBaseDiagnostics(diagnostics, "data", sizeof(TValue), "n/a");
    if (detail::IsInvalidDataWriteRequest(processId, address)) {
        error = "invalid process id or target address";
        return false;
    }

    HANDLE process = detail::OpenProcessHandle(PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ, processId, error);
    if (process == nullptr)
    {
        return false;
    }

    const auto writeOk = detail::TryWriteProcessExact(process, address, &value, sizeof(TValue), error);
    CloseHandle(process);
    return writeOk;
#else
    (void)processId;
    (void)address;
    (void)value;
    detail::SetBaseDiagnostics(
        diagnostics,
        mode == WriteMutationMode::Patch ? "patch" : "data",
        sizeof(TValue),
        "n/a");
    error = "process mutation is only supported on Windows hosts";
    return false;
#endif
}

} // namespace swfoc::extender::plugins::process_mutation
