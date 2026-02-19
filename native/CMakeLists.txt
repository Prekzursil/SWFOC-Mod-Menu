cmake_minimum_required(VERSION 3.21)
project(SwfocExtender LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_subdirectory(SwfocExtender.Core)
add_subdirectory(SwfocExtender.Bridge)
add_subdirectory(SwfocExtender.Overlay)
add_subdirectory(SwfocExtender.Plugins)
