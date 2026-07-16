/**
 * @file session.cpp
 * @brief WebTransport session — bridges streams and datagrams for one connection.
 */

#include "session.h"
#include <stdlib.h>
#include <string.h>

int32_t wt_session_init(
    wt_session_t*       session,
    HQUIC               quic_conn,
    wt_connection_id_t  conn_id)
{
    memset(session, 0, sizeof(*session));
    session->quic_conn = quic_conn;
    session->conn_id = conn_id;

    session->stream_mgr = (wt_stream_manager_t*)calloc(1, sizeof(wt_stream_manager_t));
    if (!session->stream_mgr) return WT_ERR_UNKNOWN;

    wt_stream_manager_init(session->stream_mgr, quic_conn, conn_id,
                           NULL, NULL);  /* Callbacks set by parent */

    wt_datagram_queue_init(&session->dgram_queue);

    return WT_OK;
}

void wt_session_shutdown(wt_session_t* session)
{
    if (!session) return;

    if (session->stream_mgr) {
        wt_stream_manager_shutdown(session->stream_mgr);
        free(session->stream_mgr);
        session->stream_mgr = NULL;
    }

    wt_datagram_queue_reset(&session->dgram_queue);
}

int32_t wt_session_send_stream(
    wt_session_t*       session,
    const uint8_t*      data,
    int32_t             length)
{
    if (!session || !session->stream_mgr) return WT_ERR_INVALID_STATE;
    return wt_stream_manager_send(session->stream_mgr, data, length);
}

int32_t wt_session_send_datagram(
    wt_session_t*       session,
    const uint8_t*      data,
    int32_t             length)
{
    if (!session) return WT_ERR_INVALID_STATE;

    QUIC_BUFFER dgram_buf;
    dgram_buf.Buffer = (uint8_t*)data;   /* borrowed */
    dgram_buf.Length = (uint32_t)length;

    QUIC_STATUS status = MsQuic->DatagramSend(
        session->quic_conn, &dgram_buf, 1,
        QUIC_SEND_FLAG_NONE, NULL);

    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR("DatagramSend failed: 0x%x", status);
        return WT_ERR_SEND_FAILED;
    }
    return WT_OK;
}
