#include "swfoc_extender/plugins/GlobalTogglePlugin.hpp"
#include "swfoc_extender/plugins/ProcessMutationHelpers.hpp"

// cppcheck-suppress missingIncludeSystem
#include <array>
// cppcheck-suppress missingIncludeSystem
#include <optional>
// cppcheck-suppress missingIncludeSystem
#include <cstdint>
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

PluginResult BuildInvalidAnchorResult(
    const PluginRequest& request,
    const AnchorMatch& resolvedAnchor) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "SAFETY_MUTATION_BLOCKED";
    result.hookState = "DENIED";
    result.message = "anchor value could not be parsed as target address.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"anchorKey", resolvedAnchor.first},
        {"anchorValue", resolvedAnchor.second},
        {"processMutationApplied", "false"}};
    return result;
}

PluginResult BuildWriteFailureResult(
    const PluginRequest& request,
    const AnchorMatch& resolvedAnchor,
    bool boolValue,
    const std::string& error) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "SAFETY_MUTATION_BLOCKED";
    result.hookState = "DENIED";
    result.message = "global toggle process write failed.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"processId", std::to_string(request.processId)},
        {"anchorKey", resolvedAnchor.first},
        {"anchorValue", resolvedAnchor.second},
        {"boolValue", boolValue ? "true" : "false"},
        {"error", error},
        {"processMutationApplied", "false"}};
    return result;
}

PluginResult BuildMutationSuccessResult(
    const PluginRequest& request,
    const AnchorMatch& resolvedAnchor,
    bool boolValue) {
    PluginResult result {};
    result.succeeded = true;
    result.reasonCode = "CAPABILITY_PROBE_PASS";
    result.hookState = "HOOK_ONESHOT";
    result.message = "Global toggle value applied through extender plugin.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"processId", std::to_string(request.processId)},
        {"anchorKey", resolvedAnchor.first},
        {"anchorValue", resolvedAnchor.second},
        {"boolValue", boolValue ? "true" : "false"},
        {"processMutationApplied", "true"}};
    return result;
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
        freezeTimerEnabled_.store(nextValue);
    } else if (request.featureId == "toggle_fog_reveal") {
        fogRevealEnabled_.store(nextValue);
    } else {
        aiEnabled_.store(nextValue);
    }

    std::uintptr_t targetAddress = 0;
    if (!process_mutation::TryParseAddress(resolvedAnchor->second, targetAddress)) {
        return BuildInvalidAnchorResult(request, *resolvedAnchor);
    }

    std::string writeError;
    const auto encoded = static_cast<std::uint8_t>(nextValue ? 1 : 0);
    if (!process_mutation::TryWriteValue<std::uint8_t>(request.processId, targetAddress, encoded, writeError)) {
        return BuildWriteFailureResult(request, *resolvedAnchor, nextValue, writeError);
    }

    return BuildMutationSuccessResult(request, *resolvedAnchor, nextValue);
}

CapabilitySnapshot GlobalTogglePlugin::capabilitySnapshot() const {
    CapabilitySnapshot snapshot {};
    snapshot.features.emplace("freeze_timer", BuildCapabilityState());
    snapshot.features.emplace("toggle_fog_reveal", BuildCapabilityState());
    snapshot.features.emplace("toggle_ai", BuildCapabilityState());
    return snapshot;
}

} // namespace swfoc::extender::plugins
