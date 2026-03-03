// cppcheck-suppress-file missingIncludeSystem
#pragma once

#include "swfoc_extender/plugins/PluginContracts.hpp"

namespace swfoc::extender::plugins {

class HelperLuaPlugin final : public IPlugin {
public:
    HelperLuaPlugin() = default;

    const char* id() const noexcept override;
    PluginResult execute(const PluginRequest& request) override;

    CapabilitySnapshot capabilitySnapshot() const;
};

} // namespace swfoc::extender::plugins
