#!/usr/bin/env bash
# backup-db.sh — FishMMO PostgreSQL Backup Script
# =================================================
# Creates compressed PostgreSQL backups with automatic rotation:
#   - Keep last 7 daily backups
#   - Keep last 4 weekly backups (Sundays)
#   - Keep last 3 monthly backups (1st of month)
#
# All connection parameters are read from environment variables,
# following the same conventions as .env.example / fishmmo-secrets.env.
#
# Usage:
#   ./backup-db.sh                           # uses env vars or defaults
#   ./backup-db.sh /custom/backup/dir        # custom backup directory
#
# Required environment variables (from fishmmo-secrets.env):
#   ConnectionStrings__NpgsqlConnection     Full Npgsql connection string
#
# If the connection string is not set, individual components are used:
#   PGHOST             (default: localhost)
#   PGPORT             (default: 5432)
#   PGDATABASE         (default: fishmmo)
#   PGUSER             (default: fishmmo)
#   PGPASSWORD         (required if no connection string)
#
# Optional:
#   BACKUP_DIR         (default: /srv/fishmmo/backups)
#   PGDUMP             (default: pg_dump)
#   PG_CONN_STRING     (alternative to ConnectionStrings__NpgsqlConnection)
#
# Exit codes:
#   0 — Success
#   1 — pg_dump/pg_dumpall not found
#   2 — Backup directory creation failed
#   3 — pg_dump failed
#   4 — Invalid connection string or missing credentials

set -euo pipefail

# ── Constants ─────────────────────────────────────────────────────
SCRIPT_NAME="$(basename "$0")"
TIMESTAMP="$(date +'%Y%m%d_%H%M%S')"
DATE_ONLY="$(date +'%Y%m%d')"
DAY_OF_WEEK="$(date +'%u')"        # 1=Mon..7=Sun
DAY_OF_MONTH="$(date +'%d')"
LOG_TAG="backup-db"

# ── Logging ───────────────────────────────────────────────────────
log_info()  { echo "[$LOG_TAG] [$(date +'%H:%M:%S')] INFO  $*"; }
log_warn()  { echo "[$LOG_TAG] [$(date +'%H:%M:%S')] WARN  $*" >&2; }
log_error() { echo "[$LOG_TAG] [$(date +'%H:%M:%S')] ERROR $*" >&2; }

# ── Error Handler ─────────────────────────────────────────────────
PGPASSFILE=""  # will hold path to temp .pgpass; cleaned up in cleanup()

cleanup() {
    local exit_code=$?
    # Securely remove the temporary .pgpass file if it was created
    if [ -n "$PGPASSFILE" ] && [ -f "$PGPASSFILE" ]; then
        rm -f "$PGPASSFILE"
    fi
    if [ $exit_code -ne 0 ]; then
        log_error "Backup failed with exit code $exit_code"
    fi
    exit $exit_code
}
trap cleanup EXIT

# ── Prerequisites ─────────────────────────────────────────────────
if ! command -v pg_dump &>/dev/null; then
    log_error "pg_dump not found. Install postgresql-client and ensure it is in PATH."
    exit 1
fi

PGDUMP="$(command -v pg_dump)"
log_info "Using pg_dump: $PGDUMP"

# ── Configuration ─────────────────────────────────────────────────
# Backup destination (first argument or BACKUP_DIR env or default)
BACKUP_DIR="${1:-${BACKUP_DIR:-/srv/fishmmo/backups}}"

# Try to parse the full Npgsql connection string first
PG_CONN_STRING="${PG_CONN_STRING:-${ConnectionStrings__NpgsqlConnection:-}}"

if [ -n "$PG_CONN_STRING" ]; then
    log_info "Using connection string for database connection."
    # Parse common Npgsql connection string parameters.
    # Format: Host=host;Port=5432;Database=fishmmo;Username=user;Password=pass
    # Use portable sed instead of GNU grep -oP for macOS/BSD compatibility.
    PG_HOST=$(echo "$PG_CONN_STRING" | sed -n 's/.*Host=\([^;]*\).*/\1/p' | tail -1; echo "${PGHOST:-localhost}")
    PG_HOST="${PG_HOST%$'\n'*}"; PG_HOST="${PG_HOST:-localhost}"
    PG_PORT=$(echo "$PG_CONN_STRING" | sed -n 's/.*Port=\([^;]*\).*/\1/p' | tail -1; echo "${PGPORT:-5432}")
    PG_PORT="${PG_PORT%$'\n'*}"; PG_PORT="${PG_PORT:-5432}"
    PG_DATABASE=$(echo "$PG_CONN_STRING" | sed -n 's/.*Database=\([^;]*\).*/\1/p' | tail -1; echo "${PGDATABASE:-fishmmo}")
    PG_DATABASE="${PG_DATABASE%$'\n'*}"; PG_DATABASE="${PG_DATABASE:-fishmmo}"
    PG_USER=$(echo "$PG_CONN_STRING" | sed -n 's/.*Username=\([^;]*\).*/\1/p' | tail -1; echo "${PGUSER:-fishmmo}")
    PG_USER="${PG_USER%$'\n'*}"; PG_USER="${PG_USER:-fishmmo}"
    PG_PASSWORD=$(echo "$PG_CONN_STRING" | sed -n 's/.*Password=\([^;]*\).*/\1/p' | tail -1; echo "${PGPASSWORD:-}")
    PG_PASSWORD="${PG_PASSWORD%$'\n'*}"; PG_PASSWORD="${PG_PASSWORD:-}"
else
    # Fall back to individual environment variables
    PG_HOST="${PGHOST:-localhost}"
    PG_PORT="${PGPORT:-5432}"
    PG_DATABASE="${PGDATABASE:-fishmmo}"
    PG_USER="${PGUSER:-fishmmo}"
    PG_PASSWORD="${PGPASSWORD:-}"
fi

if [ -z "$PG_PASSWORD" ]; then
    log_error "No database password found. Set PGPASSWORD or include Password= in ConnectionStrings__NpgsqlConnection."
    exit 4
fi

log_info "Backup target:   $PG_HOST:$PG_PORT/$PG_DATABASE"
log_info "Backup user:     $PG_USER"
log_info "Backup directory: $BACKUP_DIR"

# ── Prepare Backup Directory ──────────────────────────────────────
mkdir -p "$BACKUP_DIR/daily" "$BACKUP_DIR/weekly" "$BACKUP_DIR/monthly"
if [ ! -d "$BACKUP_DIR/daily" ] || [ ! -d "$BACKUP_DIR/weekly" ] || [ ! -d "$BACKUP_DIR/monthly" ]; then
    log_error "Failed to create backup subdirectories under $BACKUP_DIR"
    exit 2
fi

# ── Perform Backup ────────────────────────────────────────────────
BACKUP_FILE="${BACKUP_DIR}/daily/${PG_DATABASE}_${TIMESTAMP}.dump"
log_info "Starting pg_dump to: $BACKUP_FILE"

# Create a temporary .pgpass file instead of using PGPASSWORD environment variable.
# This avoids exposing the password in process listings (ps aux) and core dumps.
PGPASSFILE=$(mktemp)
chmod 600 "$PGPASSFILE"
echo "$PG_HOST:$PG_PORT:$PG_DATABASE:$PG_USER:$PG_PASSWORD" > "$PGPASSFILE"
export PGPASSFILE
log_info "Using temporary .pgpass for authentication (password not exposed in process env)"

if ! "$PGDUMP" \
    --host="$PG_HOST" \
    --port="$PG_PORT" \
    --username="$PG_USER" \
    --dbname="$PG_DATABASE" \
    --format=custom \
    --compress=9 \
    --file="$BACKUP_FILE" \
    --verbose \
    --no-password \
    2>> "${BACKUP_DIR}/pg_dump.log"; then
    log_error "pg_dump failed. Check ${BACKUP_DIR}/pg_dump.log for details."
    exit 3
fi

# Remove the .pgpass file immediately after use (cleanup trap also handles this)
rm -f "$PGPASSFILE"
PGPASSFILE=""
unset PGPASSFILE

# Verify the dump is readable
if ! "$PGDUMP" --format=custom --list "$BACKUP_FILE" &>/dev/null; then
    log_error "Backup file verification failed: $BACKUP_FILE is not a valid pg_dump archive."
    rm -f "$BACKUP_FILE"
    exit 3
fi

# Record backup metadata
BACKUP_SIZE=$(stat --format=%s "$BACKUP_FILE" 2>/dev/null || stat -f%z "$BACKUP_FILE" 2>/dev/null || echo "unknown")
BACKUP_CHECKSUM=$(sha256sum "$BACKUP_FILE" | cut -d' ' -f1)
echo "$TIMESTAMP $BACKUP_FILE ${BACKUP_SIZE} ${BACKUP_CHECKSUM}" >> "${BACKUP_DIR}/backup_manifest.txt"

log_info "Backup completed successfully: $BACKUP_FILE"
log_info "Backup size: $(numfmt --to=iec $BACKUP_SIZE 2>/dev/null || echo "${BACKUP_SIZE} bytes")"
log_info "SHA256: $BACKUP_CHECKSUM"

# ── Rotation: Promote to Weekly/Monthly ──────────────────────────
# Weekly: keep on Sundays (day 7)
if [ "$DAY_OF_WEEK" = "7" ]; then
    WEEKLY_FILE="${BACKUP_DIR}/weekly/${PG_DATABASE}_week_$(date +'%Y%m%d').dump"
    cp "$BACKUP_FILE" "$WEEKLY_FILE"
    log_info "Promoted to weekly: $WEEKLY_FILE"
fi

# Monthly: keep on the 1st
if [ "$DAY_OF_MONTH" = "01" ]; then
    MONTHLY_FILE="${BACKUP_DIR}/monthly/${PG_DATABASE}_month_$(date +'%Y%m').dump"
    cp "$BACKUP_FILE" "$MONTHLY_FILE"
    log_info "Promoted to monthly: $MONTHLY_FILE"
fi

# ── Rotation: Prune Old Backups ──────────────────────────────────
# Daily: keep last 7
log_info "Pruning daily backups (keeping last 7)..."
ls -1t "${BACKUP_DIR}/daily/"*.dump 2>/dev/null | tail -n +8 | while read -r old; do
    log_info "Removing old daily backup: $old"
    rm -f "$old"
done

# Weekly: keep last 4
log_info "Pruning weekly backups (keeping last 4)..."
ls -1t "${BACKUP_DIR}/weekly/"*.dump 2>/dev/null | tail -n +5 | while read -r old; do
    log_info "Removing old weekly backup: $old"
    rm -f "$old"
done

# Monthly: keep last 3
log_info "Pruning monthly backups (keeping last 3)..."
ls -1t "${BACKUP_DIR}/monthly/"*.dump 2>/dev/null | tail -n +4 | while read -r old; do
    log_info "Removing old monthly backup: $old"
    rm -f "$old"
done

log_info "Backup rotation complete."
log_info "=== Backup finished successfully: $(date) ==="
