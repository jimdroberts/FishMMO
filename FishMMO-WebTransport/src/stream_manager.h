/**
 * @file stream_manager.h
 * @brief Manages QUIC bidirectional streams for reliable data.
 *
 * WebTransport bidirectional streams map to FishNet's reliable
 * channel (channel 0). Each send creates a new stream.
 * Incoming streams are accepted and their data is delivered
 * via a callback.
 *
 * Streams are short-lived: open → send data → close.  This
 * avoids the complexity of persistent stream management while
 * keeping send latency low.
 */

#ifndef WEBTRANSPORT_STREAM_MANAGER_H
#define WEBTRANSPORT_STREAM_MANAGER_H

#include "webtransport_internal.h"
#include <stdatomic.h>

#ifdef __cplusplus
extern "C" {
#endif

#define WT_MAX_STREAMS            1024
#define WT_STREAM_RECV_BUF_SIZE   (64 * 1024)

/* ── Stream entry ───────────────────────────────────────────── */

typedef struct wt_stream_entry_s {
    wt_stream_id_t      id;
    HQUIC               quic_stream;
    bool                in_use;
    bool                send_closed;    /* we've closed our send side */
    bool                recv_closed;    /* peer has closed their send side */
} wt_stream_entry_t;

/* ── Stream manager ─────────────────────────────────────────── */

typedef struct wt_stream_manager_s {
    wt_stream_entry_t   streams[WT_MAX_STREAMS];
    atomic_uint         next_id;        /* monotonic stream ID counter */
    HQUIC               quic_conn;      /* parent QUIC connection */

    /* Incoming stream data callback */
    void (*on_stream_data)(void* ctx, wt_connection_id_t conn_id,
                           wt_stream_id_t stream_id,
                           const uint8_t* data, int32_t length);
    void*               callback_ctx;
    wt_connection_id_t  conn_id;
} wt_stream_manager_t;

/* ── API ────────────────────────────────────────────────────── */

void wt_stream_manager_init(
    wt_stream_manager_t* mgr,
    HQUIC               quic_conn,
    wt_connection_id_t  conn_id,
    void (*on_stream_data)(void* ctx, wt_connection_id_t conn_id,
                           wt_stream_id_t stream_id,
                           const uint8_t* data, int32_t length),
    void*               callback_ctx);

void wt_stream_manager_shutdown(wt_stream_manager_t* mgr);

/**
 * Create a new bidirectional stream, send data, and close the send side.
 * @return WT_OK on success, negative error on failure.
 */
int32_t wt_stream_manager_send(
    wt_stream_manager_t* mgr,
    const uint8_t*       data,
    int32_t              length);

/**
 * Handle an incoming stream event from msquic.
 * Called from QUIC callback when a peer opens a new stream.
 */
void wt_stream_manager_accept_stream(
    wt_stream_manager_t* mgr,
    HQUIC                quic_stream);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_STREAM_MANAGER_H */
