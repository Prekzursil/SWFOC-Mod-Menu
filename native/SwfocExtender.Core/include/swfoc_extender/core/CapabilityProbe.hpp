// cppcheck-suppress-file missingIncludeSystem
// cppcheck-suppress-file unusedStructMember
#pragma once

#include <string>
#include <unordered_map>

/*
Cppcheck note (targeted): if cppcheck runs without STL include paths,
suppress only this header:
  --suppress=missingIncludeSystem:native/SwfocExtender.Core/include/swfoc_extender/core/CapabilityProbe.hpp
*/

namespace swfoc::extender::core {

enum class CapabilityState {
    Unknown = 0,
    Experimental,
    Verified
};

struct CapabilityEntry {
    [[maybe_unused]] bool available {false};
    [[maybe_unused]] CapabilityState state {CapabilityState::Unknown};
    [[maybe_unused]] std::string reasonCode {"CAPABILITY_UNKNOWN"};
};

class CapabilityProbe {
public:
    CapabilityProbe() = default;

    void markAvailable(const std::string& featureId, const std::string& reasonCode = "CAPABILITY_PROBE_PASS");
    bool isAvailable(const std::string& featureId) const;
    const std::unordered_map<std::string, CapabilityEntry>& snapshot() const noexcept;

private:
    [[maybe_unused]] std::unordered_map<std::string, CapabilityEntry> capabilities_;
};

} // namespace swfoc::extender::core
