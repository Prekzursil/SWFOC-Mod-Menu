#include "swfoc_extender/core/HookLifecycleManager.hpp"

namespace swfoc::extender::core {

void HookLifecycleManager::markInstalled(const std::string& hookId) {
    HookRecord record {};
    record.state = HookState::Installed;
    record.reasonCode = "HOOK_OK";
    hooks_[hookId] = record;
}

void HookLifecycleManager::markFailed(const std::string& hookId, const std::string& reasonCode) {
    HookRecord record {};
    record.state = HookState::Failed;
    record.reasonCode = reasonCode;
    hooks_[hookId] = record;
}

void HookLifecycleManager::markRolledBack(const std::string& hookId) {
    HookRecord record {};
    record.state = HookState::RolledBack;
    record.reasonCode = "ROLLBACK_SUCCESS";
    hooks_[hookId] = record;
}

HookRecord HookLifecycleManager::get(const std::string& hookId) const {
    const auto it = hooks_.find(hookId);
    if (it == hooks_.end()) {
        return HookRecord{};
    }

    return it->second;
}

} // namespace swfoc::extender::core
