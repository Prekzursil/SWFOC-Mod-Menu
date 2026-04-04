// cppcheck-suppress-file missingIncludeSystem
#include "swfoc_extender/bridge/NamedPipeBridgeServer.hpp"
#include "swfoc_extender/plugins/BuildPatchPlugin.hpp"
#include "swfoc_extender/plugins/EconomyPlugin.hpp"
#include "swfoc_extender/plugins/GlobalTogglePlugin.hpp"
#include "swfoc_extender/plugins/HelperLuaPlugin.hpp"
#include "swfoc_extender/plugins/ProcessMutationHelpers.hpp"

#include <algorithm>
#include <array>
#include <atomic>
#include <chrono>
#include <cctype>
#include <cstdint>
#include <cstdlib>
#include <iostream>
#include <map>
#include <memory>
#include <sstream>
#include <string>
#include <string_view>
#include <thread>
#include <vector>

#if defined(_WIN32)
#define NOMINMAX
#include <Windows.h>
#endif

namespace swfoc::extender::bridge::host_json {
std::string EscapeJson(std::string_view value);
std::string ToDiagnosticsJson(const StringMap& values);
bool TryReadBool(std::string_view payloadJson, std::string_view key, bool& value);
bool TryReadInt(std::string_view payloadJson, std::string_view key, int& value);
std::string ExtractStringValue(std::string_view json, std::string_view key);
StringMap ExtractStringMap(std::string_view json, std::string_view key);
} // namespace swfoc::extender::bridge::host_json

namespace {

using swfoc::extender::bridge::BridgeCommand;
using swfoc::extender::bridge::BridgeResult;
using swfoc::extender::bridge::NamedPipeBridgeServer;
using swfoc::extender::bridge::StringMap;
using swfoc::extender::plugins::BuildPatchPlugin;
using swfoc::extender::plugins::CapabilitySnapshot;
using swfoc::extender::plugins::CapabilityState;
using swfoc::extender::plugins::EconomyPlugin;
using swfoc::extender::plugins::GlobalTogglePlugin;
using swfoc::extender::plugins::HelperLuaPlugin;
using swfoc::extender::plugins::PluginRequest;
using swfoc::extender::plugins::PluginResult;
namespace process_mutation = swfoc::extender::plugins::process_mutation;
using swfoc::extender::bridge::host_json::EscapeJson;
using swfoc::extender::bridge::host_json::ExtractStringMap;
using swfoc::extender::bridge::host_json::ExtractStringValue;
using swfoc::extender::bridge::host_json::ToDiagnosticsJson;
using swfoc::extender::bridge::host_json::TryReadBool;
using swfoc::extender::bridge::host_json::TryReadInt;

// S5421: global constants
constexpr const char* kBackendName = "extender";
constexpr const char* kDefaultPipeName = "SwfocExtenderBridge";

constexpr std::array<const char*, 14> kSupportedFeatures = {
    "freeze_timer",
    "toggle_fog_reveal",
    "toggle_ai",
    "set_unit_cap",
    "toggle_instant_build_patch",
    "set_credits",
    "spawn_unit_helper",
    "spawn_context_entity",
    "spawn_tactical_entity",
    "spawn_galactic_entity",
    "place_planet_building",
    "set_context_allegiance",
    "set_hero_state_helper",
    "toggle_roe_respawn_helper"};

/*
Cppcheck note (targeted): if cppcheck runs without STL/Windows SDK include paths,
missingIncludeSystem can be suppressed per translation unit with:
  --suppress=missingIncludeSystem:native/SwfocExtender.Bridge/src/BridgeHostMain.cpp
*/

bool ResolveLockCredits(std::string_view payloadJson) {
    if (auto lockCredits = false; TryReadBool(payloadJson, "lockCredits", lockCredits)) {
        return lockCredits;
    }

    if (auto legacyForce = false; TryReadBool(payloadJson, "forcePatchHook", legacyForce)) {
        return legacyForce;
    }

    return false;
}

int ResolveProcessId(const BridgeCommand& command) {
    if (command.processId > 0) {
        return command.processId;
    }

    if (auto payloadProcessId = 0; TryReadInt(command.payloadJson, "processId", payloadProcessId) && payloadProcessId > 0) {
        return payloadProcessId;
    }

    return 0;
}

StringMap ResolveAnchors(const BridgeCommand& command) {
    auto anchors = command.resolvedAnchors;

    const auto payloadAnchors = ExtractStringMap(command.payloadJson, "anchors");
    for (const auto& [key, value] : payloadAnchors) {
        anchors[key] = value;
    }

    // S6171: use contains() instead of find() != end()
    if (const auto legacySymbol = ExtractStringValue(command.payloadJson, "symbol"); !legacySymbol.empty() && !anchors.contains(legacySymbol)) {
        anchors.try_emplace(legacySymbol, legacySymbol);
    }

    return anchors;
}

PluginRequest BuildPluginRequest(const BridgeCommand& command) {
    PluginRequest request {};
    request.identity.featureId = command.featureId;
    request.identity.profileId = command.profileId;
    request.identity.processId = ResolveProcessId(command);
    request.anchors = ResolveAnchors(command);
    request.payload.lockValue = ResolveLockCredits(command.payloadJson);
    request.helperBridge.helperHookId = ExtractStringValue(command.payloadJson, "helperHookId");
    request.helperBridge.helperEntryPoint = ExtractStringValue(command.payloadJson, "helperEntryPoint");
    request.helperBridge.helperScript = ExtractStringValue(command.payloadJson, "helperScript");
    request.helperBridge.operationKind = ExtractStringValue(command.payloadJson, "operationKind");
    request.helperBridge.operationToken = ExtractStringValue(command.payloadJson, "operationToken");
    request.helperBridge.invocationContractVersion = ExtractStringValue(command.payloadJson, "helperInvocationContractVersion");
    request.entityContext.unitId = ExtractStringValue(command.payloadJson, "unitId");
    request.entityContext.entityId = ExtractStringValue(command.payloadJson, "entityId");
    request.entityContext.entryMarker = ExtractStringValue(command.payloadJson, "entryMarker");
    request.entityContext.faction = ExtractStringValue(command.payloadJson, "faction");
    request.entityContext.targetFaction = ExtractStringValue(command.payloadJson, "targetFaction");
    request.entityContext.sourceFaction = ExtractStringValue(command.payloadJson, "sourceFaction");
    request.entityContext.globalKey = ExtractStringValue(command.payloadJson, "globalKey");
    request.entityContext.populationPolicy = ExtractStringValue(command.payloadJson, "populationPolicy");
    request.entityContext.persistencePolicy = ExtractStringValue(command.payloadJson, "persistencePolicy");
    request.entityContext.placementMode = ExtractStringValue(command.payloadJson, "placementMode");
    request.entityContext.worldPosition = ExtractStringValue(command.payloadJson, "worldPosition");

    if (auto intValue = 0; TryReadInt(command.payloadJson, "intValue", intValue)) {
        request.payload.intValue = intValue;
    }

    if (auto boolValue = false; TryReadBool(command.payloadJson, "boolValue", boolValue)) {
        request.payload.boolValue = boolValue;
    }

    if (auto enable = false; TryReadBool(command.payloadJson, "enable", enable)) {
        request.payload.enable = enable;
    } else if (command.featureId == "set_unit_cap" || command.featureId == "toggle_instant_build_patch") {
        request.payload.enable = true;
    }

    if (auto allowCrossFaction = false; TryReadBool(command.payloadJson, "allowCrossFaction", allowCrossFaction)) {
        request.payload.allowCrossFaction = allowCrossFaction;
    }

    if (auto forceOverride = false; TryReadBool(command.payloadJson, "forceOverride", forceOverride)) {
        request.payload.forceOverride = forceOverride;
    }

    return request;
}

// S5566: use ranges algorithm instead of manual loop
bool IsSupportedFeature(std::string_view featureId) {
    return std::ranges::any_of(kSupportedFeatures, [&](const char* supported) {
        return featureId == supported;
    });
}

bool IsGlobalToggleFeature(std::string_view featureId) {
    return featureId == "freeze_timer" ||
           featureId == "toggle_fog_reveal" ||
           featureId == "toggle_ai";
}

bool IsHelperFeature(std::string_view featureId) {
    constexpr std::array<const char*, 8> kHelperFeatures = {
        "spawn_unit_helper",
        "spawn_context_entity",
        "spawn_tactical_entity",
        "spawn_galactic_entity",
        "place_planet_building",
        "set_context_allegiance",
        "set_hero_state_helper",
        "toggle_roe_respawn_helper"};
    return std::ranges::any_of(kHelperFeatures, [&](const char* f) {
        return featureId == f;
    });
}

void EnsureCapabilityEntries(CapabilitySnapshot& snapshot) {
    for (const auto* featureId : kSupportedFeatures) {
        // S6171: use contains()
        if (snapshot.features.contains(featureId)) {
            continue;
        }

        CapabilityState state {};
        state.available = false;
        state.state = "Unknown";
        state.reasonCode = "CAPABILITY_REQUIRED_MISSING";
        snapshot.features.try_emplace(featureId, state);
    }
}

const char* BoolToString(bool value) {
    return value ? "true" : "false";
}

struct AnchorProbeResult {
    bool available {false};
    bool parseOk {false};
    bool readOk {false};
    std::string anchorKey {};
    std::string anchorValue {};
    std::string readError {};
    std::string reasonCode {"CAPABILITY_REQUIRED_MISSING"};
    std::string probeSource {"candidate_missing"};
};

std::string ResolveProbeSource(std::string_view anchorValue) {
    if (anchorValue.empty()) {
        return "candidate_missing";
    }

    return anchorValue == "probe" ? "seed_placeholder" : "resolved_anchor";
}

// S134/S924: extracted helper to reduce nesting in ProbeReadableAnchor
AnchorProbeResult ProbeCandidate(
    const PluginRequest& probeContext,
    std::string_view candidateKey,
    std::string_view candidateValue) {
    AnchorProbeResult result {};
    result.anchorKey = candidateKey;
    result.anchorValue = candidateValue;
    result.probeSource = ResolveProbeSource(candidateValue);

    std::uintptr_t address = 0;
    result.parseOk = process_mutation::TryParseAddress(candidateValue, address);
    if (!result.parseOk) {
        result.reasonCode = "CAPABILITY_ANCHOR_INVALID";
        return result;
    }

    std::vector<std::uint8_t> bytes;
    std::string readError;
    result.readOk = process_mutation::TryReadBytes(probeContext.processId(), address, 1, bytes, readError);
    if (!result.readOk) {
        result.readError = readError;
        result.reasonCode = "CAPABILITY_ANCHOR_UNREADABLE";
        return result;
    }

    result.available = true;
    result.reasonCode = "CAPABILITY_PROBE_PASS";
    return result;
}

AnchorProbeResult ProbeReadableAnchor(
    const PluginRequest& probeContext,
    std::initializer_list<const char*> candidates) {
    AnchorProbeResult result {};
    if (probeContext.processId() <= 0) {
        result.reasonCode = "CAPABILITY_REQUIRED_MISSING";
        result.probeSource = "process_missing";
        return result;
    }

    for (const auto* candidate : candidates) {
        const auto it = probeContext.anchors.find(candidate);
        if (it == probeContext.anchors.end() || it->second.empty()) {
            continue;
        }

        return ProbeCandidate(probeContext, it->first, it->second);
    }

    result.reasonCode = "CAPABILITY_REQUIRED_MISSING";
    return result;
}

CapabilityState BuildProbeState(const AnchorProbeResult& probe) {
    CapabilityState state {};
    state.available = probe.available;
    state.state = probe.available ? "Verified" : "Unavailable";
    state.reasonCode = probe.reasonCode;
    state.diagnostics = {
        {"anchorKey", probe.anchorKey},
        {"anchorValue", probe.anchorValue},
        {"parseOk", BoolToString(probe.parseOk)},
        {"readOk", BoolToString(probe.readOk)},
        {"readError", probe.readError},
        {"probeSource", probe.probeSource}};
    return state;
}

void AddProbeFeature(
    CapabilitySnapshot& snapshot,
    const PluginRequest& probeContext,
    const char* featureId,
    std::initializer_list<const char*> anchorCandidates) {
    const auto probe = ProbeReadableAnchor(probeContext, anchorCandidates);
    // S6030: try_emplace
    snapshot.features.try_emplace(featureId, BuildProbeState(probe));
}

void AddHelperProbeFeature(
    CapabilitySnapshot& snapshot,
    const PluginRequest& probeContext,
    const char* featureId) {
    CapabilityState state {};
    state.available = probeContext.processId() > 0;
    state.state = state.available ? "Verified" : "Unavailable";
    state.reasonCode = state.available ? "CAPABILITY_PROBE_PASS" : "HELPER_BRIDGE_UNAVAILABLE";
    state.diagnostics = {
        {"probeSource", "native_helper_bridge"},
        {"processId", std::to_string(probeContext.processId())},
        {"helperBridgeState", state.available ? "ready" : "unavailable"}};
    // S6030: try_emplace
    snapshot.features.try_emplace(featureId, state);
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
        {"instant_build_patch_injection", "instant_build_patch", "instant_build", "toggle_instant_build_patch"});
    AddHelperProbeFeature(snapshot, probeContext, "spawn_unit_helper");
    AddHelperProbeFeature(snapshot, probeContext, "spawn_context_entity");
    AddHelperProbeFeature(snapshot, probeContext, "spawn_tactical_entity");
    AddHelperProbeFeature(snapshot, probeContext, "spawn_galactic_entity");
    AddHelperProbeFeature(snapshot, probeContext, "place_planet_building");
    AddHelperProbeFeature(snapshot, probeContext, "set_context_allegiance");
    AddHelperProbeFeature(snapshot, probeContext, "set_hero_state_helper");
    AddHelperProbeFeature(snapshot, probeContext, "toggle_roe_respawn_helper");

    EnsureCapabilityEntries(snapshot);
    return snapshot;
}

void AppendDiagnosticsJson(std::ostringstream& out, const StringMap& diagnostics) {
    out << R"(,"diagnostics":{)";
    auto firstDiagnostic = true;
    for (const auto& [key, value] : diagnostics) {
        if (!firstDiagnostic) {
            out << ',';
        }
        firstDiagnostic = false;
        out << '"' << EscapeJson(key) << R"(":")" << EscapeJson(value) << '"';
    }
    out << '}';
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
            << '"' << EscapeJson(featureId) << R"(":{)"
            << R"("available":)" << (state.available ? "true" : "false") << ','
            << R"("state":")" << EscapeJson(state.state) << R"(",)"
            << R"("reasonCode":")" << EscapeJson(state.reasonCode) << '"';
        if (!state.diagnostics.empty()) {
            AppendDiagnosticsJson(out, state.diagnostics);
        }

        out << '}';
    }
    out << '}';
    return out.str();
}

// S5566: use ranges algorithm
std::string ResolveProbeHookState(const CapabilitySnapshot& snapshot) {
    const auto hasAvailable = std::ranges::any_of(
        snapshot.features, [](const auto& entry) { return entry.second.available; });
    return hasAvailable ? "HOOK_READY" : "HOOK_NOT_INSTALLED";
}

BridgeResult BuildBridgeResult(
    const BridgeCommand& command,
    bool succeeded,
    std::string_view reasonCode,
    std::string_view hookState,
    std::string_view message,
    std::string_view diagnosticsJson) {
    BridgeResult result {};
    result.commandId = command.commandId;
    result.succeeded = succeeded;
    result.reasonCode = std::string(reasonCode);
    result.backend = kBackendName;
    result.hookState = std::string(hookState);
    result.message = std::string(message);
    result.diagnosticsJson = std::string(diagnosticsJson);
    return result;
}

BridgeResult BuildHealthResult(const BridgeCommand& command) {
    return BuildBridgeResult(command, true, "CAPABILITY_PROBE_PASS", "RUNNING", "Extender bridge is healthy.", R"({"bridge":"active"})");
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
        << R"("bridge":"active",)"
        << R"("processId":)" << probeContext.processId() << ','
        << R"("anchorCount":)" << probeContext.anchors.size() << ','
        << R"("capabilities":)" << CapabilitySnapshotToJson(merged)
        << "}";

    return BuildBridgeResult(command, true, "CAPABILITY_PROBE_PASS", ResolveProbeHookState(merged), "Capability probe completed.", diagnostics.str());
}

BridgeResult BuildMissingIntValueResult(const BridgeCommand& command) {
    return BuildBridgeResult(
        command, false, "CAPABILITY_REQUIRED_MISSING", "DENIED", "Payload is missing required intValue.", R"({"requiredField":"intValue"})");
}

BridgeResult BuildBridgeResultFromPlugin(
    const BridgeCommand& command,
    const PluginRequest& pluginRequest,
    const PluginResult& pluginResult) {
    auto diagnostics = pluginResult.diagnostics;

    diagnostics["featureId"] = command.featureId;
    if (pluginRequest.processId() > 0) {
        diagnostics["processId"] = std::to_string(pluginRequest.processId());
    }

    if (!command.processName.empty()) {
        diagnostics["processName"] = command.processName;
    }

    diagnostics["anchorCount"] = std::to_string(pluginRequest.anchors.size());

    return BuildBridgeResult(command, pluginResult.succeeded, pluginResult.reasonCode, pluginResult.hookState, pluginResult.message, ToDiagnosticsJson(diagnostics));
}

BridgeResult BuildSetCreditsResult(const BridgeCommand& command, EconomyPlugin& economyPlugin) {
    auto intValue = 0;
    if (!TryReadInt(command.payloadJson, "intValue", intValue)) {
        return BuildMissingIntValueResult(command);
    }

    auto pluginRequest = BuildPluginRequest(command);
    pluginRequest.payload.intValue = intValue;

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

BridgeResult BuildHelperResult(const BridgeCommand& command, HelperLuaPlugin& helperLuaPlugin) {
    auto pluginRequest = BuildPluginRequest(command);
    return BuildBridgeResultFromPlugin(command, pluginRequest, helperLuaPlugin.execute(pluginRequest));
}

BridgeResult BuildUnsupportedFeatureResult(const BridgeCommand& command) {
    return BuildBridgeResult(
        command, false, "CAPABILITY_REQUIRED_MISSING", "DENIED", "Feature not supported by current extender host.",
        R"({"featureId":")" + EscapeJson(command.featureId) + R"("})");
}

BridgeResult HandleBridgeCommand(
    const BridgeCommand& command,
    EconomyPlugin& economyPlugin,
    GlobalTogglePlugin& globalTogglePlugin,
    BuildPatchPlugin& buildPatchPlugin,
    HelperLuaPlugin& helperLuaPlugin) {
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

    if (IsGlobalToggleFeature(command.featureId)) {
        return BuildGlobalToggleResult(command, globalTogglePlugin);
    }

    if (IsHelperFeature(command.featureId)) {
        return BuildHelperResult(command, helperLuaPlugin);
    }

    return BuildPatchResult(command, buildPatchPlugin);
}

// S1874: replace deprecated std::getenv with MSVC-safe _dupenv_s
std::string GetEnvSafe(const char* name) {
    char* val = nullptr;
    if (std::size_t len = 0; _dupenv_s(&val, &len, name) != 0 || val == nullptr) {
        return {};
    }
    auto guard = std::unique_ptr<char, decltype(&free)>(val, &free);
    return {guard.get()};
}

std::string ResolvePipeName() {
    if (const auto envPipe = GetEnvSafe("SWFOC_EXTENDER_PIPE_NAME"); !envPipe.empty()) {
        return envPipe;
    }

    return kDefaultPipeName;
}

namespace {
constinit std::atomic<bool> g_running{true};
} // namespace

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
    BuildPatchPlugin& buildPatchPlugin,
    HelperLuaPlugin& helperLuaPlugin) {
    server.setHandler([&economyPlugin, &globalTogglePlugin, &buildPatchPlugin, &helperLuaPlugin](const BridgeCommand& command) {
        return HandleBridgeCommand(command, economyPlugin, globalTogglePlugin, buildPatchPlugin, helperLuaPlugin);
    });
}

int RunBridgeHost(
    std::string_view pipeName,
    EconomyPlugin& economyPlugin,
    GlobalTogglePlugin& globalTogglePlugin,
    BuildPatchPlugin& buildPatchPlugin,
    HelperLuaPlugin& helperLuaPlugin) {
    NamedPipeBridgeServer server{std::string(pipeName)};
    ConfigureBridgeHandler(server, economyPlugin, globalTogglePlugin, buildPatchPlugin, helperLuaPlugin);

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
    HelperLuaPlugin helperLuaPlugin;
    return RunBridgeHost(pipeName, economyPlugin, globalTogglePlugin, buildPatchPlugin, helperLuaPlugin);
}
