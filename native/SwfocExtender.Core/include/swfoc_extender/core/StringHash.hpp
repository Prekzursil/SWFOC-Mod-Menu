// cppcheck-suppress-file missingIncludeSystem
#pragma once

#include <functional>
#include <string_view>

namespace swfoc::extender::core {

/// Transparent hash functor for heterogeneous lookup in unordered containers.
struct StringHash {
    using is_transparent = void;
    std::size_t operator()(std::string_view sv) const {
        return std::hash<std::string_view>{}(sv);
    }
};

} // namespace swfoc::extender::core
