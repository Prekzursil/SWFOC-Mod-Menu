#include "swfoc_extender/plugins/BuildPatchPlugin.hpp"

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

constexpr std::array<std::string_view, 2> kUnitCapAnchors {"unit_cap", "set_unit_cap"};
constexpr std::array<std::string_view, 2> kInstantBuildAnchors {"instant_build_patch", "toggle_instant_build_patch"};
constexpr std::int32_t kMinUnitCap = 1;
constexpr std::int32_t kMaxUnitCap = 100000;

bool IsBuildPatchFeature(const std::string& featureId) {
    return featureId == "set_unit_cap" || featureId == "toggle_instant_build_patch";
}

const std::array<std::string_view, 2>& AnchorCandidates(const std::string& featureId) {
    if (featureId == "set_unit_cap") {
        return kUnitCapAnchors;
    }

    return kInstantBuildAnchors;
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
    result.message = "Build patch plugin only handles set_unit_cap and toggle_instant_build_patch.";
    result.diagnostics = {{"featureId", request.featureId}};
    return result;
}

PluginResult BuildMissingProcessResult(const PluginRequest& request) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "CAPABILITY_REQUIRED_MISSING";
    result.hookState = "DENIED";
    result.message = "processId is required for build patch mutations.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"requiredField", "processId"},
        {"processId", std::to_string(request.processId)}};
    return result;
}

PluginResult BuildInvalidUnitCapResult(const PluginRequest& request) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "SAFETY_MUTATION_BLOCKED";
    result.hookState = "DENIED";
    result.message = "set_unit_cap requires intValue within safe bounds when enabled.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"intValue", std::to_string(request.intValue)},
        {"minIntValue", std::to_string(kMinUnitCap)},
        {"maxIntValue", std::to_string(kMaxUnitCap)}};
    return result;
}

CapabilityState BuildCapabilityState() {
    CapabilityState state {};
    state.available = false;
    state.state = "Experimental";
    state.reasonCode = "CAPABILITY_FEATURE_EXPERIMENTAL";
    return state;
}

const char* BoolToString(bool value) {
    return value ? "true" : "false";
}

bool IsUnitCapOutOfBounds(const PluginRequest& request, bool enablePatch) {
    return enablePatch && (request.intValue < kMinUnitCap || request.intValue > kMaxUnitCap);
}

PluginResult BuildNotImplementedMutationResult(
    const PluginRequest& request,
    bool enablePatch,
    const std::optional<AnchorMatch>& resolvedAnchor) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "SAFETY_FAIL_CLOSED";
    result.hookState = "NOOP";
    result.message = "Mutation rejected: no process write or patch was applied by build patch plugin.";

    std::string anchorProvided = "false";
    std::string anchorKey = "none";
    std::string anchorValue = "none";
    if (resolvedAnchor.has_value()) {
        anchorProvided = "true";
        anchorKey = resolvedAnchor->first;
        anchorValue = resolvedAnchor->second;
    }

    result.diagnostics = {
        {"featureId", request.featureId},
        {"processId", std::to_string(request.processId)},
        {"anchorProvided", anchorProvided},
        {"anchorKey", anchorKey},
        {"anchorValue", anchorValue},
        {"enable", BoolToString(enablePatch)},
        {"intValue", std::to_string(request.intValue)},
        {"processMutationApplied", "false"}};
    return result;
}

} // namespace

const char* BuildPatchPlugin::id() const noexcept {
    return "build_patch";
}

PluginResult BuildPatchPlugin::execute(const PluginRequest& request) {
    if (!IsBuildPatchFeature(request.featureId)) {
        return BuildUnsupportedFeatureResult(request);
    }

    if (request.processId <= 0) {
        return BuildMissingProcessResult(request);
    }

    const auto resolvedAnchor = FindAnchor(request, request.featureId);

    const bool enablePatch = request.enable || request.boolValue;
    if (request.featureId == "set_unit_cap") {
        if (IsUnitCapOutOfBounds(request, enablePatch)) {
            return BuildInvalidUnitCapResult(request);
        }

        ApplyUnitCapState(enablePatch, request.intValue);
    } else {
        ApplyInstantBuildState(enablePatch);
    }

    return BuildNotImplementedMutationResult(request, enablePatch, resolvedAnchor);
}

CapabilitySnapshot BuildPatchPlugin::capabilitySnapshot() const {
    CapabilitySnapshot snapshot {};
    snapshot.features.emplace("set_unit_cap", BuildCapabilityState());
    snapshot.features.emplace(
        "toggle_instant_build_patch",
        BuildCapabilityState());
    return snapshot;
}

void BuildPatchPlugin::ApplyUnitCapState(bool enablePatch, std::int32_t unitCapValue) {
    unitCapPatchEnabled_.store(enablePatch);
    if (!enablePatch) {
        return;
    }

    unitCapValue_.store(unitCapValue);
}

void BuildPatchPlugin::ApplyInstantBuildState(bool enablePatch) {
    instantBuildPatchEnabled_.store(enablePatch);
}

} // namespace swfoc::extender::plugins
