/**
 * @file stream_manager.h
 * @brief Manages QUIC streams for reliable data (FishNet channel 0).
 *
 * ## Locking Protocol
 *
 * ### Mutex: streams_lock
 * Protects the streams[] slot array and all per-slot fields (id, in_use,
 * quic_stream, send_closed, recv_closed). The lock is RECURSIVE so that
 * a single thread can safely re-enter locked regions (e.g. when a
 * synchronous MsQuic callback fires while the lock is held). On Linux
 * this is PTHREAD_MUTEX_RECURSIVE; on Windows a CRITICAL_SECTION is
 * wrapped with manual thread-ID + recursion-count tracking.
 *
 * ### Fields NOT protected by streams_lock:
 * - quic_conn, conn_id, on_stream_data, callback_ctx, on_all_streams_done,
 *   done_ctx: set once during init, read-only thereafter.
 * - active_streams, total_recv_bytes, streams_done_flag, shutdown_complete,
 *   freed, shutting_down: atomic variables, no lock needed for individual
 *   reads/writes. However, multi-field decisions (e.g. check active_streams
 *   and then access streams[] under the same lock) require the lock.
 *
 * ### Functions called WITH lock held:
 * - Any code reading/writing streams[].in_use, streams[].id,
 *   streams[].quic_stream, streams[].send_closed, streams[].recv_closed.
 * - wt_stream_manager_send: slot reservation and release.
 * - wt_stream_manager_accept_stream: slot reservation.
 * - stream_cb (PEER_SEND_SHUTDOWN, SHUTDOWN_COMPLETE): slot lookup and
 *   cleanup.
 *
 * ### Functions called WITHOUT lock held:
 * - MsQuic StreamOpen, StreamStart, StreamSend, StreamShutdown, StreamClose.
 * - Callbacks to user-provided on_stream_data.
 *
 * ### Lock ordering:
 * - streams_lock is a leaf lock — never acquire any other lock while
 *   holding it.
 * - Do NOT call MsQuic stream functions while holding streams_lock,
 *   because MsQuic may call back into stream_cb synchronously, which
 *   would re-acquire streams_lock (recursive mutex makes this safe).
 */

#ifndef WEBTRANSPORT_STREAM_MANAGER_H
#define WEBTRANSPORT_STREAM_MANAGER_H

#include "webtransport_internal.h"

/* Platform mutex for streams[] array synchronisation.
 * stream_manager_send runs on the application thread while
 * stream callbacks (accept, SHUTDOWN_COMPLETE) fire on QUIC
 * worker threads — the shared streams[] array needs a lock.
 * The mutex is RECURSIVE so that a synchronous MsQuic callback
 * re-entering the lock does not deadlock. */
#if defined(WT_PLATFORM_WINDOWS)
  #include <windows.h>
#else
  #include <pthread.h>
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct wt_stream_entry_s {
    wt_stream_id_t  id;
    HQUIC           quic_stream;
    bool            in_use;
    bool            send_closed;
    bool            recv_closed;
    /* True if this stream was accepted from the peer (client-opened bidi).
     * Server replies prefer these so Chrome receives data on the same
     * WebTransport stream the client already owns (readable half). */
    bool            peer_initiated;
} wt_stream_entry_t;

typedef struct wt_stream_manager_s {
    wt_stream_entry_t   streams[WT_MAX_STREAMS];
    uint64_t            next_id;        /* uint64_t — overflow impossible in practice */
    HQUIC               quic_conn;
    atomic_uint         active_streams;
    atomic_bool         shutting_down;
    /* Set when the QUIC connection is shutting down (peer/transport initiated,
     * or local disconnect). Blocks new sends and stops wt_stream_manager_shutdown
     * from issuing graceful StreamShutdowns.
     *
     * NOTE: this does NOT mean the handles are dead — it is set while the
     * connection is still valid, well before ConnectionClose. Use
     * handles_invalid for "may I still call MsQuic on these handles". */
    atomic_bool         conn_closed;

    /* Set immediately before MsQuic->ConnectionClose, which is the point where
     * every child stream handle becomes invalid; calling StreamShutdown/
     * StreamClose after it is quic_bugcheck → SIGABRT (rc=134).
     *
     * Separate from conn_closed because conflating the two leaked handles: with
     * one flag, a stream shutting down during a peer-initiated close skipped its
     * own StreamClose even though the handle was still perfectly valid, and
     * nothing else ever closed it. msquic then held the connection (and a
     * rundown reference on the registration) forever, hanging server shutdown
     * inside MsQuicRegistrationClose. */
    atomic_bool         handles_invalid;

    /* Browser WebTransport (HTTP/3): client/server-initiated data streams
     * begin with a WEBTRANSPORT_STREAM capsule (type 0x41) carrying the
     * CONNECT stream ID as session id. Native FishMMO clients send raw
     * bytes — leave use_wt_stream_header false for them. */
    bool                use_wt_stream_header;
    uint64_t            wt_session_id;  /* CONNECT stream ID when framing on */

    /* True for server-side sessions. Controls stream reuse policy in
     * wt_stream_manager_send:
     *   server: prefer peer-initiated (client-opened) streams for replies
     *   client: only reuse locally-opened streams; never write on
     *           server-initiated streams (those often hit INVALID_STATE
     *           after the peer FINs and PEER_SEND_SHUTDOWN). */
    bool                is_server;

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

    /* Mutex protecting streams[] array.
     * stream_manager_send (app thread) and stream callbacks
     * (QUIC thread) both read/write the slot array and
     * must be serialised. */
#if defined(WT_PLATFORM_WINDOWS)
    /* CRITICAL_SECTION is NOT recursive on Windows — unlike the Linux
     * PTHREAD_MUTEX_RECURSIVE, a thread that already owns the section
     * will DEADLOCK if it tries to enter again.  The manual recursion
     * tracking (streams_lock_owner + streams_lock_rec) below implements
     * the same re-entrant behaviour using thread-ID + recursion count,
     * so that a synchronous MsQuic callback re-entering the lock does
     * not deadlock.  See sm_lock_fn / sm_unlock_fn in stream_manager.cpp. */
    CRITICAL_SECTION    streams_lock_cs;
    DWORD               streams_lock_owner;
    int                 streams_lock_rec;
#else
    pthread_mutex_t     streams_lock;
#endif
} wt_stream_manager_t;

void wt_stream_manager_init(
    wt_stream_manager_t* mgr, HQUIC quic_conn, wt_connection_id_t conn_id,
    void (*on_stream_data)(void* ctx, wt_connection_id_t conn_id,
                           wt_stream_id_t stream_id,
                           const uint8_t* data, int32_t length),
    void* callback_ctx);

void wt_stream_manager_shutdown(wt_stream_manager_t* mgr);

/**
 * Mark the parent QUIC connection as closed. Call from connection
 * SHUTDOWN_COMPLETE *before* ConnectionClose / session free so stream
 * manager teardown never issues MsQuic stream ops on dead handles.
 */
void wt_stream_manager_mark_conn_closed(wt_stream_manager_t* mgr);

/**
 * Close every stream handle the manager still owns.
 *
 * Call from connection SHUTDOWN_COMPLETE *after*
 * wt_stream_manager_mark_conn_closed and *before* ConnectionClose — the only
 * window where closing is both necessary and safe.
 *
 * Necessary: the per-stream SHUTDOWN_COMPLETE handler deliberately skips
 * StreamClose once conn_closed is set (closing a handle whose connection is
 * already gone aborts the process), so any stream still open at that point is
 * never closed by anyone. msquic keeps the connection — and with it a rundown
 * reference on the registration — alive until every stream handle is released,
 * so a single leaked handle makes MsQuicRegistrationClose block forever and
 * hangs server shutdown.
 *
 * Safe: ConnectionClose has not run yet, so the handles are still valid.
 * Ordering after mark_conn_closed also means the synchronous SHUTDOWN_COMPLETE
 * that StreamClose may deliver takes the "skip StreamClose" branch, so each
 * handle is closed exactly once while its context is still freed normally.
 */
void wt_stream_manager_close_streams(wt_stream_manager_t* mgr);

/**
 * Mark every stream handle dead. Call immediately before ConnectionClose, after
 * wt_stream_manager_close_streams has released the handles that were still open.
 */
void wt_stream_manager_mark_handles_invalid(wt_stream_manager_t* mgr);

int32_t wt_stream_manager_send(
    wt_stream_manager_t* mgr, const uint8_t* data, int32_t length);

void wt_stream_manager_accept_stream(
    wt_stream_manager_t* mgr, HQUIC quic_stream);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_STREAM_MANAGER_H */
