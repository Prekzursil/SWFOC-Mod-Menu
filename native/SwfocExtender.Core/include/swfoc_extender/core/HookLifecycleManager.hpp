// cppcheck-suppress-file missingIncludeSystem
// cppcheck-suppress-file unusedStructMember
#pragma once

#include "swfoc_extender/core/StringHash.hpp"

#include <string>
#include <string_view>
#include <unordered_map>

/*
Cppcheck note (targeted): if cppcheck runs without STL include paths,
suppress only this header:
  --suppress=missingIncludeSystem:native/SwfocExtender.Core/include/swfoc_extender/core/HookLifecycleManager.hpp
*/

namespace swfoc::extender::core {

enum class HookState {
    NotInstalled = 0,
    Installed,
    Failed,
    RolledBack
};

struct HookRecord {
    [[maybe_unused]] HookState state {HookState::NotInstalled};
    [[maybe_unused]] std::string reasonCode {"HOOK_NOT_INSTALLED"};
};

class HookLifecycleManager {
public:
    using HookMap = std::unordered_map<std::string, HookRecord, StringHash, std::equal_to<>>;

    HookLifecycleManager() = default;

    void markInstalled(std::string_view hookId);
    void markFailed(std::string_view hookId, std::string_view reasonCode);
    void markRolledBack(std::string_view hookId);
    HookRecord get(std::string_view hookId) const;

private:
    [[maybe_unused]] HookMap hooks_;
};

} // namespace swfoc::extender::core
