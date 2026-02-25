#pragma once

namespace swfoc::extender::overlay {

class OverlayState {
public:
    void setVisible(bool visible) noexcept {
        visible_ = visible;
    }

    bool visible() const noexcept {
        return visible_;
    }

private:
    bool visible_ {false};
};

} // namespace swfoc::extender::overlay
