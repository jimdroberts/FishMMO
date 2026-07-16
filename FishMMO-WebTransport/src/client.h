/**
 * @file client.h
 * @brief WebTransport QUIC client — connects to a server
 *        and manages a single WebTransport session.
 */

#ifndef WEBTRANSPORT_CLIENT_H
#define WEBTRANSPORT_CLIENT_H

#include "webtransport_internal.h"
#include "session.h"
#include "datagram_queue.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ── Client structure ───────────────────────────────────────── */

typedef struct wt_client_s {
    /* msquic handles */
    HQUIC                   registration;
    HQUIC                   session_config;
    HQUIC                   quic_conn;      /* single connection */

    /* Callbacks */
    wt_client_callbacks_t   callbacks;
    void*                   user_context;

    /* Configuration */
    char                    server_name[256];  /* SNI hostname */
    char                    address[256];
    uint16_t                port;
    bool                    use_tls;

    /* State */
    atomic_int              state;
    atomic_bool             connected;

    /* Session (one per client) */
    wt_session_t*           session;

    /* Datagram queue */
    wt_datagram_queue_t     dgram_queue;
} wt_client_s;

/* ── API ────────────────────────────────────────────────────── */

WT_CLIENT wt_client_alloc(
    const wt_client_callbacks_t* callbacks,
    void*                       context);

void wt_client_free(WT_CLIENT client);

int32_t wt_client_connect(
    WT_CLIENT client,
    const char* server_name,
    const char* address,
    uint16_t port,
    bool use_tls);

void wt_client_disconnect(WT_CLIENT client);
void wt_client_poll(WT_CLIENT client, int32_t timeout_us);

int32_t wt_client_send_stream(
    WT_CLIENT client,
    const uint8_t* data, int32_t length);

int32_t wt_client_send_datagram(
    WT_CLIENT client,
    const uint8_t* data, int32_t length);

bool wt_client_is_connected(WT_CLIENT client);
int32_t wt_client_get_mtu(WT_CLIENT client);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_CLIENT_H */
