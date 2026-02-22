#pragma once
// cppcheck-suppress-file missingIncludeSystem

#include <string>
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
    HookState state {HookState::NotInstalled};
    std::string reasonCode {"HOOK_NOT_INSTALLED"};
};

class HookLifecycleManager {
public:
    HookLifecycleManager() = default;

    void markInstalled(const std::string& hookId);
    void markFailed(const std::string& hookId, const std::string& reasonCode);
    void markRolledBack(const std::string& hookId);
    HookRecord get(const std::string& hookId) const;

private:
    std::unordered_map<std::string, HookRecord> hooks_;
};

} // namespace swfoc::extender::core
