cmake_minimum_required(VERSION 3.20)
project(fishmmo_webtransport VERSION 1.0.0 LANGUAGES C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

# ── Options ───────────────────────────────────────────────────
option(BUILD_SHARED_LIBS "Build as shared library" ON)
option(WT_STATIC_MSQUIC "Statically link msquic" ON)
option(WT_BUILD_TESTS "Build test programs" OFF)

# ── Find or fetch msquic ──────────────────────────────────────
include(FetchContent)

if(WT_STATIC_MSQUIC)
    set(QUIC_BUILD_SHARED OFF CACHE BOOL "" FORCE)
    set(QUIC_BUILD_TOOLS OFF CACHE BOOL "" FORCE)
    set(QUIC_BUILD_TEST OFF CACHE BOOL "" FORCE)
    set(QUIC_BUILD_PERF OFF CACHE BOOL "" FORCE)
    set(QUIC_TLS_LIB "quictls" CACHE STRING "" FORCE)

    FetchContent_Declare(
        msquic
        GIT_REPOSITORY https://github.com/microsoft/msquic.git
        GIT_TAG v2.5.9
        GIT_SHALLOW TRUE
    )

    # Try to fetch, but allow system-installed msquic as fallback
    find_package(msquic QUIET)
    if(NOT msquic_FOUND)
        FetchContent_MakeAvailable(msquic)
        set(MSQUIC_TARGET msquic)
        set(MSQUIC_INCLUDE_DIR "${msquic_SOURCE_DIR}/src/inc")
    else()
        set(MSQUIC_TARGET msquic::msquic)
        get_target_property(MSQUIC_INCLUDE_DIR msquic::msquic INTERFACE_INCLUDE_DIRECTORIES)
    endif()
else()
    find_package(msquic REQUIRED)
    set(MSQUIC_TARGET msquic::msquic)
    get_target_property(MSQUIC_INCLUDE_DIR msquic::msquic INTERFACE_INCLUDE_DIRECTORIES)
endif()

# ── Find OpenSSL (required by msquic for TLS) ─────────────────
find_package(OpenSSL REQUIRED)

# ── Source files ──────────────────────────────────────────────
set(WT_SOURCES
    src/webtransport_api.cpp
    src/server.cpp
    src/client.cpp
    src/session.cpp
    src/datagram_queue.cpp
    src/stream_manager.cpp
    src/http3.cpp
)

set(WT_HEADERS
    src/webtransport_api.h
    src/webtransport_internal.h
    src/server.h
    src/client.h
    src/session.h
    src/datagram_queue.h
    src/stream_manager.h
    src/http3.h
)

# ── Build library ─────────────────────────────────────────────
add_library(${PROJECT_NAME} SHARED ${WT_SOURCES} ${WT_HEADERS})

target_include_directories(${PROJECT_NAME} PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}/src
    ${MSQUIC_INCLUDE_DIR}
)

target_link_libraries(${PROJECT_NAME} PRIVATE
    ${MSQUIC_TARGET}
    OpenSSL::SSL
    OpenSSL::Crypto
)

# ── Platform-specific settings ────────────────────────────────
# Output directly to the Unity project's plugin directory so no
# manual copy step is needed after the build.
set(UNITY_PLUGIN_DIR "${CMAKE_SOURCE_DIR}/../FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins")

if(WIN32)
    target_compile_definitions(${PROJECT_NAME} PRIVATE
        WT_PLATFORM_WINDOWS
        _CRT_SECURE_NO_WARNINGS
    )
    set_target_properties(${PROJECT_NAME} PROPERTIES
        RUNTIME_OUTPUT_DIRECTORY "${UNITY_PLUGIN_DIR}/windows_x86_64"
        PREFIX ""
    )
elseif(APPLE)
    target_compile_definitions(${PROJECT_NAME} PRIVATE WT_PLATFORM_MACOS)
    set_target_properties(${PROJECT_NAME} PROPERTIES
        LIBRARY_OUTPUT_DIRECTORY "${UNITY_PLUGIN_DIR}/mac_x86_64"
        PREFIX "lib"
        SUFFIX ".dylib"
    )
else()
    target_compile_definitions(${PROJECT_NAME} PRIVATE WT_PLATFORM_LINUX)
    set_target_properties(${PROJECT_NAME} PROPERTIES
        LIBRARY_OUTPUT_DIRECTORY "${UNITY_PLUGIN_DIR}/linux_x86_64"
        PREFIX "lib"
        SUFFIX ".so"
    )
endif()

# ── Export symbols ────────────────────────────────────────────
target_compile_definitions(${PROJECT_NAME} PRIVATE WT_BUILDING_DLL)

# On Windows, mark exports with __declspec(dllexport)
if(WIN32)
    set_target_properties(${PROJECT_NAME} PROPERTIES
        WINDOWS_EXPORT_ALL_SYMBOLS ON
    )
endif()

# ── Install rules ─────────────────────────────────────────────
install(TARGETS ${PROJECT_NAME}
    RUNTIME DESTINATION bin
    LIBRARY DESTINATION lib
    ARCHIVE DESTINATION lib
)
install(FILES src/webtransport_api.h DESTINATION include)

# ── Tests (optional) ──────────────────────────────────────────
if(WT_BUILD_TESTS AND EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/tests/CMakeLists.txt")
    enable_testing()
    add_subdirectory(tests)
endif()
