// cppcheck-suppress-file missingIncludeSystem
#include "swfoc_extender/bridge/NamedPipeBridgeServer.hpp"

#include <array>
#include <cctype>
#include <chrono>
#include <map>
#include <sstream>
#include <stdexcept>
#include <string_view>
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

std::string BuildFullPipeName(std::string_view pipeName) {
    // S6009: pipeName is string_view; we concatenate with a raw string prefix
    // S3628: raw string literal for the pipe prefix
    auto result = std::string(R"(\\.\pipe\)");
    result.append(pipeName);
    return result;
}

std::string ExtractStringValue(std::string_view json, std::string_view key) {
    auto quotedKey = std::string("\"");
    quotedKey.append(key);
    quotedKey.push_back('"');

    const auto keyPos = json.find(quotedKey);
    if (keyPos == std::string_view::npos) {
        return {};
    }

    const auto colonPos = json.find(':', keyPos + quotedKey.length());
    if (colonPos == std::string_view::npos) {
        return {};
    }

    const auto firstQuote = json.find('"', colonPos + 1);
    if (firstQuote == std::string_view::npos) {
        return {};
    }

    const auto secondQuote = json.find('"', firstQuote + 1);
    if (secondQuote == std::string_view::npos || secondQuote <= firstQuote) {
        return {};
    }

    return std::string(json.substr(firstQuote + 1, secondQuote - firstQuote - 1));
}

std::string ExtractObjectJson(std::string_view json, std::string_view key) {
    auto quotedKey = std::string("\"");
    quotedKey.append(key);
    quotedKey.push_back('"');

    const auto keyPos = json.find(quotedKey);
    if (keyPos == std::string_view::npos) {
        return "{}";
    }

    const auto colonPos = json.find(':', keyPos + quotedKey.length());
    if (colonPos == std::string_view::npos) {
        return "{}";
    }

    const auto openBrace = json.find('{', colonPos + 1);
    if (openBrace == std::string_view::npos) {
        return "{}";
    }

    auto depth = 0;
    for (auto i = openBrace; i < json.size(); ++i) {
        if (json[i] == '{') {
            ++depth;
        } else if (json[i] == '}') {
            --depth;
            if (depth == 0) {
                return std::string(json.substr(openBrace, i - openBrace + 1));
            }
        }
    }

    return "{}";
}

bool TryFindValueStart(std::string_view json, std::string_view key, std::size_t& start) {
    auto quotedKey = std::string("\"");
    quotedKey.append(key);
    quotedKey.push_back('"');

    const auto keyPos = json.find(quotedKey);
    if (keyPos == std::string_view::npos) {
        return false;
    }

    const auto colonPos = json.find(':', keyPos + quotedKey.size());
    if (colonPos == std::string_view::npos) {
        return false;
    }

    start = json.find_first_not_of(" \t\r\n", colonPos + 1);
    return start != std::string_view::npos;
}

bool TryReadInt(std::string_view json, std::string_view key, std::int32_t& value) {
    std::size_t start = 0;
    if (!TryFindValueStart(json, key, start)) {
        return false;
    }

    if (json[start] == '+') {
        return false;
    }

    try {
        std::size_t consumed = 0;
        const auto parsed = std::stoi(std::string(json.substr(start)), &consumed);
        if (consumed == 0) {
            return false;
        }

        value = parsed;
        return true;
    } catch (const std::invalid_argument&) {
        return false;
    } catch (const std::out_of_range&) {
        return false;
    }
}

std::size_t FindUnescapedQuote(std::string_view value, std::size_t start) {
    auto escaped = false;
    for (auto i = start; i < value.size(); ++i) {
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

    return std::string_view::npos;
}

std::string TrimAsciiWhitespace(std::string_view value) {
    auto first = value.begin();
    while (first != value.end() && std::isspace(static_cast<unsigned char>(*first)) != 0) {
        ++first;
    }

    auto last = value.end();
    while (last != first && std::isspace(static_cast<unsigned char>(*(last - 1))) != 0) {
        --last;
    }

    return {first, last};
}

std::size_t SkipAsciiWhitespace(std::string_view value, std::size_t cursor) {
    return value.find_first_not_of(" \t\r\n", cursor);
}

bool TryParseFlatStringMapEntryKey(std::string_view objectJson, std::size_t& cursor, std::string& key);
bool TryParseFlatStringMapEntryValue(std::string_view objectJson, std::size_t& cursor, std::string& value);

bool TryParseFlatStringMapEntry(
    std::string_view objectJson,
    std::size_t& cursor,
    std::string& key,
    std::string& value) {
    cursor = SkipAsciiWhitespace(objectJson, cursor);
    if (cursor == std::string_view::npos || objectJson[cursor] == '}') {
        return false;
    }

    if (!TryParseFlatStringMapEntryKey(objectJson, cursor, key)) {
        return false;
    }

    if (!TryParseFlatStringMapEntryValue(objectJson, cursor, value)) {
        return false;
    }

    cursor = SkipAsciiWhitespace(objectJson, cursor);
    if (cursor != std::string_view::npos && objectJson[cursor] == ',') {
        ++cursor;
    }

    return true;
}

bool TryParseFlatStringMapEntryKey(std::string_view objectJson, std::size_t& cursor, std::string& key) {
    if (objectJson[cursor] != '"') {
        return false;
    }

    const auto keyEnd = FindUnescapedQuote(objectJson, cursor + 1);
    if (keyEnd == std::string_view::npos) {
        return false;
    }

    key = std::string(objectJson.substr(cursor + 1, keyEnd - cursor - 1));
    cursor = objectJson.find(':', keyEnd + 1);
    if (cursor == std::string_view::npos) {
        return false;
    }

    ++cursor;
    cursor = SkipAsciiWhitespace(objectJson, cursor);
    return cursor != std::string_view::npos;
}

bool TryParseFlatStringMapEntryValue(std::string_view objectJson, std::size_t& cursor, std::string& value) {
    if (objectJson[cursor] == '"') {
        const auto valueEnd = FindUnescapedQuote(objectJson, cursor + 1);
        if (valueEnd == std::string_view::npos) {
            return false;
        }

        value = std::string(objectJson.substr(cursor + 1, valueEnd - cursor - 1));
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

std::map<std::string, std::string, std::less<>> ParseFlatStringMapObject(std::string_view objectJson) {
    std::map<std::string, std::string, std::less<>> parsed;
    auto cursor = objectJson.find('{');
    if (cursor == std::string_view::npos) {
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

std::map<std::string, std::string, std::less<>> ExtractStringMap(std::string_view json, std::string_view key) {
    const auto objectJson = ExtractObjectJson(json, key);
    return ParseFlatStringMapObject(objectJson);
}

std::string EscapeJson(std::string_view value) {
    std::string escaped;
    escaped.reserve(value.size() + 8);
    for (const auto ch : value) {
        switch (ch) {
        case '\\':
            escaped += R"(\\)";
            break;
        case '"':
            escaped += R"(\")";
            break;
        case '\n':
            escaped += R"(\n)";
            break;
        case '\r':
            escaped += R"(\r)";
            break;
        case '\t':
            escaped += R"(\t)";
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
    out << R"("commandId":")" << EscapeJson(result.commandId) << R"(",)";
    out << R"("succeeded":)" << (result.succeeded ? "true" : "false") << ',';
    out << R"("reasonCode":")" << EscapeJson(result.reasonCode) << R"(",)";
    out << R"("backend":")" << EscapeJson(result.backend) << R"(",)";
    out << R"("hookState":")" << EscapeJson(result.hookState) << R"(",)";
    out << R"("message":")" << EscapeJson(result.message) << R"(",)";
    out << R"("diagnostics":)" << (result.diagnosticsJson.empty() ? "{}" : result.diagnosticsJson);
    out << '}';
    return out.str();
}

BridgeResult BuildBridgeFailureResult(
    std::string_view commandId,
    std::string_view hookState,
    std::string_view message,
    std::string_view diagnosticsJson) {
    BridgeResult result {};
    result.commandId = std::string(commandId);
    result.succeeded = false;
    result.reasonCode = "CAPABILITY_BACKEND_UNAVAILABLE";
    result.backend = "extender";
    result.hookState = std::string(hookState);
    result.message = std::string(message);
    result.diagnosticsJson = std::string(diagnosticsJson);
    return result;
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
    if (ConnectNamedPipe(pipe, nullptr)) {
        return true;
    }
    return GetLastError() == ERROR_PIPE_CONNECTED;
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

bool ShouldContinueReading(std::string& commandLine, DWORD bytesRead, std::size_t bufferSize) {
    if (const auto linePos = commandLine.find('\n'); linePos != std::string::npos) {
        commandLine.erase(linePos);
        return false;
    }
    return bytesRead >= bufferSize;
}

std::string ReadCommandLine(HANDLE pipe, std::array<char, kPipeBufferSize>& buffer) {
    std::string commandLine;
    DWORD bytesRead = 0;
    while (ReadFile(pipe, buffer.data(), static_cast<DWORD>(buffer.size()), &bytesRead, nullptr) != FALSE && bytesRead > 0) {
        commandLine.append(buffer.data(), bytesRead);
        if (!ShouldContinueReading(commandLine, bytesRead, buffer.size())) {
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
    (void)WriteFile(pipe, response.data(), static_cast<DWORD>(response.size()), &bytesWritten, nullptr);
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

    worker_ = std::jthread([this]() { runLoop(); });
    return true;
}

void NamedPipeBridgeServer::stop() {
    if (!running_.exchange(false)) {
        return;
    }

#if defined(_WIN32)
    const auto fullPipeName = BuildFullPipeName(pipeName_);
    const auto deadline = std::chrono::steady_clock::now() + std::chrono::milliseconds(800);
    while (std::chrono::steady_clock::now() < deadline) {
        if (auto client = CreateFileA(
                fullPipeName.c_str(),
                GENERIC_READ | GENERIC_WRITE,
                0,
                nullptr,
                OPEN_EXISTING,
                0,
                nullptr);
            client != INVALID_HANDLE_VALUE) {
            CloseHandle(client);
            break;
        }
        std::this_thread::sleep_for(kClientWakePollDelay);
    }
#endif

    // jthread auto-joins, but we request_stop + join explicitly for deterministic shutdown
    if (worker_.joinable()) {
        worker_.join();
    }
}

bool NamedPipeBridgeServer::running() const noexcept {
    return running_.load();
}

BridgeResult NamedPipeBridgeServer::handleRawCommand(std::string_view jsonLine) const {
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
    if (std::int32_t processId = 0; TryReadInt(jsonLine, "processId", processId)) {
        command.processId = processId;
    }

    if (command.commandId.empty()) {
        return BuildBridgeFailureResult(
            {},
            "invalid_command",
            "Command payload missing commandId.",
            R"({"parseError":"missing_commandId"})");
    }

    if (!handler_) {
        return BuildBridgeFailureResult(
            command.commandId,
            "handler_missing",
            "Bridge handler is not configured.",
            R"({"handler":"missing"})");
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

void NamedPipeBridgeServer::runLoop() const {
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
        auto pipe = INVALID_HANDLE_VALUE;
        if (!TryCreateConnectedPipe(fullPipeName, pipe)) {
            continue;
        }

        ProcessConnectedClient(pipe, buffer, handleCommand);
    }
#endif
}

} // namespace swfoc::extender::bridge
