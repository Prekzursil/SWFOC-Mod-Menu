// cppcheck-suppress-file missingIncludeSystem
#include "swfoc_extender/bridge/NamedPipeBridgeServer.hpp"
#include "swfoc_extender/plugins/BuildPatchPlugin.hpp"
#include "swfoc_extender/plugins/EconomyPlugin.hpp"
#include "swfoc_extender/plugins/GlobalTogglePlugin.hpp"

#include <array>
#include <atomic>
#include <chrono>
#include <cctype>
#include <cstdlib>
#include <iostream>
#include <map>
#include <sstream>
#include <string>
#include <thread>

#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#endif

namespace {

using swfoc::extender::bridge::BridgeCommand;
using swfoc::extender::bridge::BridgeResult;
using swfoc::extender::bridge::NamedPipeBridgeServer;
using swfoc::extender::plugins::BuildPatchPlugin;
using swfoc::extender::plugins::CapabilitySnapshot;
using swfoc::extender::plugins::CapabilityState;
using swfoc::extender::plugins::EconomyPlugin;
using swfoc::extender::plugins::GlobalTogglePlugin;
using swfoc::extender::plugins::PluginRequest;
using swfoc::extender::plugins::PluginResult;

constexpr const char* kBackendName = "extender";
constexpr const char* kDefaultPipeName = "SwfocExtenderBridge";

constexpr std::array<const char*, 6> kSupportedFeatures {
    "freeze_timer",
    "toggle_fog_reveal",
    "toggle_ai",
    "set_unit_cap",
    "toggle_instant_build_patch",
    "set_credits"};

/*
Cppcheck note (targeted): if cppcheck runs without STL/Windows SDK include paths,
missingIncludeSystem can be suppressed per translation unit with:
  --suppress=missingIncludeSystem:native/SwfocExtender.Bridge/src/BridgeHostMain.cpp
*/

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

std::string ToDiagnosticsJson(const std::map<std::string, std::string>& values) {
    std::ostringstream out;
    out << '{';
    auto first = true;
    for (const auto& [key, value] : values) {
        if (!first) {
            out << ',';
        }
        first = false;
        out << '"' << EscapeJson(key) << "\":\"" << EscapeJson(value) << '"';
    }
    out << '}';
    return out.str();
}

bool TryFindValueStart(const std::string& payloadJson, const std::string& key, std::size_t& start) {
    const auto quotedKey = "\"" + key + "\"";
    const auto keyPos = payloadJson.find(quotedKey);
    if (keyPos == std::string::npos) {
        return false;
    }

    const auto colonPos = payloadJson.find(':', keyPos + quotedKey.size());
    if (colonPos == std::string::npos) {
        return false;
    }

    start = payloadJson.find_first_not_of(" \t\r\n", colonPos + 1);
    return start != std::string::npos;
}

bool TryReadBool(const std::string& payloadJson, const std::string& key, bool& value) {
    std::size_t start = 0;
    if (!TryFindValueStart(payloadJson, key, start)) {
        return false;
    }

    if (payloadJson.compare(start, 4, "true") == 0) {
        value = true;
        return true;
    }

    if (payloadJson.compare(start, 5, "false") == 0) {
        value = false;
        return true;
    }

    return false;
}

bool TryParseIntFromText(const std::string& valueText, int& value) {
    try {
        std::size_t consumed = 0;
        value = std::stoi(valueText, &consumed);
        return consumed != 0;
    } catch (...) {
        return false;
    }
}

bool TryReadInt(const std::string& payloadJson, const std::string& key, int& value) {
    std::size_t start = 0;
    if (!TryFindValueStart(payloadJson, key, start)) {
        return false;
    }

    if (payloadJson[start] == '+') {
        return false;
    }

    return TryParseIntFromText(payloadJson.substr(start), value);
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

bool TryParseFlatStringMapEntry(
    const std::string& objectJson,
    std::size_t& cursor,
    std::string& key,
    std::string& value) {
    cursor = SkipAsciiWhitespace(objectJson, cursor);
    if (cursor == std::string::npos || objectJson[cursor] == '}') {
        return false;
    }

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
    if (cursor == std::string::npos) {
        return false;
    }

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

    cursor = SkipAsciiWhitespace(objectJson, cursor);
    if (cursor != std::string::npos && objectJson[cursor] == ',') {
        ++cursor;
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
    return ParseFlatStringMapObject(ExtractObjectJson(json, key));
}

bool ResolveLockCredits(const std::string& payloadJson) {
    bool lockCredits = false;
    if (TryReadBool(payloadJson, "lockCredits", lockCredits)) {
        return lockCredits;
    }

    bool legacyForce = false;
    return TryReadBool(payloadJson, "forcePatchHook", legacyForce) && legacyForce;
}

int ResolveProcessId(const BridgeCommand& command) {
    if (command.processId > 0) {
        return command.processId;
    }

    int payloadProcessId = 0;
    if (TryReadInt(command.payloadJson, "processId", payloadProcessId) && payloadProcessId > 0) {
        return payloadProcessId;
    }

    return 0;
}

std::map<std::string, std::string> ResolveAnchors(const BridgeCommand& command) {
    auto anchors = command.resolvedAnchors;

    const auto payloadAnchors = ExtractStringMap(command.payloadJson, "anchors");
    for (const auto& [key, value] : payloadAnchors) {
        anchors[key] = value;
    }

    const auto legacySymbol = ExtractStringValue(command.payloadJson, "symbol");
    if (!legacySymbol.empty() && anchors.find(legacySymbol) == anchors.end()) {
        anchors.emplace(legacySymbol, legacySymbol);
    }

    return anchors;
}

PluginRequest BuildPluginRequest(const BridgeCommand& command) {
    PluginRequest request {};
    request.featureId = command.featureId;
    request.profileId = command.profileId;
    request.processId = ResolveProcessId(command);
    request.anchors = ResolveAnchors(command);
    request.lockValue = ResolveLockCredits(command.payloadJson);

    int intValue = 0;
    if (TryReadInt(command.payloadJson, "intValue", intValue)) {
        request.intValue = intValue;
    }

    bool boolValue = false;
    if (TryReadBool(command.payloadJson, "boolValue", boolValue)) {
        request.boolValue = boolValue;
    }

    bool enable = false;
    if (TryReadBool(command.payloadJson, "enable", enable)) {
        request.enable = enable;
    }

    return request;
}

bool IsSupportedFeature(const std::string& featureId) {
    for (const auto* supported : kSupportedFeatures) {
        if (featureId == supported) {
            return true;
        }
    }

    return false;
}

void EnsureCapabilityEntries(CapabilitySnapshot& snapshot) {
    for (const auto* featureId : kSupportedFeatures) {
        if (snapshot.features.find(featureId) != snapshot.features.end()) {
            continue;
        }

        CapabilityState state {};
        state.available = false;
        state.state = "Unknown";
        state.reasonCode = "CAPABILITY_REQUIRED_MISSING";
        snapshot.features.emplace(featureId, state);
    }
}

CapabilitySnapshot MergeCapabilitySnapshots(
    const CapabilitySnapshot& economySnapshot,
    const CapabilitySnapshot& globalSnapshot,
    const CapabilitySnapshot& patchSnapshot) {
    CapabilitySnapshot merged {};

    auto mergeInto = [&merged](const CapabilitySnapshot& source) {
        for (const auto& [featureId, state] : source.features) {
            merged.features[featureId] = state;
        }
    };

    mergeInto(economySnapshot);
    mergeInto(globalSnapshot);
    mergeInto(patchSnapshot);
    EnsureCapabilityEntries(merged);
    return merged;
}

std::string CapabilitySnapshotToJson(const CapabilitySnapshot& snapshot) {
    std::ostringstream out;
    out << '{';
    auto first = true;
    for (const auto& [featureId, state] : snapshot.features) {
        if (!first) {
            out << ',';
        }
        first = false;
        out
            << '"' << EscapeJson(featureId) << "\":{"
            << "\"available\":" << (state.available ? "true" : "false") << ','
            << "\"state\":\"" << EscapeJson(state.state) << "\"," 
            << "\"reasonCode\":\"" << EscapeJson(state.reasonCode) << "\""
            << '}';
    }
    out << '}';
    return out.str();
}

std::string ResolveProbeHookState(const CapabilitySnapshot& snapshot) {
    for (const auto& [_, state] : snapshot.features) {
        if (state.available) {
            return "HOOK_READY";
        }
    }

    return "HOOK_NOT_INSTALLED";
}

BridgeResult BuildHealthResult(const BridgeCommand& command) {
    BridgeResult result {};
    result.commandId = command.commandId;
    result.succeeded = true;
    result.reasonCode = "CAPABILITY_PROBE_PASS";
    result.backend = kBackendName;
    result.hookState = "RUNNING";
    result.message = "Extender bridge is healthy.";
    result.diagnosticsJson = "{\"bridge\":\"active\"}";
    return result;
}

BridgeResult BuildCapabilityProbeResult(
    const BridgeCommand& command,
    const EconomyPlugin& economyPlugin,
    const GlobalTogglePlugin& globalTogglePlugin,
    const BuildPatchPlugin& buildPatchPlugin) {
    const auto merged = MergeCapabilitySnapshots(
        economyPlugin.capabilitySnapshot(),
        globalTogglePlugin.capabilitySnapshot(),
        buildPatchPlugin.capabilitySnapshot());

    std::ostringstream diagnostics;
    diagnostics
        << "{"
        << "\"bridge\":\"active\"," 
        << "\"capabilities\":" << CapabilitySnapshotToJson(merged)
        << "}";

    BridgeResult result {};
    result.commandId = command.commandId;
    result.succeeded = true;
    result.reasonCode = "CAPABILITY_PROBE_PASS";
    result.backend = kBackendName;
    result.hookState = ResolveProbeHookState(merged);
    result.message = "Capability probe completed.";
    result.diagnosticsJson = diagnostics.str();
    return result;
}

BridgeResult BuildMissingIntValueResult(const BridgeCommand& command) {
    BridgeResult result {};
    result.commandId = command.commandId;
    result.succeeded = false;
    result.reasonCode = "CAPABILITY_REQUIRED_MISSING";
    result.backend = kBackendName;
    result.hookState = "DENIED";
    result.message = "Payload is missing required intValue.";
    result.diagnosticsJson = "{\"requiredField\":\"intValue\"}";
    return result;
}

BridgeResult BuildBridgeResultFromPlugin(
    const BridgeCommand& command,
    const PluginRequest& pluginRequest,
    PluginResult pluginResult) {
    auto diagnostics = pluginResult.diagnostics;

    diagnostics["featureId"] = command.featureId;
    if (pluginRequest.processId > 0) {
        diagnostics["processId"] = std::to_string(pluginRequest.processId);
    }

    if (!command.processName.empty()) {
        diagnostics["processName"] = command.processName;
    }

    diagnostics["anchorCount"] = std::to_string(pluginRequest.anchors.size());

    BridgeResult result {};
    result.commandId = command.commandId;
    result.succeeded = pluginResult.succeeded;
    result.reasonCode = pluginResult.reasonCode;
    result.backend = kBackendName;
    result.hookState = pluginResult.hookState;
    result.message = pluginResult.message;
    result.diagnosticsJson = ToDiagnosticsJson(diagnostics);
    return result;
}

BridgeResult BuildSetCreditsResult(const BridgeCommand& command, EconomyPlugin& economyPlugin) {
    int intValue = 0;
    if (!TryReadInt(command.payloadJson, "intValue", intValue)) {
        return BuildMissingIntValueResult(command);
    }

    auto pluginRequest = BuildPluginRequest(command);
    pluginRequest.intValue = intValue;

    return BuildBridgeResultFromPlugin(command, pluginRequest, economyPlugin.execute(pluginRequest));
}

BridgeResult BuildGlobalToggleResult(const BridgeCommand& command, GlobalTogglePlugin& globalTogglePlugin) {
    auto pluginRequest = BuildPluginRequest(command);
    return BuildBridgeResultFromPlugin(command, pluginRequest, globalTogglePlugin.execute(pluginRequest));
}

BridgeResult BuildPatchResult(const BridgeCommand& command, BuildPatchPlugin& buildPatchPlugin) {
    auto pluginRequest = BuildPluginRequest(command);
    return BuildBridgeResultFromPlugin(command, pluginRequest, buildPatchPlugin.execute(pluginRequest));
}

BridgeResult BuildUnsupportedFeatureResult(const BridgeCommand& command) {
    BridgeResult result {};
    result.commandId = command.commandId;
    result.succeeded = false;
    result.reasonCode = "CAPABILITY_REQUIRED_MISSING";
    result.backend = kBackendName;
    result.hookState = "DENIED";
    result.message = "Feature not supported by current extender host.";
    result.diagnosticsJson = "{\"featureId\":\"" + EscapeJson(command.featureId) + "\"}";
    return result;
}

BridgeResult HandleBridgeCommand(
    const BridgeCommand& command,
    EconomyPlugin& economyPlugin,
    GlobalTogglePlugin& globalTogglePlugin,
    BuildPatchPlugin& buildPatchPlugin) {
    if (command.featureId == "health") {
        return BuildHealthResult(command);
    }

    if (command.featureId == "probe_capabilities") {
        return BuildCapabilityProbeResult(command, economyPlugin, globalTogglePlugin, buildPatchPlugin);
    }

    if (!IsSupportedFeature(command.featureId)) {
        return BuildUnsupportedFeatureResult(command);
    }

    if (command.featureId == "set_credits") {
        return BuildSetCreditsResult(command, economyPlugin);
    }

    if (command.featureId == "freeze_timer" ||
        command.featureId == "toggle_fog_reveal" ||
        command.featureId == "toggle_ai") {
        return BuildGlobalToggleResult(command, globalTogglePlugin);
    }

    return BuildPatchResult(command, buildPatchPlugin);
}

std::string ResolvePipeName() {
    const auto* envPipe = std::getenv("SWFOC_EXTENDER_PIPE_NAME");
    if (envPipe == nullptr || *envPipe == '\0') {
        return kDefaultPipeName;
    }

    return envPipe;
}

std::atomic<bool> g_running {true};

#if defined(_WIN32)
BOOL WINAPI CtrlHandler(DWORD signalType) {
    if (signalType == CTRL_C_EVENT ||
        signalType == CTRL_CLOSE_EVENT ||
        signalType == CTRL_BREAK_EVENT ||
        signalType == CTRL_SHUTDOWN_EVENT) {
        g_running.store(false);
        return TRUE;
    }
    return FALSE;
}
#endif

void InstallCtrlHandler() {
#if defined(_WIN32)
    SetConsoleCtrlHandler(CtrlHandler, TRUE);
#endif
}

void WaitForShutdownSignal() {
    while (g_running.load()) {
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }
}

void ConfigureBridgeHandler(
    NamedPipeBridgeServer& server,
    EconomyPlugin& economyPlugin,
    GlobalTogglePlugin& globalTogglePlugin,
    BuildPatchPlugin& buildPatchPlugin) {
    server.setHandler([&economyPlugin, &globalTogglePlugin, &buildPatchPlugin](const BridgeCommand& command) {
        return HandleBridgeCommand(command, economyPlugin, globalTogglePlugin, buildPatchPlugin);
    });
}

int RunBridgeHost(
    const std::string& pipeName,
    EconomyPlugin& economyPlugin,
    GlobalTogglePlugin& globalTogglePlugin,
    BuildPatchPlugin& buildPatchPlugin) {
    NamedPipeBridgeServer server(pipeName);
    ConfigureBridgeHandler(server, economyPlugin, globalTogglePlugin, buildPatchPlugin);

    if (!server.start()) {
        std::cerr << "Failed to start extender bridge host." << std::endl;
        return 1;
    }

    std::cout << "SwfocExtender bridge host started on pipe: " << pipeName << std::endl;
    WaitForShutdownSignal();
    server.stop();
    std::cout << "SwfocExtender bridge host stopped." << std::endl;
    return 0;
}

} // namespace

int main() {
    InstallCtrlHandler();
    const auto pipeName = ResolvePipeName();

    EconomyPlugin economyPlugin;
    GlobalTogglePlugin globalTogglePlugin;
    BuildPatchPlugin buildPatchPlugin;
    return RunBridgeHost(pipeName, economyPlugin, globalTogglePlugin, buildPatchPlugin);
}
