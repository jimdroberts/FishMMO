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
    /* True after we have handled the optional WEBTRANSPORT_STREAM capsule
     * at the start of a browser data stream (or decided none is present). */
    bool                 header_checked;
} stream_ctx_t;

/* WEBTRANSPORT_STREAM capsule type (draft-ietf-webtrans-http3). */
#define WT_STREAM_CAPSULE_TYPE  0x41u

/* Minimal QUIC varint helpers (local to stream framing). */
static int sm_varint_decode(const uint8_t* buf, size_t len,
                            uint64_t* out_val, size_t* out_bytes)
{
    if (len < 1) return -1;
    uint8_t prefix = buf[0] >> 6;
    size_t need = 1u << prefix;
    if (len < need) return -1;
    uint64_t v = (uint64_t)(buf[0] & 0x3F);
    for (size_t i = 1; i < need; i++)
        v = (v << 8) | buf[i];
    *out_val = v;
    *out_bytes = need;
    return 0;
}

static size_t sm_varint_encode(uint64_t val, uint8_t* out)
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

/**
 * If buf starts with WEBTRANSPORT_STREAM (HTTP/3 frame type 0x41), skip it.
 *
 * Wire format (draft-ietf-webtrans-http3 / Chromium HttpDecoder):
 *   Type (i) = 0x41
 *   Session ID (i)
 *   … remainder of stream is application data (no length field!).
 *
 * Older code treated this as type+length+payload (HTTP/3 layout) which
 * mis-parsed non-zero session IDs and could leave ClientHandshake
 * misaligned or stuck as "incomplete".
 *
 * Returns byte offset of application data (0 if no capsule).
 * incomplete=true if the session-id varint is truncated.
 */
static size_t sm_skip_wt_stream_header(const uint8_t* buf, size_t len,
                                       bool* incomplete)
{
    *incomplete = false;
    if (len < 1) { *incomplete = true; return 0; }
    /* Type is always a 1-byte varint for 0x41. */
    if (buf[0] != WT_STREAM_CAPSULE_TYPE)
        return 0;

    size_t off = 1;
    uint64_t session_id = 0;
    size_t nb = 0;
    if (sm_varint_decode(buf + off, len - off, &session_id, &nb) < 0) {
        *incomplete = true;
        return 0;
    }
    off += nb;
    return off;
}

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

        /* Per-stream bound check — prevent OOM from unbounded receive buffer. */
        if (total > WT_MAX_STREAM_RECV_BUF ||
            sctx->recv_offset > WT_MAX_STREAM_RECV_BUF - total) {
            MsQuic->StreamShutdown(stream,
                                    QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
            return QUIC_STATUS_ABORTED;
        }

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

        /* Append into recv_buf (may need prior partial WT header bytes). */
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

        /*
         * ── CRITICAL: deliver on RECEIVE, not only on peer FIN ──
         *
         * Native FishMMO clients open one stream per packet and FIN it, so the
         * old PEER_SEND_SHUTDOWN delivery path worked.
         *
         * Chrome WebGL uses a *persistent* bidirectional stream (jslib
         * WTSendStream caches the writer and never closes). Waiting for FIN
         * means ClientHandshake / CreateAccountBroadcast sit in recv_buf
         * forever and never reach FishNet — exactly the post-WT stall.
         */
        const uint8_t* app = sctx->recv_buf;
        size_t app_len = sctx->recv_offset;

        if (!sctx->header_checked) {
            if (sctx->mgr->use_wt_stream_header) {
                bool incomplete = false;
                size_t skip = sm_skip_wt_stream_header(app, app_len, &incomplete);
                if (incomplete) {
                    /* Wait for more bytes to finish the capsule. */
                    return QUIC_STATUS_SUCCESS;
                }
                if (skip > 0) {
                    WT_LOG_INFO(
                        "Stream %llu: stripped WEBTRANSPORT_STREAM capsule "
                        "(%zu bytes), app_payload=%zu",
                        (unsigned long long)sctx->stream_id,
                        skip, app_len - skip);
                    app += skip;
                    app_len -= skip;
                }
            }
            sctx->header_checked = true;
        }

        if (app_len > 0 &&
            sctx->mgr->on_stream_data &&
            sctx->mgr->callback_ctx) {
            WT_LOG_INFO(
                "Stream %llu: FIRST_APP_PAYLOAD_AFTER_SESSION deliver %zu bytes "
                "to app (conn=%llu) first=%02x %02x %02x %02x "
                "stamp=WT_CAPSULE_NO_LEN_V1 (success bar: any payload after WT establish)",
                (unsigned long long)sctx->stream_id,
                app_len,
                (unsigned long long)sctx->mgr->conn_id,
                app_len > 0 ? app[0] : 0,
                app_len > 1 ? app[1] : 0,
                app_len > 2 ? app[2] : 0,
                app_len > 3 ? app[3] : 0);
            sctx->mgr->on_stream_data(
                sctx->mgr->callback_ctx,
                sctx->mgr->conn_id,
                sctx->stream_id,
                app,
                (int32_t)app_len);
        } else if (app_len == 0) {
            WT_LOG_INFO(
                "Stream %llu: recv after header strip empty (conn=%llu) "
                "raw_len=%u header_checked=%d",
                (unsigned long long)sctx->stream_id,
                (unsigned long long)sctx->mgr->conn_id,
                (unsigned)sctx->recv_offset,
                sctx->header_checked ? 1 : 0);
        }

        /* Data delivered (or empty after header strip) — free buffer. */
        atomic_fetch_sub(&sctx->mgr->total_recv_bytes, sctx->recv_offset);
        free(sctx->recv_buf);
        sctx->recv_buf = NULL;
        sctx->recv_offset = 0;
        return QUIC_STATUS_SUCCESS;
    }

    case QUIC_STREAM_EVENT_PEER_SEND_SHUTDOWN: {
        /* Peer finished sending. Deliver any residual buffer (e.g. partial
         * header that never completed, or race with last RECEIVE). */
        if (sctx->recv_buf && sctx->recv_offset > 0 &&
            sctx->mgr->on_stream_data && sctx->mgr->callback_ctx) {
            const uint8_t* app = sctx->recv_buf;
            size_t app_len = sctx->recv_offset;
            if (!sctx->header_checked && sctx->mgr->use_wt_stream_header) {
                bool incomplete = false;
                size_t skip = sm_skip_wt_stream_header(app, app_len, &incomplete);
                if (!incomplete && skip > 0) {
                    app += skip;
                    app_len -= skip;
                }
                sctx->header_checked = true;
            }
            if (app_len > 0) {
                WT_LOG_INFO(
                    "Stream %llu: deliver %zu bytes on peer FIN",
                    (unsigned long long)sctx->stream_id, app_len);
                sctx->mgr->on_stream_data(
                    sctx->mgr->callback_ctx,
                    sctx->mgr->conn_id,
                    sctx->stream_id,
                    app,
                    (int32_t)app_len);
            }
        }

        atomic_fetch_sub(&sctx->mgr->total_recv_bytes, sctx->recv_offset);
        free(sctx->recv_buf);
        sctx->recv_buf = NULL;
        sctx->recv_offset = 0;

        sm_lock(sctx->mgr);
        for (int i = 0; i < WT_MAX_STREAMS; i++) {
            if (sctx->mgr->streams[i].id == sctx->stream_id) {
                sctx->mgr->streams[i].recv_closed = true;
                break;
            }
        }
        sm_unlock(sctx->mgr);

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
    }

}

void wt_stream_manager_shutdown(wt_stream_manager_t* mgr)
{
    /* Collect in-use streams under the lock, then shut them down
     * outside the lock. StreamShutdown can fire SHUTDOWN_COMPLETE
     * synchronously, which also acquires the lock — deadlock if
     * we held the lock across the MsQuic call. */
    /* Heap allocation — WT_MAX_STREAMS (4096) HQUIC handles is ~32 KB on
     * 64-bit.  A stack allocation of this size risks overflow on constrained
     * systems (embedded, small stacks in QUIC callback threads). */
    HQUIC* pending = (HQUIC*)malloc(sizeof(HQUIC) * (size_t)WT_MAX_STREAMS);
    if (!pending) {
        WT_LOG_ERROR("wt_stream_manager_shutdown: malloc(%zu) failed — "
                     "cannot collect stream handles for shutdown. "
                     "Leaking %u active streams.",
                     sizeof(HQUIC) * (size_t)WT_MAX_STREAMS,
                     (unsigned)atomic_load(&mgr->active_streams));
        return;  /* Can't collect handles — skip shutdown. Leaks streams but
                  * avoids stack overflow; better a leak than a crash. */
    }

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

    /* Send data — copy to ensure lifetime across async send.
     * Browser WebTransport: WEBTRANSPORT_STREAM = Type 0x41 + Session ID
     * (no length). Wrong length framing breaks Chrome stream association. */
    size_t header_len = 0;
    uint8_t header[1 + 8];
    if (mgr->use_wt_stream_header) {
        header[0] = (uint8_t)WT_STREAM_CAPSULE_TYPE;
        size_t sid_n = sm_varint_encode(mgr->wt_session_id, header + 1);
        header_len = 1 + sid_n;
        WT_LOG_INFO(
            "Stream send: WEBTRANSPORT_STREAM session_id=%llu header_len=%zu "
            "app_len=%d stamp=WT_CAPSULE_NO_LEN_V1",
            (unsigned long long)mgr->wt_session_id, header_len, length);
    }

    uint8_t* copy = (uint8_t*)malloc(header_len + (size_t)length);
    if (!copy) {
        /* Do NOT clear in_use or quic_stream — SHUTDOWN_COMPLETE from
         * StreamShutdown will handle slot cleanup. */
        MsQuic->StreamShutdown(quic_stream,
                                QUIC_STREAM_SHUTDOWN_FLAG_ABORT, 0);
        return WT_ERR_BUFFER_FULL;
    }
    if (header_len > 0)
        memcpy(copy, header, header_len);
    memcpy(copy + header_len, data, (size_t)length);

    QUIC_BUFFER send_buf;
    send_buf.Buffer = copy;
    send_buf.Length = (uint32_t)(header_len + (size_t)length);

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