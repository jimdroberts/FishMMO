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
    memset(session, 0, sizeof(*session));
    session->quic_conn = quic_conn;
    session->conn_id = conn_id;

    session->stream_mgr = (wt_stream_manager_t*)calloc(
        1, sizeof(wt_stream_manager_t));
    if (!session->stream_mgr) return WT_ERR_UNKNOWN;

    wt_stream_manager_init(session->stream_mgr, quic_conn, conn_id,
                           NULL, NULL);
    return WT_OK;
}

static void on_streams_done(void* ctx)
{
    wt_session_t* session = (wt_session_t*)ctx;
    free(session->stream_mgr);
    session->stream_mgr = NULL;
}

void wt_session_shutdown(wt_session_t* session)
{
    if (!session) return;

    if (session->stream_mgr) {
        /* Mark as shutting down and null callbacks. If no active
         * streams remain, free immediately. Otherwise defer to the
         * last SHUTDOWN_COMPLETE callback via on_all_streams_done. */
        session->stream_mgr->on_stream_data = NULL;
        session->stream_mgr->callback_ctx = NULL;
        session->stream_mgr->shutting_down = true;
        session->stream_mgr->on_all_streams_done = on_streams_done;
        session->stream_mgr->done_ctx = session;

        wt_stream_manager_shutdown(session->stream_mgr);

        if (atomic_load(&session->stream_mgr->active_streams) == 0) {
            free(session->stream_mgr);
            session->stream_mgr = NULL;
        }
    }
    session->quic_conn = NULL;
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

    /* Copy data for async send — QUIC buffers must outlive the call */
    uint8_t* copy = (uint8_t*)malloc((size_t)length);
    if (!copy) return WT_ERR_SEND_FAILED;
    memcpy(copy, data, (size_t)length);

    QUIC_BUFFER dgram_buf;
    dgram_buf.Buffer = copy;
    dgram_buf.Length = (uint32_t)length;

    QUIC_STATUS status = MsQuic->DatagramSend(
        session->quic_conn, &dgram_buf, 1,
        QUIC_SEND_FLAG_NONE, copy); /* copy freed by msquic on send complete */

    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("DatagramSend failed: 0x%x", status);
        free(copy);
        return WT_ERR_SEND_FAILED;
    }
    return WT_OK;
}