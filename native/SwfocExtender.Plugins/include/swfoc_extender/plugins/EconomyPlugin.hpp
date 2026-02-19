#pragma once

#include "swfoc_extender/plugins/PluginContracts.hpp"

#include <atomic>
#include <cstdint>
#include <string>

namespace swfoc::extender::plugins {

class EconomyPlugin final : public IPlugin {
public:
    EconomyPlugin() = default;

    const char* id() const noexcept override;
    PluginResult execute(const PluginRequest& request) override;

    CapabilitySnapshot capabilitySnapshot() const;

private:
    std::atomic<bool> hookInstalled_ {false};
    std::atomic<bool> lockEnabled_ {false};
    std::atomic<std::int32_t> lockedCreditsValue_ {0};
};

} // namespace swfoc::extender::plugins
