#!/usr/bin/env bash
# Start-up contract check (wt_abi_version, TLS provider, certificate-path
# failure vs backend failure, self-signed start). Needs no certificates.
# Usage: bash tests/run_startup_e2e.sh <build-dir>
set -euo pipefail
BUILD="${1:-build}"
BIN="$(find "$BUILD" -name wt_startup_e2e -type f | head -1)"
if [ -z "$BIN" ]; then echo "wt_startup_e2e not built (configure with -DWT_BUILD_TESTS=ON)"; exit 2; fi
"$BIN" 2>&1 | grep -E "PASS|FAIL|E2E|failed|provider"
