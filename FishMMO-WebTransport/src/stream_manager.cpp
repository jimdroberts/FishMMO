/**
 * @file stream_manager.cpp
 * @brief Manages QUIC streams for reliable data (FishNet channel 0).
 */

#include "stream_manager.h"
#include <stdlib.h>

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

static QUIC_STREAM_CALLBACK_HANDLER k_stream_handler = NULL;

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

        /* Bound check — prevent OOM from unbounded receive buffer */
        if (sctx->recv_offset + total > WT_MAX_STREAM_RECV_BUF) {
            MsQuic->StreamShutdown(stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            return QUIC_STATUS_ABORTED;
        }

        uint32_t needed = sctx->recv_offset + total;
        uint8_t* newbuf = (uint8_t*)realloc(sctx->recv_buf, needed);
        if (!newbuf) return QUIC_STATUS_OUT_OF_MEMORY;
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

        free(sctx->recv_buf);
        sctx->recv_buf = NULL;
        sctx->recv_offset = 0;

        for (int i = 0; i < WT_MAX_STREAMS; i++) {
            if (sctx->mgr->streams[i].id == sctx->stream_id) {
                sctx->mgr->streams[i].recv_closed = true;
                break;
            }
        }

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
        /* Peer aborted — discard any partial data */
        free(sctx->recv_buf);
        sctx->recv_buf = NULL;
        sctx->recv_offset = 0;
        MsQuic->StreamShutdown(stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_GRACEFUL, 0);
        return QUIC_STATUS_SUCCESS;

    case QUIC_STREAM_EVENT_SHUTDOWN_COMPLETE: {
        wt_stream_manager_t* mgr = sctx->mgr;

        /* Free context and mark slot */
        for (int i = 0; i < WT_MAX_STREAMS; i++) {
            if (mgr->streams[i].id == sctx->stream_id) {
                mgr->streams[i].in_use = false;
                mgr->streams[i].quic_stream = NULL;
                break;
            }
        }
        free(sctx->recv_buf);
        free(sctx);
        MsQuic->StreamClose(stream);

        /* Decrement refcount. If manager is shutting down and this
         * was the last stream, fire the completion callback so the
         * session can safely free the manager. */
        uint32_t prev = atomic_fetch_sub(&mgr->active_streams, 1);
        if (prev == 1 && mgr->shutting_down && mgr->on_all_streams_done) {
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
    mgr->shutting_down = false;

    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        mgr->streams[i].id = 0;
        mgr->streams[i].in_use = false;
    }

    /* Set handler once */
    k_stream_handler = stream_cb;
}

void wt_stream_manager_shutdown(wt_stream_manager_t* mgr)
{
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (mgr->streams[i].in_use && mgr->streams[i].quic_stream) {
            MsQuic->StreamShutdown(mgr->streams[i].quic_stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            /* StreamClose is called by SHUTDOWN_COMPLETE handler,
             * which also frees the stream_ctx_t. Do NOT call
             * StreamClose here — it prevents the callback. */
            mgr->streams[i].quic_stream = NULL;
        }
    }
}

int32_t wt_stream_manager_send(
    wt_stream_manager_t* mgr, const uint8_t* data, int32_t length)
{
    if (!data || length <= 0) return WT_ERR_SEND_FAILED;

    /* Find free slot */
    int slot = -1;
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (!mgr->streams[i].in_use) { slot = i; break; }
    }
    if (slot < 0) return WT_ERR_BUFFER_FULL;

    /* Open bidirectional stream */
    HQUIC quic_stream = NULL;
    QUIC_STATUS status = MsQuic->StreamOpen(
        mgr->quic_conn, QUIC_STREAM_OPEN_FLAG_NONE, NULL, NULL,
        &quic_stream);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("StreamOpen failed: 0x%x", status);
        return WT_ERR_SEND_FAILED;
    }

    wt_stream_id_t stream_id = mgr->next_id++;

    stream_ctx_t* sctx = (stream_ctx_t*)calloc(1, sizeof(stream_ctx_t));
    if (!sctx) {
        MsQuic->StreamClose(quic_stream);
        return WT_ERR_BUFFER_FULL;
    }
    sctx->mgr = mgr;
    sctx->stream_id = stream_id;
    sctx->quic_stream = quic_stream;

    MsQuic->SetCallbackHandler(quic_stream,
                                (void*)(uintptr_t)k_stream_handler, sctx);

    status = MsQuic->StreamStart(quic_stream,
                                  QUIC_STREAM_START_FLAG_IMMEDIATE);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("StreamStart failed: 0x%x", status);
        free(sctx);
        MsQuic->StreamClose(quic_stream);
        return WT_ERR_SEND_FAILED;
    }

    /* Record the stream BEFORE StreamSend — if SHUTDOWN_COMPLETE
     * fires synchronously, it needs to find the slot populated. */
    mgr->streams[slot].id = stream_id;
    mgr->streams[slot].quic_stream = quic_stream;
    mgr->streams[slot].in_use = true;
    mgr->streams[slot].send_closed = false;
    atomic_fetch_add(&mgr->active_streams, 1);

    /* Send data — copy to ensure lifetime across async send */
    uint8_t* copy = (uint8_t*)malloc((size_t)length);
    if (!copy) {
        mgr->streams[slot].in_use = false;
        mgr->streams[slot].quic_stream = NULL;
        /* SHUTDOWN_COMPLETE will call StreamClose and free sctx */
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
        mgr->streams[slot].in_use = false;
        mgr->streams[slot].quic_stream = NULL;
        /* SHUTDOWN_COMPLETE will call StreamClose and free sctx */
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        return WT_ERR_SEND_FAILED;
    }

    mgr->streams[slot].send_closed = true;  /* FIN sent */
    return WT_OK;
}

void wt_stream_manager_accept_stream(
    wt_stream_manager_t* mgr, HQUIC quic_stream)
{
    /* Find free slot to register this incoming stream */
    int slot = -1;
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (!mgr->streams[i].in_use) { slot = i; break; }
    }
    if (slot < 0) {
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        MsQuic->StreamClose(quic_stream);  /* no sctx, so no callback to do this */
        return;
    }

    stream_ctx_t* sctx = (stream_ctx_t*)calloc(1, sizeof(stream_ctx_t));
    if (!sctx) {
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        MsQuic->StreamClose(quic_stream);  /* no sctx, so no callback to do this */
        return;
    }
    sctx->mgr = mgr;
    sctx->stream_id = mgr->next_id++;
    sctx->quic_stream = quic_stream;

    /* Register BEFORE SetCallbackHandler — sync SHUTDOWN_COMPLETE
     * needs the slot populated to clean up properly. */
    mgr->streams[slot].id = sctx->stream_id;
    mgr->streams[slot].quic_stream = quic_stream;
    mgr->streams[slot].in_use = true;
    atomic_fetch_add(&mgr->active_streams, 1);

    MsQuic->SetCallbackHandler(quic_stream,
                                (void*)(uintptr_t)k_stream_handler, sctx);
}