#include "swfoc_extender/bridge/NamedPipeBridgeServer.hpp"
#include "swfoc_extender/plugins/EconomyPlugin.hpp"
// cppcheck-suppress-file missingIncludeSystem

#include <atomic>
#include <chrono>
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

constexpr const char* kBackendName = "extender";
constexpr const char* kDefaultPipeName = "SwfocExtenderBridge";

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

bool TryReadInt(const std::string& payloadJson, const std::string& key, int& value) {
    std::size_t start = 0;
    if (!TryFindValueStart(payloadJson, key, start)) {
        return false;
    }

    if (payloadJson[start] == '+') {
        return false;
    }

    try {
        std::size_t consumed = 0;
        value = std::stoi(payloadJson.substr(start), &consumed);
        return consumed != 0;
    } catch (...) {
        return false;
    }
}

BridgeResult BuildHealthResult(const BridgeCommand& command) {
    return BridgeResult{
        .commandId = command.commandId,
        .succeeded = true,
        .reasonCode = "CAPABILITY_PROBE_PASS",
        .backend = kBackendName,
        .hookState = "RUNNING",
        .message = "Extender bridge is healthy.",
        .diagnosticsJson = "{\"bridge\":\"active\"}"
    };
}

BridgeResult BuildCapabilityProbeResult(
    const BridgeCommand& command,
    const swfoc::extender::plugins::EconomyPlugin& economyPlugin) {
    const auto capability = economyPlugin.capabilitySnapshot();
    std::ostringstream diagnostics;
    diagnostics
        << "{"
        << "\"bridge\":\"active\","
        << "\"capabilities\":{"
        << "\"set_credits\":{"
        << "\"available\":" << (capability.creditsAvailable ? "true" : "false") << ","
        << "\"state\":\"" << EscapeJson(capability.creditsState) << "\","
        << "\"reasonCode\":\"" << EscapeJson(capability.reasonCode) << "\""
        << "}"
        << "}"
        << "}";

    return BridgeResult{
        .commandId = command.commandId,
        .succeeded = true,
        .reasonCode = "CAPABILITY_PROBE_PASS",
        .backend = kBackendName,
        .hookState = capability.creditsAvailable ? "HOOK_READY" : "HOOK_NOT_INSTALLED",
        .message = "Capability probe completed.",
        .diagnosticsJson = diagnostics.str()
    };
}

bool ResolveLockCredits(const std::string& payloadJson) {
    bool lockCredits = false;
    if (TryReadBool(payloadJson, "lockCredits", lockCredits)) {
        return lockCredits;
    }

    bool legacyForce = false;
    return TryReadBool(payloadJson, "forcePatchHook", legacyForce) && legacyForce;
}

BridgeResult BuildMissingIntValueResult(const BridgeCommand& command) {
    return BridgeResult{
        .commandId = command.commandId,
        .succeeded = false,
        .reasonCode = "CAPABILITY_REQUIRED_MISSING",
        .backend = kBackendName,
        .hookState = "DENIED",
        .message = "Payload is missing required intValue.",
        .diagnosticsJson = "{\"requiredField\":\"intValue\"}"
    };
}

BridgeResult BuildSetCreditsResult(
    const BridgeCommand& command,
    swfoc::extender::plugins::EconomyPlugin& economyPlugin) {
    int intValue = 0;
    if (!TryReadInt(command.payloadJson, "intValue", intValue)) {
        return BuildMissingIntValueResult(command);
    }

    const auto pluginResult = economyPlugin.execute(
        swfoc::extender::plugins::PluginRequest{
            .featureId = command.featureId,
            .profileId = command.profileId,
            .intValue = intValue,
            .lockValue = ResolveLockCredits(command.payloadJson)
        });

    return BridgeResult{
        .commandId = command.commandId,
        .succeeded = pluginResult.succeeded,
        .reasonCode = pluginResult.reasonCode,
        .backend = kBackendName,
        .hookState = pluginResult.hookState,
        .message = pluginResult.message,
        .diagnosticsJson = ToDiagnosticsJson(pluginResult.diagnostics)
    };
}

BridgeResult BuildUnsupportedFeatureResult(const BridgeCommand& command) {
    return BridgeResult{
        .commandId = command.commandId,
        .succeeded = false,
        .reasonCode = "CAPABILITY_REQUIRED_MISSING",
        .backend = kBackendName,
        .hookState = "DENIED",
        .message = "Feature not supported by current extender host.",
        .diagnosticsJson = "{\"featureId\":\"" + EscapeJson(command.featureId) + "\"}"
    };
}

BridgeResult HandleBridgeCommand(
    const BridgeCommand& command,
    swfoc::extender::plugins::EconomyPlugin& economyPlugin) {
    if (command.featureId == "health") {
        return BuildHealthResult(command);
    }

    if (command.featureId == "probe_capabilities") {
        return BuildCapabilityProbeResult(command, economyPlugin);
    }

    if (command.featureId == "set_credits") {
        return BuildSetCreditsResult(command, economyPlugin);
    }

    return BuildUnsupportedFeatureResult(command);
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

} // namespace

int main() {
    InstallCtrlHandler();
    const auto pipeName = ResolvePipeName();

    swfoc::extender::plugins::EconomyPlugin economyPlugin;
    swfoc::extender::bridge::NamedPipeBridgeServer server(pipeName);
    server.setHandler([&economyPlugin](const BridgeCommand& command) {
        return HandleBridgeCommand(command, economyPlugin);
    });

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
