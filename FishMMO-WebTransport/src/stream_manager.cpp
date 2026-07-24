/**
 * @file stream_manager.cpp
 * @brief Manages QUIC streams for reliable data (FishNet channel 0).
 */

#include "stream_manager.h"
#include <stdlib.h>

/* ── Mutex helpers ────────────────────────────────────────────── */
#if defined(WT_PLATFORM_WINDOWS)

/* Manual recursive lock using CRITICAL_SECTION + thread tracking.
 * Windows CRITICAL_SECTION is NOT recursive; this wrapper provides
 * the same behaviour as PTHREAD_MUTEX_RECURSIVE on Linux so that a
 * synchronous MsQuic callback re-entering the lock does not deadlock. */
static void sm_lock_fn(wt_stream_manager_t* mgr)
{
    DWORD tid = GetCurrentThreadId();
    if (mgr->streams_lock_owner == tid) {
        mgr->streams_lock_rec++;
        return;
    }
    EnterCriticalSection(&mgr->streams_lock_cs);
    mgr->streams_lock_owner = tid;
    mgr->streams_lock_rec = 1;
}

static void sm_unlock_fn(wt_stream_manager_t* mgr)
{
    if (--mgr->streams_lock_rec == 0) {
        mgr->streams_lock_owner = 0;
        LeaveCriticalSection(&mgr->streams_lock_cs);
    }
}

#define sm_lock(mgr)    sm_lock_fn(mgr)
#define sm_unlock(mgr)  sm_unlock_fn(mgr)

#else /* Linux / macOS */

  #define sm_lock(mgr)    pthread_mutex_lock(&(mgr)->streams_lock)
  #define sm_unlock(mgr)  pthread_mutex_unlock(&(mgr)->streams_lock)

#endif

/* ── Per-stream context (attached to each QUIC stream) ──────── */

typedef struct {
    wt_stream_manager_t* mgr;
    wt_stream_id_t       stream_id;
    HQUIC                quic_stream;
    uint8_t*             recv_buf;
    uint32_t             recv_offset;
} stream_ctx_t;

/* ── Forward ────────────────────────────────────────────────── */

static QUIC_STATUS stream_cb(HQUIC stream, void* ctx,
                               QUIC_STREAM_EVENT* event);

/* ── Stream callback (function pointer type) ────────────────── */

static QUIC_STREAM_CALLBACK_HANDLER k_stream_handler = stream_cb;

/* ── Callback implementation ────────────────────────────────── */

static QUIC_STATUS QUIC_API
stream_cb(HQUIC stream, void* ctx, QUIC_STREAM_EVENT* event)
{
    stream_ctx_t* sctx = (stream_ctx_t*)ctx;

    switch (event->Type) {

    case QUIC_STREAM_EVENT_RECEIVE: {
        const QUIC_BUFFER* bufs = event->RECEIVE.Buffers;
        uint32_t count = event->RECEIVE.BufferCount;
        uint32_t total  = event->RECEIVE.TotalBufferLength;

        if (total == 0) return QUIC_STATUS_SUCCESS;

        /* Per-stream bound check — prevent OOM from unbounded receive buffer.
         * Check for integer overflow first (defense in depth). */
        if (total > WT_MAX_STREAM_RECV_BUF ||
            sctx->recv_offset > WT_MAX_STREAM_RECV_BUF - total) {
            MsQuic->StreamShutdown(stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            return QUIC_STATUS_ABORTED;
        }

        /* Per-connection total bound check — prevent multi-stream exhaustion.
         * atomic_fetch_add returns the OLD value, so we check if the NEW value
         * would exceed the limit. Use overflow-safe subtraction to prevent
         * integer-wrapping bypass (prev_total near UINT32_MAX + total wraps). */
        {
            uint32_t prev_total = atomic_fetch_add(
                &sctx->mgr->total_recv_bytes, total);
            if (prev_total > WT_MAX_TOTAL_RECV_BUF - total) {
                atomic_fetch_sub(&sctx->mgr->total_recv_bytes, total);
                MsQuic->StreamShutdown(stream,
                                        QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
                return QUIC_STATUS_ABORTED;
            }
        }

        uint32_t needed = sctx->recv_offset + total;
        uint8_t* newbuf = (uint8_t*)realloc(sctx->recv_buf, needed);
        if (!newbuf) {
            atomic_fetch_sub(&sctx->mgr->total_recv_bytes, total);
            return QUIC_STATUS_OUT_OF_MEMORY;
        }
        sctx->recv_buf = newbuf;

        for (uint32_t i = 0; i < count; i++) {
            memcpy(sctx->recv_buf + sctx->recv_offset,
                   bufs[i].Buffer, bufs[i].Length);
            sctx->recv_offset += bufs[i].Length;
        }
        return QUIC_STATUS_SUCCESS;  /* data copied, msquic can free its buffers */
    }

    case QUIC_STREAM_EVENT_PEER_SEND_SHUTDOWN: {
        /* Peer finished sending — deliver accumulated data.
         * on_stream_data may be NULL if session is shutting down. */
        if (!sctx->mgr->on_stream_data || !sctx->mgr->callback_ctx) {
            atomic_fetch_sub(&sctx->mgr->total_recv_bytes, sctx->recv_offset);
            free(sctx->recv_buf);
            sctx->recv_buf = NULL;
            sctx->recv_offset = 0;
            MsQuic->StreamShutdown(stream, QUIC_STREAM_SHUTDOWN_FLAG_GRACEFUL, 0);
            return QUIC_STATUS_SUCCESS;
        }
                /* Snapshot data before StreamShutdown — shutdown may fire
         * SHUTDOWN_COMPLETE synchronously and free sctx+mgr. Deliver the
         * callback AFTER StreamShutdown so that a C# exception in the
         * callback cannot prevent the peer from receiving a clean
         * stream teardown (which would leak stream slots). */
        {
            /* Snapshot ALL sctx fields that may be accessed after the
             * concurrent-free window opens.  wt_stream_manager_shutdown
             * (poll thread) may call StreamShutdown(ABORT) on this stream,
             * whose synchronous SHUTDOWN_COMPLETE frees sctx before we
             * reach the lock at sm_lock below.  Capturing sctx->mgr and
             * sctx->stream_id into locals eliminates all sctx dereferences
             * after line 144, making the handler robust against
             * concurrent free(sctx). */
            wt_stream_manager_t* snap_mgr = sctx->mgr;
            uint8_t* snap_buf = sctx->recv_buf;
            uint32_t snap_len = sctx->recv_offset;
            void* snap_ctx = snap_mgr->callback_ctx;
            wt_connection_id_t snap_cid = snap_mgr->conn_id;
            wt_stream_id_t snap_sid = sctx->stream_id;
            void (*snap_fn)(void*, wt_connection_id_t, wt_stream_id_t,
                            const uint8_t*, int32_t) = snap_mgr->on_stream_data;

            /* ── Snapshot shutting_down BEFORE StreamShutdown ──
             * StreamShutdown may fire SHUTDOWN_COMPLETE synchronously
             * when this is the last active stream.  If the session is
             * shutting down (shutting_down==true), the SHUTDOWN_COMPLETE
             * handler calls on_all_streams_done → on_streams_done, which
             * may free mgr if shutdown_complete is also true.  By
             * snapshotting shutting_down here we skip the data callback
             * when the session is being torn down — the data would be
             * discarded anyway since the connection is closing.
             * Use atomic_load to pair with the store in
             * wt_stream_manager_shutdown. */
            bool mgr_shutting_down = atomic_load(&snap_mgr->shutting_down);

            /* Clear sctx pointers before StreamShutdown (prevent double-free) */
            sctx->recv_buf = NULL;
            sctx->recv_offset = 0;
            atomic_fetch_sub(&snap_mgr->total_recv_bytes, snap_len);

            /* Update recv_closed under lock BEFORE shutdown.
             * Also capture the slot index for the CAS gate below.
             * Use snap_mgr (captured before the concurrent-free window)
             * to avoid dereferencing sctx->mgr after sctx may be freed
             * by a synchronous SHUTDOWN_COMPLETE from the poll thread. */
            int slot_idx = -1;
            sm_lock(snap_mgr);
            for (int i = 0; i < WT_MAX_STREAMS; i++) {
                if (snap_mgr->streams[i].id == snap_sid) {
                    snap_mgr->streams[i].recv_closed = true;
                    slot_idx = i;
                    break;
                }
            }
            sm_unlock(snap_mgr);

            /* ── CAS gate: prevent concurrent StreamShutdown ──────
             * wt_stream_manager_shutdown (poll thread) calls
             * StreamShutdown(ABORT) on collected handles.  If it
             * already claimed this stream, our GRACEFUL shutdown
             * would be the second call on the same HQUIC handle —
             * undefined behavior in MsQuic.  The CAS guarantees
             * exactly one path calls StreamShutdown per stream. */
            bool do_shutdown = true;
            if (slot_idx >= 0) {
                int expected = 0;
                do_shutdown = atomic_compare_exchange_strong(
                    &snap_mgr->streams[slot_idx].shutdown_initiated,
                    &expected, 1);
            }

            /* Shut down BEFORE the callback — guarantees stream teardown
             * even if the callback throws or never returns. */
            if (do_shutdown) {
                MsQuic->StreamShutdown(stream,
                                        QUIC_STREAM_SHUTDOWN_FLAG_GRACEFUL, 0);
            }

            /* Deliver data after shutdown.  Skip the callback if the
             * session is shutting down — mgr and snap_ctx may have been
             * freed by a synchronous SHUTDOWN_COMPLETE above.  When the
             * session is shutting down, the data is discarded anyway. */
            if (!mgr_shutting_down &&
                snap_fn && snap_ctx && snap_buf && snap_len > 0) {
                snap_fn(snap_ctx, snap_cid, snap_sid,
                        snap_buf, (int32_t)snap_len);
            }
            free(snap_buf);
        }
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_SEND_COMPLETE:
        /* Free the copy buffer we malloc'd in wt_stream_manager_send */
        free(event->SEND_COMPLETE.ClientContext);
        return QUIC_STATUS_SUCCESS;

    case QUIC_STREAM_EVENT_PEER_SEND_ABORTED:
        /* Peer aborted — discard any partial data.
         * CAS-gate StreamShutdown to prevent racing with
         * wt_stream_manager_shutdown's ABORT on the same handle.
         * Capture sctx->mgr early: a synchronous SHUTDOWN_COMPLETE
         * from the poll thread's ABORT may free sctx before we
         * acquire the lock below (same pattern as PEER_SEND_SHUTDOWN). */
        {
            wt_stream_manager_t* snap_mgr = sctx->mgr;
            wt_stream_id_t snap_sid = sctx->stream_id;

            atomic_fetch_sub(&snap_mgr->total_recv_bytes, sctx->recv_offset);
            free(sctx->recv_buf);
            sctx->recv_buf = NULL;
            sctx->recv_offset = 0;

            bool do_shutdown = true;
            sm_lock(snap_mgr);
            for (int i = 0; i < WT_MAX_STREAMS; i++) {
                if (snap_mgr->streams[i].id == snap_sid) {
                    int expected = 0;
                    do_shutdown = atomic_compare_exchange_strong(
                        &snap_mgr->streams[i].shutdown_initiated,
                        &expected, 1);
                    break;
                }
            }
            sm_unlock(snap_mgr);

            if (do_shutdown) {
                MsQuic->StreamShutdown(stream,
                                        QUIC_STREAM_SHUTDOWN_FLAG_GRACEFUL, 0);
            }
        }
        return QUIC_STATUS_SUCCESS;

    case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE: {
        wt_stream_manager_t* mgr = sctx->mgr;

        /* Clear the slot under lock — concurrent with send/accept. */
        sm_lock(mgr);
        for (int i = 0; i < WT_MAX_STREAMS; i++) {
            if (mgr->streams[i].id == sctx->stream_id) {
                mgr->streams[i].in_use = false;
                mgr->streams[i].quic_stream = NULL;
                break;
            }
        }
        uint32_t prev = atomic_fetch_sub(&mgr->active_streams, 1);
        sm_unlock(mgr);

        atomic_fetch_sub(&mgr->total_recv_bytes, sctx->recv_offset);
        free(sctx->recv_buf);
        free(sctx);
        MsQuic->StreamClose(stream);

        /* If manager is shutting down and this was the last stream,
         * fire the completion callback so the session can safely
         * free the manager. */
        if (prev == 1 && atomic_load(&mgr->shutting_down) && mgr->on_all_streams_done) {
            mgr->on_all_streams_done(mgr->done_ctx);
        }
        return QUIC_STATUS_SUCCESS;
    }

    default:
        return QUIC_STATUS_SUCCESS;
    }
}

/* ── Stream manager API ─────────────────────────────────────── */

void wt_stream_manager_init(
    wt_stream_manager_t* mgr, HQUIC quic_conn,
    wt_connection_id_t conn_id,
    void (*on_stream_data)(void* ctx, wt_connection_id_t conn_id,
                           wt_stream_id_t stream_id,
                           const uint8_t* data, int32_t length),
    void* callback_ctx)
{
    memset(mgr, 0, sizeof(*mgr));
    mgr->quic_conn = quic_conn;
    mgr->conn_id = conn_id;
    mgr->on_stream_data = on_stream_data;
    mgr->callback_ctx = callback_ctx;
    mgr->next_id = 1;
    atomic_init(&mgr->active_streams, 0);
    atomic_init(&mgr->streams_done_flag, false);
    atomic_init(&mgr->shutdown_complete, false);
    atomic_init(&mgr->freed, false);
    atomic_store(&mgr->shutting_down, false);
    atomic_init(&mgr->total_recv_bytes, 0);

#if defined(WT_PLATFORM_WINDOWS)
    InitializeCriticalSection(&mgr->streams_lock_cs);
    mgr->streams_lock_owner = 0;
    mgr->streams_lock_rec = 0;
#else
    {
        pthread_mutexattr_t attr;
        pthread_mutexattr_init(&attr);
        pthread_mutexattr_settype(&attr, PTHREAD_MUTEX_RECURSIVE);
        pthread_mutex_init(&mgr->streams_lock, &attr);
        pthread_mutexattr_destroy(&attr);
    }
#endif

    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        mgr->streams[i].id = 0;
        mgr->streams[i].in_use = false;
        atomic_init(&mgr->streams[i].shutdown_initiated, false);
    }

}

void wt_stream_manager_shutdown(wt_stream_manager_t* mgr)
{
    /* Collect in-use streams under the lock, then shut them down
     * outside the lock. StreamShutdown can fire SHUTDOWN_COMPLETE
     * synchronously, which also acquires the lock — deadlock if
     * we held the lock across the MsQuic call. */
    /* Heap allocation — WT_MAX_STREAMS (4096) entries of two pointers
     * is ~64 KB on 64-bit.  A stack allocation of this size risks
     * overflow on constrained systems. */
    struct { HQUIC handle; int slot_idx; } *pending = NULL;
    pending = (typeof(pending))malloc(
        sizeof(*pending) * (size_t)WT_MAX_STREAMS);
    if (!pending) {
        WT_LOG_ERROR("wt_stream_manager_shutdown: malloc(%zu) failed — "
                     "cannot collect stream handles for shutdown. "
                     "Leaking %u active streams.",
                     sizeof(*pending) * (size_t)WT_MAX_STREAMS,
                     (unsigned)atomic_load(&mgr->active_streams));
        return;
    }

    int pending_count = 0;
    sm_lock(mgr);
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (mgr->streams[i].in_use && mgr->streams[i].quic_stream) {
            /* ── CAS gate: claim shutdown for this stream ───────
             * If a QUIC callback thread is concurrently processing
             * PEER_SEND_SHUTDOWN for this stream, it will also try
             * to CAS shutdown_initiated 0→1.  Whichever path loses
             * the CAS skips its StreamShutdown call, preventing
             * dual StreamShutdown(GRACEFUL+ABORT) on the same
             * HQUIC handle (undefined behavior in MsQuic). */
            int expected = 0;
            if (!atomic_compare_exchange_strong(
                    &mgr->streams[i].shutdown_initiated,
                    &expected, 1)) {
                /* PEER_SEND_SHUTDOWN already claimed this stream.
                 * Skip shutdown here — the GRACEFUL path will
                 * handle cleanup. */
                continue;
            }
            pending[pending_count].handle = mgr->streams[i].quic_stream;
            pending[pending_count].slot_idx = i;
            pending_count++;
            mgr->streams[i].quic_stream = NULL;  /* NULL before unlock — prevents
                SHUTDOWN_COMPLETE from finding this slot again */
        }
    }
    sm_unlock(mgr);

    for (int i = 0; i < pending_count; i++) {
        MsQuic->StreamShutdown(pending[i].handle,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        /* StreamShutdown may fire SHUTDOWN_COMPLETE synchronously,
         * which decrements active_streams, but the mgr is not freed
         * here because shutdown_complete is not yet set. Safe to
         * continue iterating. */
    }
    free(pending);
}

int32_t wt_stream_manager_send(
    wt_stream_manager_t* mgr, const uint8_t* data, int32_t length)
{
    if (!data || length <= 0) return WT_ERR_SEND_FAILED;
    if (atomic_load(&mgr->shutting_down)) return WT_ERR_INVALID_STATE;

    /* Find free slot under lock — concurrent with accept on QUIC thread. */
    int slot = -1;
    sm_lock(mgr);
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (!mgr->streams[i].in_use) { slot = i; break; }
    }
    if (slot < 0) { sm_unlock(mgr); return WT_ERR_BUFFER_FULL; }

    /* Reserve the slot immediately so accept doesn't grab it.
     * Increment active_streams while under the lock so that the count
     * is consistent with the reserved slot — shutdown logic checks
     * active_streams against streams_done_flag to decide whether to
     * free the mgr. Moving the increment here (vs. after StreamOpen)
     * prevents a theoretical underflow if SHUTDOWN_COMPLETE raced
     * between StreamOpen and the old increment. */
	/* Generate unique stream ID. next_id is uint64_t — in practice
	 * this never wraps (~584K years at 1M streams/s). On the off
	 * chance it wraps to 0 (reserved), scan the active stream slots
	 * for an unused ID.  We hold the lock so the slot table is stable. */
	wt_stream_id_t stream_id = mgr->next_id++;
	if (stream_id == 0) {
	    stream_id = 1;
	    mgr->next_id = WT_MAX_STREAMS + 2;  /* skip scan range for future allocs */
	    for (;;) {
	        bool conflict = false;
	        for (int i = 0; i < WT_MAX_STREAMS; i++) {
	            if (mgr->streams[i].in_use && mgr->streams[i].id == stream_id) {
	                conflict = true;
	                break;
	            }
	        }
	        if (!conflict) break;
	        if (++stream_id == 0) stream_id = 1;  /* still skip 0 */
	    }
	}
    mgr->streams[slot].id = stream_id;
    mgr->streams[slot].in_use = true;
    mgr->streams[slot].send_closed = false;
    atomic_store(&mgr->streams[slot].shutdown_initiated, false);
    atomic_fetch_add(&mgr->active_streams, 1);
    /* quic_stream set below after StreamOpen; SHUTDOWN_COMPLETE will
     * see in_use=true but quic_stream=NULL and skip cleanup — that's
     * fine because we haven't opened the stream yet. */
    sm_unlock(mgr);

    /* Open bidirectional stream (no lock needed — MsQuic handles its own
     * synchronisation). */
    HQUIC quic_stream = NULL;
    QUIC_STATUS status = MsQuic->StreamOpen(
        mgr->quic_conn, QUIC_STREAM_OPEN_FLAG_NONE, NULL, NULL,
        &quic_stream);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("StreamOpen failed: 0x%x", status);
        /* Release the reserved slot and undo the active_streams increment. */
        sm_lock(mgr);
        mgr->streams[slot].in_use = false;
        mgr->streams[slot].id = 0;
        atomic_fetch_sub(&mgr->active_streams, 1);
        sm_unlock(mgr);
        return WT_ERR_SEND_FAILED;
    }

    stream_ctx_t* sctx = (stream_ctx_t*)calloc(1, sizeof(stream_ctx_t));
    if (!sctx) {
        MsQuic->StreamClose(quic_stream);
        sm_lock(mgr);
        mgr->streams[slot].in_use = false;
        mgr->streams[slot].id = 0;
        atomic_fetch_sub(&mgr->active_streams, 1);
        sm_unlock(mgr);
        return WT_ERR_BUFFER_FULL;
    }
    sctx->mgr = mgr;
    sctx->stream_id = stream_id;
    sctx->quic_stream = quic_stream;

    /* Record quic_stream under lock so SHUTDOWN_COMPLETE can find the slot.
     * active_streams was already incremented at slot-reservation time. */
    sm_lock(mgr);
    mgr->streams[slot].quic_stream = quic_stream;
    sm_unlock(mgr);

    status = MsQuic->StreamStart(quic_stream,
                                  QUIC_STREAM_START_FLAG_IMMEDIATE);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("StreamStart failed: 0x%x", status);
        /* MsQuic does NOT guarantee SHUTDOWN_COMPLETE fires for a stream
         * that never successfully started. Clean up the slot, sctx, and
         * active_streams counter inline to prevent a permanent leak.
         * SetCallbackHandler is called below (only on success), so
         * StreamClose here won't trigger SHUTDOWN_COMPLETE — safe. */
        sm_lock(mgr);
        mgr->streams[slot].in_use = false;
        mgr->streams[slot].quic_stream = NULL;
        mgr->streams[slot].id = 0;
        atomic_fetch_sub(&mgr->active_streams, 1);
        sm_unlock(mgr);
        free(sctx);
        MsQuic->StreamClose(quic_stream);
        return WT_ERR_SEND_FAILED;
    }

    MsQuic->SetCallbackHandler(quic_stream,
                                (void*)(uintptr_t)k_stream_handler, sctx);

    /* Send data — copy to ensure lifetime across async send */
    uint8_t* copy = (uint8_t*)malloc((size_t)length);
    if (!copy) {
        /* Do NOT clear in_use or quic_stream — SHUTDOWN_COMPLETE from
         * StreamShutdown will handle slot cleanup. */
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        return WT_ERR_BUFFER_FULL;
    }
    memcpy(copy, data, (size_t)length);

    QUIC_BUFFER send_buf;
    send_buf.Buffer = copy;
    send_buf.Length = (uint32_t)length;

    status = MsQuic->StreamSend(quic_stream, &send_buf, 1,
                                 QUIC_SEND_FLAG_FIN, copy);
    /* `copy` is freed by stream_send_complete on SEND_COMPLETE event */
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("StreamSend failed: 0x%x", status);
        free(copy);
        /* Do NOT clear in_use or quic_stream — SHUTDOWN_COMPLETE from
         * StreamShutdown will handle slot cleanup. */
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        return WT_ERR_SEND_FAILED;
    }

    /* Update send_closed under lock. */
    sm_lock(mgr);
    mgr->streams[slot].send_closed = true;  /* FIN sent */
    sm_unlock(mgr);
    return WT_OK;
}

void wt_stream_manager_accept_stream(
    wt_stream_manager_t* mgr, HQUIC quic_stream)
{
    /* Find and reserve a free slot under lock — concurrent with send
     * on the application thread. */
    int slot = -1;
    sm_lock(mgr);
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (!mgr->streams[i].in_use) { slot = i; break; }
    }
    if (slot < 0) {
        sm_unlock(mgr);
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        MsQuic->StreamClose(quic_stream);  /* no sctx, so no callback to do this */
        return;
    }

    /* Reserve the slot immediately, unlock before alloc + MsQuic calls. */
    /* Generate unique stream ID. next_id is uint64_t — in practice
     * this never wraps (~584K years at 1M streams/s). On the off
     * chance it wraps to 0 (reserved), scan the active stream slots
     * for an unused ID.  We hold the lock so the slot table is stable. */
    wt_stream_id_t stream_id = mgr->next_id++;
    if (stream_id == 0) {
        stream_id = 1;
        mgr->next_id = WT_MAX_STREAMS + 2;  /* skip scan range for future allocs */
        for (;;) {
            bool conflict = false;
            for (int i = 0; i < WT_MAX_STREAMS; i++) {
                if (mgr->streams[i].in_use && mgr->streams[i].id == stream_id) {
                    conflict = true;
                    break;
                }
            }
            if (!conflict) break;
            if (++stream_id == 0) stream_id = 1;  /* still skip 0 */
        }
    }
    mgr->streams[slot].quic_stream = quic_stream;
    mgr->streams[slot].in_use = true;
    mgr->streams[slot].id = stream_id;
    atomic_store(&mgr->streams[slot].shutdown_initiated, false);
    atomic_fetch_add(&mgr->active_streams, 1);
    sm_unlock(mgr);

    stream_ctx_t* sctx = (stream_ctx_t*)calloc(1, sizeof(stream_ctx_t));
    if (!sctx) {
        /* Release reserved slot. */
        sm_lock(mgr);
        mgr->streams[slot].in_use = false;
        mgr->streams[slot].quic_stream = NULL;
        mgr->streams[slot].id = 0;
        atomic_fetch_sub(&mgr->active_streams, 1);
        sm_unlock(mgr);
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        MsQuic->StreamClose(quic_stream);  /* no sctx, so no callback to do this */
        return;
    }
    sctx->mgr = mgr;
    sctx->stream_id = stream_id;
    sctx->quic_stream = quic_stream;

    /* SetCallbackHandler outside lock — safe because the slot is already
     * registered above. If SHUTDOWN_COMPLETE fires synchronously it will
     * find the slot via stream_id and clean up correctly. */
    MsQuic->SetCallbackHandler(quic_stream,
                                (void*)(uintptr_t)k_stream_handler, sctx);
}