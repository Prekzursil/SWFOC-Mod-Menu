// cppcheck-suppress-file missingIncludeSystem
#include "swfoc_extender/bridge/NamedPipeBridgeServer.hpp"

#include <array>
#include <cctype>
#include <chrono>
#include <map>
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

bool TryFindValueStart(const std::string& json, const std::string& key, std::size_t& start) {
    const auto quotedKey = "\"" + key + "\"";
    const auto keyPos = json.find(quotedKey);
    if (keyPos == std::string::npos) {
        return false;
    }

    const auto colonPos = json.find(':', keyPos + quotedKey.size());
    if (colonPos == std::string::npos) {
        return false;
    }

    start = json.find_first_not_of(" \t\r\n", colonPos + 1);
    return start != std::string::npos;
}

bool TryReadInt(const std::string& json, const std::string& key, std::int32_t& value) {
    std::size_t start = 0;
    if (!TryFindValueStart(json, key, start)) {
        return false;
    }

    if (json[start] == '+') {
        return false;
    }

    try {
        std::size_t consumed = 0;
        const auto parsed = std::stoi(json.c_str() + start, &consumed);
        if (consumed == 0) {
            return false;
        }

        value = static_cast<std::int32_t>(parsed);
        return true;
    } catch (...) {
        return false;
    }
}

std::size_t FindUnescapedQuote(const std::string& value, std::size_t start) {
    auto escaped = false;
    for (std::size_t i = start; i < value.size(); ++i) {
        if (escaped) {
            escaped = false;
            continue;
        }

        if (value[i] == '\\') {
            escaped = true;
            continue;
        }

        if (value[i] == '"') {
            return i;
        }
    }

    return std::string::npos;
}

std::string TrimAsciiWhitespace(std::string value) {
    auto first = value.begin();
    while (first != value.end() && std::isspace(static_cast<unsigned char>(*first)) != 0) {
        ++first;
    }

    auto last = value.end();
    while (last != first && std::isspace(static_cast<unsigned char>(*(last - 1))) != 0) {
        --last;
    }

    return std::string(first, last);
}

std::size_t SkipAsciiWhitespace(const std::string& value, std::size_t cursor) {
    return value.find_first_not_of(" \t\r\n", cursor);
}

bool TryParseFlatStringMapEntryKey(const std::string& objectJson, std::size_t& cursor, std::string& key);
bool TryParseFlatStringMapEntryValue(const std::string& objectJson, std::size_t& cursor, std::string& value);

bool TryParseFlatStringMapEntry(
    const std::string& objectJson,
    std::size_t& cursor,
    std::string& key,
    std::string& value) {
    cursor = SkipAsciiWhitespace(objectJson, cursor);
    if (cursor == std::string::npos || objectJson[cursor] == '}') {
        return false;
    }

    if (!TryParseFlatStringMapEntryKey(objectJson, cursor, key)) {
        return false;
    }

    if (!TryParseFlatStringMapEntryValue(objectJson, cursor, value)) {
        return false;
    }

    cursor = SkipAsciiWhitespace(objectJson, cursor);
    if (cursor != std::string::npos && objectJson[cursor] == ',') {
        ++cursor;
    }

    return true;
}

bool TryParseFlatStringMapEntryKey(const std::string& objectJson, std::size_t& cursor, std::string& key) {
    if (objectJson[cursor] != '"') {
        return false;
    }

    const auto keyEnd = FindUnescapedQuote(objectJson, cursor + 1);
    if (keyEnd == std::string::npos) {
        return false;
    }

    key = objectJson.substr(cursor + 1, keyEnd - cursor - 1);
    cursor = objectJson.find(':', keyEnd + 1);
    if (cursor == std::string::npos) {
        return false;
    }

    ++cursor;
    cursor = SkipAsciiWhitespace(objectJson, cursor);
    return cursor != std::string::npos;
}

bool TryParseFlatStringMapEntryValue(const std::string& objectJson, std::size_t& cursor, std::string& value) {
    if (objectJson[cursor] == '"') {
        const auto valueEnd = FindUnescapedQuote(objectJson, cursor + 1);
        if (valueEnd == std::string::npos) {
            return false;
        }

        value = objectJson.substr(cursor + 1, valueEnd - cursor - 1);
        cursor = valueEnd + 1;
    } else {
        auto tokenEnd = cursor;
        while (tokenEnd < objectJson.size() && objectJson[tokenEnd] != ',' && objectJson[tokenEnd] != '}') {
            ++tokenEnd;
        }

        value = TrimAsciiWhitespace(objectJson.substr(cursor, tokenEnd - cursor));
        cursor = tokenEnd;
    }

    return true;
}

std::map<std::string, std::string> ParseFlatStringMapObject(const std::string& objectJson) {
    std::map<std::string, std::string> parsed;
    auto cursor = objectJson.find('{');
    if (cursor == std::string::npos) {
        return parsed;
    }

    ++cursor;
    std::string key;
    std::string value;
    while (TryParseFlatStringMapEntry(objectJson, cursor, key, value)) {
        if (!key.empty()) {
            parsed[key] = value;
        }
    }

    return parsed;
}

std::map<std::string, std::string> ExtractStringMap(const std::string& json, const std::string& key) {
    const auto objectJson = ExtractObjectJson(json, key);
    return ParseFlatStringMapObject(objectJson);
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

bool TryCreateConnectedPipe(const std::string& fullPipeName, HANDLE& pipe) {
    pipe = CreateBridgePipe(fullPipeName);
    if (pipe == INVALID_HANDLE_VALUE) {
        std::this_thread::sleep_for(kServerPollDelay);
        return false;
    }

    if (!TryConnectClient(pipe)) {
        CloseHandle(pipe);
        return false;
    }

    return true;
}

std::string ReadCommandLine(HANDLE pipe, std::array<char, kPipeBufferSize>& buffer) {
    std::string commandLine;
    DWORD bytesRead = 0;
    while (ReadFile(pipe, buffer.data(), static_cast<DWORD>(buffer.size()), &bytesRead, nullptr) && bytesRead > 0) {
        commandLine.append(buffer.data(), bytesRead);
        const auto linePos = commandLine.find('\n');
        if (linePos != std::string::npos) {
            commandLine.erase(linePos);
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

template <typename CommandHandler>
void ProcessConnectedClient(
    HANDLE pipe,
    std::array<char, kPipeBufferSize>& buffer,
    CommandHandler&& handler) {
    const auto commandLine = ReadCommandLine(pipe, buffer);
    WriteResponse(pipe, handler(commandLine));
    CloseServerPipe(pipe);
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
    command.processName = ExtractStringValue(jsonLine, "processName");
    command.resolvedAnchors = ExtractStringMap(jsonLine, "resolvedAnchors");
    std::int32_t processId = 0;
    if (TryReadInt(jsonLine, "processId", processId)) {
        command.processId = processId;
    }

    if (command.commandId.empty()) {
        BridgeResult result {};
        result.commandId = {};
        result.succeeded = false;
        result.reasonCode = "CAPABILITY_BACKEND_UNAVAILABLE";
        result.backend = "extender";
        result.hookState = "invalid_command";
        result.message = "Command payload missing commandId.";
        result.diagnosticsJson = "{\"parseError\":\"missing_commandId\"}";
        return result;
    }

    if (!handler_) {
        BridgeResult result {};
        result.commandId = command.commandId;
        result.succeeded = false;
        result.reasonCode = "CAPABILITY_BACKEND_UNAVAILABLE";
        result.backend = "extender";
        result.hookState = "handler_missing";
        result.message = "Bridge handler is not configured.";
        result.diagnosticsJson = "{\"handler\":\"missing\"}";
        return result;
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
    const auto handleCommand = [this](const std::string& commandLine) {
        return handleRawCommand(commandLine);
    };

    while (running_.load()) {
        HANDLE pipe = INVALID_HANDLE_VALUE;
        if (!TryCreateConnectedPipe(fullPipeName, pipe)) {
            continue;
        }

        ProcessConnectedClient(pipe, buffer, handleCommand);
    }
#endif
}

} // namespace swfoc::extender::bridge
