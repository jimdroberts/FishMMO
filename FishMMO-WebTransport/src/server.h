/**
 * @file server.h
 * @brief WebTransport QUIC server — listens for QUIC connections
 *        and manages WebTransport sessions.
 */

#ifndef WEBTRANSPORT_SERVER_H
#define WEBTRANSPORT_SERVER_H

#include "webtransport_internal.h"
#include "session.h"
#include "datagram_queue.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ── Per-connection context on the server ───────────────────── */

typedef struct wt_server_conn_s {
    wt_connection_id_t      id;             /* unique within this server */
    HQUIC                   quic_conn;      /* msquic connection handle */
    wt_session_t*           session;        /* WebTransport session */
    wt_connection_state_t   state;
    char                    remote_addr[WT_MAX_ADDRESS_LENGTH];
    atomic_bool             in_use;
} wt_server_conn_t;

/* ── Server structure ───────────────────────────────────────── */

typedef struct wt_server_s {
    /* msquic handles */
    HQUIC                   registration;
    HQUIC                   session_config; /* server-wide QUIC config */
    HQUIC                   listener;

    /* Callbacks */
    wt_server_callbacks_t   callbacks;
    void*                   user_context;

    /* Configuration */
    char                    alpn[WT_MAX_ALPN_LENGTH];
    char                    bind_address[WT_MAX_ADDRESS_LENGTH];
    uint16_t                port;
    uint32_t                max_clients;
    char                    cert_path[512];
    char                    key_path[512];

    /* TLS — QUIC always requires TLS 1.3 */
    QUIC_CREDENTIAL_FLAGS   cred_flags;
    void*                   cert_credential;  /* QUIC_CERTIFICATE* */

    /* State */
    atomic_int              state;          /* wt_server_state_t */

    /* Connections — fixed-size array, indexed by connection_id */
    wt_server_conn_t*       connections;    /* [max_clients] */
    uint32_t                connection_count;

    /* Datagram queue shared across all connections */
    wt_datagram_queue_t     dgram_queue;
} wt_server_s;

/* ── API ────────────────────────────────────────────────────── */

/**
 * Allocate and initialise a server.  Does NOT start listening.
 * Call wt_server_start() to begin accepting connections.
 */
WT_SERVER wt_server_alloc(
    const char*                 certificate_path,
    const char*                 private_key_path,
    const char*                 alpn,
    const char*                 bind_address,
    uint16_t                    port,
    uint32_t                    max_clients,
    const wt_server_callbacks_t* callbacks,
    void*                       context);

void wt_server_free(WT_SERVER server);
int32_t wt_server_start(WT_SERVER server);
void wt_server_stop(WT_SERVER server);
void wt_server_poll(WT_SERVER server, int32_t timeout_us);

int32_t wt_server_send_stream(
    WT_SERVER server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length);

int32_t wt_server_send_datagram(
    WT_SERVER server, wt_connection_id_t conn_id,
    const uint8_t* data, int32_t length);

void wt_server_disconnect(WT_SERVER server, wt_connection_id_t conn_id);

const char* wt_server_get_client_addr(
    WT_SERVER server, wt_connection_id_t conn_id);

int32_t wt_server_get_client_count(WT_SERVER server);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_SERVER_H */
