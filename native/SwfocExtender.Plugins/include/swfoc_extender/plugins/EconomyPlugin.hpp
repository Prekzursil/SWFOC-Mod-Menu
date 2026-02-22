#pragma once
// cppcheck-suppress-file missingIncludeSystem

#include "swfoc_extender/plugins/PluginContracts.hpp"

#include <atomic>
#include <cstdint>

/*
Cppcheck note (targeted): if cppcheck runs without STL include paths,
suppress only this header:
  --suppress=missingIncludeSystem:native/SwfocExtender.Plugins/include/swfoc_extender/plugins/EconomyPlugin.hpp
*/

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
