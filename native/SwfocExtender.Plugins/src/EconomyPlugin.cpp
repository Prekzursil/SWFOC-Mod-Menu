#include "swfoc_extender/plugins/EconomyPlugin.hpp"
#include "swfoc_extender/plugins/ProcessMutationHelpers.hpp"

#include <array>
#include <optional>
#include <string_view>

namespace swfoc::extender::plugins {

namespace {

using AnchorMatch = std::pair<std::string, std::string>;

constexpr std::array<std::string_view, 2> kCreditsAnchors {"credits", "set_credits"};

std::optional<AnchorMatch> FindCreditsAnchor(const PluginRequest& request) {
    for (const auto key : kCreditsAnchors) {
        const auto it = request.anchors.find(std::string(key));
        if (it != request.anchors.end() && !it->second.empty()) {
            return AnchorMatch {it->first, it->second};
        }
    }

    return std::nullopt;
}

PluginResult BuildMissingAnchorResult(const PluginRequest& request) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "CAPABILITY_REQUIRED_MISSING";
    result.hookState = "DENIED";
    result.message = "anchors map missing required credits anchor.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"requiredField", "anchors"},
        {"anchorCount", std::to_string(request.anchors.size())}};
    return result;
}

PluginResult BuildInvalidAnchorResult(const PluginRequest& request, const AnchorMatch& anchor) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "SAFETY_MUTATION_BLOCKED";
    result.hookState = "DENIED";
    result.message = "credits anchor value is invalid.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"anchorKey", anchor.first},
        {"anchorValue", anchor.second}};
    return result;
}

PluginResult BuildWriteFailureResult(
    const PluginRequest& request,
    const AnchorMatch& anchor,
    const std::string& error) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "SAFETY_MUTATION_BLOCKED";
    result.hookState = "DENIED";
    result.message = "credits process write failed.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"anchorKey", anchor.first},
        {"anchorValue", anchor.second},
        {"error", error},
        {"processMutationApplied", "false"}};
    return result;
}

PluginResult BuildMutationSuccessResult(
    const PluginRequest& request,
    const AnchorMatch& anchor,
    std::int32_t appliedValue) {
    PluginResult result {};
    result.succeeded = true;
    result.reasonCode = "CAPABILITY_PROBE_PASS";
    result.hookState = request.lockValue ? "HOOK_LOCK" : "HOOK_ONESHOT";
    result.message = "Credits value applied through extender plugin.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"processId", std::to_string(request.processId)},
        {"anchorKey", anchor.first},
        {"anchorValue", anchor.second},
        {"intValue", std::to_string(appliedValue)},
        {"lockValue", request.lockValue ? "true" : "false"},
        {"processMutationApplied", "true"}};
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

const char* EconomyPlugin::id() const noexcept {
    return "economy";
}

PluginResult EconomyPlugin::execute(const PluginRequest& request) {
    if (request.featureId != "set_credits") {
        PluginResult result {};
        result.succeeded = false;
        result.reasonCode = "CAPABILITY_REQUIRED_MISSING";
        result.hookState = "DENIED";
        result.message = "Economy plugin only handles set_credits.";
        result.diagnostics = {{"featureId", request.featureId}};
        return result;
    }

    if (request.intValue < 0) {
        PluginResult result {};
        result.succeeded = false;
        result.reasonCode = "SAFETY_MUTATION_BLOCKED";
        result.hookState = "DENIED";
        result.message = "intValue must be non-negative for set_credits.";
        result.diagnostics = {{"intValue", std::to_string(request.intValue)}};
        return result;
    }

    const auto resolvedAnchor = FindCreditsAnchor(request);
    if (!resolvedAnchor.has_value()) {
        return BuildMissingAnchorResult(request);
    }

    std::uintptr_t targetAddress = 0;
    if (!process_mutation::TryParseAddress(resolvedAnchor->second, targetAddress)) {
        return BuildInvalidAnchorResult(request, *resolvedAnchor);
    }

    std::string writeError;
    if (!process_mutation::TryWriteValue<std::int32_t>(
            request.processId,
            targetAddress,
            request.intValue,
            writeError)) {
        return BuildWriteFailureResult(request, *resolvedAnchor, writeError);
    }

    lockEnabled_.store(request.lockValue);
    lockedCreditsValue_.store(request.intValue);
    return BuildMutationSuccessResult(request, *resolvedAnchor, request.intValue);
}

CapabilitySnapshot EconomyPlugin::capabilitySnapshot() const {
    CapabilitySnapshot snapshot {};
    snapshot.features.emplace("set_credits", BuildCapabilityState());
    return snapshot;
}

} // namespace swfoc::extender::plugins
