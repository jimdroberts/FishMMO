#!/usr/bin/env bash
# ═══════════════════════════════════════════════════════════════════════════════
# substitute-security-sentinels.sh
# ═══════════════════════════════════════════════════════════════════════════════
# Replaces sentinel placeholder values in the IL-embedded generated files with
# real values from environment variables. Run BEFORE invoking the Unity build.
#
# Required env vars for release builds:
#   FISHMMO_CLIENT_GATE_SECRET  — shared secret for X-FishMMO-Client HMAC header
#   FISHMMO_PIN_ACTIVE          — SHA-256 SPKI base64 pin (primary key)
#   FISHMMO_PIN_BACKUP          — SHA-256 SPKI base64 pin (backup key)
#
# Optional env vars (defaults to fishmmo.com sentinels if unset):
#   FISHMMO_API_HOST            — API gateway URL  (e.g. "https://api.fishmmo.com")
#   FISHMMO_GAME_HOST           — Game server host  (e.g. "game.fishmmo.com")
#   FISHMMO_PLAY_HOST           — WebGL client host (e.g. "play.fishmmo.com")
#   FISHMMO_ROOT_DOMAIN         — Root domain        (e.g. "fishmmo.com")
#
# Usage:
#   source substitute-security-sentinels.sh
#   # ...then run Unity build...
#
#   Or in CI:
#   bash substitute-security-sentinels.sh /path/to/FishMMO-Unity
#
# After the build, restore templates with:
#   git checkout -- Assets/Scripts/Client/Security/CertificatePins.generated.cs \
#                   Assets/Scripts/Client/Security/ClientApiSecret.generated.cs \
#                   Assets/Scripts/Shared/Implementation/HostConfig.generated.cs
# ═══════════════════════════════════════════════════════════════════════════════

set -euo pipefail

UNITY_PROJECT="${1:-.}"
SECURITY_DIR="${UNITY_PROJECT}/Assets/Scripts/Client/Security"
SHARED_DIR="${UNITY_PROJECT}/Assets/Scripts/Shared/Implementation"

PINS_FILE="${SECURITY_DIR}/CertificatePins.generated.cs"
SECRET_FILE="${SECURITY_DIR}/ClientApiSecret.generated.cs"
HOST_FILE="${SHARED_DIR}/HostConfig.generated.cs"

# ── Validate files exist ──────────────────────────────────────────────

MISSING=0
for f in "${PINS_FILE}" "${SECRET_FILE}" "${HOST_FILE}"; do
    if [ ! -f "${f}" ]; then
        echo "ERROR: ${f} not found. Is the Unity project path correct?"
        MISSING=1
    fi
done
if [ "${MISSING}" -eq 1 ]; then
    exit 1
fi

echo "=== Substituting security sentinels ==="

# ── Gate secret ──────────────────────────────────────────────────────

if [ -n "${FISHMMO_CLIENT_GATE_SECRET:-}" ]; then
    echo "  Substituting FISHMMO_CLIENT_GATE_SECRET..."
    sed -i "s|FISHMMO_SENTINEL_PLACEHOLDER_CLIENT_GATE_SECRET|${FISHMMO_CLIENT_GATE_SECRET}|g" "${SECRET_FILE}"
    if grep -q "FISHMMO_SENTINEL_PLACEHOLDER" "${SECRET_FILE}"; then
        echo "  WARNING: Sentinel marker still present in ClientApiSecret.generated.cs after substitution."
    else
        echo "  OK: Gate secret substituted."
    fi
else
    echo "  WARNING: FISHMMO_CLIENT_GATE_SECRET not set. Sentinel will remain."
    echo "  The build validator will block a release build."
fi

# ── Certificate pins ─────────────────────────────────────────────────

if [ -n "${FISHMMO_PIN_ACTIVE:-}" ] && [ -n "${FISHMMO_PIN_BACKUP:-}" ]; then
    echo "  Substituting FISHMMO_PIN_ACTIVE and FISHMMO_PIN_BACKUP..."
    sed -i "s|FISHMMO_SENTINEL_PLACEHOLDER_ACTIVE_PIN|${FISHMMO_PIN_ACTIVE}|g" "${PINS_FILE}"
    sed -i "s|FISHMMO_SENTINEL_PLACEHOLDER_BACKUP_PIN|${FISHMMO_PIN_BACKUP}|g" "${PINS_FILE}"
    if grep -q "FISHMMO_SENTINEL_PLACEHOLDER" "${PINS_FILE}"; then
        echo "  WARNING: Sentinel marker still present in CertificatePins.generated.cs after substitution."
    else
        echo "  OK: Certificate pins substituted."
    fi
else
    echo "  WARNING: FISHMMO_PIN_ACTIVE and/or FISHMMO_PIN_BACKUP not set."
    echo "  Sentinels will remain. The build validator will block a release build."
fi

# ── Host configuration ────────────────────────────────────────────────

if [ -n "${FISHMMO_API_HOST:-}" ]; then
    echo "  Substituting FISHMMO_API_HOST..."
    sed -i "s|FISHMMO_SENTINEL_PLACEHOLDER_API_HOST|${FISHMMO_API_HOST#https://}|g" "${HOST_FILE}"
else
    echo "  INFO: FISHMMO_API_HOST not set. Using sentinel default."
fi

if [ -n "${FISHMMO_GAME_HOST:-}" ]; then
    echo "  Substituting FISHMMO_GAME_HOST..."
    sed -i "s|FISHMMO_SENTINEL_PLACEHOLDER_GAME_HOST|${FISHMMO_GAME_HOST}|g" "${HOST_FILE}"
else
    echo "  INFO: FISHMMO_GAME_HOST not set. Using sentinel default."
fi

if [ -n "${FISHMMO_PLAY_HOST:-}" ]; then
    echo "  Substituting FISHMMO_PLAY_HOST..."
    sed -i "s|FISHMMO_SENTINEL_PLACEHOLDER_PLAY_HOST|${FISHMMO_PLAY_HOST}|g" "${HOST_FILE}"
else
    echo "  INFO: FISHMMO_PLAY_HOST not set. Using sentinel default."
fi

if [ -n "${FISHMMO_ROOT_DOMAIN:-}" ]; then
    echo "  Substituting FISHMMO_ROOT_DOMAIN..."
    sed -i "s|FISHMMO_SENTINEL_PLACEHOLDER_ROOT_DOMAIN|${FISHMMO_ROOT_DOMAIN}|g" "${HOST_FILE}"
else
    echo "  INFO: FISHMMO_ROOT_DOMAIN not set. Using sentinel default."
fi

if grep -q "FISHMMO_SENTINEL_PLACEHOLDER" "${HOST_FILE}"; then
    echo "  WARNING: Sentinel markers still present in HostConfig.generated.cs."
    echo "  This is OK for development builds but will fail the release validator."
else
    echo "  OK: All host config sentinels substituted."
fi

echo "=== Substitution complete ==="
echo ""
echo "Restore after build:"
echo "  git checkout -- Assets/Scripts/Client/Security/CertificatePins.generated.cs \\"
echo "                  Assets/Scripts/Client/Security/ClientApiSecret.generated.cs \\"
echo "                  Assets/Scripts/Shared/Implementation/HostConfig.generated.cs"
