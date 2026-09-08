# Built only with -DWT_BUILD_TESTS=ON (see the root CMakeLists.txt).
#
#   cmake -B build -DWT_STATIC_MSQUIC=ON -DWT_BUILD_TESTS=ON && cmake --build build
#   bash tests/run_limits_e2e.sh build
add_executable(wt_limits_e2e limits_e2e.cpp)
target_include_directories(wt_limits_e2e PRIVATE ${CMAKE_SOURCE_DIR}/src)
target_link_libraries(wt_limits_e2e PRIVATE fishmmo_webtransport)
set_target_properties(wt_limits_e2e PROPERTIES CXX_STANDARD 17 CXX_STANDARD_REQUIRED ON)
