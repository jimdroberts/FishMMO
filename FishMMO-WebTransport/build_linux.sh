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
    -DWT_STATIC_MSQUIC=OFF \
    -G "Unix Makefiles"

# Build
cmake --build "${BUILD_DIR}" --config Release -j"$(nproc)"

# Copy output
cp "${BUILD_DIR}"/libfishmmo_webtransport.so "${OUT_DIR}/" 2>/dev/null || true

echo "=== Build complete ==="
echo "Output: ${OUT_DIR}/libfishmmo_webtransport.so"
ls -lh "${OUT_DIR}/libfishmmo_webtransport.so"
