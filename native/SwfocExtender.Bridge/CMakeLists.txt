add_library(SwfocExtender.Bridge
    src/NamedPipeBridgeServer.cpp)

target_include_directories(SwfocExtender.Bridge
    PUBLIC
        ${CMAKE_CURRENT_SOURCE_DIR}/include)

target_link_libraries(SwfocExtender.Bridge
    PUBLIC
        SwfocExtender.Core)

add_executable(SwfocExtender.Host
    src/BridgeHostMain.cpp)

target_include_directories(SwfocExtender.Host
    PRIVATE
        ${CMAKE_CURRENT_SOURCE_DIR}/include
        ${CMAKE_SOURCE_DIR}/SwfocExtender.Plugins/include)

target_link_libraries(SwfocExtender.Host
    PRIVATE
        SwfocExtender.Bridge
        SwfocExtender.Plugins)
