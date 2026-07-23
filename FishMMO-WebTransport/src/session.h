/**
 * @file session.h
 * @brief WebTransport session — manages QUIC streams (reliable)
 *        and datagrams (unreliable) for a single connection.
 *
 * ## Shutdown Sequence
 *
 * Session shutdown is a multi-step, cross-thread process that must
 * guarantee no use-after-free of the session or its stream_manager.
 *
 * ### Refcount semantics
 * - wt_session_init sets ref_count = 1 ("owner reference").
 * - Each call to wt_session_acquire increments ref_count.
 * - Each call to wt_session_release decrements ref_count.
 * - When ref_count reaches 0 AND atomic_load(&released) is true,
 *   the session is freed.
 *
 * ### Shutdown flow (initiated by wt_session_shutdown):
 *
 *   1. atomic_store(&released, true)
 *      → Prevents NEW acquires from succeeding (acquire skips if
 *        ref_count was 0, but the owner reference keeps it >= 1).
 *
 *   2. Wire mgr->on_all_streams_done to on_streams_done().
 *      Set mgr->shutting_down = true.
 *   3. Call wt_stream_manager_shutdown(mgr).
 *      → Aborts all open streams. As each stream's SHUTDOWN_COMPLETE
 *        fires, it decrements active_streams. When the last stream
 *        completes, on_streams_done() is called.
 *
 *   4. atomic_store(&shutdown_complete, true)
 *   5. Null session->stream_mgr (prevents dangling pointer).
 *   6. If active_streams == 0 or streams_done_flag is set, free mgr.
 *      Otherwise on_streams_done() will free it.
 *
 *   7. wt_session_release(session) — drops the owner reference.
 *      If no in-flight sends hold a ref, session is freed now.
 *
 * ### The role of on_all_streams_done
 * - Set during shutdown as mgr->on_all_streams_done.
 * - Fires from SHUTDOWN_COMPLETE (stream_cb in stream_manager.cpp)
 *   when active_streams drops to 0 and shutting_down is true.
 * - Called on_streams_done() which checks shutdown_complete:
 *   - If true: mgr was already freed, or try_free_mgr() wins the CAS.
 *   - If false: sets streams_done_flag = true. Re-checks after store
 *     to catch a concurrent shutdown_complete write.
 *
 * ### Relationship between active_streams, streams_done_flag, shutdown_complete
 * - active_streams: number of streams in use. Decremented atomically.
 * - streams_done_flag: set by on_streams_done when last stream finishes.
 * - shutdown_complete: set by wt_session_shutdown after
 *   wt_stream_manager_shutdown returns.
 * - freed: CAS gate ensuring exactly one path (session_shutdown or
 *   on_streams_done) calls free(mgr).
 * - The race: session_shutdown sets shutdown_complete and checks
 *   active_streams/streams_done_flag. on_streams_done sets
 *   streams_done_flag and checks shutdown_complete. The CAS on
 *   mgr->freed ensures whichever runs second sees the other's
 *   state and does not double-free.
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

    /* Refcount: 1 on creation, +1 for each in-flight send.
     * Free only when refcount reaches 0 after the owner releases. */
    atomic_uint             ref_count;
    atomic_bool             released;     /* owner has released its reference */
} wt_session_t;

/* Wrapper for datagram send buffers -- ensures safe ownership transfer
 * between wt_session_send_datagram (allocation) and the
 * DATAGRAM_SEND_STATE_CHANGED callback (free).
 *
 * msquic guarantees that DATAGRAM_SEND_STATE_CHANGED never fires
 * synchronously from within DatagramSend, but this flag makes the
 * ownership explicit and prevents a double-free should that guarantee
 * ever change (e.g. a future msquic version).
 *
 * The struct is allocated with space for the data payload at the end
 * (flexible array member).  The struct pointer is passed as
 * ClientContext to DatagramSend.  On success, owned_by_msquic is set
 * to true.  Both the callback and the failure path check the flag
 * before freeing: the callback frees only if true, the failure path
 * frees only if false. */
typedef struct {
    bool owned_by_msquic;
    uint8_t data[];
} wt_dgram_send_ctx_t;

/* ── API ────────────────────────────────────────────────────── */

/**
 * IMPORTANT THREADING REQUIREMENT
 * ===============================
 * All send functions (wt_session_send_stream, wt_session_send_datagram)
 * AND wt_session_acquire / wt_session_release MUST be called from the
 * same thread as the corresponding poll function (wt_server_poll_impl /
 * wt_client_poll_impl).  Calling these from a different thread creates
 * a use-after-free race: wt_session_shutdown (deferred to the poll
 * thread) may free the session between the acquire and the send on
 * the other thread.
 *
 * The poll thread owns session lifecycle; the send/acquire path must
 * synchronize with it by running on the same thread. */

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

/** Acquire a reference for in-flight send. Returns true if acquired.
 *  If the session has been released (released==true), returns false.
 *  Call wt_session_release() when the send operation completes. */
bool wt_session_acquire(wt_session_t* session);

/** Release a reference. Frees the session if refcount reaches 0
 *  and the owner has released. Safe to call from any thread. */
void wt_session_release(wt_session_t* session);

#ifdef __cplusplus
}
#endif

#endif /* WEBTRANSPORT_SESSION_H */