#include "swfoc_extender/plugins/GlobalTogglePlugin.hpp"

// cppcheck-suppress missingIncludeSystem
#include <array>
// cppcheck-suppress missingIncludeSystem
#include <optional>
// cppcheck-suppress missingIncludeSystem
#include <string>
// cppcheck-suppress missingIncludeSystem
#include <string_view>

namespace swfoc::extender::plugins {

namespace {

using AnchorMatch = std::pair<std::string, std::string>;

constexpr std::array<std::string_view, 2> kFreezeTimerAnchors {"game_timer_freeze", "freeze_timer"};
constexpr std::array<std::string_view, 2> kFogRevealAnchors {"fog_reveal", "toggle_fog_reveal"};
constexpr std::array<std::string_view, 2> kAiAnchors {"ai_enabled", "toggle_ai"};

bool IsGlobalToggleFeature(const std::string& featureId) {
    return featureId == "freeze_timer" || featureId == "toggle_fog_reveal" || featureId == "toggle_ai";
}

const std::array<std::string_view, 2>& AnchorCandidates(const std::string& featureId) {
    if (featureId == "freeze_timer") {
        return kFreezeTimerAnchors;
    }

    if (featureId == "toggle_fog_reveal") {
        return kFogRevealAnchors;
    }

    return kAiAnchors;
}

std::optional<AnchorMatch> FindAnchor(const PluginRequest& request, const std::string& featureId) {
    const auto& candidates = AnchorCandidates(featureId);
    for (const auto key : candidates) {
        const auto it = request.anchors.find(std::string(key));
        if (it != request.anchors.end() && !it->second.empty()) {
            return AnchorMatch {it->first, it->second};
        }
    }

    return std::nullopt;
}

PluginResult BuildUnsupportedFeatureResult(const PluginRequest& request) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "CAPABILITY_REQUIRED_MISSING";
    result.hookState = "DENIED";
    result.message = "Global toggle plugin only handles freeze_timer, toggle_fog_reveal, and toggle_ai.";
    result.diagnostics = {{"featureId", request.featureId}};
    return result;
}

PluginResult BuildMissingProcessResult(const PluginRequest& request) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "CAPABILITY_REQUIRED_MISSING";
    result.hookState = "DENIED";
    result.message = "processId is required for global toggle mutations.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"requiredField", "processId"},
        {"processId", std::to_string(request.processId)}};
    return result;
}

PluginResult BuildMissingAnchorResult(const PluginRequest& request) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "CAPABILITY_REQUIRED_MISSING";
    result.hookState = "DENIED";
    result.message = "anchors map missing required symbol anchor for feature.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"requiredField", "anchors"},
        {"anchorCount", std::to_string(request.anchors.size())}};
    return result;
}

CapabilityState BuildCapabilityState() {
    CapabilityState state {};
    state.available = true;
    state.state = "Verified";
    state.reasonCode = "CAPABILITY_PROBE_PASS";
    return state;
}

} // namespace

const char* GlobalTogglePlugin::id() const noexcept {
    return "global_toggle";
}

PluginResult GlobalTogglePlugin::execute(const PluginRequest& request) {
    if (!IsGlobalToggleFeature(request.featureId)) {
        return BuildUnsupportedFeatureResult(request);
    }

    if (request.processId <= 0) {
        return BuildMissingProcessResult(request);
    }

    const auto resolvedAnchor = FindAnchor(request, request.featureId);
    if (!resolvedAnchor.has_value()) {
        return BuildMissingAnchorResult(request);
    }

    const bool nextValue = request.boolValue;
    if (request.featureId == "freeze_timer") {
        freezeTimerInstalled_.store(true);
        freezeTimerEnabled_.store(nextValue);
    } else if (request.featureId == "toggle_fog_reveal") {
        fogRevealInstalled_.store(true);
        fogRevealEnabled_.store(nextValue);
    } else {
        aiToggleInstalled_.store(true);
        aiEnabled_.store(nextValue);
    }

    PluginResult result {};
    result.succeeded = true;
    result.reasonCode = "CAPABILITY_PROBE_PASS";
    result.hookState = nextValue ? "HOOK_ENABLED" : "HOOK_DISABLED";
    result.message = "Global toggle mutation accepted by extender plugin.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"processId", std::to_string(request.processId)},
        {"anchorKey", resolvedAnchor->first},
        {"anchorValue", resolvedAnchor->second},
        {"boolValue", nextValue ? "true" : "false"}};
    return result;
}

CapabilitySnapshot GlobalTogglePlugin::capabilitySnapshot() const {
    CapabilitySnapshot snapshot {};
    snapshot.features.emplace("freeze_timer", BuildCapabilityState());
    snapshot.features.emplace("toggle_fog_reveal", BuildCapabilityState());
    snapshot.features.emplace("toggle_ai", BuildCapabilityState());
    return snapshot;
}

} // namespace swfoc::extender::plugins
