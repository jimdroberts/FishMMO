#!/usr/bin/env bash
set -euo pipefail

cd "$(dirname "$0")"

BUILD_DIR="build"
OUT_DIR="unity/linux_x86_64"

echo "=== FishMMO WebTransport — Linux Build ==="

# Install system dependencies if needed
if ! pkg-config --exists openssl 2>/dev/null; then
    echo "OpenSSL dev libraries required. Install with:"
    echo "  sudo apt-get install libssl-dev        # Debian/Ubuntu"
    echo "  sudo pacman -S openssl                  # Arch"
    exit 1
fi

# Create build directory
mkdir -p "${BUILD_DIR}" "${OUT_DIR}"

# Configure
cmake -S . -B "${BUILD_DIR}" \
    -DCMAKE_BUILD_TYPE=Release \
    -DWT_BUILD_TESTS=OFF \
    -DBUILD_SHARED_LIBS=ON \
    -DWT_STATIC_MSQUIC=ON \
    -G "Unix Makefiles"

# Build
cmake --build "${BUILD_DIR}" --config Release -j"$(nproc)"

echo "=== Build complete ==="
echo "Output: ${OUT_DIR}/libfishmmo_webtransport.so"
ls -lh "${OUT_DIR}/libfishmmo_webtransport.so"

# Copy to Unity project plugin directory
UNITY_PLUGIN_DIR="../FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/linux_x86_64"
if [ -d "${UNITY_PLUGIN_DIR}" ]; then
    cp "${OUT_DIR}/libfishmmo_webtransport.so" "${UNITY_PLUGIN_DIR}/"
    echo "Copied to Unity project: ${UNITY_PLUGIN_DIR}/libfishmmo_webtransport.so"
else
    echo "Warning: Unity plugin directory not found at ${UNITY_PLUGIN_DIR}"
fi
