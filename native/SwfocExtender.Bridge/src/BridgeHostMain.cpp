#include "swfoc_extender/bridge/NamedPipeBridgeServer.hpp"
#include "swfoc_extender/plugins/EconomyPlugin.hpp"

#include <atomic>
#include <chrono>
#include <cctype>
#include <cstdlib>
#include <iostream>
#include <sstream>
#include <string>
#include <thread>

#if defined(_WIN32)
#define NOMINMAX
#include <windows.h>
#endif

namespace {

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

bool TryReadBool(const std::string& payloadJson, const std::string& key, bool& value) {
    const auto quotedKey = "\"" + key + "\"";
    auto keyPos = payloadJson.find(quotedKey);
    if (keyPos == std::string::npos) {
        return false;
    }

    auto colonPos = payloadJson.find(':', keyPos + quotedKey.size());
    if (colonPos == std::string::npos) {
        return false;
    }

    auto start = payloadJson.find_first_not_of(" \t\r\n", colonPos + 1);
    if (start == std::string::npos) {
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
    const auto quotedKey = "\"" + key + "\"";
    auto keyPos = payloadJson.find(quotedKey);
    if (keyPos == std::string::npos) {
        return false;
    }

    auto colonPos = payloadJson.find(':', keyPos + quotedKey.size());
    if (colonPos == std::string::npos) {
        return false;
    }

    auto start = payloadJson.find_first_not_of(" \t\r\n", colonPos + 1);
    if (start == std::string::npos) {
        return false;
    }

    auto end = start;
    if (payloadJson[end] == '-') {
        ++end;
    }
    while (end < payloadJson.size() && std::isdigit(static_cast<unsigned char>(payloadJson[end])) != 0) {
        ++end;
    }

    if (end <= start) {
        return false;
    }

    try {
        value = std::stoi(payloadJson.substr(start, end - start));
        return true;
    } catch (...) {
        return false;
    }
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

} // namespace

int main() {
#if defined(_WIN32)
    SetConsoleCtrlHandler(CtrlHandler, TRUE);
#endif

    const auto* envPipe = std::getenv("SWFOC_EXTENDER_PIPE_NAME");
    std::string pipeName = envPipe == nullptr || std::string(envPipe).empty()
        ? "SwfocExtenderBridge"
        : std::string(envPipe);

    swfoc::extender::plugins::EconomyPlugin economyPlugin;
    swfoc::extender::bridge::NamedPipeBridgeServer server(pipeName);
    server.setHandler([&economyPlugin](const swfoc::extender::bridge::BridgeCommand& command) {
        if (command.featureId == "health") {
            return swfoc::extender::bridge::BridgeResult{
                .commandId = command.commandId,
                .succeeded = true,
                .reasonCode = "CAPABILITY_PROBE_PASS",
                .backend = "extender",
                .hookState = "RUNNING",
                .message = "Extender bridge is healthy.",
                .diagnosticsJson = "{\"bridge\":\"active\"}"
            };
        }

        if (command.featureId == "probe_capabilities") {
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

            return swfoc::extender::bridge::BridgeResult{
                .commandId = command.commandId,
                .succeeded = true,
                .reasonCode = "CAPABILITY_PROBE_PASS",
                .backend = "extender",
                .hookState = capability.creditsAvailable ? "HOOK_READY" : "HOOK_NOT_INSTALLED",
                .message = "Capability probe completed.",
                .diagnosticsJson = diagnostics.str()
            };
        }

        if (command.featureId == "set_credits") {
            int intValue = 0;
            if (!TryReadInt(command.payloadJson, "intValue", intValue)) {
                return swfoc::extender::bridge::BridgeResult{
                    .commandId = command.commandId,
                    .succeeded = false,
                    .reasonCode = "CAPABILITY_REQUIRED_MISSING",
                    .backend = "extender",
                    .hookState = "DENIED",
                    .message = "Payload is missing required intValue.",
                    .diagnosticsJson = "{\"requiredField\":\"intValue\"}"
                };
            }

            bool lockCredits = false;
            if (!TryReadBool(command.payloadJson, "lockCredits", lockCredits)) {
                bool legacyForce = false;
                if (TryReadBool(command.payloadJson, "forcePatchHook", legacyForce) && legacyForce) {
                    lockCredits = true;
                }
            }

            const auto pluginResult = economyPlugin.execute(
                swfoc::extender::plugins::PluginRequest{
                    .featureId = command.featureId,
                    .profileId = command.profileId,
                    .intValue = intValue,
                    .lockValue = lockCredits
                });

            return swfoc::extender::bridge::BridgeResult{
                .commandId = command.commandId,
                .succeeded = pluginResult.succeeded,
                .reasonCode = pluginResult.reasonCode,
                .backend = "extender",
                .hookState = pluginResult.hookState,
                .message = pluginResult.message,
                .diagnosticsJson = ToDiagnosticsJson(pluginResult.diagnostics)
            };
        }

        return swfoc::extender::bridge::BridgeResult{
            .commandId = command.commandId,
            .succeeded = false,
            .reasonCode = "CAPABILITY_REQUIRED_MISSING",
            .backend = "extender",
            .hookState = "DENIED",
            .message = "Feature not supported by current extender host.",
            .diagnosticsJson = "{\"featureId\":\"" + EscapeJson(command.featureId) + "\"}"
        };
    });

    if (!server.start()) {
        std::cerr << "Failed to start extender bridge host." << std::endl;
        return 1;
    }

    std::cout << "SwfocExtender bridge host started on pipe: " << pipeName << std::endl;
    while (g_running.load()) {
        std::this_thread::sleep_for(std::chrono::milliseconds(100));
    }

    server.stop();
    std::cout << "SwfocExtender bridge host stopped." << std::endl;
    return 0;
}
