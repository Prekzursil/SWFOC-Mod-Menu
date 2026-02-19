#pragma once

#include <string>
#include <unordered_map>

namespace swfoc::extender::core {

enum class CapabilityState {
    Unknown = 0,
    Experimental,
    Verified
};

struct CapabilityEntry {
    bool available {false};
    CapabilityState state {CapabilityState::Unknown};
    std::string reasonCode {"CAPABILITY_UNKNOWN"};
};

class CapabilityProbe {
public:
    CapabilityProbe() = default;

    void markAvailable(const std::string& featureId, const std::string& reasonCode = "CAPABILITY_PROBE_PASS");
    bool isAvailable(const std::string& featureId) const;
    const std::unordered_map<std::string, CapabilityEntry>& snapshot() const noexcept;

private:
    std::unordered_map<std::string, CapabilityEntry> capabilities_;
};

} // namespace swfoc::extender::core
