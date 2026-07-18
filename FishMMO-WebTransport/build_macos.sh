#!/usr/bin/env bash
# Build libfishmmo_webtransport.dylib for macOS x86_64.
# Must be run on a Mac.  Cross-compilation from other platforms is not
# supported because msquic's quictls dependency contains platform-specific
# assembly that cannot be cross-compiled.
#
#   brew install cmake openssl@3
set -euo pipefail
cd "$(dirname "$0")"

BUILD_DIR="build"

echo "=== FishMMO WebTransport — macOS x86_64 ==="

# Homebrew OpenSSL (not pkg-config registered by default).
if [ -d "/opt/homebrew/opt/openssl@3" ]; then
    export PKG_CONFIG_PATH="/opt/homebrew/opt/openssl@3/lib/pkgconfig"
elif [ -d "/usr/local/opt/openssl@3" ]; then
    export PKG_CONFIG_PATH="/usr/local/opt/openssl@3/lib/pkgconfig"
fi

cmake -S . -B "$BUILD_DIR" \
    -DCMAKE_BUILD_TYPE=Release \
    -DWT_BUILD_TESTS=OFF \
    -DBUILD_SHARED_LIBS=ON \
    -DWT_STATIC_MSQUIC=ON

cmake --build "$BUILD_DIR" --config Release -j"$(sysctl -n hw.ncpu 2>/dev/null || echo 4)"

UNITY_DIR="../FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/mac_x86_64"
echo ""
echo "=== Done ==="
ls -lh "${UNITY_DIR}/libfishmmo_webtransport.dylib"