#!/usr/bin/env bash
# restore-db.sh — FishMMO PostgreSQL Restore Script
# ===================================================
# Restores a FishMMO database from a compressed custom-format dump
# created by backup-db.sh.
#
# Usage:
#   ./restore-db.sh <backup-file>                # restore to configured database
#   ./restore-db.sh <backup-file> <target-db>    # restore to a different database
#
# Connection parameters are read from the same environment variables
# as backup-db.sh (see that script or .env.example for details).
#
# WARNING: This will DROP and recreate the target database.
# All existing data in the target database will be lost.
#
# Exit codes:
#   0 — Success
#   1 — pg_restore not found
#   2 — Backup file not found or invalid
#   3 — pg_restore failed
#   4 — Missing connection parameters

set -euo pipefail

# ── Constants ─────────────────────────────────────────────────────
SCRIPT_NAME="$(basename "$0")"
LOG_TAG="restore-db"

# ── Logging ───────────────────────────────────────────────────────
log_info()  { echo "[$LOG_TAG] [$(date +'%H:%M:%S')] INFO  $*"; }
log_error() { echo "[$LOG_TAG] [$(date +'%H:%M:%S')] ERROR $*" >&2; }

# ── Prerequisites ─────────────────────────────────────────────────
if ! command -v pg_restore &>/dev/null; then
    log_error "pg_restore not found. Install postgresql-client and ensure it is in PATH."
    exit 1
fi

if ! command -v psql &>/dev/null; then
    log_error "psql not found. Install postgresql-client and ensure it is in PATH."
    exit 1
fi

# ── Arguments ─────────────────────────────────────────────────────
if [ $# -lt 1 ]; then
    echo "Usage: $0 <backup-file> [target-database]"
    echo ""
    echo "Restores a FishMMO database from a pg_dump custom-format archive."
    echo "If target-database is omitted, the database name is parsed from"
    echo "the connection string or PGDATABASE environment variable."
    echo ""
    echo "Examples:"
    echo "  $0 /srv/fishmmo/backups/daily/fishmmo_20240101_120000.dump"
    echo "  $0 /srv/fishmmo/backups/daily/fishmmo_20240101_120000.dump fishmmo_restore_test"
    exit 1
fi

BACKUP_FILE="$1"
TARGET_DB="${2:-}"

if [ ! -f "$BACKUP_FILE" ]; then
    log_error "Backup file not found: $BACKUP_FILE"
    exit 2
fi

# Verify the dump is a valid pg_dump custom-format archive
if ! pg_restore --format=custom --list "$BACKUP_FILE" &>/dev/null; then
    log_error "Not a valid pg_dump custom-format archive: $BACKUP_FILE"
    exit 2
fi

# ── Configuration ─────────────────────────────────────────────────
PG_CONN_STRING="${ConnectionStrings__NpgsqlConnection:-}"

if [ -n "$PG_CONN_STRING" ]; then
    PG_HOST=$(echo "$PG_CONN_STRING" | grep -oP 'Host=\K[^;]+' || echo "${PGHOST:-localhost}")
    PG_PORT=$(echo "$PG_CONN_STRING" | grep -oP 'Port=\K[^;]+' || echo "${PGPORT:-5432}")
    PG_USER=$(echo "$PG_CONN_STRING" | grep -oP 'Username=\K[^;]+' || echo "${PGUSER:-fishmmo}")
    PG_PASSWORD=$(echo "$PG_CONN_STRING" | grep -oP 'Password=\K[^;]+' || echo "${PGPASSWORD:-}")
    DEFAULT_DB=$(echo "$PG_CONN_STRING" | grep -oP 'Database=\K[^;]+' || echo "${PGDATABASE:-fishmmo}")
else
    PG_HOST="${PGHOST:-localhost}"
    PG_PORT="${PGPORT:-5432}"
    PG_USER="${PGUSER:-fishmmo}"
    PG_PASSWORD="${PGPASSWORD:-}"
    DEFAULT_DB="${PGDATABASE:-fishmmo}"
fi

if [ -z "$PG_PASSWORD" ]; then
    log_error "No database password found. Set PGPASSWORD or include Password= in ConnectionStrings__NpgsqlConnection."
    exit 4
fi

TARGET_DB="${TARGET_DB:-$DEFAULT_DB}"

log_info "Restore target:  $PG_HOST:$PG_PORT/$TARGET_DB"
log_info "Restore user:    $PG_USER"
log_info "Backup file:     $BACKUP_FILE"

# ── Confirmation ──────────────────────────────────────────────────
if [ -t 0 ]; then
    echo ""
    echo "WARNING: This will DROP and recreate database '$TARGET_DB' on $PG_HOST:$PG_PORT."
    echo "All existing data in '$TARGET_DB' will be lost."
    echo ""
    read -r -p "Are you sure you want to proceed? (yes/NO): " CONFIRM
    if [ "$CONFIRM" != "yes" ]; then
        log_info "Restore cancelled by user."
        exit 0
    fi
fi

# ── Perform Restore ───────────────────────────────────────────────
export PGPASSWORD="$PG_PASSWORD"

# Step 1: Terminate existing connections and drop/recreate the database
log_info "Terminating connections to '$TARGET_DB'..."
psql \
    --host="$PG_HOST" \
    --port="$PG_PORT" \
    --username="$PG_USER" \
    --dbname="postgres" \
    --no-password \
    --echo-errors \
    -c "SELECT pg_terminate_backend(pg_stat_activity.pid)
        FROM pg_stat_activity
        WHERE pg_stat_activity.datname = '$TARGET_DB'
          AND pid <> pg_backend_pid();" >/dev/null 2>&1 || true

log_info "Dropping database '$TARGET_DB'..."
psql \
    --host="$PG_HOST" \
    --port="$PG_PORT" \
    --username="$PG_USER" \
    --dbname="postgres" \
    --no-password \
    --echo-errors \
    -c "DROP DATABASE IF EXISTS \"$TARGET_DB\";"

log_info "Creating database '$TARGET_DB'..."
psql \
    --host="$PG_HOST" \
    --port="$PG_PORT" \
    --username="$PG_USER" \
    --dbname="postgres" \
    --no-password \
    --echo-errors \
    -c "CREATE DATABASE \"$TARGET_DB\";"

# Step 2: Restore from the custom-format dump
log_info "Starting pg_restore from: $BACKUP_FILE"
if ! pg_restore \
    --host="$PG_HOST" \
    --port="$PG_PORT" \
    --username="$PG_USER" \
    --dbname="$TARGET_DB" \
    --format=custom \
    --verbose \
    --no-password \
    --clean \
    --if-exists \
    --exit-on-error \
    "$BACKUP_FILE"; then
    log_error "pg_restore failed. The target database may be in an incomplete state."
    unset PGPASSWORD
    exit 3
fi

unset PGPASSWORD

log_info "Database restore completed successfully."
log_info "=== Restore finished: $TARGET_DB restored from $BACKUP_FILE ==="
