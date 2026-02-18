#include "swfoc_extender/core/CapabilityProbe.hpp"

namespace swfoc::extender::core {

void CapabilityProbe::markAvailable(const std::string& featureId, const std::string& reasonCode) {
    capabilities_[featureId] = CapabilityEntry{
        .available = true,
        .state = CapabilityState::Verified,
        .reasonCode = reasonCode
    };
}

bool CapabilityProbe::isAvailable(const std::string& featureId) const {
    const auto it = capabilities_.find(featureId);
    return it != capabilities_.end() && it->second.available;
}

const std::unordered_map<std::string, CapabilityEntry>& CapabilityProbe::snapshot() const noexcept {
    return capabilities_;
}

} // namespace swfoc::extender::core
