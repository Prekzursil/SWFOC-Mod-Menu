// cppcheck-suppress-file missingIncludeSystem
#pragma once

#include <cstdint>
#include <map>
#include <string>

/*
Cppcheck note (targeted): if cppcheck runs without STL include paths,
suppress only this header:
  --suppress=missingIncludeSystem:native/SwfocExtender.Plugins/include/swfoc_extender/plugins/PluginContracts.hpp
*/

namespace swfoc::extender::plugins {

struct PluginRequest {
    [[maybe_unused]] std::string featureId;
    [[maybe_unused]] std::string profileId;
    [[maybe_unused]] std::int32_t intValue {0};
    [[maybe_unused]] bool lockValue {false};
};

struct CapabilitySnapshot {
    [[maybe_unused]] bool creditsAvailable {false};
    [[maybe_unused]] std::string creditsState {"Unknown"};
    [[maybe_unused]] std::string reasonCode {"CAPABILITY_BACKEND_UNAVAILABLE"};
};

struct PluginResult {
    [[maybe_unused]] bool succeeded {false};
    [[maybe_unused]] std::string reasonCode {"CAPABILITY_UNKNOWN"};
    [[maybe_unused]] std::string hookState {"none"};
    [[maybe_unused]] std::string message {};
    [[maybe_unused]] std::map<std::string, std::string> diagnostics {};
};

class IPlugin {
public:
    virtual ~IPlugin() = default;
    virtual const char* id() const noexcept = 0;
    virtual PluginResult execute(const PluginRequest& request) = 0;
};

} // namespace swfoc::extender::plugins
