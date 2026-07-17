/**
 * @file stream_manager.h
 * @brief Manages QUIC streams for reliable data (FishNet channel 0).
 */

#ifndef WEBTRANSPORT_STREAM_MANAGER_H
#define WEBTRANSPORT_STREAM_MANAGER_H

#include "webtransport_internal.h"

#ifdef __cplusplus
extern "C" {
#endif

typedef struct wt_stream_entry_s {
    wt_stream_id_t  id;
    HQUIC           quic_stream;
    bool            in_use;
    bool            send_closed;
    bool            recv_closed;
} wt_stream_entry_t;

typedef struct wt_stream_manager_s {
    wt_stream_entry_t   streams[WT_MAX_STREAMS];
    uint32_t            next_id;
    HQUIC               quic_conn;
    atomic_uint         active_streams;
    atomic_bool         shutting_down;

    void (*on_stream_data)(void* ctx, wt_connection_id_t conn_id,
                           wt_stream_id_t stream_id,
                           const uint8_t* data, int32_t length);
    void*               callback_ctx;
    wt_connection_id_t  conn_id;

    /* Called when active_streams reaches 0 during shutdown.
     * The session uses this to defer free(mgr) until all streams are done. */
    void (*on_all_streams_done)(void* ctx);
    void*               done_ctx;
    atomic_bool         streams_done_flag;
    atomic_bool         shutdown_complete;
    atomic_bool         freed;              /* CAS gate — exactly one path frees */

    /* Per-connection total receive buffer tracking.
     * Prevents a single peer from exhausting memory by opening
     * many streams without sending FIN. */
    atomic_uint         total_recv_bytes;
#define WT_MAX_TOTAL_RECV_BUF  (16 * 1024 * 1024)  /* 16 MB per connection */
} wt_stream_manager_t;

void wt_stream_manager_init(
    wt_stream_manager_t* mgr, HQUIC quic_conn, wt_connection_id_t conn_id,
    void (*on_stream_data)(void* ctx, wt_connection_id_t conn_id,
                           wt_stream_id_t stream_id,
                           const uint8_t* data, int32_t length),
    void* callback_ctx);

void wt_stream_manager_shutdown(wt_stream_manager_t* mgr);

int32_t wt_stream_manager_send(
    wt_stream_manager_t* mgr, const uint8_t* data, int32_t length);

void wt_stream_manager_accept_stream(
    wt_stream_manager_t* mgr, HQUIC quic_stream);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_STREAM_MANAGER_H */