/**
 * @file session.h
 * @brief WebTransport session — manages QUIC streams (reliable)
 *        and datagrams (unreliable) for a single connection.
 */

#ifndef WEBTRANSPORT_SESSION_H
#define WEBTRANSPORT_SESSION_H

#include "webtransport_internal.h"
#include "stream_manager.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct wt_session_s {
    HQUIC                   quic_conn;
    wt_stream_manager_t*    stream_mgr;

    /* Parent reference (tagged union) */
    int parent_type;  /* 0 = WT_PARENT_SERVER, 1 = WT_PARENT_CLIENT */
#define WT_PARENT_SERVER  0
#define WT_PARENT_CLIENT  1
    union {
        struct wt_server_s* server;
        struct wt_client_s* client;
    } parent;

    wt_connection_id_t      conn_id;
} wt_session_t;

/* ── API ────────────────────────────────────────────────────── */

int32_t wt_session_init(
    wt_session_t* session, HQUIC quic_conn, wt_connection_id_t conn_id);

void wt_session_shutdown(wt_session_t* session);

/** Wire stream manager callbacks to the parent server/client.
 *  Must be called AFTER parent_type and parent union are set. */
void wt_session_wire_callbacks(wt_session_t* session);

int32_t wt_session_send_stream(
    wt_session_t* session, const uint8_t* data, int32_t length);

int32_t wt_session_send_datagram(
    wt_session_t* session, const uint8_t* data, int32_t length);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_SESSION_H */