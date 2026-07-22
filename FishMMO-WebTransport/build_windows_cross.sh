#!/usr/bin/env bash
# Cross-compile fishmmo_webtransport.dll for Windows x86_64.
# Runs on Linux using Zig 0.13+.  Downloads the msquic NuGet package
# for the import library and runtime DLL.
#
#   Arch:    sudo pacman -S zig
#   Manual:  download from https://ziglang.org/download/
set -euo pipefail
cd "$(dirname "$0")"

MSQUIC_VER="2.5.9"

# ── Find Zig ───────────────────────────────────────────────────
ZIG=""
for candidate in zig /usr/bin/zig /usr/local/bin/zig "$HOME/.local/bin/zig" /tmp/zig-*/zig; do
    if [ -x "$candidate" ]; then ZIG="$candidate"; break; fi
done
if [ -z "$ZIG" ]; then
    echo "Zig 0.13+ required: https://ziglang.org/download/"
    echo "  pacman -S zig     # Arch"
    exit 1
fi
echo "Zig: $ZIG ($($ZIG version))"

# ── Directories ─────────────────────────────────────────────────
BDIR="build_win"
UNITY_DIR="../FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Plugins/windows_x86_64"
mkdir -p "$BDIR" "$UNITY_DIR"

# ── Step 1: Download msquic NuGet ───────────────────────────────
MSQUIC_DIR="$BDIR/msquic-win"
if [ ! -f "$MSQUIC_DIR/build/native/bin/x64/msquic.dll" ]; then
    echo "Downloading msquic v${MSQUIC_VER}..."
    mkdir -p "$MSQUIC_DIR"
    curl -sL "https://www.nuget.org/api/v2/package/Microsoft.Native.Quic.MsQuic.Schannel/${MSQUIC_VER}" \
        -o "$BDIR/msquic.nupkg"
    unzip -q -o "$BDIR/msquic.nupkg" -d "$MSQUIC_DIR"
fi

# ── Step 2: Prepare headers ────────────────────────────────────
if [ ! -f "$BDIR/msquic.h" ]; then
    cp "$MSQUIC_DIR/build/native/include/msquic.h" "$BDIR/msquic.h"
    sed -i 's/\[\[nodiscard\]\]/\/* nodiscard *\//g' "$BDIR/msquic.h"
    for h in msquicp.h msquic.hpp msquic_winuser.h; do
        [ -f "$MSQUIC_DIR/build/native/include/$h" ] && cp "$MSQUIC_DIR/build/native/include/$h" "$BDIR/"
    done
fi

# ── SAL annotation stubs (required for non-MSVC compilation) ────
SAL=(
    -D_In_= -D_Inout_= -D_Out_= -D_Outptr_= -D_Outptr_opt_=
    -D_Pre_defensive_= -D_Post_defensive_= -D_In_defensive_=
    '-D__drv_allocatesMem(x)=' '-D__drv_freesMem(x)='
    '-D_Check_return_=' '-D__forceinline=inline'
    '-D_Success_(x)=' '-D_Reserved_=' '-D_IRQL_requires_max_(x)='
    '-D_Field_size_(x)=' '-D_Field_size_bytes_(x)=' '-D_Struct_size_bytes_(x)='
    '-D_Out_writes_bytes_(x)=' '-D_Out_writes_(x)=' '-D_In_reads_bytes_(x)='
    '-D_Out_writes_bytes_opt_(x)=' '-D_At_(x,y)=' '-D_Ret_range_(x,y)='
    '-D_Ret_maybenull_=' '-D_Analysis_noreturn_='
)

# ── Step 3: Compile ────────────────────────────────────────────
SOURCES=(webtransport_api server client session datagram_queue stream_manager http3)
OBJECTS=()
ok=0

echo "Compiling for x86_64-windows-gnu..."
for src in "${SOURCES[@]}"; do
    obj="$BDIR/${src}.o"
    errs=$($ZIG c++ -target x86_64-windows-gnu -c -std=c++17 -O2 \
        -I"$BDIR" -Isrc \
        -DWT_BUILDING_DLL -DWT_PLATFORM_WINDOWS -D_CRT_SECURE_NO_WARNINGS -DNDEBUG \
        "${SAL[@]}" -Wno-format -Wno-unused-command-line-argument \
        -o "$obj" "src/${src}.cpp" 2>&1 | grep -c "error:" || true)
    if [ "$errs" = "0" ] || [ -z "$errs" ]; then
        OBJECTS+=("$obj"); ok=$((ok+1))
    else
        echo "  $src.cpp: FAILED"
        $ZIG c++ -target x86_64-windows-gnu -c -std=c++17 -O2 \
            -I"$BDIR" -Isrc \
            -DWT_BUILDING_DLL -DWT_PLATFORM_WINDOWS -D_CRT_SECURE_NO_WARNINGS -DNDEBUG \
            "${SAL[@]}" -Wno-format \
            -o "$obj" "src/${src}.cpp" 2>&1 | grep "error:" | head -3
    fi
done

if [ "$ok" -ne 7 ]; then
    echo "ERROR: $ok/7 files compiled"
    exit 1
fi
echo "  Compiled $ok/7 objects"

# ── Step 4: Link ───────────────────────────────────────────────
MSQUIC_DLL="$MSQUIC_DIR/build/native/bin/x64/msquic.dll"
echo "Linking fishmmo_webtransport.dll..."

$ZIG c++ -target x86_64-windows-gnu -shared -O2 \
    -o "$UNITY_DIR/fishmmo_webtransport.dll" \
    "${OBJECTS[@]}" \
    "$MSQUIC_DLL" \
    -lws2_32 -lbcrypt -lncrypt \
    -Wl,--out-implib,"$BDIR/libmsquic.a" \
    2>&1 | grep -v "warning:" | grep -v "^$" | head -3 || true
# NOTE: link failures may not be caught — verify the output DLL exists before proceeding.

# Copy runtime msquic.dll alongside our DLL
cp "$MSQUIC_DLL" "$UNITY_DIR/"

echo ""
echo "=== Done ==="
ls -lh "$UNITY_DIR/fishmmo_webtransport.dll" "$UNITY_DIR/msquic.dll"