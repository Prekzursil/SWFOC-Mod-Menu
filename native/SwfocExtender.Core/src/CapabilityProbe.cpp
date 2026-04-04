#include "swfoc_extender/core/CapabilityProbe.hpp"

namespace swfoc::extender::core {

void CapabilityProbe::markAvailable(std::string_view featureId, std::string_view reasonCode) {
    CapabilityEntry entry {};
    entry.available = true;
    entry.state = CapabilityState::Verified;
    entry.reasonCode = reasonCode;
    capabilities_[std::string{featureId}] = entry;
}

bool CapabilityProbe::isAvailable(std::string_view featureId) const {
    const auto it = capabilities_.find(featureId);
    return it != capabilities_.end() && it->second.available;
}

const std::unordered_map<std::string, CapabilityEntry, StringHash, std::equal_to<>>& CapabilityProbe::snapshot() const noexcept {
    return capabilities_;
}

} // namespace swfoc::extender::core
