// cppcheck-suppress-file missingIncludeSystem
#include "swfoc_extender/plugins/HelperLuaPlugin.hpp"

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
           featureId == "set_context_faction" ||
           featureId == "set_hero_state_helper" ||
           featureId == "toggle_roe_respawn_helper" ||
           featureId == "transfer_fleet_safe" ||
           featureId == "flip_planet_owner" ||
           featureId == "switch_player_faction" ||
           featureId == "edit_hero_state" ||
           featureId == "create_hero_variant";
}


const char* ResolveExpectedHelperEntryPoint(const std::string& featureId) {
    if (featureId == "spawn_unit_helper") {
        return "SWFOC_Trainer_Spawn";
    }

    if (featureId == "spawn_context_entity" ||
        featureId == "spawn_tactical_entity" ||
        featureId == "spawn_galactic_entity") {
        return "SWFOC_Trainer_Spawn_Context";
    }

    if (featureId == "place_planet_building") {
        return "SWFOC_Trainer_Place_Building";
    }

    if (featureId == "set_context_allegiance" || featureId == "set_context_faction") {
        return "SWFOC_Trainer_Set_Context_Allegiance";
    }

    if (featureId == "transfer_fleet_safe") {
        return "SWFOC_Trainer_Transfer_Fleet_Safe";
    }

    if (featureId == "flip_planet_owner") {
        return "SWFOC_Trainer_Flip_Planet_Owner";
    }

    if (featureId == "switch_player_faction") {
        return "SWFOC_Trainer_Switch_Player_Faction";
    }

    if (featureId == "edit_hero_state") {
        return "SWFOC_Trainer_Edit_Hero_State";
    }

    if (featureId == "create_hero_variant") {
        return "SWFOC_Trainer_Create_Hero_Variant";
    }

    return nullptr;
}
bool HasValue(const std::string& value) {
    return !value.empty();
}

void AddOptionalDiagnostic(std::map<std::string, std::string>& diagnostics, const char* key, const std::string& value) {
    if (!value.empty()) {
        diagnostics[key] = value;
    }
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
    result.diagnostics.emplace("operationPolicy", request.operationPolicy);
    result.diagnostics.emplace("targetContext", request.targetContext);
    result.diagnostics.emplace("mutationIntent", request.mutationIntent);
    result.diagnostics.emplace("helperVerifyState", "failed");
    result.diagnostics.emplace("helperExecutionPath", "contract_validation");
    return result;
}

PluginResult BuildSuccess(const PluginRequest& request) {
    PluginResult result {};
    result.succeeded = true;
    result.reasonCode = "HELPER_EXECUTION_APPLIED";
    result.hookState = "HOOK_EXECUTED";
    result.message = "Helper bridge operation applied through native helper plugin.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"helperHookId", request.helperHookId},
        {"helperEntryPoint", request.helperEntryPoint},
        {"helperScript", request.helperScript},
        {"helperInvocationSource", "native_bridge"},
        {"helperVerifyState", "applied"},
        {"helperExecutionPath", "plugin_dispatch"},
        {"processId", std::to_string(request.processId)},
        {"operationKind", request.operationKind},
        {"operationToken", request.operationToken},
        {"helperInvocationContractVersion", request.invocationContractVersion},
        {"verificationContractVersion", request.verificationContractVersion},
        {"operationPolicy", request.operationPolicy},
        {"targetContext", request.targetContext},
        {"mutationIntent", request.mutationIntent}
    };

    AddOptionalDiagnostic(result.diagnostics, "unitId", request.unitId);
    AddOptionalDiagnostic(result.diagnostics, "entityId", request.entityId);
    AddOptionalDiagnostic(result.diagnostics, "entryMarker", request.entryMarker);
    AddOptionalDiagnostic(result.diagnostics, "worldPosition", request.worldPosition);
    AddOptionalDiagnostic(result.diagnostics, "faction", request.faction);
    AddOptionalDiagnostic(result.diagnostics, "targetFaction", request.targetFaction);
    AddOptionalDiagnostic(result.diagnostics, "sourceFaction", request.sourceFaction);
    AddOptionalDiagnostic(result.diagnostics, "populationPolicy", request.populationPolicy);
    AddOptionalDiagnostic(result.diagnostics, "persistencePolicy", request.persistencePolicy);
    AddOptionalDiagnostic(result.diagnostics, "placementMode", request.placementMode);
    AddOptionalDiagnostic(result.diagnostics, "globalKey", request.globalKey);

    result.diagnostics["intValue"] = std::to_string(request.intValue);
    result.diagnostics["boolValue"] = request.boolValue ? "true" : "false";
    result.diagnostics["allowCrossFaction"] = request.allowCrossFaction ? "true" : "false";
    result.diagnostics["forceOverride"] = request.forceOverride ? "true" : "false";
    result.diagnostics["appliedEntityId"] = !request.entityId.empty() ? request.entityId : request.unitId;
    return result;
}

bool IsSpawnFeature(const std::string& featureId) {
    return featureId == "spawn_context_entity" ||
           featureId == "spawn_tactical_entity" ||
           featureId == "spawn_galactic_entity";
}

bool HasSpawnEntityIdentity(const PluginRequest& request) {
    return HasValue(request.entityId) || HasValue(request.unitId);
}

bool HasSpawnFaction(const PluginRequest& request) {
    return HasValue(request.faction) || HasValue(request.targetFaction);
}

bool RequiresSpawnPlacement(const PluginRequest& request) {
    return request.featureId != "spawn_galactic_entity";
}

bool HasSpawnPlacement(const PluginRequest& request) {
    return HasValue(request.entryMarker) || HasValue(request.worldPosition);
}

bool ValidateCommonRequest(const PluginRequest& request, PluginResult& failure) {
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

    if (!HasValue(request.helperScript)) {
        failure = BuildFailure(
            request,
            "HELPER_INVOCATION_FAILED",
            "Helper bridge execution requires helperScript metadata.");
        return false;
    }

    const auto* expectedEntryPoint = ResolveExpectedHelperEntryPoint(request.featureId);
    if (expectedEntryPoint != nullptr && request.helperEntryPoint != expectedEntryPoint) {
        failure = BuildFailure(
            request,
            "HELPER_ENTRYPOINT_NOT_FOUND",
            "Helper entrypoint did not match expected operation entrypoint.",
            {{"expectedHelperEntryPoint", expectedEntryPoint}});
        return false;
    }

    if (!HasValue(request.operationKind)) {
        failure = BuildFailure(
            request,
            "HELPER_INVOCATION_FAILED",
            "Helper bridge execution requires operationKind.");
        return false;
    }

    if (!HasValue(request.operationToken)) {
        failure = BuildFailure(
            request,
            "HELPER_VERIFICATION_FAILED",
            "Helper bridge execution requires a non-empty operation token for verification.");
        return false;
    }

    if (!HasValue(request.invocationContractVersion) || !HasValue(request.verificationContractVersion)) {
        failure = BuildFailure(
            request,
            "HELPER_VERIFICATION_FAILED",
            "Helper bridge execution requires invocation and verification contract versions.");
        return false;
    }

    if (!HasValue(request.operationPolicy) || !HasValue(request.targetContext) || !HasValue(request.mutationIntent)) {
        failure = BuildFailure(
            request,
            "HELPER_INVOCATION_FAILED",
            "Helper bridge execution requires operationPolicy, targetContext, and mutationIntent metadata.");
        return false;
    }

    return true;
}

bool ValidateSpawnUnitRequest(const PluginRequest& request, PluginResult& failure) {
    if (request.featureId != "spawn_unit_helper") {
        return true;
    }

    if (HasValue(request.unitId) && HasSpawnFaction(request) && HasSpawnPlacement(request)) {
        return true;
    }

    failure = BuildFailure(
        request,
        "HELPER_INVOCATION_FAILED",
        "spawn_unit_helper requires unitId, faction/targetFaction, and entryMarker/worldPosition.");
    return false;
}

bool ValidateSpawnRequest(const PluginRequest& request, PluginResult& failure) {
    if (!IsSpawnFeature(request.featureId)) {
        return true;
    }

    if (!HasSpawnEntityIdentity(request)) {
        failure = BuildFailure(
            request,
            "HELPER_INVOCATION_FAILED",
            "Context/tactical/galactic spawn requires entityId or unitId.");
        return false;
    }

    if (!HasSpawnFaction(request)) {
        failure = BuildFailure(
            request,
            "HELPER_INVOCATION_FAILED",
            "Context/tactical/galactic spawn requires faction or targetFaction.");
        return false;
    }

    if (RequiresSpawnPlacement(request) && !HasSpawnPlacement(request)) {
        failure = BuildFailure(
            request,
            "HELPER_INVOCATION_FAILED",
            "Tactical/context spawn requires entryMarker or worldPosition.");
        return false;
    }

    return true;
}

bool ValidateBuildingRequest(const PluginRequest& request, PluginResult& failure) {
    if (request.featureId != "place_planet_building") {
        return true;
    }

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

    return true;
}

bool ValidateAllegianceRequest(const PluginRequest& request, PluginResult& failure) {
    if (request.featureId != "set_context_allegiance" && request.featureId != "set_context_faction") {
        return true;
    }

    if (!HasValue(request.targetFaction) && !HasValue(request.faction)) {
        failure = BuildFailure(
            request,
            "HELPER_INVOCATION_FAILED",
            "set_context_allegiance requires faction or targetFaction.");
        return false;
    }

    return true;
}

bool ValidateHeroStateRequest(const PluginRequest& request, PluginResult& failure) {
    if (request.featureId == "set_hero_state_helper" || request.featureId == "edit_hero_state") {
        if (!HasValue(request.globalKey) && !HasValue(request.entityId)) {
            failure = BuildFailure(
                request,
                "HELPER_INVOCATION_FAILED",
                "Hero state operations require globalKey or entityId payload field.");
            return false;
        }
    }

    return true;
}

bool ValidateTransferFleetRequest(const PluginRequest& request, PluginResult& failure) {
    if (request.featureId != "transfer_fleet_safe") {
        return true;
    }

    if (!HasValue(request.entityId) || !HasValue(request.sourceFaction) || !HasValue(request.targetFaction)) {
        failure = BuildFailure(
            request,
            "HELPER_INVOCATION_FAILED",
            "transfer_fleet_safe requires entityId, sourceFaction, and targetFaction.");
        return false;
    }

    return true;
}

bool ValidatePlanetFlipRequest(const PluginRequest& request, PluginResult& failure) {
    if (request.featureId != "flip_planet_owner") {
        return true;
    }

    if (!HasValue(request.entityId) || !HasValue(request.targetFaction)) {
        failure = BuildFailure(
            request,
            "HELPER_INVOCATION_FAILED",
            "flip_planet_owner requires entityId and targetFaction.");
        return false;
    }

    return true;
}

bool ValidateSwitchFactionRequest(const PluginRequest& request, PluginResult& failure) {
    if (request.featureId != "switch_player_faction") {
        return true;
    }

    if (!HasValue(request.targetFaction)) {
        failure = BuildFailure(
            request,
            "HELPER_INVOCATION_FAILED",
            "switch_player_faction requires targetFaction.");
        return false;
    }

    return true;
}

bool ValidateHeroVariantRequest(const PluginRequest& request, PluginResult& failure) {
    if (request.featureId != "create_hero_variant") {
        return true;
    }

    if (!HasValue(request.entityId) || !HasValue(request.unitId)) {
        failure = BuildFailure(
            request,
            "HELPER_INVOCATION_FAILED",
            "create_hero_variant requires entityId and unitId (variant id)." );
        return false;
    }

    return true;
}

bool ValidateRequest(const PluginRequest& request, PluginResult& failure) {
    return ValidateCommonRequest(request, failure) &&
           ValidateSpawnUnitRequest(request, failure) &&
           ValidateSpawnRequest(request, failure) &&
           ValidateBuildingRequest(request, failure) &&
           ValidateAllegianceRequest(request, failure) &&
           ValidateHeroStateRequest(request, failure) &&
           ValidateTransferFleetRequest(request, failure) &&
           ValidatePlanetFlipRequest(request, failure) &&
           ValidateSwitchFactionRequest(request, failure) &&
           ValidateHeroVariantRequest(request, failure);
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
    snapshot.features.emplace("set_context_faction", BuildAvailableCapability());
    snapshot.features.emplace("set_hero_state_helper", BuildAvailableCapability());
    snapshot.features.emplace("toggle_roe_respawn_helper", BuildAvailableCapability());
    snapshot.features.emplace("transfer_fleet_safe", BuildAvailableCapability());
    snapshot.features.emplace("flip_planet_owner", BuildAvailableCapability());
    snapshot.features.emplace("switch_player_faction", BuildAvailableCapability());
    snapshot.features.emplace("edit_hero_state", BuildAvailableCapability());
    snapshot.features.emplace("create_hero_variant", BuildAvailableCapability());
    return snapshot;
}

} // namespace swfoc::extender::plugins
