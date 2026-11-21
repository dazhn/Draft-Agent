cmake_minimum_required(VERSION 3.20)

project(PsdLayerLoaderNative LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

set(PSD_SDK_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/third_party/psd_sdk")
set(PSD_SDK_SRC "${PSD_SDK_ROOT}/src/Psd")

file(GLOB PSD_SDK_SOURCES CONFIGURE_DEPENDS
    "${PSD_SDK_SRC}/*.cpp"
    "${PSD_SDK_SRC}/*.c"
)

list(FILTER PSD_SDK_SOURCES EXCLUDE REGEX ".*PsdNativeFile_Linux\\.cpp$")
list(FILTER PSD_SDK_SOURCES EXCLUDE REGEX ".*PsdNativeFile_Mac\\.mm$")

add_library(PsdLayerLoader.Native SHARED
    src/psd_layer_loader_bridge.cpp
    ${PSD_SDK_SOURCES}
)

target_include_directories(PsdLayerLoader.Native PRIVATE
    "${PSD_SDK_ROOT}/src"
    "${PSD_SDK_SRC}"
)

target_compile_definitions(PsdLayerLoader.Native PRIVATE
    _CRT_SECURE_NO_WARNINGS
    PSD_BUILD_DLL
)

if (MSVC)
    target_compile_options(PsdLayerLoader.Native PRIVATE /W3 /permissive-)
endif()
