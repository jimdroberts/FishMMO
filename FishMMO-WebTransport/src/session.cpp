/**
 * @file session.cpp
 * @brief WebTransport session — bridges streams and datagrams.
 */

#include "session.h"
#include "server.h"
#include "client.h"
#include <stdlib.h>

/* ── Stream data callback that dispatches to parent ─────────── */

static void session_on_stream_data(
    void* ctx, wt_connection_id_t conn_id,
    wt_stream_id_t stream_id,
    const uint8_t* data, int32_t length)
{
    wt_session_t* session = (wt_session_t*)ctx;

    if (session->parent_type == WT_PARENT_SERVER) {
        wt_server_s* srv = session->parent.server;
        if (srv && srv->callbacks.on_stream_data) {
            srv->callbacks.on_stream_data(
                srv->user_context, conn_id, stream_id, data, length);
        }
    } else {
        wt_client_s* cli = session->parent.client;
        if (cli && cli->callbacks.on_stream_data) {
            cli->callbacks.on_stream_data(
                cli->user_context, stream_id, data, length);
        }
    }
}

/* ── API ────────────────────────────────────────────────────── */

int32_t wt_session_init(
    wt_session_t* session, HQUIC quic_conn, wt_connection_id_t conn_id)
{
    session->quic_conn = quic_conn;
    session->conn_id = conn_id;
    atomic_init(&session->ref_count, 1);   /* owner reference */
    atomic_init(&session->released, false);

    session->stream_mgr = (wt_stream_manager_t*)calloc(
        1, sizeof(wt_stream_manager_t));
    if (!session->stream_mgr) return WT_ERR_UNKNOWN;

    wt_stream_manager_init(session->stream_mgr, quic_conn, conn_id,
                           NULL, NULL);
    return WT_OK;
}

bool wt_session_acquire(wt_session_t* session)
{
    if (!session) return false;
    /* Increment refcount. If the owner has already released,
     * we may still acquire as long as refcount > 0. The release
     * path won't free until we decrement. */
    uint32_t prev = atomic_fetch_add(&session->ref_count, 1);
    if (prev == 0) {
        /* refcount was 0 — session is already freed or being freed.
         * Undo our increment. */
        atomic_fetch_sub(&session->ref_count, 1);
        return false;
    }
    return true;
}

void wt_session_release(wt_session_t* session)
{
    if (!session) return;
    uint32_t prev = atomic_fetch_sub(&session->ref_count, 1);
    if (prev == 1) {
        /* We were the last reference. If the owner has released,
         * it's safe to free. Otherwise the owner still holds it. */
        if (atomic_load(&session->released)) {
            free(session);
        }
    }
}

static void try_free_mgr(wt_stream_manager_t* mgr)
{
    /* atomic_bool is typedef'd to int (4 bytes); the CAS macro writes
     * 4 bytes to *expected. Using C++ bool (1 byte) would overflow. */
    int expected = 0;
    if (atomic_compare_exchange_strong(&mgr->freed, &expected, 1)) {
#if defined(WT_PLATFORM_WINDOWS)
        DeleteCriticalSection(&mgr->streams_lock);
#else
        pthread_mutex_destroy(&mgr->streams_lock);
#endif
        free(mgr);
    }
}

static void on_streams_done(void* ctx)
{
    wt_stream_manager_t* mgr = (wt_stream_manager_t*)ctx;

    /* Check shutdown_complete FIRST — if set, mgr may be concurrently
     * freed by wt_session_shutdown. Only write to mgr fields after
     * confirming shutdown_complete is still false. */
    if (atomic_load(&mgr->shutdown_complete)) {
        try_free_mgr(mgr);
        return;  /* do NOT touch mgr after try_free_mgr */
    }

    atomic_store(&mgr->streams_done_flag, true);
    /* Re-check after store — shutdown may have completed between
     * our first check and the store. */
    if (atomic_load(&mgr->shutdown_complete))
        try_free_mgr(mgr);
}

void wt_session_shutdown(wt_session_t* session)
{
    if (!session) return;

    /* Mark as released — no new acquires will succeed after this point
     * (acquire checks ref_count > 0 before incrementing, but we still
     * hold our owner reference, so ref_count >= 1). */
    atomic_store(&session->released, true);

    if (session->stream_mgr) {
        wt_stream_manager_t* mgr = session->stream_mgr;

        mgr->on_stream_data = NULL;
        mgr->callback_ctx = NULL;
        mgr->on_all_streams_done = on_streams_done;
        mgr->done_ctx = mgr;
        atomic_store(&mgr->streams_done_flag, false);
        atomic_store(&mgr->shutdown_complete, false);
        atomic_store(&mgr->freed, false);
        atomic_store(&mgr->shutting_down, true);

        wt_stream_manager_shutdown(mgr);

        atomic_store(&mgr->shutdown_complete, true);

        /* Free if no streams active or callback already fired. CAS ensures
         * exactly one of session_shutdown or on_streams_done wins the free. */
        if (atomic_load(&mgr->active_streams) == 0 ||
            atomic_load(&mgr->streams_done_flag)) {
            try_free_mgr(mgr);
        }
        /* Always null the pointer — if we didn't free mgr, on_streams_done
         * will free it via its done_ctx reference. Leaving a dangling pointer
         * here would allow sends to access freed memory. */
        session->stream_mgr = NULL;
    }
    session->quic_conn = NULL;

    /* Release owner reference — may free session if no in-flight sends. */
    wt_session_release(session);
}

void wt_session_wire_callbacks(wt_session_t* session)
{
    if (!session || !session->stream_mgr) return;

    session->stream_mgr->on_stream_data = session_on_stream_data;
    session->stream_mgr->callback_ctx = session;
    session->stream_mgr->conn_id = session->conn_id;
}

int32_t wt_session_send_stream(
    wt_session_t* session, const uint8_t* data, int32_t length)
{
    if (!session || !session->stream_mgr || !session->quic_conn)
        return WT_ERR_INVALID_STATE;

    return wt_stream_manager_send(session->stream_mgr, data, length);
}

int32_t wt_session_send_datagram(
    wt_session_t* session, const uint8_t* data, int32_t length)
{
    if (!session || !session->quic_conn) return WT_ERR_INVALID_STATE;
    if (!data || length <= 0) return WT_ERR_SEND_FAILED;

    /* Copy data for async send — QUIC buffers must outlive the call */
    uint8_t* copy = (uint8_t*)malloc((size_t)length);
    if (!copy) return WT_ERR_SEND_FAILED;
    memcpy(copy, data, (size_t)length);

    QUIC_BUFFER dgram_buf;
    dgram_buf.Buffer = copy;
    dgram_buf.Length = (uint32_t)length;

    QUIC_STATUS status = MsQuic->DatagramSend(
        session->quic_conn, &dgram_buf, 1,
        QUIC_SEND_FLAG_NONE, copy);
    /* On success, `copy` is freed by the DATAGRAM_SEND_STATE_CHANGED
     * callback when QUIC_DATAGRAM_SEND_STATE_IS_FINAL (ACK or LOST).
     * On failure, the datagram was never queued — no callback will fire
     * for it, so we free `copy` ourselves. MsQuic guarantees that
     * DATAGRAM_SEND_STATE_CHANGED does NOT fire synchronously from
     * within DatagramSend, so there is no double-free risk. */

    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("DatagramSend failed: 0x%x", status);
        free(copy);
        return WT_ERR_SEND_FAILED;
    }
    return WT_OK;
}