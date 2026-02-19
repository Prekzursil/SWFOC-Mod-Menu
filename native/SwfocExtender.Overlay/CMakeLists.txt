add_library(SwfocExtender.Overlay
    src/OverlayState.cpp)

target_include_directories(SwfocExtender.Overlay
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include)

target_link_libraries(SwfocExtender.Overlay
    PUBLIC
        SwfocExtender.Core)
