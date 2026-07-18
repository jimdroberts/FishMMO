#!/usr/bin/env bash
# Build fishmmo_webtransport.so for Linux x86_64.
# Prerequisites: cmake, gcc/clang, OpenSSL dev headers.
#
#   Arch:    sudo pacman -S cmake openssl gcc
#   Ubuntu:  sudo apt-get install cmake libssl-dev build-essential
#   Fedora:  sudo dnf install cmake openssl-devel gcc-c++
set -euo pipefail
cd "$(dirname "$0")"

BUILD_DIR="build"

echo "=== FishMMO WebTransport — Linux x86_64 ==="

cmake -S . -B "$BUILD_DIR" \
    -DCMAKE_BUILD_TYPE=Release \
    -DWT_BUILD_TESTS=OFF \
    -DBUILD_SHARED_LIBS=ON \
    -DWT_STATIC_MSQUIC=ON

cmake --build "$BUILD_DIR" --config Release -j"$(nproc)"

# CMake outputs directly to the Unity plugins directory (see CMakeLists.txt).
UNITY_DIR="../FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/linux_x86_64"
echo ""
echo "=== Done ==="
ls -lh "${UNITY_DIR}/libfishmmo_webtransport.so"