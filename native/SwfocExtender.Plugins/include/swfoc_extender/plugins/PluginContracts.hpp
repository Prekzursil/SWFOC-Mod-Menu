#pragma once

#include <cstdint>
#include <map>
#include <string>

namespace swfoc::extender::plugins {

struct PluginRequest {
    std::string featureId;
    std::string profileId;
    std::int32_t intValue {0};
    bool lockValue {false};
};

struct CapabilitySnapshot {
    bool creditsAvailable {false};
    std::string creditsState {"Unknown"};
    std::string reasonCode {"CAPABILITY_BACKEND_UNAVAILABLE"};
};

struct PluginResult {
    bool succeeded {false};
    std::string reasonCode {"CAPABILITY_UNKNOWN"};
    std::string hookState {"none"};
    std::string message {};
    std::map<std::string, std::string> diagnostics {};
};

class IPlugin {
public:
    virtual ~IPlugin() = default;
    virtual const char* id() const noexcept = 0;
    virtual PluginResult execute(const PluginRequest& request) = 0;
};

} // namespace swfoc::extender::plugins
