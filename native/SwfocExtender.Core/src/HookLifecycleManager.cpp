#include "swfoc_extender/core/HookLifecycleManager.hpp"

namespace swfoc::extender::core {

void HookLifecycleManager::markInstalled(const std::string& hookId) {
    hooks_[hookId] = HookRecord{.state = HookState::Installed, .reasonCode = "HOOK_OK"};
}

void HookLifecycleManager::markFailed(const std::string& hookId, const std::string& reasonCode) {
    hooks_[hookId] = HookRecord{.state = HookState::Failed, .reasonCode = reasonCode};
}

void HookLifecycleManager::markRolledBack(const std::string& hookId) {
    hooks_[hookId] = HookRecord{.state = HookState::RolledBack, .reasonCode = "ROLLBACK_SUCCESS"};
}

HookRecord HookLifecycleManager::get(const std::string& hookId) const {
    const auto it = hooks_.find(hookId);
    if (it == hooks_.end()) {
        return HookRecord{};
    }

    return it->second;
}

} // namespace swfoc::extender::core
