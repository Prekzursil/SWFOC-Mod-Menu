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

namespace swfoc::extender::bridge::host_json {
std::string EscapeJson(const std::string& value);
std::string ToDiagnosticsJson(const std::map<std::string, std::string>& values);
bool TryReadBool(const std::string& payloadJson, const std::string& key, bool& value);
bool TryReadInt(const std::string& payloadJson, const std::string& key, int& value);
std::string ExtractStringValue(const std::string& json, const std::string& key);
std::map<std::string, std::string> ExtractStringMap(const std::string& json, const std::string& key);
} // namespace swfoc::extender::bridge::host_json

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
using swfoc::extender::bridge::host_json::EscapeJson;
using swfoc::extender::bridge::host_json::ExtractStringMap;
using swfoc::extender::bridge::host_json::ExtractStringValue;
using swfoc::extender::bridge::host_json::ToDiagnosticsJson;
using swfoc::extender::bridge::host_json::TryReadBool;
using swfoc::extender::bridge::host_json::TryReadInt;

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

bool HasNonEmptyAnchor(
    const std::map<std::string, std::string>& anchors,
    std::initializer_list<const char*> candidates) {
    for (const auto* candidate : candidates) {
        const auto it = anchors.find(candidate);
        if (it != anchors.end() && !it->second.empty()) {
            return true;
        }
    }

    return false;
}

CapabilityState BuildProbeState(bool available) {
    CapabilityState state {};
    state.available = available;
    state.state = available ? "Verified" : "Unavailable";
    state.reasonCode = available ? "CAPABILITY_PROBE_PASS" : "CAPABILITY_REQUIRED_MISSING";
    return state;
}

bool IsMutationImplementationReady(const std::string& featureId) {
    // Build-patch features are currently addressable but do not provide
    // mature runtime mutation/readback verification in the extender plugin path.
    return featureId != "set_unit_cap" && featureId != "toggle_instant_build_patch";
}

void AddProbeFeature(
    CapabilitySnapshot& snapshot,
    const PluginRequest& probeContext,
    const char* featureId,
    std::initializer_list<const char*> anchorCandidates) {
    const auto processAddressable = probeContext.processId > 0;
    if (!processAddressable) {
        CapabilityState state {};
        state.available = false;
        state.state = "Unavailable";
        state.reasonCode = "CAPABILITY_BACKEND_UNAVAILABLE";
        snapshot.features.emplace(featureId, state);
        return;
    }

    const auto anchorAddressable = HasNonEmptyAnchor(probeContext.anchors, anchorCandidates);
    if (!anchorAddressable) {
        snapshot.features.emplace(featureId, BuildProbeState(false));
        return;
    }

    if (!IsMutationImplementationReady(featureId)) {
        CapabilityState state {};
        state.available = true;
        state.state = "Experimental";
        state.reasonCode = "CAPABILITY_FEATURE_EXPERIMENTAL";
        snapshot.features.emplace(featureId, state);
        return;
    }

    snapshot.features.emplace(featureId, BuildProbeState(true));
}

CapabilitySnapshot BuildCapabilityProbeSnapshot(const PluginRequest& probeContext) {
    CapabilitySnapshot snapshot {};

    AddProbeFeature(snapshot, probeContext, "set_credits", {"credits", "set_credits"});
    AddProbeFeature(snapshot, probeContext, "freeze_timer", {"game_timer_freeze", "freeze_timer"});
    AddProbeFeature(snapshot, probeContext, "toggle_fog_reveal", {"fog_reveal", "toggle_fog_reveal"});
    AddProbeFeature(snapshot, probeContext, "toggle_ai", {"ai_enabled", "toggle_ai"});
    AddProbeFeature(snapshot, probeContext, "set_unit_cap", {"unit_cap", "set_unit_cap"});
    AddProbeFeature(
        snapshot,
        probeContext,
        "toggle_instant_build_patch",
        {"instant_build_patch", "toggle_instant_build_patch"});

    EnsureCapabilityEntries(snapshot);
    return snapshot;
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

BridgeResult BuildBridgeResult(
    const BridgeCommand& command,
    bool succeeded,
    const std::string& reasonCode,
    const std::string& hookState,
    const std::string& message,
    const std::string& diagnosticsJson) {
    BridgeResult result {};
    result.commandId = command.commandId;
    result.succeeded = succeeded;
    result.reasonCode = reasonCode;
    result.backend = kBackendName;
    result.hookState = hookState;
    result.message = message;
    result.diagnosticsJson = diagnosticsJson;
    return result;
}

BridgeResult BuildHealthResult(const BridgeCommand& command) {
    return BuildBridgeResult(command, true, "CAPABILITY_PROBE_PASS", "RUNNING", "Extender bridge is healthy.", "{\"bridge\":\"active\"}");
}

BridgeResult BuildCapabilityProbeResult(
    const BridgeCommand& command,
    const EconomyPlugin&,
    const GlobalTogglePlugin&,
    const BuildPatchPlugin&) {
    const auto probeContext = BuildPluginRequest(command);
    const auto merged = BuildCapabilityProbeSnapshot(probeContext);

    std::ostringstream diagnostics;
    diagnostics
        << "{"
        << "\"bridge\":\"active\","
        << "\"processId\":" << probeContext.processId << ','
        << "\"anchorCount\":" << probeContext.anchors.size() << ','
        << "\"capabilities\":" << CapabilitySnapshotToJson(merged)
        << "}";

    return BuildBridgeResult(command, true, "CAPABILITY_PROBE_PASS", ResolveProbeHookState(merged), "Capability probe completed.", diagnostics.str());
}

BridgeResult BuildMissingIntValueResult(const BridgeCommand& command) {
    return BuildBridgeResult(
        command, false, "CAPABILITY_REQUIRED_MISSING", "DENIED", "Payload is missing required intValue.", "{\"requiredField\":\"intValue\"}");
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

    return BuildBridgeResult(command, pluginResult.succeeded, pluginResult.reasonCode, pluginResult.hookState, pluginResult.message, ToDiagnosticsJson(diagnostics));
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
    return BuildBridgeResult(
        command, false, "CAPABILITY_REQUIRED_MISSING", "DENIED", "Feature not supported by current extender host.", "{\"featureId\":\"" + EscapeJson(command.featureId) + "\"}");
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
