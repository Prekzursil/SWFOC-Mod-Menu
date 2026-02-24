#include "swfoc_extender/plugins/EconomyPlugin.hpp"

namespace swfoc::extender::plugins {

const char* EconomyPlugin::id() const noexcept {
    return "economy";
}

PluginResult EconomyPlugin::execute(const PluginRequest& request) {
    if (request.featureId != "set_credits") {
        PluginResult result {};
        result.succeeded = false;
        result.reasonCode = "CAPABILITY_REQUIRED_MISSING";
        result.hookState = "none";
        result.message = "Economy plugin only handles set_credits.";
        result.diagnostics = {{"featureId", request.featureId}};
        return result;
    }

    if (request.intValue < 0) {
        PluginResult result {};
        result.succeeded = false;
        result.reasonCode = "SAFETY_MUTATION_BLOCKED";
        result.hookState = "denied";
        result.message = "intValue must be non-negative for set_credits.";
        result.diagnostics = {{"intValue", std::to_string(request.intValue)}};
        return result;
    }

    hookInstalled_.store(true);
    lockEnabled_.store(request.lockValue);
    lockedCreditsValue_.store(request.intValue);

    PluginResult result {};
    result.succeeded = true;
    result.reasonCode = "CAPABILITY_PROBE_PASS";
    result.hookState = request.lockValue ? "HOOK_LOCK" : "HOOK_ONESHOT";
    result.message = request.lockValue
        ? "Credits lock activated via extender economy plugin."
        : "Credits one-shot applied via extender economy plugin.";
    result.diagnostics = {
        {"intValue", std::to_string(request.intValue)},
        {"lockCredits", request.lockValue ? "true" : "false"},
        {"hookInstalled", "true"}
    };
    return result;
}

CapabilitySnapshot EconomyPlugin::capabilitySnapshot() const {
    CapabilitySnapshot snapshot {};
    CapabilityState state {};
    state.available = true;
    state.state = "Verified";
    state.reasonCode = "CAPABILITY_PROBE_PASS";
    snapshot.features.emplace("set_credits", state);
    return snapshot;
}

} // namespace swfoc::extender::plugins
