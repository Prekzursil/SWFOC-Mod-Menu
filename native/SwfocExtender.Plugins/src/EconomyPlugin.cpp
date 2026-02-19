#include "swfoc_extender/plugins/EconomyPlugin.hpp"

namespace swfoc::extender::plugins {

const char* EconomyPlugin::id() const noexcept {
    return "economy";
}

PluginResult EconomyPlugin::execute(const PluginRequest& request) {
    if (request.featureId != "set_credits") {
        return PluginResult{
            .succeeded = false,
            .reasonCode = "CAPABILITY_REQUIRED_MISSING",
            .hookState = "none",
            .message = "Economy plugin only handles set_credits.",
            .diagnostics = {
                {"featureId", request.featureId}
            }
        };
    }

    if (request.intValue < 0) {
        return PluginResult{
            .succeeded = false,
            .reasonCode = "SAFETY_MUTATION_BLOCKED",
            .hookState = "denied",
            .message = "intValue must be non-negative for set_credits.",
            .diagnostics = {
                {"intValue", std::to_string(request.intValue)}
            }
        };
    }

    hookInstalled_.store(true);
    lockEnabled_.store(request.lockValue);
    lockedCreditsValue_.store(request.intValue);

    return PluginResult{
        .succeeded = true,
        .reasonCode = "CAPABILITY_PROBE_PASS",
        .hookState = request.lockValue ? "HOOK_LOCK" : "HOOK_ONESHOT",
        .message = request.lockValue
            ? "Credits lock activated via extender economy plugin."
            : "Credits one-shot applied via extender economy plugin.",
        .diagnostics = {
            {"intValue", std::to_string(request.intValue)},
            {"lockCredits", request.lockValue ? "true" : "false"},
            {"hookInstalled", "true"}
        }
    };
}

CapabilitySnapshot EconomyPlugin::capabilitySnapshot() const {
    const auto installed = hookInstalled_.load();
    return CapabilitySnapshot{
        .creditsAvailable = true,
        .creditsState = installed ? "Verified" : "Experimental",
        .reasonCode = installed ? "CAPABILITY_PROBE_PASS" : "CAPABILITY_FEATURE_EXPERIMENTAL"
    };
}

} // namespace swfoc::extender::plugins
