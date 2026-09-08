#!/usr/bin/env bash
# End-to-end check of the transport-level limits (wt_server_set_limits):
# per-connection inbound token bucket + flood kick, per-IP concurrent cap,
# half-open cap, and that a paced sender is untouched.
#
# The native client validates TLS strictly, so a throwaway CA is generated
# and handed to OpenSSL through SSL_CERT_FILE for this process only.
# Usage: bash tests/run_limits_e2e.sh <build-dir>
set -euo pipefail
BUILD="${1:-build}"
BIN="$(find "$BUILD" -name wt_limits_e2e -type f | head -1)"
if [ -z "$BIN" ]; then echo "wt_limits_e2e not built (configure with -DWT_BUILD_TESTS=ON)"; exit 2; fi
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
cd "$WORK"
openssl req -x509 -newkey rsa:2048 -nodes -keyout ca.key -out ca.pem -days 2 -subj "/CN=wt-limits-e2e-ca" >/dev/null 2>&1
openssl req -newkey rsa:2048 -nodes -keyout server.key -out server.csr -subj "/CN=localhost" >/dev/null 2>&1
printf "subjectAltName=DNS:localhost,IP:127.0.0.1\n" > ext.cnf
openssl x509 -req -in server.csr -CA ca.pem -CAkey ca.key -CAcreateserial -out server.pem -days 2 -extfile ext.cnf >/dev/null 2>&1
SSL_CERT_FILE="$WORK/ca.pem" "$OLDPWD/$BIN" 2>&1 | grep -E "PASS|FAIL|E2E|failed"
