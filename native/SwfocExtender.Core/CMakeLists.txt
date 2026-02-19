add_library(SwfocExtender.Core
    src/CapabilityProbe.cpp
    src/HookLifecycleManager.cpp)

target_include_directories(SwfocExtender.Core
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include)
