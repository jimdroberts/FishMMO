#!/usr/bin/env bash
# The native WT_ABI_VERSION and the C# ExpectedAbiVersion must agree, or every
# freshly built library is refused by the transport. Run from anywhere.
set -euo pipefail
HERE="$(cd "$(dirname "$0")/.." && pwd)"
HDR="$HERE/src/webtransport_api.h"
CS="$HERE/../FishMMO-Unity/Assets/Plugins/FishNet/Plugins/WebTransport/Native/WebTransportNative.cs"
NATIVE="$(sed -nE 's/^#define WT_ABI_VERSION[[:space:]]+([0-9]+).*/\1/p' "$HDR")"
MANAGED="$(sed -nE 's/.*const int ExpectedAbiVersion = ([0-9]+);.*/\1/p' "$CS")"
if [ -z "$NATIVE" ] || [ -z "$MANAGED" ]; then
    echo "check_abi_version: could not read one of the constants (native='$NATIVE' managed='$MANAGED')"; exit 2
fi
if [ "$NATIVE" != "$MANAGED" ]; then
    echo "check_abi_version: MISMATCH native WT_ABI_VERSION=$NATIVE vs C# ExpectedAbiVersion=$MANAGED"; exit 1
fi
echo "check_abi_version: OK (ABI $NATIVE)"
