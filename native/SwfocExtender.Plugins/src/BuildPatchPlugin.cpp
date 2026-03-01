#include "swfoc_extender/plugins/BuildPatchPlugin.hpp"
#include "swfoc_extender/plugins/ProcessMutationHelpers.hpp"

// cppcheck-suppress missingIncludeSystem
#include <array>
// cppcheck-suppress missingIncludeSystem
#include <algorithm>
// cppcheck-suppress missingIncludeSystem
#include <cstdint>
// cppcheck-suppress missingIncludeSystem
#include <optional>
// cppcheck-suppress missingIncludeSystem
#include <string>
// cppcheck-suppress missingIncludeSystem
#include <string_view>
// cppcheck-suppress missingIncludeSystem
#include <vector>

namespace swfoc::extender::plugins {

namespace {

using AnchorMatch = std::pair<std::string, std::string>;

constexpr std::array<std::string_view, 2> kUnitCapAnchors {"unit_cap", "set_unit_cap"};
constexpr std::array<std::string_view, 4> kInstantBuildAnchors {"instant_build_patch_injection", "instant_build_patch", "instant_build", "toggle_instant_build_patch"};
constexpr std::int32_t kMinUnitCap = 1;
constexpr std::int32_t kMaxUnitCap = 100000;

bool IsBuildPatchFeature(const std::string& featureId) {
    return featureId == "set_unit_cap" || featureId == "toggle_instant_build_patch";
}

std::vector<std::string_view> AnchorCandidates(const std::string& featureId) {
    if (featureId == "set_unit_cap") {
        return {kUnitCapAnchors.begin(), kUnitCapAnchors.end()};
    }

    return {kInstantBuildAnchors.begin(), kInstantBuildAnchors.end()};
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

PluginResult BuildMissingAnchorResult(const PluginRequest& request) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "CAPABILITY_REQUIRED_MISSING";
    result.hookState = "DENIED";
    result.message = "anchors map missing required symbol anchor for build patch operation.";
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

const char* BoolToString(bool value) {
    return value ? "true" : "false";
}

bool IsUnitCapOutOfBounds(const PluginRequest& request, bool enablePatch) {
    return enablePatch && (request.intValue < kMinUnitCap || request.intValue > kMaxUnitCap);
}

PluginResult BuildPatchRestoreStateMissingResult(
    const PluginRequest& request,
    const AnchorMatch& resolvedAnchor,
    const std::string& restoreKey) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "PATCH_RESTORE_STATE_MISSING";
    result.hookState = "DENIED";
    result.message = "Build patch restore was requested without a cached pre-patch snapshot.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"processId", std::to_string(request.processId)},
        {"anchorKey", resolvedAnchor.first},
        {"anchorValue", resolvedAnchor.second},
        {"intValue", std::to_string(request.intValue)},
        {"restoreKey", restoreKey},
        {"processMutationApplied", "false"},
        {"operation", "restore_missing"}};
    return result;
}

PluginResult BuildInvalidAnchorResult(const PluginRequest& request, const AnchorMatch& resolvedAnchor) {
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
    bool enablePatch,
    const std::string& error,
    const process_mutation::WriteOperationDiagnostics& diagnostics) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "SAFETY_MUTATION_BLOCKED";
    result.hookState = "DENIED";
    result.message = "build patch process write failed.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"processId", std::to_string(request.processId)},
        {"anchorKey", resolvedAnchor.first},
        {"anchorValue", resolvedAnchor.second},
        {"enable", BoolToString(enablePatch)},
        {"intValue", std::to_string(request.intValue)},
        {"error", error},
        {"writeMode", diagnostics.writeMode},
        {"oldProtect", diagnostics.oldProtect},
        {"len", diagnostics.len},
        {"restoreProtectOk", diagnostics.restoreProtectOk},
        {"processMutationApplied", "false"}};
    return result;
}

PluginResult BuildMutationSuccessResult(
    const PluginRequest& request,
    const AnchorMatch& resolvedAnchor,
    bool enablePatch,
    std::int32_t appliedValue,
    const std::string& reasonCode,
    const std::string& message,
    const std::string& operation,
    const std::string& restoreKey,
    const process_mutation::WriteOperationDiagnostics& diagnostics) {
    PluginResult result {};
    result.succeeded = true;
    result.reasonCode = reasonCode;
    result.hookState = "HOOK_ONESHOT";
    result.message = message;
    result.diagnostics = {
        {"featureId", request.featureId},
        {"processId", std::to_string(request.processId)},
        {"anchorKey", resolvedAnchor.first},
        {"anchorValue", resolvedAnchor.second},
        {"enable", BoolToString(enablePatch)},
        {"intValue", std::to_string(appliedValue)},
        {"restoreKey", restoreKey},
        {"operation", operation},
        {"writeMode", diagnostics.writeMode},
        {"oldProtect", diagnostics.oldProtect},
        {"len", diagnostics.len},
        {"restoreProtectOk", diagnostics.restoreProtectOk},
        {"processMutationApplied", "true"}};
    return result;
}

PluginResult BuildReadFailureResult(
    const PluginRequest& request,
    const AnchorMatch& resolvedAnchor,
    const std::string& error,
    const std::string& operation) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "SAFETY_MUTATION_BLOCKED";
    result.hookState = "DENIED";
    result.message = "build patch memory read failed.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"processId", std::to_string(request.processId)},
        {"anchorKey", resolvedAnchor.first},
        {"anchorValue", resolvedAnchor.second},
        {"operation", operation},
        {"error", error},
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
    }

    if (!resolvedAnchor.has_value()) {
        return BuildMissingAnchorResult(request);
    }

    std::uintptr_t targetAddress = 0;
    if (!process_mutation::TryParseAddress(resolvedAnchor->second, targetAddress)) {
        return BuildInvalidAnchorResult(request, *resolvedAnchor);
    }

    const auto restoreKey = BuildRestoreKey(request, resolvedAnchor->first, targetAddress);
    if (!enablePatch) {
        std::vector<std::uint8_t> restoreBytes;
        if (!TryReadRestoreBytes(restoreKey, restoreBytes)) {
            return BuildPatchRestoreStateMissingResult(request, *resolvedAnchor, restoreKey);
        }

        std::string writeError;
        process_mutation::WriteOperationDiagnostics writeDiagnostics {};
        if (!process_mutation::TryWriteBytesPatchSafe(
                request.processId,
                targetAddress,
                restoreBytes.data(),
                restoreBytes.size(),
                writeError,
                &writeDiagnostics)) {
            return BuildWriteFailureResult(request, *resolvedAnchor, enablePatch, writeError, writeDiagnostics);
        }

        RemoveRestoreBytes(restoreKey);
        if (request.featureId == "set_unit_cap") {
            ApplyUnitCapState(enablePatch, request.intValue);
        } else {
            ApplyInstantBuildState(enablePatch);
        }

        return BuildMutationSuccessResult(
            request,
            *resolvedAnchor,
            enablePatch,
            request.featureId == "set_unit_cap" ? request.intValue : 0,
            "PATCH_RESTORE_APPLIED",
            "Build patch restore applied through extender plugin.",
            "restore",
            restoreKey,
            writeDiagnostics);
    }

    const auto writeLength = request.featureId == "set_unit_cap"
        ? sizeof(std::int32_t)
        : sizeof(std::uint8_t);
    std::vector<std::uint8_t> originalBytes;
    std::string readError;
    if (!process_mutation::TryReadBytes(
            request.processId,
            targetAddress,
            writeLength,
            originalBytes,
            readError)) {
        return BuildReadFailureResult(request, *resolvedAnchor, readError, "capture_original");
    }

    StoreRestoreBytes(restoreKey, std::move(originalBytes));

    std::string writeError;
    process_mutation::WriteOperationDiagnostics writeDiagnostics {};
    if (request.featureId == "set_unit_cap") {
        const auto clamped = std::clamp(request.intValue, kMinUnitCap, kMaxUnitCap);
        const auto encoded = static_cast<std::int32_t>(clamped);
        if (!process_mutation::TryWriteValue<std::int32_t>(
                request.processId,
                targetAddress,
                encoded,
                writeError,
                process_mutation::WriteMutationMode::Patch,
                &writeDiagnostics)) {
            return BuildWriteFailureResult(request, *resolvedAnchor, enablePatch, writeError, writeDiagnostics);
        }

        ApplyUnitCapState(enablePatch, request.intValue);
        return BuildMutationSuccessResult(
            request,
            *resolvedAnchor,
            enablePatch,
            clamped,
            "CAPABILITY_PROBE_PASS",
            "Build patch value applied through extender plugin.",
            "apply",
            restoreKey,
            writeDiagnostics);
    }

    const auto enabledByte = static_cast<std::uint8_t>(1);
    if (!process_mutation::TryWriteValue<std::uint8_t>(
            request.processId,
            targetAddress,
            enabledByte,
            writeError,
            process_mutation::WriteMutationMode::Patch,
            &writeDiagnostics)) {
        return BuildWriteFailureResult(request, *resolvedAnchor, enablePatch, writeError, writeDiagnostics);
    }

    ApplyInstantBuildState(enablePatch);
    return BuildMutationSuccessResult(
        request,
        *resolvedAnchor,
        enablePatch,
        1,
        "CAPABILITY_PROBE_PASS",
        "Build patch value applied through extender plugin.",
        "apply",
        restoreKey,
        writeDiagnostics);
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

std::string BuildPatchPlugin::BuildRestoreKey(const PluginRequest& request, const std::string& anchorKey, std::uintptr_t address) {
    return std::to_string(request.processId) + "|" + request.featureId + "|" + anchorKey + "|" + std::to_string(address);
}

bool BuildPatchPlugin::TryReadRestoreBytes(const std::string& key, std::vector<std::uint8_t>& bytes) {
    std::scoped_lock lock(restoreBytesMutex_);
    const auto it = restoreBytesByKey_.find(key);
    if (it == restoreBytesByKey_.end()) {
        bytes.clear();
        return false;
    }

    bytes = it->second;
    return true;
}

void BuildPatchPlugin::StoreRestoreBytes(std::string key, std::vector<std::uint8_t> bytes) {
    std::scoped_lock lock(restoreBytesMutex_);
    restoreBytesByKey_[std::move(key)] = std::move(bytes);
}

void BuildPatchPlugin::RemoveRestoreBytes(const std::string& key) {
    std::scoped_lock lock(restoreBytesMutex_);
    restoreBytesByKey_.erase(key);
}

} // namespace swfoc::extender::plugins
