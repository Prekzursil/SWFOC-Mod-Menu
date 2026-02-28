// cppcheck-suppress-file missingIncludeSystem
#pragma once

#include "swfoc_extender/plugins/PluginContracts.hpp"

#include <atomic>
#include <cstdint>
#include <mutex>
#include <unordered_map>
#include <vector>

namespace swfoc::extender::plugins {

class BuildPatchPlugin final : public IPlugin {
public:
    BuildPatchPlugin() = default;

    const char* id() const noexcept override;
    PluginResult execute(const PluginRequest& request) override;

    CapabilitySnapshot capabilitySnapshot() const;

private:
    void ApplyUnitCapState(bool enablePatch, std::int32_t unitCapValue);
    void ApplyInstantBuildState(bool enablePatch);
    static std::string BuildRestoreKey(const PluginRequest& request, const std::string& anchorKey, std::uintptr_t address);
    bool TryReadRestoreBytes(const std::string& key, std::vector<std::uint8_t>& bytes);
    void StoreRestoreBytes(std::string key, std::vector<std::uint8_t> bytes);
    void RemoveRestoreBytes(const std::string& key);

    std::atomic<bool> unitCapPatchInstalled_ {false};
    std::atomic<bool> instantBuildPatchInstalled_ {false};
    std::atomic<bool> unitCapPatchEnabled_ {false};
    std::atomic<bool> instantBuildPatchEnabled_ {false};
    std::atomic<std::int32_t> unitCapValue_ {0};
    std::mutex restoreBytesMutex_;
    // cppcheck-suppress unusedStructMember
    std::unordered_map<std::string, std::vector<std::uint8_t>> restoreBytesByKey_;
};

} // namespace swfoc::extender::plugins
