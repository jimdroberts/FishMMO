#!/usr/bin/env bash
set -euo pipefail
fish_webgpu_root="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")/../.." && pwd)"
fish_webgpu_debug=()
if [[ -n "${FISHMMO_WEBGPU_DEBUG_PORT:-}" ]]; then
    fish_webgpu_debug=("--remote-debugging-port=${FISHMMO_WEBGPU_DEBUG_PORT}" --remote-debugging-address=127.0.0.1)
fi
# Optional: unquantized WebGPU timestamps for gpu_timing_webgpu.mjs (Chrome rounds them to 100 µs otherwise).
if [[ "${FISHMMO_WEBGPU_DEV_FEATURES:-}" == "1" ]]; then
    fish_webgpu_debug+=(--enable-webgpu-developer-features)
fi
# WebGPU is on by default (Brave 155, Linux): --enable-unsafe-webgpu is no longer needed and only adds an
# "unsupported command-line flag" warning bar.
exec brave --user-data-dir="$fish_webgpu_root/Library/WebGpuSmoke/brave-profile" \
    --ozone-platform=x11 --no-first-run --no-default-browser-check \
    --enable-features=Vulkan \
    "${fish_webgpu_debug[@]}" --new-window "${1:-http://127.0.0.1:8090/}"
