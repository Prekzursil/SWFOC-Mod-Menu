#include "swfoc_extender/bridge/NamedPipeBridgeServer.hpp"

#include <array>
#include <chrono>
#include <sstream>
#include <utility>

#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#endif

namespace swfoc::extender::bridge {

namespace {

constexpr auto kServerPollDelay = std::chrono::milliseconds(100);
constexpr auto kClientWakePollDelay = std::chrono::milliseconds(25);
constexpr std::size_t kPipeBufferSize = 16 * 1024;

/*
Cppcheck note (targeted): if cppcheck runs without STL/Windows SDK include paths,
missingIncludeSystem can be suppressed per translation unit with:
  --suppress=missingIncludeSystem:native/SwfocExtender.Bridge/src/NamedPipeBridgeServer.cpp
*/

std::string BuildFullPipeName(const std::string& pipeName) {
    return std::string("\\\\.\\pipe\\") + pipeName;
}

std::string ExtractStringValue(const std::string& json, const std::string& key) {
    const auto quotedKey = "\"" + key + "\"";
    auto keyPos = json.find(quotedKey);
    if (keyPos == std::string::npos) {
        return {};
    }

    auto colonPos = json.find(':', keyPos + quotedKey.length());
    if (colonPos == std::string::npos) {
        return {};
    }

    auto firstQuote = json.find('"', colonPos + 1);
    if (firstQuote == std::string::npos) {
        return {};
    }

    auto secondQuote = json.find('"', firstQuote + 1);
    if (secondQuote == std::string::npos || secondQuote <= firstQuote) {
        return {};
    }

    return json.substr(firstQuote + 1, secondQuote - firstQuote - 1);
}

std::string ExtractObjectJson(const std::string& json, const std::string& key) {
    const auto quotedKey = "\"" + key + "\"";
    auto keyPos = json.find(quotedKey);
    if (keyPos == std::string::npos) {
        return "{}";
    }

    auto colonPos = json.find(':', keyPos + quotedKey.length());
    if (colonPos == std::string::npos) {
        return "{}";
    }

    auto openBrace = json.find('{', colonPos + 1);
    if (openBrace == std::string::npos) {
        return "{}";
    }

    auto depth = 0;
    for (std::size_t i = openBrace; i < json.size(); ++i) {
        if (json[i] == '{') {
            ++depth;
        } else if (json[i] == '}') {
            --depth;
            if (depth == 0) {
                return json.substr(openBrace, i - openBrace + 1);
            }
        }
    }

    return "{}";
}

std::string EscapeJson(const std::string& value) {
    std::string escaped;
    escaped.reserve(value.size() + 8);
    for (const auto ch : value) {
        switch (ch) {
        case '\\':
            escaped += "\\\\";
            break;
        case '"':
            escaped += "\\\"";
            break;
        case '\n':
            escaped += "\\n";
            break;
        case '\r':
            escaped += "\\r";
            break;
        case '\t':
            escaped += "\\t";
            break;
        default:
            escaped.push_back(ch);
            break;
        }
    }

    return escaped;
}

std::string ToJsonLine(const BridgeResult& result) {
    std::ostringstream out;
    out << '{';
    out << "\"commandId\":\"" << EscapeJson(result.commandId) << "\",";
    out << "\"succeeded\":" << (result.succeeded ? "true" : "false") << ',';
    out << "\"reasonCode\":\"" << EscapeJson(result.reasonCode) << "\",";
    out << "\"backend\":\"" << EscapeJson(result.backend) << "\",";
    out << "\"hookState\":\"" << EscapeJson(result.hookState) << "\",";
    out << "\"message\":\"" << EscapeJson(result.message) << "\",";
    out << "\"diagnostics\":" << (result.diagnosticsJson.empty() ? "{}" : result.diagnosticsJson);
    out << '}';
    return out.str();
}

#if defined(_WIN32)
HANDLE CreateBridgePipe(const std::string& fullPipeName) {
    return CreateNamedPipeA(
        fullPipeName.c_str(),
        PIPE_ACCESS_DUPLEX,
        PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
        PIPE_UNLIMITED_INSTANCES,
        kPipeBufferSize,
        kPipeBufferSize,
        0,
        nullptr);
}

bool TryConnectClient(HANDLE pipe) {
    const BOOL connected = ConnectNamedPipe(pipe, nullptr)
        ? TRUE
        : (GetLastError() == ERROR_PIPE_CONNECTED);
    return connected != FALSE;
}

std::string ReadCommandLine(HANDLE pipe, std::array<char, kPipeBufferSize>& buffer) {
    std::string commandLine;
    DWORD bytesRead = 0;
    while (ReadFile(pipe, buffer.data(), static_cast<DWORD>(buffer.size()), &bytesRead, nullptr) && bytesRead > 0) {
        commandLine.append(buffer.data(), bytesRead);
        const auto linePos = commandLine.find('\n');
        if (linePos != std::string::npos) {
            commandLine = commandLine.substr(0, linePos);
            break;
        }

        if (bytesRead < buffer.size()) {
            break;
        }
    }

    while (!commandLine.empty() && (commandLine.back() == '\r' || commandLine.back() == '\n')) {
        commandLine.pop_back();
    }

    return commandLine;
}

void WriteResponse(HANDLE pipe, const BridgeResult& result) {
    auto response = ToJsonLine(result);
    response.push_back('\n');

    DWORD bytesWritten = 0;
    WriteFile(pipe, response.data(), static_cast<DWORD>(response.size()), &bytesWritten, nullptr);
    FlushFileBuffers(pipe);
}

void CloseServerPipe(HANDLE pipe) {
    DisconnectNamedPipe(pipe);
    CloseHandle(pipe);
}
#endif

} // namespace

NamedPipeBridgeServer::NamedPipeBridgeServer(std::string pipeName)
    : pipeName_(std::move(pipeName)) {}

void NamedPipeBridgeServer::setHandler(Handler handler) {
    handler_ = std::move(handler);
}

bool NamedPipeBridgeServer::start() {
    if (running_.exchange(true)) {
        return true;
    }

    worker_ = std::thread([this]() { runLoop(); });
    return true;
}

void NamedPipeBridgeServer::stop() {
    if (!running_.exchange(false)) {
        return;
    }

#if defined(_WIN32)
    const auto fullPipeName = BuildFullPipeName(pipeName_);
    auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(800);
    while (std::chrono::steady_clock::now() < deadline) {
        auto client = CreateFileA(
            fullPipeName.c_str(),
            GENERIC_READ | GENERIC_WRITE,
            0,
            nullptr,
            OPEN_EXISTING,
            0,
            nullptr);
        if (client != INVALID_HANDLE_VALUE) {
            CloseHandle(client);
            break;
        }
        std::this_thread::sleep_for(kClientWakePollDelay);
    }
#endif

    if (worker_.joinable()) {
        worker_.join();
    }
}

bool NamedPipeBridgeServer::running() const noexcept {
    return running_.load();
}

BridgeResult NamedPipeBridgeServer::handleRawCommand(const std::string& jsonLine) const {
    BridgeCommand command;
    command.commandId = ExtractStringValue(jsonLine, "commandId");
    command.featureId = ExtractStringValue(jsonLine, "featureId");
    command.profileId = ExtractStringValue(jsonLine, "profileId");
    command.mode = ExtractStringValue(jsonLine, "mode");
    command.requestedBy = ExtractStringValue(jsonLine, "requestedBy");
    command.timestampUtc = ExtractStringValue(jsonLine, "timestampUtc");
    command.payloadJson = ExtractObjectJson(jsonLine, "payload");

    if (command.commandId.empty()) {
        return BridgeResult{
            .commandId = {},
            .succeeded = false,
            .reasonCode = "CAPABILITY_BACKEND_UNAVAILABLE",
            .backend = "extender",
            .hookState = "invalid_command",
            .message = "Command payload missing commandId.",
            .diagnosticsJson = "{\"parseError\":\"missing_commandId\"}"
        };
    }

    if (!handler_) {
        return BridgeResult{
            .commandId = command.commandId,
            .succeeded = false,
            .reasonCode = "CAPABILITY_BACKEND_UNAVAILABLE",
            .backend = "extender",
            .hookState = "handler_missing",
            .message = "Bridge handler is not configured.",
            .diagnosticsJson = "{\"handler\":\"missing\"}"
        };
    }

    auto result = handler_(command);
    if (result.commandId.empty()) {
        result.commandId = command.commandId;
    }

    if (result.backend.empty()) {
        result.backend = "extender";
    }

    return result;
}

void NamedPipeBridgeServer::runLoop() {
#if !defined(_WIN32)
    while (running_.load()) {
        std::this_thread::sleep_for(kServerPollDelay);
    }
    return;
#else
    const auto fullPipeName = BuildFullPipeName(pipeName_);
    std::array<char, kPipeBufferSize> buffer {};

    while (running_.load()) {
        HANDLE pipe = CreateBridgePipe(fullPipeName);
        if (pipe == INVALID_HANDLE_VALUE) {
            std::this_thread::sleep_for(kServerPollDelay);
            continue;
        }

        if (!TryConnectClient(pipe)) {
            CloseHandle(pipe);
            continue;
        }

        const auto commandLine = ReadCommandLine(pipe, buffer);
        WriteResponse(pipe, handleRawCommand(commandLine));
        CloseServerPipe(pipe);
    }
#endif
}

} // namespace swfoc::extender::bridge
