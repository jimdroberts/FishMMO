#!/usr/bin/env bash
#
# Runs the FishMMO Control Panel on localhost, against the real database.
#
# Credentials are never passed on the command line and never printed. The panel resolves
# them itself the way every FishMMO process does: the FISHMMO_DB_* environment variables
# first, then /etc/fishmmo/db-secrets.env. This script only checks that one of those will
# work before starting, so a missing secret is a clear message rather than a stack trace.
#
# THE PANEL IS SERVED FROM A PUBLISH, NOT FROM bin/. The web assets are mapped back to the
# source tree only in Development; a Production host started from bin/ answers 404 for
# every page with one easily-missed warning. Publishing makes both environments behave the
# same, which is the point of testing locally at all.
#
# Kestrel binds loopback only, so nothing here is reachable from another machine.
#
# Usage:
#   ./run-local.sh                      start on http://localhost:8100 (Development)
#   ./run-local.sh --port 9000          start on another port
#   ./run-local.sh --env Production     start the way a real deployment runs
#   ./run-local.sh --migrate            apply pending migrations, then start
#   ./run-local.sh --check              run the preflight checks and exit
#   ./run-local.sh --grant-admin NAME   make an existing account an Admin, then exit
#   ./run-local.sh --no-build           skip the publish and run what is already there
#
set -euo pipefail

readonly SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
readonly REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
readonly PROJECT="$SCRIPT_DIR/ControlPanel/ControlPanel.csproj"
readonly MIGRATOR="$REPO_ROOT/FishMMO-Database/FishMMO-DB-Migrator"
readonly PUBLISH_DIR="$SCRIPT_DIR/ControlPanel/bin/Release/net8.0/publish"
readonly SECRETS_FILE="${FISHMMO_SECRETS_FILE:-/etc/fishmmo/db-secrets.env}"

PORT=8100
ENVIRONMENT=Development
DO_BUILD=1
DO_MIGRATE=0
CHECK_ONLY=0
GRANT_ADMIN=""

# ── Output ───────────────────────────────────────────────────────────────────
# Colour only when stdout is a terminal, so piping to a file stays readable.
if [ -t 1 ]; then
	C_OK=$'\033[32m'; C_WARN=$'\033[33m'; C_ERR=$'\033[31m'; C_DIM=$'\033[2m'; C_OFF=$'\033[0m'
else
	C_OK=""; C_WARN=""; C_ERR=""; C_DIM=""; C_OFF=""
fi
ok()   { printf '%s  ok%s  %s\n' "$C_OK" "$C_OFF" "$1"; }
warn() { printf '%swarn%s  %s\n' "$C_WARN" "$C_OFF" "$1"; }
die()  { printf '%sfail%s  %s\n' "$C_ERR" "$C_OFF" "$1" >&2; exit 1; }
note() { printf '%s      %s%s\n' "$C_DIM" "$1" "$C_OFF"; }

usage() { sed -n '2,24p' "${BASH_SOURCE[0]}" | sed 's/^# \{0,1\}//'; exit 0; }

# ── Arguments ────────────────────────────────────────────────────────────────
while [ $# -gt 0 ]; do
	case "$1" in
		--port)        PORT="${2:-}"; shift 2 ;;
		--env)         ENVIRONMENT="${2:-}"; shift 2 ;;
		--migrate)     DO_MIGRATE=1; shift ;;
		--check)       CHECK_ONLY=1; shift ;;
		--grant-admin) GRANT_ADMIN="${2:-}"; shift 2 ;;
		--no-build)    DO_BUILD=0; shift ;;
		-h|--help)     usage ;;
		*)             die "Unknown option '$1'. Try --help." ;;
	esac
done

[[ "$PORT" =~ ^[0-9]+$ ]] && [ "$PORT" -ge 1 ] && [ "$PORT" -le 65535 ] \
	|| die "--port must be a TCP port, got '$PORT'."
case "$ENVIRONMENT" in
	Development|Production) ;;
	*) die "--env must be Development or Production, got '$ENVIRONMENT'." ;;
esac

# ── Preflight ────────────────────────────────────────────────────────────────
echo "Preflight"

command -v dotnet >/dev/null || die "dotnet is not on PATH."
ok "dotnet $(dotnet --version)"

[ -f "$PROJECT" ] || die "Cannot find the Control Panel project at $PROJECT."

# Credentials. Either the environment already carries them, or the secrets file must be
# readable by this user. The file is chmod 600 and owned by the account the panel runs as,
# so "exists but unreadable" is the common mistake and deserves its own message.
CREDS_FROM="environment"
if [ -z "${FISHMMO_DB_USERNAME:-}" ] || [ -z "${FISHMMO_DB_PASSWORD:-}" ]; then
	CREDS_FROM="$SECRETS_FILE"
	[ -e "$SECRETS_FILE" ] || die "No FISHMMO_DB_USERNAME/PASSWORD in the environment and $SECRETS_FILE does not exist.
      The installer writes that file. To point elsewhere, set FISHMMO_SECRETS_FILE."
	[ -r "$SECRETS_FILE" ] || die "$SECRETS_FILE exists but this user cannot read it.
      It is chmod 600 and owned by the account the panel runs as. Run as that user."
	grep -q '^FISHMMO_DB_PASSWORD=' "$SECRETS_FILE" \
		|| die "$SECRETS_FILE has no FISHMMO_DB_PASSWORD line."
fi
ok "database credentials resolve from $CREDS_FROM"

# Read connection details for the checks below. Values stay inside this function and the
# password is only ever handed to psql through PGPASSWORD, never echoed or put in argv.
load_db_env() {
	if [ -r "$SECRETS_FILE" ]; then
		set -a; . "$SECRETS_FILE"; set +a
	fi
	DB_HOST="${FISHMMO_DB_HOST:-127.0.0.1}"
	DB_PORT="${FISHMMO_DB_PORT:-5432}"
	DB_NAME="${FISHMMO_DB_NAME:-fishmmo}"
	DB_USER="${FISHMMO_DB_USERNAME:-}"
}

# One query, returning a single unaligned value. Any failure is the caller's to interpret.
psql_value() {
	PGPASSWORD="$FISHMMO_DB_PASSWORD" psql -h "$DB_HOST" -p "$DB_PORT" -U "$DB_USER" \
		-d "$DB_NAME" -qtAX -c "$1" 2>/dev/null
}

HAVE_PSQL=0
command -v psql >/dev/null && HAVE_PSQL=1

if [ "$HAVE_PSQL" -eq 1 ]; then
	load_db_env
	if [ -z "$(psql_value 'SELECT 1')" ]; then
		die "Cannot reach the database at $DB_HOST:$DB_PORT/$DB_NAME as '$DB_USER'.
      Check that PostgreSQL is running and that the credentials are current."
	fi
	ok "database reachable at $DB_HOST:$DB_PORT/$DB_NAME as '$DB_USER'"

	# The panel opens every page against these. A missing one means the migration chain has
	# not been applied here, which otherwise shows up as a 500 on whichever page is opened
	# first rather than as a clear answer now.
	MISSING=""
	for t in accounts web_sessions admin_audit_log support_tickets world_servers \
	         daemon_hosts daemon_app_events maintenance_operations deployment_secrets; do
		[ "$(psql_value "SELECT to_regclass('public.$t') IS NOT NULL")" = "t" ] || MISSING="$MISSING $t"
	done
	if [ -n "$MISSING" ]; then
		if [ "$DO_MIGRATE" -eq 1 ]; then
			warn "schema incomplete, missing:$MISSING"
			note "--migrate was given, so these will be created below."
		else
			die "The database is missing these tables:$MISSING
      Re-run with --migrate to apply the pending migrations."
		fi
	else
		ok "schema is applied"
	fi

	# Without this row two-factor cannot be verified. Development logs a warning and carries
	# on; Production refuses to start, so say which one is about to happen.
	if [ "$(psql_value "SELECT count(*) FROM deployment_secrets WHERE key = 'totp_master_kek'")" = "1" ]; then
		ok "two-factor master key is present"
	elif [ "$ENVIRONMENT" = "Production" ]; then
		die "deployment_secrets has no 'totp_master_kek' row, and the panel refuses to start in Production without it.
      The installer creates it. Do not invent one: it encrypts every enrolled two-factor
      secret, so a new key makes every existing authenticator permanently unusable."
	else
		warn "no 'totp_master_kek' row — two-factor cannot be verified, so sign-in will not complete"
	fi
else
	warn "psql not found, skipping the database checks"
	note "The panel will still resolve its own credentials; failures will surface at startup."
fi

# Nothing else may hold the port, or the panel exits with an address-in-use error that
# reads like a fault in the panel. Skipped for --grant-admin, which never binds anything:
# refusing it because a panel is already running would be nonsense, and that panel is
# usually the very one the account is being granted access to.
if [ -z "$GRANT_ADMIN" ]; then
	if command -v ss >/dev/null && ss -lntH "sport = :$PORT" 2>/dev/null | grep -q .; then
		die "Port $PORT is already in use. Pick another with --port, or stop what is holding it."
	fi
	ok "port $PORT is free"
fi

if [ "$ENVIRONMENT" = "Production" ] && [ -z "$GRANT_ADMIN" ]; then
	warn "Production mode: account auto-verification is OFF"
	note "A new account stays unverified until the email queue delivers its link. For"
	note "testing a fresh sign-up, use Development instead."
fi

# ── --grant-admin ────────────────────────────────────────────────────────────
# Access level is a database column with no panel route that can raise the first operator,
# which is deliberate: the panel must never be able to mint its own first administrator.
# This is the documented way to bootstrap one, and it is opt-in per invocation.
if [ -n "$GRANT_ADMIN" ]; then
	[ "$HAVE_PSQL" -eq 1 ] || die "--grant-admin needs psql, which is not installed."
	EXISTS="$(psql_value "SELECT count(*) FROM accounts WHERE name = '${GRANT_ADMIN//\'/\'\'}'")"
	[ "$EXISTS" = "1" ] || die "No account named '$GRANT_ADMIN'. Register it in the panel first, then run this again."
	psql_value "UPDATE accounts SET access_level = 3 WHERE name = '${GRANT_ADMIN//\'/\'\'}'" >/dev/null
	LEVEL="$(psql_value "SELECT access_level FROM accounts WHERE name = '${GRANT_ADMIN//\'/\'\'}'")"
	[ "$LEVEL" = "3" ] || die "The update did not take; access_level is still '$LEVEL'."
	ok "'$GRANT_ADMIN' is now an Admin (access level 3)"
	note "This is a direct database write and is NOT in the panel's audit log, because no"
	note "operator performed it. Every grant made through the panel itself is recorded."
	exit 0
fi

if [ "$CHECK_ONLY" -eq 1 ]; then
	echo
	ok "preflight passed; --check requested, so not starting"
	exit 0
fi

# ── Migrate ──────────────────────────────────────────────────────────────────
# The migrator reads credentials from the ENVIRONMENT only; unlike the panel it does not
# open the secrets file itself. Sourcing happens in a subshell so the password never
# enters this script's own environment.
if [ "$DO_MIGRATE" -eq 1 ]; then
	echo
	echo "Applying migrations"
	[ -d "$MIGRATOR" ] || die "Cannot find the migrator at $MIGRATOR."
	(
		set -a
		[ -r "$SECRETS_FILE" ] && . "$SECRETS_FILE"
		set +a
		dotnet run --project "$MIGRATOR" -c Release --nologo
	) || die "Migration failed. The panel was not started."
	ok "migrations applied"
fi

# ── Build ────────────────────────────────────────────────────────────────────
if [ "$DO_BUILD" -eq 1 ]; then
	echo
	echo "Publishing"
	dotnet publish "$PROJECT" -c Release --nologo -v quiet \
		|| die "The publish failed. Nothing was started."
	ok "published to $PUBLISH_DIR"
else
	[ -f "$PUBLISH_DIR/FishMMO-ControlPanel.dll" ] \
		|| die "--no-build was given but there is no publish at $PUBLISH_DIR. Run once without it."
	ok "using the existing publish (--no-build)"
fi

[ -d "$PUBLISH_DIR/wwwroot" ] || die "The publish has no wwwroot; the panel would serve no pages."

# ── Run ──────────────────────────────────────────────────────────────────────
echo
echo "Starting the Control Panel"
note "environment  $ENVIRONMENT"
note "address      http://localhost:$PORT  (loopback only)"
note "database     resolved by the panel from $CREDS_FROM"
echo
printf '%sPress Ctrl-C to stop.%s\n\n' "$C_DIM" "$C_OFF"

# AllowedHosts is overridden because the bundled Production settings name the real panel
# hostname, which would reject every request to localhost with a bare 400.
export FISHMMO_ENVIRONMENT="$ENVIRONMENT"
export WebServer__HttpPort="$PORT"
export AllowedHosts="localhost"

# In Production the panel refuses to start unless the reverse-proxy trust is configured,
# so that a deployment cannot silently believe attacker-supplied X-Forwarded-For headers.
# Here there is no proxy and Kestrel is on loopback, so trusting only loopback is both the
# truth and the safe answer.
if [ "$ENVIRONMENT" = "Production" ]; then
	export ForwardedHeaders__AllowUnconfigured=true
fi

# exec so Ctrl-C reaches the panel directly and it shuts down the way systemd would stop it.
cd "$PUBLISH_DIR"
exec dotnet FishMMO-ControlPanel.dll
