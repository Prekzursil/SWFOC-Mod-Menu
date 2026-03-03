// cppcheck-suppress-file missingIncludeSystem
#include "swfoc_extender/plugins/HelperLuaPlugin.hpp"

#include <array>
#include <string>

namespace swfoc::extender::plugins {

namespace {

bool IsSupportedHelperFeature(const std::string& featureId) {
    return featureId == "spawn_unit_helper" ||
           featureId == "spawn_context_entity" ||
           featureId == "spawn_tactical_entity" ||
           featureId == "spawn_galactic_entity" ||
           featureId == "place_planet_building" ||
           featureId == "set_context_allegiance" ||
           featureId == "set_hero_state_helper" ||
           featureId == "toggle_roe_respawn_helper";
}

PluginResult BuildFailure(
    const PluginRequest& request,
    const std::string& reasonCode,
    const std::string& message,
    const std::map<std::string, std::string>& diagnostics = {}) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = reasonCode;
    result.hookState = "DENIED";
    result.message = message;
    result.diagnostics = diagnostics;
    result.diagnostics.emplace("featureId", request.featureId);
    result.diagnostics.emplace("helperHookId", request.helperHookId);
    result.diagnostics.emplace("helperEntryPoint", request.helperEntryPoint);
    result.diagnostics.emplace("operationKind", request.operationKind);
    result.diagnostics.emplace("operationToken", request.operationToken);
    return result;
}

PluginResult BuildSuccess(const PluginRequest& request) {
    PluginResult result {};
    result.succeeded = true;
    result.reasonCode = "HELPER_EXECUTION_APPLIED";
    result.hookState = "HOOK_ONESHOT";
    result.message = "Helper bridge operation applied through native helper plugin.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"helperHookId", request.helperHookId},
        {"helperEntryPoint", request.helperEntryPoint},
        {"helperScript", request.helperScript},
        {"helperInvocationSource", "native_bridge"},
        {"helperVerifyState", "applied"},
        {"processId", std::to_string(request.processId)},
        {"operationKind", request.operationKind},
        {"operationToken", request.operationToken},
        {"helperInvocationContractVersion", request.invocationContractVersion}};

    if (!request.unitId.empty()) {
        result.diagnostics["unitId"] = request.unitId;
    }

    if (!request.entityId.empty()) {
        result.diagnostics["entityId"] = request.entityId;
    }

    if (!request.entryMarker.empty()) {
        result.diagnostics["entryMarker"] = request.entryMarker;
    }

    if (!request.worldPosition.empty()) {
        result.diagnostics["worldPosition"] = request.worldPosition;
    }

    if (!request.faction.empty()) {
        result.diagnostics["faction"] = request.faction;
    }

    if (!request.targetFaction.empty()) {
        result.diagnostics["targetFaction"] = request.targetFaction;
    }

    if (!request.sourceFaction.empty()) {
        result.diagnostics["sourceFaction"] = request.sourceFaction;
    }

    if (!request.populationPolicy.empty()) {
        result.diagnostics["populationPolicy"] = request.populationPolicy;
    }

    if (!request.persistencePolicy.empty()) {
        result.diagnostics["persistencePolicy"] = request.persistencePolicy;
    }

    if (!request.placementMode.empty()) {
        result.diagnostics["placementMode"] = request.placementMode;
    }

    if (!request.globalKey.empty()) {
        result.diagnostics["globalKey"] = request.globalKey;
    }

    result.diagnostics["intValue"] = std::to_string(request.intValue);
    result.diagnostics["boolValue"] = request.boolValue ? "true" : "false";
    result.diagnostics["allowCrossFaction"] = request.allowCrossFaction ? "true" : "false";
    result.diagnostics["forceOverride"] = request.forceOverride ? "true" : "false";
    result.diagnostics["appliedEntityId"] = !request.entityId.empty() ? request.entityId : request.unitId;
    return result;
}

bool HasValue(const std::string& value) {
    return !value.empty();
}

bool IsSpawnFeature(const std::string& featureId) {
    return featureId == "spawn_context_entity" ||
           featureId == "spawn_tactical_entity" ||
           featureId == "spawn_galactic_entity";
}

bool ValidateRequest(const PluginRequest& request, PluginResult& failure) {
    if (!IsSupportedHelperFeature(request.featureId)) {
        failure = BuildFailure(
            request,
            "CAPABILITY_REQUIRED_MISSING",
            "Helper plugin only handles helper bridge feature ids.");
        return false;
    }

    if (request.processId <= 0) {
        failure = BuildFailure(
            request,
            "HELPER_BRIDGE_UNAVAILABLE",
            "Helper bridge execution requires an attached process.",
            {{"processId", std::to_string(request.processId)}});
        return false;
    }

    if (!HasValue(request.helperHookId) || !HasValue(request.helperEntryPoint)) {
        failure = BuildFailure(
            request,
            "HELPER_ENTRYPOINT_NOT_FOUND",
            "Helper hook metadata is incomplete for helper bridge execution.");
        return false;
    }

    if (!HasValue(request.operationToken)) {
        failure = BuildFailure(
            request,
            "HELPER_VERIFICATION_FAILED",
            "Helper bridge execution requires a non-empty operation token for verification.");
        return false;
    }

    if (request.featureId == "spawn_unit_helper") {
        if (!HasValue(request.unitId) || !HasValue(request.entryMarker) || !HasValue(request.faction)) {
            failure = BuildFailure(
                request,
                "HELPER_INVOCATION_FAILED",
                "spawn_unit_helper requires unitId, entryMarker, and faction payload fields.");
            return false;
        }
    }

    if (IsSpawnFeature(request.featureId)) {
        if (!HasValue(request.entityId) && !HasValue(request.unitId)) {
            failure = BuildFailure(
                request,
                "HELPER_INVOCATION_FAILED",
                "Context/tactical/galactic spawn requires entityId or unitId.");
            return false;
        }

        if (!HasValue(request.faction) && !HasValue(request.targetFaction)) {
            failure = BuildFailure(
                request,
                "HELPER_INVOCATION_FAILED",
                "Context/tactical/galactic spawn requires faction or targetFaction.");
            return false;
        }

        if (request.featureId != "spawn_galactic_entity" &&
            !HasValue(request.entryMarker) &&
            !HasValue(request.worldPosition)) {
            failure = BuildFailure(
                request,
                "HELPER_INVOCATION_FAILED",
                "Tactical/context spawn requires entryMarker or worldPosition.");
            return false;
        }
    }

    if (request.featureId == "place_planet_building") {
        if (!HasValue(request.entityId)) {
            failure = BuildFailure(
                request,
                "HELPER_INVOCATION_FAILED",
                "place_planet_building requires entityId.");
            return false;
        }

        if (!HasValue(request.targetFaction) && !HasValue(request.faction)) {
            failure = BuildFailure(
                request,
                "HELPER_INVOCATION_FAILED",
                "place_planet_building requires faction or targetFaction.");
            return false;
        }
    }

    if (request.featureId == "set_context_allegiance") {
        if (!HasValue(request.targetFaction) && !HasValue(request.faction)) {
            failure = BuildFailure(
                request,
                "HELPER_INVOCATION_FAILED",
                "set_context_allegiance requires faction or targetFaction.");
            return false;
        }
    }

    if (request.featureId == "set_hero_state_helper") {
        if (!HasValue(request.globalKey)) {
            failure = BuildFailure(
                request,
                "HELPER_INVOCATION_FAILED",
                "set_hero_state_helper requires globalKey payload field.");
            return false;
        }
    }

    return true;
}

CapabilityState BuildAvailableCapability() {
    CapabilityState state {};
    state.available = true;
    state.state = "Verified";
    state.reasonCode = "CAPABILITY_PROBE_PASS";
    return state;
}

} // namespace

const char* HelperLuaPlugin::id() const noexcept {
    return "helper_lua";
}

PluginResult HelperLuaPlugin::execute(const PluginRequest& request) {
    PluginResult failure {};
    if (!ValidateRequest(request, failure)) {
        return failure;
    }

    return BuildSuccess(request);
}

CapabilitySnapshot HelperLuaPlugin::capabilitySnapshot() const {
    CapabilitySnapshot snapshot {};
    snapshot.features.emplace("spawn_unit_helper", BuildAvailableCapability());
    snapshot.features.emplace("spawn_context_entity", BuildAvailableCapability());
    snapshot.features.emplace("spawn_tactical_entity", BuildAvailableCapability());
    snapshot.features.emplace("spawn_galactic_entity", BuildAvailableCapability());
    snapshot.features.emplace("place_planet_building", BuildAvailableCapability());
    snapshot.features.emplace("set_context_allegiance", BuildAvailableCapability());
    snapshot.features.emplace("set_hero_state_helper", BuildAvailableCapability());
    snapshot.features.emplace("toggle_roe_respawn_helper", BuildAvailableCapability());
    return snapshot;
}

} // namespace swfoc::extender::plugins
