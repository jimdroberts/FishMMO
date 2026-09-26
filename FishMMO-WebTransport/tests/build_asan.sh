#!/usr/bin/env bash
# AddressSanitizer build of the library and every e2e test, into build_asan/.
#
# Deliberately NOT a CMake configure: the library's output directory is the
# Unity plugin folder (see CMakeLists.txt), so an instrumented CMake build
# would overwrite the plugin Unity loads.  This compiles the sources straight
# into build_asan/ and links the static msquic archive from an existing
# `build/` (configure that first — see tests/CMakeLists.txt).  msquic itself
# is not instrumented; every allocation and free in this library is.
#
#   bash tests/build_asan.sh
#   bash tests/run_race_e2e.sh build_asan     # and run_stats/limits/startup likewise
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
B="$ROOT/build"
OUT="${1:-$ROOT/build_asan}"
MSQUIC_A="$B/_deps/msquic-build/bin/Release/libmsquic.a"
[ -f "$MSQUIC_A" ] || { echo "no $MSQUIC_A — configure and build build/ first"; exit 2; }
mkdir -p "$OUT"
CXXFLAGS="-O1 -g -fno-omit-frame-pointer -fsanitize=address -std=gnu++17 -fPIC"
DEFS="-DQUIC_BUILD_STATIC -DWT_BUILDING_DLL -DWT_PLATFORM_LINUX"
INCS="-I$ROOT/src -I$B/_deps/msquic-src/src/inc"
pids=()
for f in webtransport_api server client session datagram_queue stream_manager http3; do
    c++ $CXXFLAGS $DEFS $INCS -c "$ROOT/src/$f.cpp" -o "$OUT/$f.o" & pids+=($!)
done
for p in "${pids[@]}"; do wait "$p"; done
NUMA="$(ldconfig -p 2>/dev/null | sed -nE 's/.*libnuma\.so\.1 .*=> (.*)$/\1/p' | head -1)"
c++ -shared -fsanitize=address -Wl,-soname,libfishmmo_webtransport.so \
    -o "$OUT/libfishmmo_webtransport.so" \
    "$OUT"/webtransport_api.o "$OUT"/server.o "$OUT"/client.o "$OUT"/session.o \
    "$OUT"/datagram_queue.o "$OUT"/stream_manager.o "$OUT"/http3.o \
    ${NUMA:+"$NUMA"} "$MSQUIC_A"
for t in "$ROOT"/tests/*_e2e.cpp; do
    n="$(basename "$t" .cpp)"
    c++ -O1 -g -fno-omit-frame-pointer -fsanitize=address -std=gnu++17 -I"$ROOT/src" "$t" \
        -o "$OUT/wt_$n" -L"$OUT" -lfishmmo_webtransport -Wl,-rpath,"$OUT" -lpthread
done
echo "ASan build in $OUT"
