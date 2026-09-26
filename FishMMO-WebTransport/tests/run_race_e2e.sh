#!/usr/bin/env bash
# End-to-end check that connection teardown on a QUIC worker never frees state
# the application thread is still using: connect/disconnect churn against a
# server polled in a tight loop (with sends racing both ends' closes), first
# bytes parked before SETTINGS, disconnects timed around the native-protocol
# rescue, and the rescue attacked on purpose (1-byte trigger, then a drop or a
# burst of data inside it; delivery must stay exact). Meant to run against an
# AddressSanitizer build as well as the normal one (tests/build_asan.sh).
#
# The native client validates TLS strictly, so a throwaway CA is generated
# and handed to OpenSSL through SSL_CERT_FILE for this process only.
# Usage: bash tests/run_race_e2e.sh <build-dir>   (WT_RACE_SECONDS=n for a longer churn)
set -euo pipefail
BUILD="${1:-build}"
BIN="$(find "$BUILD" -name wt_race_e2e -type f | head -1)"
if [ -z "$BIN" ]; then echo "wt_race_e2e not built (configure with -DWT_BUILD_TESTS=ON)"; exit 2; fi
case "$BIN" in /*) ;; *) BIN="$PWD/$BIN" ;; esac
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT
cd "$WORK"
openssl req -x509 -newkey rsa:2048 -nodes -keyout ca.key -out ca.pem -days 2 -subj "/CN=wt-race-e2e-ca" >/dev/null 2>&1
openssl req -newkey rsa:2048 -nodes -keyout server.key -out server.csr -subj "/CN=localhost" >/dev/null 2>&1
printf "subjectAltName=DNS:localhost,IP:127.0.0.1\n" > ext.cnf
openssl x509 -req -in server.csr -CA ca.pem -CAkey ca.key -CAcreateserial -out server.pem -days 2 -extfile ext.cnf >/dev/null 2>&1
set +e
SSL_CERT_FILE="$WORK/ca.pem" "$BIN" > out.log 2>&1
RC=$?
set -e
grep -E "PASS|FAIL|E2E|^\[(churn|parked|rescue|rescue-race|rescue-integrity|end)\]" out.log || true
if grep -qE "ERROR: (AddressSanitizer|LeakSanitizer)" out.log; then
    grep -A 30 -E "ERROR: (AddressSanitizer|LeakSanitizer)" out.log | head -60
    echo "RACE-E2E FAILED (sanitizer report above)"
    exit 1
fi
exit $RC
