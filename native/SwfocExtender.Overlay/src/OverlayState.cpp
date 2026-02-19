#include "swfoc_extender/overlay/OverlayState.hpp"

namespace swfoc::extender::overlay {

void OverlayState::setVisible(bool visible) noexcept {
    visible_ = visible;
}

bool OverlayState::visible() const noexcept {
    return visible_;
}

} // namespace swfoc::extender::overlay
