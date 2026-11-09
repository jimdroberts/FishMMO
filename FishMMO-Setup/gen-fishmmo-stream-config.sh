#!/usr/bin/env bash
# Generates one NGINX stream (Layer 4 UDP) server block per FishMMO game port.
#
# nginx.conf's stream {} block includes $STREAM_DIR/*.conf. Every Login, World and Scene server
# port that clients may reach needs a block here, or NGINX never listens on it and the QUIC
# handshake times out with nothing in any log. NGINX forwards the raw QUIC datagrams untouched;
# each game server terminates its own TLS with the certificate in its .cfg.
#
# Usage (root): gen-fishmmo-stream-config.sh
# The FishMMO-Installer installs this as /usr/local/bin/gen-fishmmo-stream-config.sh and runs it
# from Web Server > Deploy FishMMO nginx.conf.
#
# Environment (all optional):
#   STREAM_DIR     /etc/nginx/stream.d   output directory, included by nginx.conf
#   BACKEND_IP     127.0.0.1             where the game servers listen (their .cfg Address)
#   LISTEN_IP      auto                  the address NGINX listens on. auto = this host's IPv4
#                                        source address for the default route (the LAN address
#                                        the router forwards to), which therefore must not change:
#                                        give the host a static address or DHCP reservation.
#                                        NGINX must NOT listen on 0.0.0.0 while the game servers
#                                        are on this host: the game servers use the same ports on
#                                        127.0.0.1, and 0.0.0.0:PORT includes 127.0.0.1:PORT, so
#                                        NGINX would forward every datagram back to itself in a
#                                        loop ("worker_connections are not enough") and the game
#                                        servers could not bind at all. 0.0.0.0 is accepted only
#                                        for a BACKEND_IP on another machine.
#   LOGIN_START    7770  LOGIN_END 7779
#   WORLD_START    7780  WORLD_END 7789
#   SCENE_START    7790  SCENE_END 7999
#   PROXY_TIMEOUT  130s                  idle time before NGINX forgets a client's UDP session.
#                                        Must outlive the QUIC idle timeout (120 s, see
#                                        WT_DEFAULT_IDLE_TIMEOUT_MS): a shorter value drops the
#                                        mapping of a quiet but live connection (a player idling
#                                        at character select), after which the server's packets
#                                        go nowhere until the client happens to send again.
#   MAX_SESSIONS_PER_IP 32               UDP sessions one client address may hold on one port; 0 turns
#                                        the limit off. NGINX is the only layer that sees the real
#                                        address before the QUIC handshake. A session outlives its
#                                        connection by PROXY_TIMEOUT, so this is set above the 16 live
#                                        connections per real IP the game servers allow, or a player
#                                        reconnecting a few times would be refused.
#   ENABLE_IPV6    0                     1 also listens on [::]:PORT
#   NO_RELOAD      0                     1 validates but does not reload NGINX
#
# The new files are written to a temporary directory, swapped in, and tested with nginx -t. If the
# test fails the previous files are restored, so a bad run never leaves NGINX unloadable.
set -euo pipefail

STREAM_DIR=${STREAM_DIR:-/etc/nginx/stream.d}
BACKEND_IP=${BACKEND_IP:-127.0.0.1}
LISTEN_IP=${LISTEN_IP:-auto}
LOGIN_START=${LOGIN_START:-7770}; LOGIN_END=${LOGIN_END:-7779}
WORLD_START=${WORLD_START:-7780}; WORLD_END=${WORLD_END:-7789}
SCENE_START=${SCENE_START:-7790}; SCENE_END=${SCENE_END:-7999}
PROXY_TIMEOUT=${PROXY_TIMEOUT:-130s}
MAX_SESSIONS_PER_IP=${MAX_SESSIONS_PER_IP:-32}
ENABLE_IPV6=${ENABLE_IPV6:-0}
NO_RELOAD=${NO_RELOAD:-0}

die() { echo "gen-fishmmo-stream-config: $*" >&2; exit 1; }

[[ $EUID -eq 0 ]] || die "run as root (it writes $STREAM_DIR and reloads nginx)"
command -v nginx >/dev/null || die "nginx is not installed"
[[ $BACKEND_IP =~ ^[0-9A-Fa-f:.]+$ ]] || die "BACKEND_IP '$BACKEND_IP' is not an IP address"
[[ $PROXY_TIMEOUT =~ ^[0-9]+(ms|s|m|h)?$ ]] || die "PROXY_TIMEOUT '$PROXY_TIMEOUT' is not an nginx time"
[[ $MAX_SESSIONS_PER_IP =~ ^[0-9]+$ ]] || die "MAX_SESSIONS_PER_IP '$MAX_SESSIONS_PER_IP' is not a number"

check_range() {
    local name=$1 start=$2 end=$3
    [[ $start =~ ^[0-9]+$ && $end =~ ^[0-9]+$ ]] || die "$name range must be numeric"
    (( start >= 1 && end <= 65535 && start <= end )) || die "$name range $start-$end is invalid"
}
check_range LOGIN "$LOGIN_START" "$LOGIN_END"
check_range WORLD "$WORLD_START" "$WORLD_END"
check_range SCENE "$SCENE_START" "$SCENE_END"

# An IPv6 backend must be bracketed in proxy_pass.
backend=$BACKEND_IP
if [[ $backend == *:* ]]; then backend="[$backend]"; fi

if [[ $LISTEN_IP == auto ]]; then
    LISTEN_IP=$(ip -4 route get 1.1.1.1 2>/dev/null | sed -n 's/.* src \([0-9.]*\).*/\1/p' | head -n 1)
    [[ -n $LISTEN_IP ]] || die "could not find this host's IPv4 address on the default route; set LISTEN_IP"
fi
[[ $LISTEN_IP =~ ^[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+$ ]] || die "LISTEN_IP '$LISTEN_IP' is not an IPv4 address"
# A backend on this host: loopback, or one of this host's own addresses.
backend_is_local=0
if [[ $BACKEND_IP == 127.* || $BACKEND_IP == ::1 ]] || ip -o addr show 2>/dev/null | grep -qw -- "$BACKEND_IP"; then
    backend_is_local=1
fi
if [[ $LISTEN_IP == "$BACKEND_IP" ]]; then
    die "LISTEN_IP and BACKEND_IP are both $LISTEN_IP: NGINX would forward to itself"
fi
if [[ $LISTEN_IP == 0.0.0.0 && $backend_is_local == 1 ]]; then
    die "LISTEN_IP 0.0.0.0 includes the local backend $BACKEND_IP on the same ports, so NGINX would forward to itself; use this host's LAN address"
fi
if [[ $LISTEN_IP == 127.* ]]; then
    die "LISTEN_IP $LISTEN_IP is loopback; the router cannot forward to it"
fi
if [[ $ENABLE_IPV6 == 1 && $BACKEND_IP == *:* && $backend_is_local == 1 ]]; then
    die "ENABLE_IPV6 listens on [::], which includes the IPv6 backend $BACKEND_IP on the same ports"
fi
listen_address=$LISTEN_IP
if [[ $listen_address == 0.0.0.0 ]]; then listen_address=""; else listen_address="$listen_address:"; fi

parent=$(dirname "$STREAM_DIR")
mkdir -p "$parent"
staging=$(mktemp -d "$parent/.stream.d.new.XXXXXX")
previous=""
cleanup() { if [[ -n $staging && -d $staging ]]; then rm -rf "$staging"; fi; }
trap cleanup EXIT

write_range() {
    local role=$1 start=$2 end=$3 port
    for (( port = start; port <= end; port++ )); do
        {
            echo "# FishMMO $role server, port $port. Generated by gen-fishmmo-stream-config.sh; do not edit."
            echo "server {"
            # reuseport hashes each client's address to one worker, so every datagram of a QUIC
            # connection leaves through the same upstream socket. Without it a second worker can
            # pick up the session and the game server sees the client change source port.
            echo "    listen $listen_address$port udp reuseport;"
            if [[ $ENABLE_IPV6 == 1 ]]; then
                echo "    listen [::]:$port udp reuseport;"
            fi
            echo "    proxy_pass $backend:$port;"
            echo "    proxy_timeout $PROXY_TIMEOUT;"
            echo "    proxy_upload_rate 100m;"
            echo "    proxy_download_rate 100m;"
            if (( MAX_SESSIONS_PER_IP > 0 )); then
                echo "    limit_conn game_udp_per_ip $MAX_SESSIONS_PER_IP;"
            fi
            echo "}"
        } > "$staging/$port.conf"
    done
}
if (( MAX_SESSIONS_PER_IP > 0 )); then
    {
        echo "# Counts each client address separately on each port. Generated by gen-fishmmo-stream-config.sh; do not edit."
        echo 'limit_conn_zone $binary_remote_addr$server_port zone=game_udp_per_ip:10m;'
    } > "$staging/00-limits.conf"
fi
write_range login "$LOGIN_START" "$LOGIN_END"
write_range world "$WORLD_START" "$WORLD_END"
write_range scene "$SCENE_START" "$SCENE_END"
chmod 0755 "$staging"
chmod 0644 "$staging"/*.conf

# Swap the directories, then test the live configuration that now includes them.
if [[ -d $STREAM_DIR ]]; then
    previous=$(mktemp -d "$parent/.stream.d.old.XXXXXX")
    rmdir "$previous"
    mv "$STREAM_DIR" "$previous"
fi
mv "$staging" "$STREAM_DIR"
staging=""

if ! nginx -t; then
    rm -rf "$STREAM_DIR"
    if [[ -n $previous ]]; then mv "$previous" "$STREAM_DIR"; fi
    die "nginx -t failed; the previous $STREAM_DIR was restored"
fi
if [[ -n $previous ]]; then rm -rf "$previous"; fi

count=$(( LOGIN_END - LOGIN_START + WORLD_END - WORLD_START + SCENE_END - SCENE_START + 3 ))
echo "Wrote $count UDP stream blocks to $STREAM_DIR (listen ${LISTEN_IP}, backend $BACKEND_IP, proxy_timeout $PROXY_TIMEOUT)."

if [[ $NO_RELOAD != 1 ]] && command -v systemctl >/dev/null && systemctl is-active --quiet nginx; then
    systemctl reload nginx
    # systemctl reports success as soon as the signal is sent. If the master then fails to open
    # the new sockets (most often "Too many open files") it logs that to error.log and keeps the
    # old configuration, so confirm the first login port is actually being listened on.
    listening=0
    for _ in 1 2 3 4 5 6 7 8 9 10; do
        if [[ -n $(ss -Hulnp "sport = :$LOGIN_START" 2>/dev/null | grep nginx) ]]; then listening=1; break; fi
        sleep 0.5
    done
    if [[ $listening != 1 ]]; then
        die "NGINX did not take the new configuration: nothing listens on UDP $LOGIN_START. See /var/log/nginx/error.log (a 'Too many open files' error needs LimitNOFILE raised for nginx.service, then 'systemctl restart nginx')."
    fi
    echo "NGINX reloaded; UDP $LOGIN_START is listening."
fi
exit 0
