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

    void markAvailable(std::string_view featureId, std::string_view reasonCode = "CAPABILITY_PROBE_PASS");
    bool isAvailable(std::string_view featureId) const;
    const std::unordered_map<std::string, CapabilityEntry, StringHash, std::equal_to<>>& snapshot() const noexcept;

private:
    [[maybe_unused]] std::unordered_map<std::string, CapabilityEntry, StringHash, std::equal_to<>> capabilities_;
};

} // namespace swfoc::extender::core
