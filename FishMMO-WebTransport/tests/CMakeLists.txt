# Built only with -DWT_BUILD_TESTS=ON (see the root CMakeLists.txt).
#
#   cmake -B build -DWT_STATIC_MSQUIC=ON -DWT_BUILD_TESTS=ON && cmake --build build
#   bash tests/run_startup_e2e.sh build     # ABI / TLS provider / cert-path contract, no certs needed
#   bash tests/run_limits_e2e.sh build      # transport-level limits on loopback, generates its own CA
#   bash tests/check_abi_version.sh         # native WT_ABI_VERSION == C# ExpectedAbiVersion
add_executable(wt_limits_e2e limits_e2e.cpp)
target_include_directories(wt_limits_e2e PRIVATE ${CMAKE_SOURCE_DIR}/src)
target_link_libraries(wt_limits_e2e PRIVATE fishmmo_webtransport)
set_target_properties(wt_limits_e2e PROPERTIES CXX_STANDARD 17 CXX_STANDARD_REQUIRED ON)

add_executable(wt_startup_e2e startup_e2e.cpp)
target_include_directories(wt_startup_e2e PRIVATE ${CMAKE_SOURCE_DIR}/src)
target_link_libraries(wt_startup_e2e PRIVATE fishmmo_webtransport)
set_target_properties(wt_startup_e2e PROPERTIES CXX_STANDARD 17 CXX_STANDARD_REQUIRED ON)
