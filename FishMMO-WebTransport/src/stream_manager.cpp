/**
 * @file stream_manager.cpp
 * @brief Manages QUIC bidirectional streams for reliable data (FishNet channel 0).
 */

#include "stream_manager.h"
#include <string.h>
#include <stdlib.h>

/* ── msquic stream callbacks (static, C linkage) ────────────── */

static QUIC_STATUS
stream_recv_callback(HQUIC stream, void* ctx, QUIC_STREAM_EVENT* event);

/* Per-stream context we attach to each QUIC stream */
typedef struct {
    wt_stream_manager_t* mgr;
    wt_stream_id_t       stream_id;
    HQUIC                quic_stream;
    /* Receive buffer */
    uint8_t*             recv_buf;
    uint32_t             recv_offset;
} stream_ctx_t;

static const QUIC_STREAM_CALLBACKS k_stream_callbacks = {
    .Receive = stream_recv_callback,
    .SendComplete = NULL,
    .StartComplete = NULL,
    .ShutdownComplete = NULL,
};

static QUIC_STATUS
stream_recv_callback(HQUIC stream, void* ctx, QUIC_STREAM_EVENT* event)
{
    stream_ctx_t* sctx = (stream_ctx_t*)ctx;

    switch (event->Type) {
    case QUIC_STREAM_EVENT_RECEIVE: {
        /* Collect all receive buffers and deliver as a single chunk */
        const QUIC_BUFFER* bufs = event->RECEIVE.Buffers;
        uint32_t count = event->RECEIVE.BufferCount;
        uint32_t total = event->RECEIVE.TotalBufferLength;

        if (total == 0) return QUIC_STATUS_SUCCESS;

        /* Allocate or grow buffer */
        uint32_t needed = sctx->recv_offset + total;
        uint8_t* newbuf = (uint8_t*)realloc(sctx->recv_buf, needed);
        if (!newbuf) return QUIC_STATUS_OUT_OF_MEMORY;
        sctx->recv_buf = newbuf;

        for (uint32_t i = 0; i < count; i++) {
            memcpy(sctx->recv_buf + sctx->recv_offset,
                   bufs[i].Buffer, bufs[i].Length);
            sctx->recv_offset += bufs[i].Length;
        }
        return QUIC_STATUS_PENDING; /* hold off — we'll complete below */
    }

    case QUIC_STREAM_EVENT_RECEIVE_COMPLETE: {
        /* All data received, deliver to callback */
        if (sctx->mgr->on_stream_data && sctx->recv_buf && sctx->recv_offset > 0) {
            sctx->mgr->on_stream_data(
                sctx->mgr->callback_ctx,
                sctx->mgr->conn_id,
                sctx->stream_id,
                sctx->recv_buf,
                (int32_t)sctx->recv_offset);
        }

        /* Mark stream as done */
        for (int i = 0; i < WT_MAX_STREAMS; i++) {
            if (sctx->mgr->streams[i].id == sctx->stream_id) {
                sctx->mgr->streams[i].recv_closed = true;
                sctx->mgr->streams[i].in_use = false;
                break;
            }
        }

        free(sctx->recv_buf);
        free(sctx);
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_PEER_SEND_SHUTDOWN:
        /* Peer finished sending — complete the pending receive */
        MsQuic->StreamReceiveComplete(stream, event->RECEIVE_COMPLETE.RecvLen);
        return QUIC_STATUS_SUCCESS;

    default:
        return QUIC_STATUS_SUCCESS;
    }
}

/* ── Stream manager API ─────────────────────────────────────── */

void wt_stream_manager_init(
    wt_stream_manager_t* mgr,
    HQUIC                quic_conn,
    wt_connection_id_t   conn_id,
    void (*on_stream_data)(void* ctx, wt_connection_id_t conn_id,
                           wt_stream_id_t stream_id,
                           const uint8_t* data, int32_t length),
    void*                callback_ctx)
{
    memset(mgr, 0, sizeof(*mgr));
    mgr->quic_conn = quic_conn;
    mgr->conn_id = conn_id;
    mgr->on_stream_data = on_stream_data;
    mgr->callback_ctx = callback_ctx;
    atomic_init(&mgr->next_id, 1);
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        mgr->streams[i].id = 0;
        mgr->streams[i].in_use = false;
    }
}

void wt_stream_manager_shutdown(wt_stream_manager_t* mgr)
{
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (mgr->streams[i].in_use && mgr->streams[i].quic_stream) {
            MsQuic->StreamShutdown(
                mgr->streams[i].quic_stream,
                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        }
    }
    memset(mgr->streams, 0, sizeof(mgr->streams));
}

int32_t wt_stream_manager_send(
    wt_stream_manager_t* mgr,
    const uint8_t*       data,
    int32_t              length)
{
    if (!data || length <= 0) return WT_ERR_SEND_FAILED;

    /* Find a free stream slot */
    int slot = -1;
    for (int i = 0; i < WT_MAX_STREAMS; i++) {
        if (!mgr->streams[i].in_use) {
            slot = i;
            break;
        }
    }
    if (slot < 0) return WT_ERR_BUFFER_FULL;

    /* Create the QUIC stream */
    QUIC_STATUS status;
    HQUIC quic_stream = NULL;
    status = MsQuic->StreamOpen(mgr->quic_conn,
                                 QUIC_STREAM_OPEN_FLAG_UNIDIRECTIONAL,
                                 NULL, NULL, &quic_stream);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("StreamOpen failed: 0x%x", status);
        return WT_ERR_SEND_FAILED;
    }

    wt_stream_id_t stream_id = atomic_fetch_add(&mgr->next_id, 1);

    /* Allocate and attach per-stream context */
    stream_ctx_t* sctx = (stream_ctx_t*)calloc(1, sizeof(stream_ctx_t));
    sctx->mgr = mgr;
    sctx->stream_id = stream_id;
    sctx->quic_stream = quic_stream;

    MsQuic->SetCallbackHandler(quic_stream,
                                (void*)k_stream_callbacks.callbacks, sctx);

    status = MsQuic->StreamStart(quic_stream,
                                  QUIC_STREAM_START_FLAG_IMMEDIATE);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("StreamStart failed: 0x%x", status);
        free(sctx);
        MsQuic->StreamClose(quic_stream);
        return WT_ERR_SEND_FAILED;
    }

    /* Send the data */
    QUIC_BUFFER send_buf;
    send_buf.Buffer = (uint8_t*)data;   /* borrowed — caller must keep alive */
    send_buf.Length = (uint32_t)length;

    status = MsQuic->StreamSend(quic_stream, &send_buf, 1,
                                 QUIC_SEND_FLAG_FIN, NULL);
    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("StreamSend failed: 0x%x", status);
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        free(sctx);
        return WT_ERR_SEND_FAILED;
    }

    /* Record the stream */
    mgr->streams[slot].id = stream_id;
    mgr->streams[slot].quic_stream = quic_stream;
    mgr->streams[slot].in_use = true;
    mgr->streams[slot].send_closed = true;  /* FIN sent */

    return WT_OK;
}

void wt_stream_manager_accept_stream(
    wt_stream_manager_t* mgr,
    HQUIC                quic_stream)
{
    /* Allocate per-stream context for receiving */
    stream_ctx_t* sctx = (stream_ctx_t*)calloc(1, sizeof(stream_ctx_t));
    sctx->mgr = mgr;
    sctx->stream_id = atomic_fetch_add(&mgr->next_id, 1);
    sctx->quic_stream = quic_stream;

    MsQuic->SetCallbackHandler(quic_stream,
                                (void*)k_stream_callbacks.callbacks, sctx);
}
