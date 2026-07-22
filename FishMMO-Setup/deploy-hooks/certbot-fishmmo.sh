#!/bin/bash
# certbot deploy hook for FishMMO game servers.
# Called by certbot after successful certificate renewal.
# Copies renewed certs to the game server cert directory and signals services.
#
# Install: ln -s $(pwd)/certbot-fishmmo.sh /etc/letsencrypt/renewal-hooks/deploy/fishmmo.sh
# Or add to certbot command: certbot renew --deploy-hook /path/to/certbot-fishmmo.sh
set -euo pipefail

CERT_SRC="/etc/letsencrypt/live/fishmmo.com"
CERT_DST="${FISHMMO_CERT_DIR:-/etc/fishmmo/certs}"
GAME_USER="${FISHMMO_GAME_USER:-fishmmo}"

# Verify the nginx user has access to the certificate directory. The cert files
# must be readable by nginx worker processes. Add the nginx user to the fishmmo
# group during server setup:
#   sudo usermod -aG fishmmo nginx
if ! groups nginx 2>/dev/null | grep -qw "$GAME_USER"; then
    echo "  WARNING: nginx user does not appear to be in the $GAME_USER group." >&2
    echo "  Certificates may not be readable by NGINX. Run: usermod -aG $GAME_USER nginx" >&2
fi

echo "[$(date -Iseconds)] certbot deploy hook: renewing FishMMO certs"

# 0. Validate the renewed certificate before copying
if [ ! -f "$CERT_SRC/fullchain.pem" ] || [ ! -f "$CERT_SRC/privkey.pem" ]; then
    echo "  ERROR: Certificate files missing from $CERT_SRC" >&2
    exit 1
fi
if [ ! -s "$CERT_SRC/fullchain.pem" ] || [ ! -s "$CERT_SRC/privkey.pem" ]; then
    echo "  ERROR: Certificate files are empty in $CERT_SRC" >&2
    exit 1
fi
if ! openssl x509 -in "$CERT_SRC/fullchain.pem" -noout -checkend 0; then
    echo "  ERROR: Certificate in $CERT_SRC is expired or invalid" >&2
    exit 1
fi
echo "  Certificate validation passed"

# 1. Copy renewed certs to shared game server directory
mkdir -p "$CERT_DST"
cp "$CERT_SRC/fullchain.pem" "$CERT_DST/fullchain.pem"
cp "$CERT_SRC/privkey.pem"  "$CERT_DST/privkey.pem"
# Set group to nginx so the nginx worker processes can read the certs.
# The nginx user must be in the fishmmo group (or the cert files use group nginx).
chown "$GAME_USER:nginx" "$CERT_DST/fullchain.pem" "$CERT_DST/privkey.pem"
chmod 640 "$CERT_DST/fullchain.pem" "$CERT_DST/privkey.pem"
echo "  Certs copied to $CERT_DST"

# 1b. Verify private key matches certificate (pubkey hash comparison, works for RSA/ECDSA/Ed25519)
KEY_HASH=$(openssl pkey -noout -pubkey -in "$CERT_DST/privkey.pem" 2>/dev/null | openssl dgst -sha256)
CERT_HASH=$(openssl x509 -noout -pubkey -in "$CERT_DST/fullchain.pem" 2>/dev/null | openssl dgst -sha256)
if [ "$KEY_HASH" != "$CERT_HASH" ]; then
    echo "  ERROR: Private key does not match certificate!" >&2
    exit 1
fi
echo "  Private key and certificate match OK"

# 2. Reload NGINX (zero-downtime)
if ! nginx -t 2>/dev/null; then
    echo "  ERROR: nginx config test failed" >&2
    exit 1
fi
if ! nginx -s reload 2>/dev/null; then
    echo "  ERROR: nginx reload failed" >&2
    exit 1
fi
echo "  NGINX reloaded"

# Signal-based reload fallback (used when systemd is unavailable).
#
# WARNING: Game servers MUST implement SIGHUP handling to reload TLS
# certificates from disk. If a server does not handle SIGHUP, the
# default action is termination — this WILL kill the process.
# Verify that every FishMMO.*Server binary registers a SIGHUP handler
# (e.g. via POSIX signal() or .NET's Console.CancelKeyPress) before
# deploying this hook.
signal_reload_fallback() {
    echo "  Signaling game servers to reload certs via SIGHUP..."
    pkill -HUP -f "FishMMO.*Server" 2>/dev/null || echo "  WARNING: no game server processes found"
}

# 3. Restart game servers so MsQuic picks up the new certs.
# MsQuic reads certs once at QUIC configuration load time and does
# not auto-reload. A restart is required.
#
# The wait_for_server_healthy helper below implements rolling restart
# with health-check polling: after each restart it polls systemd and
# checks for a listening port (via ss) with exponential backoff up to
# 60 seconds before proceeding to the next server.
#
# Between scene server restarts a 2-second sleep prevents
# thundering-herd reconnections.  For true zero-downtime across all
# server tiers, consider adding an application-level health endpoint
# (e.g., /healthz) and waiting for a 200 response instead of just a
# listening port.
#
# ── HEALTH-CHECK POLLING HELPER ──────────────────────────────
# Polls systemd unit status AND verifies at least one listening
# port is up (via ss).  Uses exponential backoff up to 10s.
wait_for_server_healthy() {
    local unit_name="$1"
    local timeout="${2:-60}"
    local elapsed=0
    local delay=1

    echo "  Waiting for $unit_name to become healthy (timeout ${timeout}s)..."
    while [ "$elapsed" -lt "$timeout" ]; do
        if systemctl is-active --quiet "$unit_name" 2>/dev/null; then
            local pid
            pid=$(systemctl show --property MainPID --value "$unit_name" 2>/dev/null)
            if [ -n "$pid" ] && [ "$pid" -gt 1 ] 2>/dev/null; then
                if ss -tuln -p 2>/dev/null | grep -q "pid=$pid,"; then
                    echo "  $unit_name is healthy (port listening)"
                    return 0
                fi
            fi
        fi
        sleep "$delay"
        elapsed=$((elapsed + delay))
        delay=$((delay * 2))
        [ "$delay" -gt 10 ] && delay=10
    done
    echo "  WARNING: $unit_name did not become healthy within ${timeout}s"
    return 1
}

# Option A: systemd service restart (brief downtime per server)
if command -v systemctl &>/dev/null; then
    # Broader systemd check: if any fishmmo unit exists, use systemctl
    if systemctl list-units --all --plain --no-legend 'fishmmo-*' 2>/dev/null | grep -q .; then
        echo "  Reloading game servers via systemd (rolling restart)..."
        for unit in fishmmo-login fishmmo-world; do
            if systemctl is-active --quiet "$unit" 2>/dev/null; then
                systemctl restart "$unit" || echo "  WARNING: $unit restart failed"
                # Poll for health instead of a fixed sleep.
                wait_for_server_healthy "$unit" 60
            fi
        done
        for unit in $(systemctl list-units --all --plain --no-legend 'fishmmo-scene@*' 2>/dev/null | awk '{print $1}'); do
            if systemctl is-active --quiet "$unit" 2>/dev/null; then
                systemctl restart "$unit" || true
                # Brief pause between scene server restarts to prevent
                # thundering-herd reconnections from all disconnected clients.
                sleep 2
            fi
        done
    else
        # No fishmmo systemd units found, falling back to signal-based reload.
        echo "  No fishmmo systemd units found, falling back to signal-based reload."
        signal_reload_fallback
    fi
else
    signal_reload_fallback
fi

echo "[$(date -Iseconds)] certbot deploy hook complete"