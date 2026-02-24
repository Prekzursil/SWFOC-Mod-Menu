// cppcheck-suppress-file missingIncludeSystem
#pragma once

#include "swfoc_extender/plugins/PluginContracts.hpp"

#include <atomic>
#include <cstdint>

namespace swfoc::extender::plugins {

class GlobalTogglePlugin final : public IPlugin {
public:
    GlobalTogglePlugin() = default;

    const char* id() const noexcept override;
    PluginResult execute(const PluginRequest& request) override;

    CapabilitySnapshot capabilitySnapshot() const;

private:
    std::atomic<bool> freezeTimerInstalled_ {false};
    std::atomic<bool> fogRevealInstalled_ {false};
    std::atomic<bool> aiToggleInstalled_ {false};
    std::atomic<bool> freezeTimerEnabled_ {false};
    std::atomic<bool> fogRevealEnabled_ {false};
    std::atomic<bool> aiEnabled_ {true};
};

} // namespace swfoc::extender::plugins
