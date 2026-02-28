#include "swfoc_extender/plugins/EconomyPlugin.hpp"

namespace swfoc::extender::plugins {

namespace {

PluginResult BuildNotImplementedMutationResult(const PluginRequest& request) {
    PluginResult result {};
    result.succeeded = false;
    result.reasonCode = "SAFETY_FAIL_CLOSED";
    result.hookState = "NOOP";
    result.message = "Mutation rejected: no process write or patch was applied by economy plugin.";
    result.diagnostics = {
        {"featureId", request.featureId},
        {"processMutationApplied", "false"}};
    return result;
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

    lockEnabled_.store(request.lockValue);
    lockedCreditsValue_.store(request.intValue);
    return BuildNotImplementedMutationResult(request);
}

CapabilitySnapshot EconomyPlugin::capabilitySnapshot() const {
    CapabilitySnapshot snapshot {};
    CapabilityState state {};
    state.available = false;
    state.state = "Experimental";
    state.reasonCode = "CAPABILITY_FEATURE_EXPERIMENTAL";
    snapshot.features.emplace("set_credits", state);
    return snapshot;
}

} // namespace swfoc::extender::plugins
