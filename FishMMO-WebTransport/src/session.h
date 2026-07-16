/**
 * @file session.h
 * @brief WebTransport session — manages QUIC streams and
 *        bridges them to the datagram queue for a single connection.
 *
 * Each connected client (server-side) or the single connection
 * (client-side) has one wt_session_t.
 *
 * Reliable data  (channel 0) → bidirectional streams
 * Unreliable data (channel 1) → QUIC DATAGRAM frames
 */

#ifndef WEBTRANSPORT_SESSION_H
#define WEBTRANSPORT_SESSION_H

#include "webtransport_internal.h"
#include "datagram_queue.h"
#include "stream_manager.h"

#ifdef __cplusplus
extern "C" {
#endif

/* ── Forward ref ────────────────────────────────────────────── */

typedef struct wt_stream_manager_s wt_stream_manager_t;

/* ── Session ────────────────────────────────────────────────── */

typedef struct wt_session_s {
    HQUIC                   quic_conn;      /* parent QUIC connection */

    /* Stream manager for reliable data */
    wt_stream_manager_t*    stream_mgr;

    /* Datagram queue from datagram_queue.h */
    wt_datagram_queue_t     dgram_queue;

    /* Reference to parent type (tagged union) */
    enum { WT_PARENT_SERVER, WT_PARENT_CLIENT } parent_type;
    union {
        struct wt_server_s* server;
        struct wt_client_s* client;
    } parent;

    wt_connection_id_t      conn_id;        /* server-side connection ID */
} wt_session_t;

/* ── API ────────────────────────────────────────────────────── */

/**
 * Initialise a session for an existing QUIC connection.
 * Must be called after QUIC handshake completes.
 */
int32_t wt_session_init(
    wt_session_t*       session,
    HQUIC               quic_conn,
    wt_connection_id_t  conn_id);

/**
 * Clean up a session — close all streams and release resources.
 */
void wt_session_shutdown(wt_session_t* session);

/**
 * Send reliable data on a new bidirectional stream.
 * Creates a new stream, sends data, and closes the send side.
 */
int32_t wt_session_send_stream(
    wt_session_t*       session,
    const uint8_t*      data,
    int32_t             length);

/**
 * Send unreliable data via QUIC DATAGRAM.
 */
int32_t wt_session_send_datagram(
    wt_session_t*       session,
    const uint8_t*      data,
    int32_t             length);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_SESSION_H */
