#pragma once

namespace swfoc::extender::overlay {

class OverlayState {
public:
    void setVisible(bool visible) noexcept;
    bool visible() const noexcept;

private:
    bool visible_ {false};
};

} // namespace swfoc::extender::overlay
