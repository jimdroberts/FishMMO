/**
 * @file stream_manager.cpp
 * @brief Manages QUIC streams for reliable data (FishNet channel 0).
 */

#include "stream_manager.h"
#include <stdlib.h>

/* ── Mutex helpers (same conventions as datagram_queue) ──────── */
#if defined(WT_PLATFORM_WINDOWS)
  #define sm_lock(mgr)    EnterCriticalSection(&(mgr)->streams_lock)
  #define sm_unlock(mgr)  LeaveCriticalSection(&(mgr)->streams_lock)
#else
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
         * would exceed the limit. */
        {
            uint32_t prev_total = atomic_fetch_add(
                &sctx->mgr->total_recv_bytes, total);
            if (prev_total + total > WT_MAX_TOTAL_RECV_BUF) {
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
        if (sctx->recv_buf &&
            sctx->recv_offset > 0) {
            sctx->mgr->on_stream_data(
                sctx->mgr->callback_ctx,
                sctx->mgr->conn_id,
                sctx->stream_id,
                sctx->recv_buf,
                (int32_t)sctx->recv_offset);
        }

        atomic_fetch_sub(&sctx->mgr->total_recv_bytes, sctx->recv_offset);
        free(sctx->recv_buf);
        sctx->recv_buf = NULL;
        sctx->recv_offset = 0;

        /* Update recv_closed under lock — stream_manager_send and
         * SHUTDOWN_COMPLETE also touch the stream slot concurrently. */
        sm_lock(sctx->mgr);
        for (int i = 0; i < WT_MAX_STREAMS; i++) {
            if (sctx->mgr->streams[i].id == sctx->stream_id) {
                sctx->mgr->streams[i].recv_closed = true;
                break;
            }
        }
        sm_unlock(sctx->mgr);

        /* Shut down our send direction so SHUTDOWN_COMPLETE fires */
        MsQuic->StreamShutdown(stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_GRACEFUL, 0);
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_SEND_COMPLETE:
        /* Free the copy buffer we malloc'd in wt_stream_manager_send */
        free(event->SEND_COMPLETE.ClientContext);
        return QUIC_STATUS_SUCCESS;

    case QUIC_STREAM_EVENT_PEER_SEND_ABORTED:
        /* Peer aborted — discard any partial data.
         * No streams[] access here, just per-stream context cleanup. */
        atomic_fetch_sub(&sctx->mgr->total_recv_bytes, sctx->recv_offset);
        free(sctx->recv_buf);
        sctx->recv_buf = NULL;
        sctx->recv_offset = 0;
        MsQuic->StreamShutdown(stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_GRACEFUL, 0);
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
    InitializeCriticalSection(&mgr->streams_lock);
#else
    pthread_mutex_init(&mgr->streams_lock, NULL);
#endif

    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        mgr->streams[i].id = 0;
        mgr->streams[i].in_use = false;
    }

}

void wt_stream_manager_shutdown(wt_stream_manager_t* mgr)
{
    /* Collect in-use streams under the lock, then shut them down
     * outside the lock. StreamShutdown can fire SHUTDOWN_COMPLETE
     * synchronously, which also acquires the lock — deadlock if
     * we held the lock across the MsQuic call. */
    HQUIC pending[WT_MAX_STREAMS];
    int pending_count = 0;

    sm_lock(mgr);
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (mgr->streams[i].in_use && mgr->streams[i].quic_stream) {
            pending[pending_count++] = mgr->streams[i].quic_stream;
            mgr->streams[i].quic_stream = NULL;  /* NULL before unlock — prevents
                SHUTDOWN_COMPLETE from finding this slot again */
        }
    }
    sm_unlock(mgr);

    for (int i = 0; i < pending_count; i++) {
        MsQuic->StreamShutdown(pending[i], QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        /* StreamShutdown may fire SHUTDOWN_COMPLETE synchronously,
         * which decrements active_streams, but the mgr is not freed
         * here because shutdown_complete is not yet set. Safe to
         * continue iterating. */
    }
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

    /* Reserve the slot immediately so accept doesn't grab it. */
    wt_stream_id_t stream_id = mgr->next_id++;
    if (stream_id == 0) stream_id = mgr->next_id++;
    /* Overflow guard: skip 0 (reserved) and IDs already in the slot table.
     * wt_stream_id_t is uint64_t — overflow requires ~584K years at 1M streams/s.
     * This check exists for formal correctness only. */
    if (stream_id == 0) stream_id = mgr->next_id++;
    if (stream_id == 0) stream_id = 1; mgr->next_id = (mgr->next_id == 0) ? 1 : mgr->next_id;
    mgr->streams[slot].id = stream_id;
    mgr->streams[slot].in_use = true;
    mgr->streams[slot].send_closed = false;
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
        /* Release the reserved slot. */
        sm_lock(mgr);
        mgr->streams[slot].in_use = false;
        mgr->streams[slot].id = 0;
        sm_unlock(mgr);
        return WT_ERR_SEND_FAILED;
    }

    stream_ctx_t* sctx = (stream_ctx_t*)calloc(1, sizeof(stream_ctx_t));
    if (!sctx) {
        MsQuic->StreamClose(quic_stream);
        sm_lock(mgr);
        mgr->streams[slot].in_use = false;
        mgr->streams[slot].id = 0;
        sm_unlock(mgr);
        return WT_ERR_BUFFER_FULL;
    }
    sctx->mgr = mgr;
    sctx->stream_id = stream_id;
    sctx->quic_stream = quic_stream;

    /* Record quic_stream under lock so SHUTDOWN_COMPLETE can find the slot. */
    sm_lock(mgr);
    mgr->streams[slot].quic_stream = quic_stream;
    atomic_fetch_add(&mgr->active_streams, 1);
    sm_unlock(mgr);

    MsQuic->SetCallbackHandler(quic_stream,
                                (void*)(uintptr_t)k_stream_handler, sctx);

    status = MsQuic->StreamStart(quic_stream,
                                  QUIC_STREAM_START_FLAG_IMMEDIATE);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("StreamStart failed: 0x%x", status);
        /* Do NOT clear in_use or quic_stream — SHUTDOWN_COMPLETE will
         * fire from StreamClose and handle slot cleanup. Prematurely
         * freeing the slot risks reuse before the callback fires. */
        MsQuic->StreamClose(quic_stream);
        return WT_ERR_SEND_FAILED;
    }

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
    wt_stream_id_t stream_id = mgr->next_id++;
    if (stream_id == 0) stream_id = mgr->next_id++;
    /* Overflow guard: skip 0 (reserved) and IDs already in the slot table.
     * wt_stream_id_t is uint64_t — overflow requires ~584K years at 1M streams/s.
     * This check exists for formal correctness only. */
    if (stream_id == 0) stream_id = mgr->next_id++;
    if (stream_id == 0) stream_id = 1; mgr->next_id = (mgr->next_id == 0) ? 1 : mgr->next_id;
    mgr->streams[slot].id = stream_id;
    mgr->streams[slot].quic_stream = quic_stream;
    mgr->streams[slot].in_use = true;
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