// cppcheck-suppress-file missingIncludeSystem
#pragma once

#include "swfoc_extender/plugins/PluginContracts.hpp"

#include <atomic>
#include <cstdint>

namespace swfoc::extender::plugins {

class BuildPatchPlugin final : public IPlugin {
public:
    BuildPatchPlugin() = default;

    const char* id() const noexcept override;
    PluginResult execute(const PluginRequest& request) override;

    CapabilitySnapshot capabilitySnapshot() const;

private:
    std::atomic<bool> unitCapPatchInstalled_ {false};
    std::atomic<bool> instantBuildPatchInstalled_ {false};
    std::atomic<bool> unitCapPatchEnabled_ {false};
    std::atomic<bool> instantBuildPatchEnabled_ {false};
    std::atomic<std::int32_t> unitCapValue_ {0};
};

} // namespace swfoc::extender::plugins
