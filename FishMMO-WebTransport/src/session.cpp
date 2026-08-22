/**
 * @file session.cpp
 * @brief WebTransport session — bridges streams and datagrams.
 */

#include "session.h"
#include "server.h"
#include "client.h"
#include <stdlib.h>
#include <stdio.h>

/* ── Stream manager lock helpers for session use ────────────────
 * We use the stream_manager's existing streams_lock to synchronize
 * the shutdown decision between wt_session_shutdown and
 * on_streams_done.  These macros provide a platform-conditional
 * lock/unlock that operates on the mgr's lock fields.
 *
 * NOTE: On Windows, we use EnterCriticalSection/LeaveCriticalSection
 * directly (not the recursive sm_lock_fn from stream_manager.cpp).
 * The session path never enters the mgr lock recursively, so the
 * bare CRITICAL_SECTION is correct here. */
#if defined(WT_PLATFORM_WINDOWS)
  #define session_mgr_lock(mgr)    EnterCriticalSection(&(mgr)->streams_lock_cs)
  #define session_mgr_unlock(mgr)  LeaveCriticalSection(&(mgr)->streams_lock_cs)
#else
  #define session_mgr_lock(mgr)    pthread_mutex_lock(&(mgr)->streams_lock)
  #define session_mgr_unlock(mgr)  pthread_mutex_unlock(&(mgr)->streams_lock)
#endif

/* ── Stream data callback that dispatches to parent ─────────── */

static void session_on_stream_data(
    void* ctx, wt_connection_id_t conn_id,
    wt_stream_id_t stream_id,
    const uint8_t* data, int32_t length)
{
    wt_session_t* session = (wt_session_t*)ctx;

    WT_LOG_INFO("H3-DEBUG: session_on_stream_data parent=%s conn=%llu stream=%llu len=%d",
                session->parent_type == WT_PARENT_SERVER ? "SERVER" : "CLIENT",
                (unsigned long long)conn_id, (unsigned long long)stream_id, length);
    if (data && length > 0) {
        char linebuf[128];
        int n = length < 32 ? length : 32;
        int pos = 0;
        for (int i = 0; i < n; i++)
            pos += snprintf(linebuf + pos, sizeof(linebuf) - (size_t)pos, "%02x ", data[i]);
        WT_LOG_INFO("H3-DEBUG:   first %d bytes: %s%s", n, linebuf, length > 32 ? "..." : "");
    }

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

    /* Atomically increment ref_count only if it is > 0.  This CAS retry
     * loop eliminates the TOCTOU race between the old separate "released"
     * check and ref_count increment: a concurrent wt_session_shutdown /
     * wt_session_release could set released=true and free(session) in the
     * window between those two operations.  By operating on the SAME memory
     * location (ref_count) for both the load and the compare-and-swap, we
     * close that window entirely — if the session is being freed (ref_count
     * drops to 0) between the load and the CAS, the CAS fails, expected is
     * updated to 0, and the loop exits with false. */
    unsigned int expected = atomic_load(&session->ref_count);
    while (expected > 0) {
        if (atomic_compare_exchange_strong(&session->ref_count, &expected, expected + 1))
            return true;
    }
    return false;
}

void wt_session_release(wt_session_t* session)
{
    if (!session) return;

    /* CAS loop: decrement ref_count only if it is > 0.
     * This eliminates the TOCTOU-underflow race in the old
     * atomic_fetch_sub approach: between fetch_sub returning 1
     * (ref_count went 1->0) and the subsequent load of `released`,
     * a concurrent wt_session_shutdown could call its own
     * wt_session_release, which would fetch_sub on 0 and underflow
     * to UINT32_MAX, leaking the session.
     *
     * With the CAS loop we never decrement below 0, so no underflow
     * is possible.  The race window between releasing the last ref
     * and checking `released` still exists, but it is benign: if
     * shutdown's release runs first and calls free(session), this
     * thread's CAS fails (ref_count==0, expected reloads to 0,
     * loop exits) and we return without touching freed memory. */
    unsigned int expected = atomic_load(&session->ref_count);
    while (expected > 0) {
        if (atomic_compare_exchange_strong(
                &session->ref_count, &expected, expected - 1)) {
            /* We decremented from expected to expected-1.
             * If expected was 1, we went from 1 to 0 — we hold
             * the last reference. Check if the owner has released. */
            if (expected == 1) {
                if (atomic_load(&session->released)) {
                    free(session);
                }
            }
            return;
        }
        /* CAS failed: `expected` was reloaded with the current value.
         * If it dropped to 0, we exit the loop (should not happen
         * under normal usage). */
    }
    /* ref_count was already 0 — double-release or use-after-free. */
    WT_LOG_WARN("wt_session_release: ref_count already 0");
}

static void try_free_mgr(wt_stream_manager_t* mgr)
{
    /* atomic_bool is typedef'd to int (4 bytes); the CAS macro writes
     * 4 bytes to *expected. Using C++ bool (1 byte) would overflow. */
    int expected = 0;
    if (atomic_compare_exchange_strong(&mgr->freed, &expected, 1)) {
#if defined(WT_PLATFORM_WINDOWS)
        DeleteCriticalSection(&mgr->streams_lock_cs);
#else
        pthread_mutex_destroy(&mgr->streams_lock);
#endif
        free(mgr);
    }
}

static void on_streams_done(void* ctx)
{
    wt_stream_manager_t* mgr = (wt_stream_manager_t*)ctx;

    session_mgr_lock(mgr);
    atomic_store(&mgr->streams_done_flag, true);
    int should_free = atomic_load(&mgr->shutdown_complete);
    session_mgr_unlock(mgr);

    if (should_free)
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

        /* Serialize with on_streams_done: under streams_lock, set
         * shutdown_complete and check if on_streams_done has already
         * set its flag.  Whichever thread runs second under the lock
         * sees the other's state and avoids touching freed memory. */
        session_mgr_lock(mgr);
        atomic_store(&mgr->shutdown_complete, true);
        int should_free = atomic_load(&mgr->streams_done_flag) ||
                          atomic_load(&mgr->active_streams) == 0;
        session->stream_mgr = NULL;
        session_mgr_unlock(mgr);

        if (should_free)
            try_free_mgr(mgr);
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

/* ── HTTP/3 Datagram framing (RFC 9297 §2.1) ──────────────────── */

/** Encode @p val as a QUIC varint (RFC 9000 §16). Returns bytes written. */
static size_t wt_dgram_varint_encode(uint64_t val, uint8_t* out)
{
    if (val < 64) {
        out[0] = (uint8_t)val;
        return 1;
    }
    if (val < 16384) {
        out[0] = (uint8_t)(0x40 | (val >> 8));
        out[1] = (uint8_t)(val & 0xFF);
        return 2;
    }
    if (val < 1073741824ull) {
        out[0] = (uint8_t)(0x80 | (val >> 24));
        out[1] = (uint8_t)((val >> 16) & 0xFF);
        out[2] = (uint8_t)((val >> 8) & 0xFF);
        out[3] = (uint8_t)(val & 0xFF);
        return 4;
    }
    out[0] = (uint8_t)(0xC0 | ((val >> 56) & 0x3F));
    out[1] = (uint8_t)((val >> 48) & 0xFF);
    out[2] = (uint8_t)((val >> 40) & 0xFF);
    out[3] = (uint8_t)((val >> 32) & 0xFF);
    out[4] = (uint8_t)((val >> 24) & 0xFF);
    out[5] = (uint8_t)((val >> 16) & 0xFF);
    out[6] = (uint8_t)((val >> 8) & 0xFF);
    out[7] = (uint8_t)(val & 0xFF);
    return 8;
}

/** Decode a QUIC varint. Returns bytes consumed, or 0 if truncated. */
static size_t wt_dgram_varint_decode(const uint8_t* buf, size_t len,
                                     uint64_t* out_val)
{
    if (len < 1) return 0;
    size_t need = (size_t)1u << (buf[0] >> 6);
    if (len < need) return 0;
    uint64_t v = (uint64_t)(buf[0] & 0x3F);
    for (size_t i = 1; i < need; i++)
        v = (v << 8) | buf[i];
    *out_val = v;
    return need;
}

void wt_session_enable_h3_datagrams(
    wt_session_t* session, uint64_t connect_stream_id)
{
    if (!session) return;
    session->h3_datagrams = true;
    session->wt_session_id = connect_stream_id;
}

bool wt_session_datagram_payload(
    wt_session_t* session, const uint8_t* data, uint32_t length,
    const uint8_t** out_payload, uint32_t* out_length)
{
    if (!data || !out_payload || !out_length) return false;

    if (!session || !session->h3_datagrams) {
        /* Raw QUIC peer — the datagram is the payload. */
        *out_payload = data;
        *out_length = length;
        return true;
    }

    uint64_t quarter = 0;
    size_t n = wt_dgram_varint_decode(data, length, &quarter);
    if (n == 0) {
        WT_LOG_WARN("Datagram dropped: truncated Quarter Stream ID (len=%u)",
                    length);
        return false;
    }
    if (quarter != (session->wt_session_id / 4)) {
        WT_LOG_WARN(
            "Datagram dropped: Quarter Stream ID %llu is not this session's "
            "(%llu)",
            (unsigned long long)quarter,
            (unsigned long long)(session->wt_session_id / 4));
        return false;
    }

    *out_payload = data + n;
    *out_length = length - (uint32_t)n;
    return true;
}

int32_t wt_session_max_datagram_payload(wt_session_t* session)
{
    int32_t budget = (int32_t)WT_DGRAM_MAX_SIZE;
    if (session && session->h3_datagrams) {
        uint8_t tmp[8];
        budget -= (int32_t)wt_dgram_varint_encode(
            session->wt_session_id / 4, tmp);
    }
    return budget;
}

int32_t wt_session_send_datagram(
    wt_session_t* session, const uint8_t* data, int32_t length)
{
    if (!session || !session->quic_conn) return WT_ERR_INVALID_STATE;
    if (!data || length <= 0) return WT_ERR_SEND_FAILED;

    /* SAFE_SHUTDOWN_V4: never call DatagramSend after peer/transport teardown
     * began. Prior AV was in MsQuicClose on a worker after DatagramSend races
     * (stack QUIC_BUFFER + double-free ownership flag). */
    if (atomic_load(&session->released))
        return WT_ERR_INVALID_STATE;
    if (session->stream_mgr && atomic_load(&session->stream_mgr->conn_closed))
        return WT_ERR_INVALID_STATE;

    /* HTTP/3 sessions spend part of the datagram budget on the Quarter
     * Stream ID, so the usable payload is smaller than the raw QUIC one. */
    if (length > wt_session_max_datagram_payload(session)) {
        WT_LOG_ERROR(
            "DatagramSend rejected: len=%d > max payload %d (conn=%llu)",
            length, (int)wt_session_max_datagram_payload(session),
            (unsigned long long)session->conn_id);
        return WT_ERR_SEND_FAILED;
    }

    HQUIC qconn = session->quic_conn;
    if (!qconn)
        return WT_ERR_INVALID_STATE;

    /* Build the HTTP/3 Datagram header, if this session uses one. */
    uint8_t h3_hdr[8];
    size_t h3_hdr_len = 0;
    if (session->h3_datagrams) {
        h3_hdr_len = wt_dgram_varint_encode(
            session->wt_session_id / 4, h3_hdr);
    }

    /* sizeof includes data[1]; allocate the rest. */
    size_t total = h3_hdr_len + (size_t)length;
    size_t bytes = offsetof(wt_dgram_send_ctx_t, data) + total;
    wt_dgram_send_ctx_t* ctx = (wt_dgram_send_ctx_t*)malloc(bytes);
    if (!ctx) return WT_ERR_SEND_FAILED;
    ctx->magic = WT_DGRAM_SEND_CTX_MAGIC;
    atomic_init(&ctx->freed, 0);
    if (h3_hdr_len > 0)
        memcpy(ctx->data, h3_hdr, h3_hdr_len);
    memcpy(ctx->data + h3_hdr_len, data, (size_t)length);
    ctx->buf.Buffer = ctx->data;
    ctx->buf.Length = (uint32_t)total;

    QUIC_STATUS status = MsQuic->DatagramSend(
        qconn, &ctx->buf, 1,
        QUIC_SEND_FLAG_NONE, ctx);

    if (QUIC_FAILED(status)) {
        WT_LOG_ERROR(
            "DatagramSend failed: 0x%x len=%d conn=%llu stamp=SAFE_SHUTDOWN_V4",
            status, length, (unsigned long long)session->conn_id);
        /* Exactly-once free (safe if a sync FINAL callback already ran). */
        wt_dgram_send_ctx_free(ctx, "datagram_send_failed");
        return WT_ERR_SEND_FAILED;
    }

    WT_LOG_INFO(
        "DatagramSend queued len=%d conn=%llu stamp=SAFE_SHUTDOWN_V4",
        length, (unsigned long long)session->conn_id);
    return WT_OK;
}