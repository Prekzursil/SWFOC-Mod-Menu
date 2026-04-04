// cppcheck-suppress-file missingIncludeSystem
#pragma once

#include "swfoc_extender/plugins/PluginContracts.hpp"

#include <atomic>
#include <cstdint>
#include <functional>
#include <mutex>
#include <string>
#include <string_view>
#include <unordered_map>
#include <vector>

namespace swfoc::extender::plugins {

namespace detail {

struct StringHash {
    using is_transparent = void;
    std::size_t operator()(std::string_view sv) const noexcept {
        return std::hash<std::string_view>{}(sv);
    }
};

} // namespace detail

class BuildPatchPlugin final : public IPlugin {
public:
    using RestoreBytesMap = std::unordered_map<std::string, std::vector<std::uint8_t>, detail::StringHash, std::equal_to<>>;

    BuildPatchPlugin() = default;

    const char* id() const noexcept override;
    PluginResult execute(const PluginRequest& request) override;

    CapabilitySnapshot capabilitySnapshot() const;

private:
    void ApplyUnitCapState(bool enablePatch, std::int32_t unitCapValue);
    void ApplyInstantBuildState(bool enablePatch);
    static std::string BuildRestoreKey(const PluginRequest& request, std::string_view anchorKey, std::uintptr_t address);
    bool TryReadRestoreBytes(std::string_view key, std::vector<std::uint8_t>& bytes);
    void StoreRestoreBytes(std::string key, std::vector<std::uint8_t> bytes);
    void RemoveRestoreBytes(std::string_view key);

    std::atomic<bool> unitCapPatchInstalled_ {false};
    std::atomic<bool> instantBuildPatchInstalled_ {false};
    std::atomic<bool> unitCapPatchEnabled_ {false};
    std::atomic<bool> instantBuildPatchEnabled_ {false};
    std::atomic<std::int32_t> unitCapValue_ {0};
    std::mutex restoreBytesMutex_;
    // cppcheck-suppress unusedStructMember
    RestoreBytesMap restoreBytesByKey_;
};

} // namespace swfoc::extender::plugins
