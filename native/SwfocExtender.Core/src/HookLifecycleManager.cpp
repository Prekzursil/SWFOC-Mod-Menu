#include "swfoc_extender/core/HookLifecycleManager.hpp"

namespace swfoc::extender::core {

void HookLifecycleManager::markInstalled(std::string_view hookId) {
    HookRecord record {};
    record.state = HookState::Installed;
    record.reasonCode = "HOOK_OK";
    hooks_[std::string{hookId}] = record;
}

void HookLifecycleManager::markFailed(std::string_view hookId, std::string_view reasonCode) {
    HookRecord record {};
    record.state = HookState::Failed;
    record.reasonCode = reasonCode;
    hooks_[std::string{hookId}] = record;
}

void HookLifecycleManager::markRolledBack(std::string_view hookId) {
    HookRecord record {};
    record.state = HookState::RolledBack;
    record.reasonCode = "ROLLBACK_SUCCESS";
    hooks_[std::string{hookId}] = record;
}

HookRecord HookLifecycleManager::get(std::string_view hookId) const {
    const auto it = hooks_.find(hookId);
    if (it == hooks_.end()) {
        return HookRecord{};
    }

    return it->second;
}

} // namespace swfoc::extender::core
