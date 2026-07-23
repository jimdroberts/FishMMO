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
if ! openssl x509 -in "$CERT_SRC/fullchain.pem" -noout -checkend 86400; then
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

# ── DRY-RUN MODE ─────────────────────────────────────────────
# Set FISHMMO_DRY_RUN=1 to print what would be done without
# actually signaling or restarting any services.
DRY_RUN="${FISHMMO_DRY_RUN:-0}"

# ── DOCKER HEALTH CHECK HELPER ──────────────────────────────
# Polls Docker container health status with exponential backoff.
docker_wait_for_healthy() {
    local container_name="$1"
    local timeout="${2:-60}"
    local elapsed=0
    local delay=1

    echo "  Waiting for Docker container $container_name to become healthy (timeout ${timeout}s)..."
    while [ "$elapsed" -lt "$timeout" ]; do
        local status
        status=$(docker inspect --format='{{.State.Health.Status}}' "$container_name" 2>/dev/null || echo "starting")
        if [ "$status" = "healthy" ]; then
            echo "  Docker container $container_name is healthy"
            return 0
        fi
        sleep "$delay"
        elapsed=$((elapsed + delay))
        delay=$((delay * 2))
        [ "$delay" -gt 10 ] && delay=10
    done
    echo "  WARNING: Docker container $container_name did not become healthy within ${timeout}s"
    return 1
}

# ── DOCKER RESTART FUNCTION ─────────────────────────────────
# Restarts game server containers using docker compose.
# Used when game servers are deployed via Docker rather than systemd.
docker_restart_servers() {
    local compose_dir="${1:-/etc/fishmmo}"
    local compose_file="${compose_dir}/docker-compose.yml"

    echo "  Restarting game servers via Docker Compose..."

    # Check that docker is available and the compose file exists.
    if ! command -v docker &>/dev/null; then
        echo "  WARNING: docker command not found, cannot restart containers."
        return 1
    fi
    if [ ! -f "$compose_file" ] || [ ! -f "${compose_dir}/fishmmo-secrets.env" ]; then
        echo "  WARNING: Missing required files in $compose_dir. Skipping Docker restart."
        return 1
    fi

    # Check if the fishmmo stack is running (any container from it).
    local project_name
    project_name=$(docker compose ls --filter name="fishmmo" --format json 2>/dev/null | grep -o '"Name":"[^"]*"' | head -1 | cut -d'"' -f4 || echo "")
    if [ -z "$project_name" ]; then
        echo "  WARNING: No fishmmo Docker Compose stack appears to be running."
        return 1
    fi

    echo "  Found running Docker Compose project: $project_name"

    if [ "$DRY_RUN" = "1" ]; then
        echo "  [DRY RUN] Would run: docker compose -p $project_name restart nginx"
        echo "  [DRY RUN] Would run: docker compose -p $project_name restart login-server world-server scene-server"
        return 0
    fi

    # 1. Reload nginx (config may reference renewed certs).
    echo "  Restarting nginx container..."
    docker compose -p "$project_name" restart nginx
    docker_wait_for_healthy "fishmmo-nginx" 30

    # 2. Restart game server containers (rolling where possible).
    #    Docker compose restart stops and starts each container in parallel.
    #    We use --no-deps to avoid restarting dependencies.
    for svc in login-server world-server scene-server; do
        echo "  Restarting $svc..."
        docker compose -p "$project_name" restart --no-deps "$svc" 2>/dev/null || \
            docker compose -p "$project_name" restart "$svc" 2>/dev/null || \
            echo "  WARNING: $svc restart failed (container may not exist in stack)"
    done

    echo "  Docker Compose restart completed"
    return 0
}

# ── SIGNAL-BASED RELOAD FALLBACK ───────────────────────────
# Used when neither Docker nor systemd are available.
#
# !!! WARNING !!!
# SIGHUP sent via pkill will TERMINATE any process that does not
# explicitly handle the signal.  The default action for SIGHUP on
# Linux is process termination — NOT a config reload.
# Verify that EVERY FishMMO.*Server binary registers a SIGHUP
# handler (e.g., via POSIX signal(SIGHUP, handler) or .NET's
# Console.CancelKeyPress) before relying on this path.
#
# This function attempts safer alternatives first:
#   1. docker kill -s HUP <container>  (if Docker is available)
#   2. systemctl reload <unit>         (if systemd is available)
#   3. pkill -HUP                      (last resort, with warning)
signal_reload_fallback() {
    local signaled_any=0

    # ── Attempt 1: Docker-based SIGHUP ──────────────────────
    if command -v docker &>/dev/null; then
        for container in fishmmo-login fishmmo-world fishmmo-scene; do
            if docker ps --format '{{.Names}}' 2>/dev/null | grep -qx "$container"; then
                echo "  Signaling $container to reload certs via docker kill -s HUP..."
                if [ "$DRY_RUN" = "1" ]; then
                    echo "  [DRY RUN] Would run: docker kill -s HUP $container"
                else
                    docker kill -s HUP "$container" 2>/dev/null && signaled_any=1 || \
                        echo "  WARNING: failed to signal $container"
                    # ── 5-second health check after signal ──
                    local hup_elapsed=0
                    while [ "$hup_elapsed" -lt 5 ]; do
                        if docker ps --format '{{.Names}}' 2>/dev/null | grep -qx "$container" && \
                           docker inspect --format='{{.State.Status}}' "$container" 2>/dev/null | grep -q "running"; then
                            break
                        fi
                        sleep 1
                        hup_elapsed=$((hup_elapsed + 1))
                    done
                    if [ "$hup_elapsed" -ge 5 ]; then
                        echo "  CRITICAL: $container may have been terminated by SIGHUP (no SIGHUP handler?)" >&2
                    fi
                fi
            fi
        done
    fi

    # ── Attempt 2: systemctl reload ─────────────────────────
    if command -v systemctl &>/dev/null; then
        for unit in fishmmo-login fishmmo-world; do
            if systemctl is-active --quiet "$unit" 2>/dev/null; then
                echo "  Signaling $unit to reload via systemctl reload..."
                if [ "$DRY_RUN" = "1" ]; then
                    echo "  [DRY RUN] Would run: systemctl reload $unit"
                else
                    systemctl reload "$unit" 2>/dev/null && signaled_any=1 || \
                        echo "  WARNING: systemctl reload $unit failed (unit may not support reload)"
                fi
            fi
        done
    fi

    # ── Attempt 3: pkill -HUP (last resort) ─────────────────
    echo ""
    echo "  >>> WARNING: Falling back to pkill -HUP <<<"
    echo "  >>> If game servers do NOT handle SIGHUP, they WILL be terminated. <<<"
    echo "  >>> Verify SIGHUP handler registration before deploying. <<<"
    echo ""
    if [ "$DRY_RUN" = "1" ]; then
        echo "  [DRY RUN] Would run: pkill -HUP -f \"FishMMO.*Server\""
    else
        echo "  Signaling game servers to reload certs via pkill -HUP..."
        pkill -HUP -f "FishMMO.*Server" 2>/dev/null && signaled_any=1
        # ── 5-second health check after pkill ────────────────
        local pkill_elapsed=0
        while [ "$pkill_elapsed" -lt 5 ]; do
            if pgrep -f "FishMMO.*Server" >/dev/null 2>&1; then
                break
            fi
            sleep 1
            pkill_elapsed=$((pkill_elapsed + 1))
        done
        if [ "$pkill_elapsed" -ge 5 ]; then
            echo "  CRITICAL: pkill -HUP may have terminated game server processes (no SIGHUP handler?)" >&2
            echo "  Check process status and restart if needed." >&2
        fi
    fi

    if [ "$signaled_any" -eq 0 ] && [ "$DRY_RUN" != "1" ]; then
        echo "  WARNING: no game server processes found to signal"
    fi
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

# ── Game Server Restart ──────────────────────────────────────
if [ "$DRY_RUN" = "1" ]; then
    echo ""
    echo "  >>> DRY RUN MODE — no services will be restarted <<<"
    echo ""
fi

# Option A: Docker Compose restart
if docker_restart_servers; then
    echo "  Docker Compose restart path completed successfully."
# Option B: systemd service restart (brief downtime per server)
elif command -v systemctl &>/dev/null; then
    # Broader systemd check: if any fishmmo unit exists, use systemctl
    if systemctl list-units --all --plain --no-legend 'fishmmo-*' 2>/dev/null | grep -q .; then
        echo "  Reloading game servers via systemd (rolling restart)..."
        for unit in fishmmo-login fishmmo-world; do
            if systemctl is-active --quiet "$unit" 2>/dev/null; then
                if [ "$DRY_RUN" = "1" ]; then
                    echo "  [DRY RUN] Would run: systemctl restart $unit"
                else
                    systemctl restart "$unit" || echo "  WARNING: $unit restart failed"
                    # Poll for health instead of a fixed sleep.
                    wait_for_server_healthy "$unit" 60
                fi
            fi
        done
        for unit in $(systemctl list-units --all --plain --no-legend 'fishmmo-scene@*' 2>/dev/null | awk '{print $1}'); do
            if systemctl is-active --quiet "$unit" 2>/dev/null; then
                if [ "$DRY_RUN" = "1" ]; then
                    echo "  [DRY RUN] Would run: systemctl restart $unit"
                else
                    systemctl restart "$unit" || true
                    # Brief pause between scene server restarts to prevent
                    # thundering-herd reconnections from all disconnected clients.
                    sleep 2
                fi
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