add_library(SwfocExtender.Plugins
    src/BuildPatchPlugin.cpp
    src/EconomyPlugin.cpp
    src/GlobalTogglePlugin.cpp
    src/HelperLuaPlugin.cpp)

target_include_directories(SwfocExtender.Plugins
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include)

target_link_libraries(SwfocExtender.Plugins
    PUBLIC
        SwfocExtender.Core)
